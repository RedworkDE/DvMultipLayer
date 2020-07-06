using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CommandTerminal;
using RedworkDE.DVMP.Utils;

namespace RedworkDE.DVMP.Networking
{
	/// <summary>
	/// All the low level network things, there should always be only one instance of this to dispatch messages on the main thread
	/// </summary>
	public class NetworkManager : AutoCreateMonoBehaviour<NetworkManager>, IPacketReceiver<WelcomePacket>
	{
		private enum ConnectionState
		{
			InitialConnection = 0,
			Connected,
			Disconnected,
			WaitingForWelcome
		}

		private const int BUFFER_SIZE = 1 << 20;
		public static NetworkManager Instance { get; private set; } = null!;

		private static TcpListener? _listener;
		private static IAsyncResult? _acceptListener;
		private static TcpClient? _client;
		private static IAsyncResult? _acceptClient;

		private static readonly List<ConnectionState> _state = new List<ConnectionState>();
		private static readonly List<TcpClient> _tcpClients = new List<TcpClient>();
		private static readonly List<Stream> _clients = new List<Stream>();
		private static readonly List<ClientId> _clientIds = new List<ClientId>();
		private static readonly List<byte[]> _readBuffers = new List<byte[]>();
		private static readonly List<IAsyncResult> _reads = new List<IAsyncResult>();
		private static readonly List<IAsyncResult?> _writes = new List<IAsyncResult?>();
		private static readonly List<ConcurrentQueue<ArraySegment<byte>>> _writeQueue = new List<ConcurrentQueue<ArraySegment<byte>>>();

		private static readonly Dictionary<PacketType, Func<IPacket>> _packetTypes = new Dictionary<PacketType, Func<IPacket>>();
		private static readonly Dictionary<Type, Action<IPacket, int>> _dispatchers = new Dictionary<Type, Action<IPacket, int>>();

		private static readonly ConcurrentDictionary<PacketType, ConcurrentDictionary<object /*IPacketReceiver<T>*/, object?>> _receivers = new ConcurrentDictionary<PacketType, ConcurrentDictionary<object /*IPacketReceiver<T>*/, object?>>();
		private static readonly ConcurrentQueue<(IPacket packet, int client)> _incoming = new ConcurrentQueue<(IPacket packet, int client)>();
		
		private static readonly ConcurrentDictionary<INotifyClientConnection, object?> _notifyClientConnections = new ConcurrentDictionary<INotifyClientConnection, object?>();
		private static readonly ConcurrentQueue<(int, bool)> _clientUpdates = new ConcurrentQueue<(int, bool)>();

		private static readonly ConcurrentDictionary<ClientId, int> _clientIdLookup = new ConcurrentDictionary<ClientId, int>();
		private static readonly ConcurrentDictionary<uint, int> _blockLookup = new ConcurrentDictionary<uint, int>();
		
		public static event Action<IPacket, int>? UnhandledPacket;

