using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace RedworkDE.DVMP.Server.Data
{
	public class DataContainer
	{
		public static readonly ConcurrentDictionary<Guid, UserInfo> Users = new ConcurrentDictionary<Guid, UserInfo>();
		public static readonly ConcurrentDictionary<Guid, SessionInfo> Sessions = new ConcurrentDictionary<Guid, SessionInfo>();
		public static UserInfo CreateUser()
		{
			var ui = new UserInfo();

			Guid guid;
			do
			{
				guid = Guid.NewGuid();
			} while (!Users.TryAdd(guid, ui));
			ui.UserId = guid;

			return ui;
		}

		public static SessionInfo CreateSession()
		{
			var si = new SessionInfo();

			Guid guid;
			do
			{
				guid = Guid.NewGuid();
			} while (!Sessions.TryAdd(guid, si));
			si.SessionId = guid;

			return si;
		}
	}
}