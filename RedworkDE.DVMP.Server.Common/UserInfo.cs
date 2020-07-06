using System;
using System.Collections.Generic;
using System.Text;

namespace RedworkDE.DVMP.Server.Common
{
	public class UserInfo
	{
		public Guid UserId { get; set; }
		public List<string> LocalIps { get; set; } = null!;
		public List<string> PublicIps { get; set; } = null!;
		public bool HasIpV4 { get; set; }
		public bool HasIpV6 { get; set; }
		public int ListenPort { get; set; }
	}
}
