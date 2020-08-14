using System;
using System.Collections.Generic;

namespace RedworkDE.DVMP.Server.Data
{
	public class SessionInfo
	{
		public Guid SessionId { get; set; }
		public List<Guid> Users { get; } = new List<Guid>();
	}
}