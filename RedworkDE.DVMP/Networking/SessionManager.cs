using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Open.Nat;
using RedworkDE.DVMP.Server.Common;
using Task = System.Threading.Tasks.Task;

namespace RedworkDE.DVMP.Networking
{
	/// <summary>
	/// todo: better establishing of connections
	/// </summary>
	public class SessionManager : HasLogger<SessionManager>
	{
		private readonly List<Socket> _sockets = new List<Socket>();
		private Guid _userId;
		private bool _init;


		public static readonly SessionManager Instance = new SessionManager();
		private SessionManager(){}


		private async Task InitSession()
		{
			if (_init) return;
			_init = true;
			
			Logger.LogDebug("InitSession");

			NetworkManager.Listen(0);
			var listenPort = NetworkManager.ListenPort;

			Logger.LogDebug($"Listening on {listenPort}, forwarding port");

			var forward = await SetupPortForward(listenPort);

			Logger.LogDebug($"discovered public ports: {string.Join(", ", forward)}");
			
			var ips = NetworkInterface.GetAllNetworkInterfaces()
				.Where(i => i.OperationalStatus == OperationalStatus.Up)
				.SelectMany(i => i.GetIPProperties().UnicastAddresses)
				.Select(a => a.Address)
				.Where(a => a.AddressFamily == AddressFamily.InterNetwork || a.AddressFamily == AddressFamily.InterNetworkV6)
				.Select(a => new IPEndPoint(a, listenPort).ToString())
				.ToList();

			Logger.LogDebug($"discovered private ports: {string.Join(", ", forward)}");

			ips.AddRange(forward);

			var session = await Api.Send<CreateUserResponse>("session/user", new CreateUserRequest() {LocalIps = ips});
			_userId = session.UserId;

			Logger.LogDebug($"Created session {_userId}, discovery targets: {string.Join(", ", session.ConnectToIps)}");
			
			var tasks = new List<Task>();
			foreach (var targets in session.ConnectToIps)
			{
				if (!Api.TryParse(targets, out var ipEndPoint)) continue;

				var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
				var task = socket.ConnectAsync(ipEndPoint);

				_sockets.Add(socket);
				tasks.Add(task);
			}

			Logger.LogDebug("connected started");

			try
			{
				await Task.WhenAll(tasks);
			}
			catch(Exception ex)
			{
				Logger.LogDebug("Errors connection: " + ex);
			}

			Logger.LogDebug("connections finished");
		}

		private static async Task<List<string>> SetupPortForward(int listenPort)
		{
			var mappingRequest = new Mapping(Protocol.Tcp, listenPort, 0, "Derail Valley MultipLayer");

			Logger.LogDebug($"Discover devices");

			var devices = new []{ await new NatDiscoverer().DiscoverDeviceAsync(PortMapper.Pmp | PortMapper.Upnp, new CancellationTokenSource(10_000))};

			Logger.LogDebug($"Discovered:\n {string.Join("\n\n", (object[])devices)}");

			try
			{
				await Task.WhenAll(devices.Select(natDevice => natDevice.CreatePortMapAsync(mappingRequest)));
			}
			catch(Exception ex)
			{
				Logger.LogWarning("Error creating port forwarding: " + ex);
			}

			Logger.LogDebug($"Created mappings");

			var results = new List<string>();

			foreach (var mappings in await Task.WhenAll(devices.Select(natDevice => natDevice.GetAllMappingsAsync())))
			{
				foreach (var mapping in mappings)
				{
					if (mapping.PrivatePort == listenPort)
					{
						results.Add(new IPEndPoint(mapping.PublicIP, mapping.PublicPort).ToString());
					}
				}
			}

			Logger.LogDebug("found all");

			return results;
		}

		public async Task<Guid> HostAsync()
		{
			await InitSession();

			var session = await Api.Send<CreateSessionResponse>("session", new CreateSessionRequest()
			{
				User = _userId
			});

			Logger.LogDebug($"created session: {session.SessionId}");

			return session.SessionId;
		}

		public async Task JoinAsync(Guid sessionId)
		{
			await InitSession();

			var session = await Api.Send<JoinSessionResponse>($"session/{sessionId:N}", new JoinSessionRequest() {User = _userId});
			foreach (var remoteHost in session.RemoteHosts)
			{
				foreach (var target in remoteHost)
				{
					if (Api.TryParse(target, out var ip))
					{
						retry:
						var task = NetworkManager.Connect(ip.Address, ip.Port);
						if (task is null)
						{
							await Task.Delay(10);
							goto retry;
						}

						await task;
					}
				}
			}
		}
	}


	public class Api : HasLogger<Api>
	{
		private const string API_BASE = "http://localhost:5000/api/";

		public static async Task<TResult> Send<TResult>(string url, object body, string method = "POST")
		{
			var jo = JToken.FromObject(body);
			using var sw = new StringWriter();
			using var jw = new JsonTextWriter(sw);
			jo.WriteTo(jw);
			using var ms = new MemoryStream();
			var bytes = Encoding.UTF8.GetBytes(sw.ToString());
			var request = WebRequest.CreateHttp(API_BASE+url);
			request.Method = method;
			request.ContentLength = bytes.LongLength;
			request.ContentType = "application/json";
			request.Accept = "application/json";
			var requestStream = await request.GetRequestStreamAsync();
			await requestStream.WriteAsync(bytes, 0, bytes.Length);
			var response = await request.GetResponseAsync();
			var responseStream = response.GetResponseStream();
			using var sr = new StreamReader(responseStream);
			using var jr = new JsonTextReader(sr); 
			jo = JToken.Load(jr);
			return jo.ToObject<TResult>();
		}

		// src: https://github.com/dotnet/corefx/blob/ba6a1037c4e3dd0f0cc46a02a8a2b649e6adec7c/src/System.Net.Primitives/src/System/Net/IPEndPoint.cs#L127
		public static bool TryParse(string s, out IPEndPoint result)
		{
			int addressLength = s.Length;  // If there's no port then send the entire string to the address parser
			int lastColonPos = s.LastIndexOf(':');

			// Look to see if this is an IPv6 address with a port.
			if (lastColonPos > 0)
			{
				if (s[lastColonPos - 1] == ']')
				{
					addressLength = lastColonPos;
				}
				// Look to see if this is IPv4 with a port (IPv6 will have another colon)
				else if (s.Substring(0, lastColonPos).LastIndexOf(':') == -1)
				{
					addressLength = lastColonPos;
				}
			}

			if (IPAddress.TryParse(s.Substring(0, addressLength), out IPAddress address))
			{
				uint port = 0;
				if (addressLength == s.Length ||
				    (uint.TryParse(s.Substring(addressLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= IPEndPoint.MaxPort))

				{
					result = new IPEndPoint(address, (int)port);
					return true;
				}
			}

			result = null!;
			return false;
		}
	}
}