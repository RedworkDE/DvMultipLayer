using System;
using System.Collections.Concurrent;
using RedworkDE.DVMP.Server.Common;

namespace RedworkDE.DVMP.Server
{
	public class UserContainer
	{
		private static ConcurrentDictionary<Guid, UserInfo> _users = new ConcurrentDictionary<Guid, UserInfo>();


	}
}