		static NetworkManager()
		{
			foreach (var type in typeof(NetworkManager).Assembly.DefinedTypes)
			{
				if (type.GetInterfaces().Contains(typeof(IPacket)) && type != typeof(UnknownPacket))
				{
					var factoryMethod = type.GetMethod("CreateInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.InvokeMethod, null, new Type[0], new ParameterModifier[0]);
					if (factoryMethod is { })
					{
						Logger.LogDebug($"found packet type {type} / Assigned id {PacketType.GetByType(type)}");
						var factory = (Func<IPacket>)factoryMethod.CreateDelegate(typeof(Func<IPacket>));
						var packetType = PacketType.GetByType(type);
						_packetTypes.Add(packetType, factory);
						var methodInfo = Info.OfMethod<NetworkManager>(nameof(DispatchPacketOuter));
						var makeGenericMethod = methodInfo.MakeGenericMethod(type);
						var dispatch = (Action<IPacket, int>)makeGenericMethod.CreateDelegate(typeof(Action<IPacket, int>));
						_dispatchers.Add(type, dispatch);
					}
					else
					{
						throw new InvalidOperationException($"Type {type} inherits {typeof(IPacket)} but has no factory method");
					}
				}
			}

			new Thread(NetworkThread){IsBackground = true}.Start();
		}
		
		/// <summary>
		/// Listen for incoming connections on <paramref name="port"/>. You can only listen on one port. It is not possible to stop listening.
		/// </summary>
		/// <returns>false, if listen was called before</returns>
		public static bool Listen(int port)
		{
			if (_listener is {}) return false;

			MultiPlayerManager.Instance.IsBeforeMultiPlayer = true;

			_listener = new TcpListener(IPAddress.Loopback, port);
			_listener.Start();
			_acceptListener = _listener.BeginAcceptTcpClient(null, null);

			Logger.LogInfo($"Now Listening on {_listener.LocalEndpoint}");
			return true;
		}
		
		public static bool Connect(IPAddress remote, int remotePort)
		{
			if (_client is {}) return false;

			MultiPlayerManager.Instance.IsBeforeMultiPlayer = true;

			_client = new TcpClient();
			_acceptClient = _client.BeginConnect(remote, remotePort, null, null);
			return true;
		}

		/// <summary>
		/// Register a receiver packets of type <typeparamref name="T"/>. This will not not receive packets for derivative types. One receiver object can be registered for multiple types.
		/// </summary>
		public static void RegisterReceiver<T>(IPacketReceiver<T> receiver) where T : IPacket
		{
			_receivers.GetOrAdd(PacketType.Get<T>(), _ => new ConcurrentDictionary<object, object?>())[receiver] = null;
		}

		/// <summary>
		/// Unregister a receiver packets of type <typeparamref name="T"/>. This will only remove the receiver for this exact type.
		/// </summary>
		public static void UnregisterReceiver<T>(IPacketReceiver<T> receiver) where T : IPacket
		{
			_ = _receivers.TryGetValue(PacketType.Get<T>(), out var inner) && inner.TryRemove(receiver, out _);
		}

		/// <summary>
		/// Receive notifications for when clients join / leave
		/// </summary>
		public static void RegisterClientUpdates(INotifyClientConnection receiver)
		{
			_notifyClientConnections[receiver] = null;
		}

		/// <summary>
		/// Stop client notification
		/// </summary>
		public static void UnregisterClientUpdates(INotifyClientConnection receiver)
		{
			_notifyClientConnections.TryRemove(receiver, out _);
		}

		void Awake()
		{
			Instance = this;
			RegisterReceiver<WelcomePacket>(this);
			
			RegisterReceiver(new DefaultPacketReceiver<RequestIdBlockPacket>((packet, i) =>
			{
				if (!MultiPlayerManager.Instance.HasIdAuthority) return false;

				var block = MultiPlayerManager.Instance.Ids.AllocateBlock();
				Send(new AllocateIdBlockPacket(){Block = block, Client = i});
				return true;

			}));

			RegisterReceiver(new DefaultPacketReceiver<AllocateIdBlockPacket>((packet, i) =>
			{
				if (i != MultiPlayerManager.Instance.IdAuthority)
				{
					Logger.LogWarning($"Got {nameof(AllocateIdBlockPacket)} from {i} but authority is {MultiPlayerManager.Instance.IdAuthority}");
					return false;
				}

				if (packet.Client == MultiPlayerManager.Instance.OwnId)
				{
					if ((packet.Block & 0x80000000) == 0) // ignore initial packet, it is already handled
						MultiPlayerManager.Instance.Ids.AcquireBlock(packet.Block);
				}

				_blockLookup.TryAdd(packet.Block & ~0x80000000, ResolveClient(i));

				return true;
			}));

			UnhandledPacket += (packet, i) =>
			{
				Logger.LogWarning($"Unhandled packet: {packet}");
			};
		}

		void FixedUpdate()
		{
			while (_clientUpdates.TryDequeue(out var item)) DispatchClientUpdate(item.Item1, item.Item2);
			while (_incoming.TryDequeue(out var item)) _dispatchers[item.packet.GetType()].Invoke(item.packet, item.client);
		}

		static void NetworkThread()
		{
			while (true)
			{
				try
				{
					if (_acceptListener?.IsCompleted ?? false)
					{
						var client = _listener!.EndAcceptTcpClient(_acceptListener);
						_acceptListener = _listener.BeginAcceptSocket(null, null);

						AddClient(client);
					}

					if (_acceptClient?.IsCompleted ?? false)
					{
						_client!.EndConnect(_acceptClient);
						AddClient(_client);

						_acceptClient = null;
						_client = null;
					}

					for (int i = 0; i < _tcpClients.Count; i++)
					{
						switch (_state[i])
						{
							case ConnectionState.InitialConnection:
								Logger.LogDebug($"InitialConnection on {i}");

								var packet = new WelcomePacket();
								packet.Version = VersionHash.Version;
								packet.Id = MultiPlayerManager.Instance.OwnId;

								if (MultiPlayerManager.Instance.HasIdAuthority)
								{
									packet.InitialAllocatedBlock = MultiPlayerManager.Instance.Ids.AllocateBlock();
									_blockLookup.TryAdd(packet.InitialAllocatedBlock, i);
									var clientIdVal = packet.InitialAllocatedBlock * IdDispenser.BLOCK_SIZE;
									var clientId = Unsafe.As<uint, ClientId>(ref clientIdVal);
									_clientIds[i] = clientId;
									_clientIdLookup[clientId] = i;
									Send(new AllocateIdBlockPacket() {Block = packet.InitialAllocatedBlock | 0x80000000, Client = clientId});
								}

								_state[i] = ConnectionState.WaitingForWelcome;

								Logger.LogDebug("Sending Welcome");
								Send(packet, i);
								Logger.LogDebug("Sent Welcome");

								break;
							case ConnectionState.WaitingForWelcome:
								Logger.LogDebug($"WaitingForWelcome on {i}");
								break;
							case ConnectionState.Connected: break;

							case ConnectionState.Disconnected: continue;
							default: continue;
						}



						if (_reads[i].IsCompleted)
						{
							int read;
							try
							{
								read = _clients[i].EndRead(_reads[i]);
								if (read == 0)
								{
									DroppedClient(i);
									continue;
								}
							}
							catch (Exception ex)
							{
								Logger.LogError($"Failed to read client {i}: {ex}");

								DroppedClient(i);
								continue;
							}

							ReceivedMessage(i, read);

							_reads[i] = _clients[i].BeginRead(_readBuffers[i], 0, BUFFER_SIZE, null, null);
						}

						if (_writes[i]?.IsCompleted ?? false)
						{
							try
							{
								_clients[i].EndWrite(_writes[i]);
							}
							catch (Exception ex)
							{
								Logger.LogError($"Failed to write client {i}: {ex}");

								DroppedClient(i);
								continue;
							}

							_writes[i] = null;
						}

						if (_writes[i] is null && _writeQueue[i].TryDequeue(out var seg))
						{
							_writes[i] = _clients[i].BeginWrite(seg.Array, seg.Offset, seg.Count, null, null);
						}
					}

					Thread.Yield();
				}
				catch (Exception ex)
				{
					Logger.LogError($"Crash in NetworkThread: {ex}");
				}
			}
		}

		private static int ResolveClient(ClientId clientId)
		{
			if (clientId)
			{
				if (_clientIdLookup.TryGetValue(clientId, out var client)) return client;
				Logger.LogWarning($"Failed to resolve the unknown client id {clientId}, broadcasting instead");
				return -1; // todo 
			}

			return Unsafe.As<ClientId, int>(ref clientId) - 1;
		}

		/// <summary>
		/// Find the client that owns the given <see cref="MultiPlayerId"/>
		/// </summary>
		// todo: fix
		public static ClientId MultiPlayerIdToClient(MultiPlayerId id)
		{
			return FindClientId(ResolveMultiPlayerId(id));
		}

		private static int ResolveMultiPlayerId(MultiPlayerId multiPlayerId)
		{
			var block = Unsafe.As<MultiPlayerId, uint>(ref multiPlayerId) / IdDispenser.BLOCK_SIZE;
			if (_blockLookup.TryGetValue(block, out var client)) return client;
			Logger.LogWarning($"Failed to resolve the MultiPlayerId {multiPlayerId}, broadcasting instead");
			return -1;
		}

		private static void DroppedClient(int i)
		{
			Logger.LogInfo($"Disconnected from {_tcpClients[i].Client.RemoteEndPoint}");
			_clientUpdates.Enqueue((i, false));
			_state[i] = ConnectionState.Disconnected;
		}

		private static int AddClient(TcpClient client)
		{
			Logger.LogInfo($"Connected to {client.Client.RemoteEndPoint}");

			var clientNum = _tcpClients.Count;
			_state.Add(ConnectionState.InitialConnection);
			_tcpClients.Add(client);
			_clients.Add(client.GetStream());
			var id = clientNum + 1;
			_clientIds.Add(Unsafe.As<int, ClientId>(ref id));
			_readBuffers.Add(new byte[BUFFER_SIZE]);
			_reads.Add(_clients[clientNum].BeginRead(_readBuffers[clientNum], 0, BUFFER_SIZE, null, null));
			_writes.Add(null);
			_writeQueue.Add(new ConcurrentQueue<ArraySegment<byte>>());
			
			//_clientUpdates.Enqueue((clientNum, true));

			return clientNum;
		}

		private static void ReceivedMessage(int client, int numBytes)
		{
			var data = _readBuffers[client].AsSpan().Slice(0, numBytes);

			while (data.Length > 0)
			{
				IPacket packet;
				try
				{
					data.Read(out PacketType header);
					data.Read(out ushort packetSize);
					var packetData = data.Reserve(packetSize);

					packet = _packetTypes.TryGetValue(header, out var factory) ? factory() : new UnknownPacket(header);
					packet.ParseData(ref packetData);
				}
				catch (Exception ex)
				{
					Logger.LogError($"Failed to parse data from client {client}: {ex}");
					continue;
				}

				HandlePacket(packet, client);
			}
		}

		private static void Send<T>(T packet, int client) where T : IPacket
		{
			var buffer = new byte[packet.MaxSize + 4];
			var span = buffer.AsSpan();
			span.Write(typeof(T) == typeof(UnknownPacket) ? Unsafe.As<UnknownPacket>(packet).PacketType : PacketType.Get<T>());
			var size = span.Reserve<ushort>();
			packet.SerializeData(ref span);
			size.Write(checked((ushort) (buffer.Length - span.Length - 4)));
			var realSize = buffer.Length - span.Length;

			if (client == -2)
			{
				HandlePacket(packet, -2);
			}
			else if (client == -1)
			{
				for (int i = 0; i < _writeQueue.Count; i++)
					_writeQueue[i].Enqueue(new ArraySegment<byte>(buffer, 0, realSize));
			}
			else
			{
				_writeQueue[client].Enqueue(new ArraySegment<byte>(buffer, 0, realSize));
			}
		}

		/// <summary>
		/// Send a <paramref name="packet"/> to everyone
		/// </summary>
		/// <remarks>
		/// <typeparamref name="T"/> MUST be the actual type of <paramref name="packet"/> or deserialization will fail
		/// </remarks>
		public static void Send<T>(T packet) where T : IPacket
		{
			Send(packet, -1);
		}

		/// <summary>
		/// Send a <paramref name="packet"/> to <paramref name="target"/>
		/// </summary>
		/// <remarks>
		/// <typeparamref name="T"/> MUST be the actual type of <paramref name="packet"/> or deserialization will fail
		/// </remarks>
		public static void Send<T>(T packet, ClientId target) where T : IPacket
		{
			Send(packet, ResolveClient(target));
		}

		/// <summary>
		/// Send a <paramref name="packet"/> to the client that owns <paramref name="target"/>
		/// </summary>
		/// <remarks>
		/// <typeparamref name="T"/> MUST be the actual type of <paramref name="packet"/> or deserialization will fail
		/// </remarks>
		public static void Send<T>(T packet, MultiPlayerId target) where T : IPacket
		{
			if (!target) Send(packet, ResolveMultiPlayerId(target));
		}

		private static void HandlePacket(IPacket packet, int client)
		{
			_incoming.Enqueue((packet, client));
		}

		private static void DispatchPacketOuter<T>(IPacket packet, int client) where T : IPacket
		{
			DispatchPacket((T)packet, client);
		}

		private static void DispatchPacket<T>(T packet, int client) where T : IPacket
		{
			var handled = false;

			var clientId = FindClientId(client);

			handled |= DispatchPacketInner<IPacket>(packet, clientId);
			handled |= DispatchPacketInner<T>(packet, clientId);
			if (!handled) UnhandledPacket?.Invoke(packet, client);
		}

		private static ClientId FindClientId(int client)
		{
			var clientId = client == -2 ? MultiPlayerManager.Instance.OwnId : _clientIds[client];
			return clientId;
		}

		private static bool DispatchPacketInner<T>(T packet, ClientId client) where T : IPacket
		{
			var handled = false;

			if (!_receivers.TryGetValue(PacketType.Get<T>(), out var receivers)) return false;

			foreach (var receiver in receivers.Keys)
			{
				try
				{
					handled |= ((IPacketReceiver<T>)receiver).Receive(packet, client);
				}
				catch (Exception ex)
				{
					Logger.LogError($"Receiver {receiver} failed to handle packet {packet} from client {client}: {ex}");
				}
			}

			return handled;
		}
		
		private void DispatchClientUpdate(int client, bool joined)
		{
			var clientId = FindClientId(client);

			foreach (var receiver in _notifyClientConnections.Keys)
			{

				try
				{
					if (joined)
					{
						MultiPlayerManager.Instance.IsMultiPlayer = true;
						receiver.ClientConnected(clientId);
					}
					else receiver.ClientDisconnected(clientId);
				}
				catch (Exception ex)
				{
					Logger.LogError($"Receiver {receiver} failed to handle {(joined ? "join" : "disconnect")} from client {client}: {ex}");
				}
			}
		}

		public bool Receive(WelcomePacket packet, ClientId clientId)
		{
			Logger.LogDebug($"Received WelcomePacket from {clientId}");

			if (clientId == default)
			{
				Logger.LogWarning($"Received WelcomePacket from client with default id");
				return true;
			}

			var client = ResolveClient(clientId);

			if (packet.Version != VersionHash.Version) 
			{
				Logger.LogWarning($"Received WelcomePacket from client {clientId} with hash {packet.Version:N} but expected {VersionHash.Version:N}");

				_tcpClients[client].Close();
				return true;
			}

			// todo: better authority checks / replace the whole thing with a central server
			if (packet.InitialAllocatedBlock != 0 && MultiPlayerManager.Instance.HasIdAuthority)
			{
				Logger.LogWarning($"Authority conflict with client {clientId}");

				// Both ends have authority drop, the connection
				_tcpClients[client].Close();
				return true;
			}

			if (packet.InitialAllocatedBlock == 0 && !MultiPlayerManager.Instance.HasIdAuthority)
			{
				Logger.LogWarning($"No authority with client {clientId}");

				// todo: find a way to determine an authority
				_tcpClients[client].Close();
				return true;
			}

			if (packet.InitialAllocatedBlock != 0)
			{
				Logger.LogWarning($"Received initial block {packet.InitialAllocatedBlock} client {clientId}");

				MultiPlayerManager.Instance.Ids.AcquireBlock(packet.InitialAllocatedBlock);
				var id = MultiPlayerManager.Instance.NewId();
				if (Unsafe.As<MultiPlayerId, uint>(ref id) != packet.InitialAllocatedBlock * IdDispenser.BLOCK_SIZE)
					Logger.LogWarning($"Allocated ClientId ({id}) does not match expected value ({packet.InitialAllocatedBlock * IdDispenser.BLOCK_SIZE:x8})");
				MultiPlayerManager.Instance.OwnId = Unsafe.As<MultiPlayerId, ClientId>(ref id);
				MultiPlayerManager.Instance.IdAuthority = packet.Id;
			}

			Logger.LogDebug($"Client {client} claims id {packet.Id}");

			if (packet.Id != default)
			{
				if (!_clientIdLookup.TryAdd(packet.Id, client))
				{
					Logger.LogWarning($"Failed to add ClientId {packet.Id} for client {client}");
				}

				if (_clientIds[client])
				{
					Logger.LogWarning($"Client {client} already has ClientId {_clientIds[client]}, discarding {packet.Id}");
				}
				else
				{
					_clientIds[client] = packet.Id;
				}
			}

			_state[client] = ConnectionState.Connected;
			_clientUpdates.Enqueue((client, true));

			return true;
		}
	}

