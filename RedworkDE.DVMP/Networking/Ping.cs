using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RedworkDE.DVMP.Networking
{
	/// <summary>
	/// Ping Utility
	/// </summary>
	public class Ping : AutoCreateMonoBehaviour<Ping>, IPacketReceiver<PingPacket>, IPacketReceiver<PongPacket>
	{
		private readonly Dictionary<Guid, Stopwatch> _pings = new Dictionary<Guid, Stopwatch>();

		public event Action<Guid, ClientId, TimeSpan>? PingResponse;

		public static Ping Instance = null!;

		void Awake()
		{
			Instance = this;

			NetworkManager.RegisterReceiver<PingPacket>(this);
			NetworkManager.RegisterReceiver<PongPacket>(this);
		}

		public Guid SendPing(ClientId target)
		{
			var guid = Guid.NewGuid();
			_pings[guid] = Stopwatch.StartNew();
			NetworkManager.Send(new PingPacket(){Id = guid}, target);
			return guid;
		}

		public bool Receive(PingPacket packet, ClientId client)
		{
			NetworkManager.Send(new PongPacket() {Id = packet.Id}, client);
			return true;
		}

		public bool Receive(PongPacket packet, ClientId client)
		{
			if (_pings.TryGetValue(packet.Id, out var sw))
			{
				var elapsed = sw.Elapsed;
				PingResponse?.Invoke(packet.Id, client, elapsed);
			}
			else
			{
				PingResponse?.Invoke(packet.Id, client, TimeSpan.MinValue);
			}
			return true;
		}
	}

	public class PingPacket : AutoPacket
	{
		public Guid Id;
	}

	public class PongPacket : AutoPacket
	{
		public Guid Id;
	}

}