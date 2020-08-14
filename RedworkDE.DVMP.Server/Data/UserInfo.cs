using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace RedworkDE.DVMP.Server.Data
{
	public class UserInfo
	{
		public Guid UserId { get; set; }
		public List<string> Ips { get; } = new List<string>();
		public List<Socket> Sockets { get; } = new List<Socket>();
		public bool HasIpV4 { get; set; }
		public bool HasIpV6 { get; set; }
		public int ListenPort { get; set; }
	}
}
