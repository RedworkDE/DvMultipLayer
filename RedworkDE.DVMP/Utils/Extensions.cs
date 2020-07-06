using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine.SceneManagement;

namespace RedworkDE.DVMP.Utils
{
	public static class Extensions
	{
		/// <summary>
		/// Find all objects of a type, including on inactive objects
		/// </summary>
		public static IEnumerable<T> FindObjectsOfTypeAll<T>()
		{
			return SceneManager.GetActiveScene().GetRootGameObjects()
				.SelectMany(g => g.GetComponentsInChildren<T>(true));
		}

		public static string ToHexString(this Span<byte> data)
		{
			const string alphabet = "0123456789ABCDEF";

			var sb = new StringBuilder(data.Length * 3);
			for (int i = 0; i < data.Length; i++)
			{
				sb.Append(alphabet[data[i] & 0xf]);
				sb.Append(alphabet[data[i] >> 4]);
				sb.Append(' ');
			}

			return sb.ToString();
		}

		/// <summary>
		/// Removes <paramref name="strip"/> from the end of <paramref name="str"/> if <paramref name="str"/> ends with it
		/// </summary>
		public static string? StripEnd(this string? str, string strip)
		{
			if (str?.EndsWith(strip) ?? false) return str!.Substring(0, str.Length - strip.Length);
			return str;
		}

		/// <summary>
		/// Removes <paramref name="strip"/> from the start of <paramref name="str"/> if <paramref name="str"/> starts with it
		/// </summary>
		public static string? StripStart(this string? str, string strip)
		{
			if (str?.StartsWith(strip) ?? false) return str!.Substring(strip.Length);
			return str;
		}

		/// <summary>
		/// Distance between the pointers of both spans
		/// </summary>
		/// <param name="a"></param>
		/// <param name="b"></param>
		/// <returns></returns>
		public static int DistanceTo(this Span<byte> a, Span<byte> b)
		{
			unsafe
			{
				fixed (byte* ptrA = &a[0])
				fixed (byte* ptrB = &b[0])
					return checked((int) (ptrB - ptrA));
			}
		}

		// Serialization Helpers, simply copies items byte by byte
		// this will fail if clients use different number formats
		// references to these methods will be generated for AutoPackets by PacketWeaver

		public delegate void ReadItem<T>(ref Span<byte> data, out T value);
		public delegate void WriteItem<T>(ref Span<byte> data, in T value);

		public static void Read<T>(this ref Span<byte> data, out T target) where T : unmanaged
		{
			unsafe
			{
				fixed (byte* ptr = &data[0])
					target = *(T*) ptr;
				data = data.Slice(sizeof(T));
			}
		}
		
		public static void ReadA(this ref Span<byte> data, out string value)
		{
			data.Read(out ushort length);
			if (data.Length < length) throw new InvalidOperationException();
			unsafe
			{
				fixed (byte* ptr = &data[0])
					value = Encoding.ASCII.GetString(ptr, length);
			}
			data = data.Slice(length);
		}
		
		public static void ReadW(this ref Span<byte> data, out string value)
		{
			data.Read(out ushort length);
			if (data.Length < length*2) throw new InvalidOperationException();
			unsafe
			{
				fixed (byte* ptr = &data[0])
					value = new string((char*) ptr, 0, length);
			}
			data = data.Slice(length * 2);
		}

		public static void ReadArray<T>(this ref Span<byte> data, out T[] items) where T : unmanaged
			=> ReadArray(ref data, out items, Read);

		public static void ReadArray<T>(this ref Span<byte> data, out T[] items, ReadItem<T> readItem)
		{
			data.Read(out ushort length);
			var arr = new T[length];
			for (int i = 0; i < length; i++) readItem(ref data, out arr[i]);
			items = arr;
		}

		public static Span<byte> Reserve<T>(this ref Span<byte> data) where T:unmanaged
		{
			unsafe
			{
				var result = data.Slice(0, sizeof(T));
				data = data.Slice(sizeof(T));
				return result;
			}
		}

		public static Span<byte> Reserve(this ref Span<byte> data, int length)
		{
			var result = data.Slice(0, length);
			data = data.Slice(length);
			return result;
		}

		public static void Write<T>(this ref Span<byte> data, in T target) where T : unmanaged
		{
			unsafe
			{
				fixed (byte* ptr = &data[0])
					*(T*)ptr = target;
				data = data.Slice(sizeof(T));
			}
		}

		public static void WriteA(this ref Span<byte> data, in string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				data.Write((ushort)0);
				return;
			}

			var length = (ushort) value.Length;
			data.Write(length);
			if (data.Length < length) throw new InvalidOperationException();
			unsafe
			{
				fixed (byte* ptr = &data[0])
				fixed (char* cha = value)
					Encoding.ASCII.GetBytes(cha, value.Length, ptr, length);
			}
			data = data.Slice(length);
		}

		public static void WriteW(this ref Span<byte> data, in string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				data.Write((ushort)0);
				return;
			}

			var length = (ushort)value.Length;
			data.Write(length);
			unsafe
			{
				fixed (byte* ptr = &data[0])
				fixed (char* cha = value)
					Buffer.MemoryCopy(cha, ptr, data.Length, length * 2);
			}
			data = data.Slice(length * 2);
		}

		public static void WriteArray<T>(this ref Span<byte> data, in T[] items) where T : unmanaged
			=> WriteArray(ref data, items, Write);

		public static void WriteArray<T>(this ref Span<byte> data, in T[] items, WriteItem<T> writeItem)
		{
			data.Write((ushort)items.Length);
			for (int i = 0; i < items.Length; i++) writeItem(ref data, items[i]);
		}

		public static int SizeA(this string? str) => str?.Length ?? 0 + 2;
		public static int SizeW(this string? str) => (str?.Length ?? 0) * 2 + 2;

		public static unsafe int Size<T>(this T type) where T : unmanaged => sizeof(T);
		public static unsafe int SizeArray<T>(this T[] arr) where T : unmanaged => sizeof(T) * arr.Length + 2;
		public static int SizeArray<T>(this T[] arr, int elementSize) => elementSize * arr.Length + 2;
	}
}