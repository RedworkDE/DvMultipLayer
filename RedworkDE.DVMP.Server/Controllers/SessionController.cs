using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using RedworkDE.DVMP.Server.Common;
using RedworkDE.DVMP.Server.Data;

namespace RedworkDE.DVMP.Server.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class SessionController : ControllerBase
	{
		private static IPAddress _ipv4;
		private static IPAddress _ipv6;

		static SessionController()
		{
			var ips = NetworkInterface.GetAllNetworkInterfaces()
				.Where(i => i.OperationalStatus == OperationalStatus.Up)
				.SelectMany(i => i.GetIPProperties().UnicastAddresses)
				.Select(a => a.Address)
				.ToList();

			_ipv4 = ips.First(ip => ip.AddressFamily == AddressFamily.InterNetwork);
			_ipv6 = ips.First(ip => ip.AddressFamily == AddressFamily.InterNetworkV6);
		}

		[HttpPost("user")]
		public CreateUserResponse CreateUser([FromBody] CreateUserRequest user)
		{
			var userInfo = DataContainer.CreateUser();
			userInfo.Ips.AddRange(user.LocalIps);

			var socket4 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket4.Bind(new IPEndPoint(_ipv4, 0));
			socket4.Listen();
			socket4.BeginAccept(ar =>
			{
				var sock = socket4.EndAccept(ar);
				userInfo.HasIpV4 = true;
				lock(userInfo.Ips) userInfo.Ips.Add(sock.RemoteEndPoint.ToString());
				lock (userInfo.Sockets) userInfo.Sockets.Add(sock);
			}, null);
			var socket6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
			socket6.Bind(new IPEndPoint(_ipv6, 0));
			socket6.Listen();
			socket6.BeginAccept(ar =>
			{
				var sock = socket6.EndAccept(ar);
				userInfo.HasIpV6 = true;
				lock (userInfo.Ips) userInfo.Ips.Add(sock.RemoteEndPoint.ToString());
				lock (userInfo.Sockets) userInfo.Sockets.Add(sock);
			}, null);

			lock (userInfo.Sockets)
			{
				userInfo.Sockets.Add(socket4);
				userInfo.Sockets.Add(socket6);
			}

			return new CreateUserResponse()
			{
				UserId = userInfo.UserId,
				ConnectToIps = new List<string>()
				{
					socket4.LocalEndPoint.ToString(),
					socket6.LocalEndPoint.ToString(),
				}
			};
		}

		[HttpPost]
		public CreateSessionResponse CreateSession([FromBody] CreateSessionRequest request)
		{
			var session = DataContainer.CreateSession();

			lock (session.Users) session.Users.Add(request.User);

			return new CreateSessionResponse()
			{
				SessionId = session.SessionId
			};
		}

		[HttpPost("{id}")]
		public IActionResult JoinSession(Guid id, [FromBody] JoinSessionRequest value)
		{
			if (!DataContainer.Users.TryGetValue(value.User, out _)) return NotFound();
			if (!DataContainer.Sessions.TryGetValue(id, out var session)) return NotFound();

			List<string[]> targets = new List<string[]>();
			lock (session.Users)
			{
				foreach (var user in session.Users)
				{
					if (DataContainer.Users.TryGetValue(user, out var userInfo))
					{
						lock (userInfo.Ips)
						{
							targets.Add(userInfo.Ips.ToArray());
						}
					}
				}

				session.Users.Add(value.User);
			}

			return Ok(new JoinSessionResponse()
			{
				RemoteHosts = targets.ToArray()
			});
		}

		//// DELETE api/<SessionController>/5
		//[HttpDelete("{id}")]
		//public void Delete(int id)
		//{
		//}
	}
}
