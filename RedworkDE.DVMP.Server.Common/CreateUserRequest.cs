using System.Collections.Generic;

namespace RedworkDE.DVMP.Server.Common
{
	public class CreateUserRequest
	{
		public List<string> LocalIps { get; set; } = null!;
	}
}