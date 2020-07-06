using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RedworkDE.DVMP.Utils
{
	/// <summary>
	/// Load ids for objects to provide a simple mapping of objects to simple numeric ids for serialization purposes
	/// This only works for objects that are always loaded, not for those streamed
	/// A GameObject can never hold two objects of different type, or the ids will get mixed up
	///
	/// Currently there are ids for <see cref="RailTrack"/> and <see cref="Junction"/>
	/// </summary>
	public class ObjectIdLoader : AutoLoad<ObjectIdLoader>
	{
		static ObjectIdLoader()
		{
			WorldStreamingInit.LoadingFinished += InitIds;

		}

		private static void InitIds()
		{
			ObjectId<RailTrack>.InitIds();
			ObjectId<Junction>.InitIds();
		}
	}

	/// <summary>
	/// Contains the id of an object
	/// </summary>
	public class ObjectId : MonoBehaviour
	{
		public int Id = -1;

		public void Init(int id)
		{
			Id = id;
		}
	}

	/// <summary>
	/// Lookup objects by their id
	/// </summary>
	public class ObjectId<T> : HasLogger<ObjectId<T>> where T : MonoBehaviour
	{
		private static List<T> _items = new List<T>();
		
		public static T? GetById(int id)
		{
			if (id < 0 || id >= _items.Count) return null;
			return _items[id];
		}

		internal static void InitIds()
		{
			_items.Clear();

			var items = Extensions.FindObjectsOfTypeAll<T>().ToList();

			Logger.LogInfo($"Found {items.Count} objects of type {typeof(T)}");

			foreach (var railTrack in items/*.OrderBy(rt => rt.transform.position.x).ThenBy(rt => rt.transform.position.y).ThenBy(rt => rt.transform.position.z)*/)
			{
				railTrack.gameObject.AddComponent<ObjectId>().Init(_items.Count);
				_items.Add(railTrack);
			}
		}
	}
}