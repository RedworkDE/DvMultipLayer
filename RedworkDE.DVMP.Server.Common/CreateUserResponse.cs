using System;
using System.Collections.Generic;

namespace RedworkDE.DVMP.Server.Common
{
	public class CreateUserResponse
	{
		public Guid UserId { get; set; }
		public List<string> ConnectToIps { get; set; } = null!;
	}
}