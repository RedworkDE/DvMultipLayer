using System;
using System.Threading;

namespace RedworkDE.DVMP.Networking
{
	/// <summary>
	/// Base interface for sendable packets
	/// </summary>
	public interface IPacket
	{
		public int MaxSize { get; }
		void ParseData(ref Span<byte> data);
		void SerializeData(ref Span<byte> data);
	}

	/// <summary>
	/// Packets that (indirectly) inherit from this type will have their IPacket interface implemented automatically
	/// Supported field types: unmanaged structs, szarrays of unamanged structs and strings
	/// String fields can specify if they should be serialized as Utf16 or ascii
	/// </summary>
	public abstract class AutoPacket : IPacket
	{
		public int MaxSize => -1;
		public void ParseData(ref Span<byte> data)
		{
		}

		public void SerializeData(ref Span<byte> data)
		{
		}
	}

	// ReSharper disable once TypeParameterCanBeVariant // no it cant be it breaks things
	/// <summary>
	/// Implement to be able to receive packets of type <typeparamref name="T"/>
	/// It is possible to implement this interface multiple times for different types
	/// </summary>
	public interface IPacketReceiver<T> where T : IPacket
	{
		bool Receive(T packet, ClientId client);
	}


	public sealed class DefaultPacketReceiver<T> : IPacketReceiver<T> where T : IPacket
	{
		private readonly Func<T, ClientId, bool> _handler;

		public DefaultPacketReceiver(Func<T, ClientId, bool> handler)
		{
			_handler = handler;
		}

		public bool Receive(T packet, ClientId client)
		{
			return _handler(packet, client);
		}
	}

	public struct PacketType : IEquatable<PacketType>
	{
		public bool Equals(PacketType other)
		{
			return __value == other.__value;
		}

		public override bool Equals(object? obj)
		{
			return obj is PacketType other && Equals(other);
		}

		public override int GetHashCode()
		{
			return __value.GetHashCode();
		}

		public static bool operator ==(PacketType left, PacketType right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(PacketType left, PacketType right)
		{
			return !left.Equals(right);
		}

		public PacketType(ushort value)
		{
			__value = value;
		}

		private ushort __value;

		public override string ToString()
		{
			return __value.ToString("x4");
		}

		private static int _nextValue;
		
		internal static PacketType AllocatePacketType() => new PacketType(checked((ushort)Interlocked.Increment(ref _nextValue)));
		public static PacketType Get<T>() => Cache<T>.Type;
		public static PacketType GetByType(Type type) => (PacketType) typeof(PacketType).GetMethod(nameof(Get)).MakeGenericMethod(type).Invoke(null, null);

		// ReSharper disable once UnusedTypeParameter
		private struct Cache<T>
		{
			// ReSharper disable once StaticMemberInGenericType
			public static readonly PacketType Type = AllocatePacketType();
		}
	}

	
}