	/// <summary>
	/// An opaque id that represents a client.
	/// If it casts to true the id is portable across all clients
	/// If it is default it is broadcast
	/// Otherwise it is a local id and cannot be transmitted to others; outside the network manager such ids SHOULD never appear
	/// </summary>
	public readonly struct ClientId : IEquatable<ClientId>
	{
		public bool Equals(ClientId other)
		{
			return __value == other.__value;
		}

		public override bool Equals(object? obj)
		{
			return obj is ClientId other && Equals(other);
		}

		public override int GetHashCode()
		{
			// todo: investigate better hash code generation, but uint::GetHashCode() is the same as this
			return (int)__value;
		}

		public static bool operator ==(ClientId left, ClientId right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(ClientId left, ClientId right)
		{
			return !left.Equals(right);
		}

		public static implicit operator bool(ClientId self)
		{
			return self.__value >= IdDispenser.BLOCK_SIZE;
		}


		public static explicit operator MultiPlayerId(ClientId self)
		{
			return Unsafe.As<ClientId, MultiPlayerId>(ref self);
		}

#pragma warning disable 649
		private readonly uint __value;
#pragma warning restore 649

		public override string ToString()
		{
			return __value.ToString("x8");
		}
	}

	public class WelcomePacket : AutoPacket
	{
		public Guid Version;
		public ClientId Id;
		public uint InitialAllocatedBlock;
	}

	public class BroadcastMessagePacket : AutoPacket
	{
		[MarshalAs(UnmanagedType.BStr)]
		public string Message = null!;
	}

	public class RequestIdBlockPacket : AutoPacket
	{

	}

	public class AllocateIdBlockPacket : AutoPacket
	{
		public uint Block;
		public ClientId Client;
	}

	/// <summary>
	/// A packet was received with a PacketType that is not assigned
	/// </summary>
	public class UnknownPacket : IPacket
	{
		public UnknownPacket(PacketType packet)
		{
			PacketType = packet;
			Data = null!;
		}

		public PacketType PacketType { get; }
		public int MaxSize => Data.Length;
		public byte[] Data { get; private set; }

		public void ParseData(ref Span<byte> data)
		{
			Data = data.ToArray();
			data.Slice(0, 0);
		}

		public void SerializeData(ref Span<byte> data)
		{
			Data.AsSpan().CopyTo(data);
			data = data.Slice(0, Data.Length);
		}
	}

	/// <summary>
	/// Receive Notifications when clients change
	/// </summary>
	public interface INotifyClientConnection
	{
		void ClientConnected(ClientId client);
		void ClientDisconnected(ClientId client);
	}
}
