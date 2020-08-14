using System;

namespace RedworkDE.DVMP.Server.Common
{
	public class JoinSessionRequest
	{
		public Guid User { get; set; }
	}

	public class JoinSessionResponse
	{
		public string[][] RemoteHosts { get; set; } = null!;
	}
}