using System;

namespace RedworkDE.DVMP.Server.Common
{
	public class CreateSessionRequest
	{
		public Guid User { get; set; }
	}

	public class CreateSessionResponse
	{
		public Guid SessionId { get; set; }
	}
}