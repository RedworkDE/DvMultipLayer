using System;
using System.IO;
using System.Security.Cryptography;

namespace RedworkDE.DVMP.Utils
{
	/// <summary>
	/// Unique identifier for this version of DVMP
	/// todo: This is both too strict and not strict enough to detect incompatible clients
	/// Different build of the same code can fail the check and the same version on different architectures can be incompatible
	/// </summary>
	public static class VersionHash
	{
		public static Guid Version;

		static VersionHash()
		{
			using var fs = new FileStream(typeof(VersionHash).Assembly.Location, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var md5 = MD5.Create();
			var hash = md5.ComputeHash(fs);
			Version = new Guid(hash);
		}
	}
}