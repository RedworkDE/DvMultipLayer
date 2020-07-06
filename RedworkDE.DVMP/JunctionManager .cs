using System.Linq;
using RedworkDE.DVMP.Networking;
using RedworkDE.DVMP.Utils;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Synchronizes junction states
	/// </summary>
	public class JunctionManager : AutoCreateMonoBehaviour<JunctionManager>, INotifyClientConnection, IPacketReceiver<JunctionStatePacket>, IPacketReceiver<JunctionSwitchedPacket>
	{
		private Junction[] _junctions = null!;

		void Awake()
		{
			MultiPlayerManager.Instance.OnMultiPlayerStarted += () =>
			{
				_junctions = FindObjectsOfType<Junction>();
				foreach (var junction in _junctions)
				{
					junction.gameObject.AddComponent<JunctionSync>();
				}

				if (MultiPlayerManager.Instance.HasJunctionAuthority)
				{
					NetworkManager.RegisterClientUpdates(this);
				}
			};

			NetworkManager.RegisterReceiver<JunctionStatePacket>(this);
			NetworkManager.RegisterReceiver<JunctionSwitchedPacket>(this);
		}

		void OnDestroy()
		{
			NetworkManager.RegisterReceiver<JunctionStatePacket>(this);
			NetworkManager.RegisterReceiver<JunctionSwitchedPacket>(this);
		}

		public void ClientConnected(ClientId client)
		{
			NetworkManager.Send(new JunctionStatePacket
			{
				JunctionState = _junctions.Select(j => j.selectedBranch).ToArray()
			});
		}

		public void ClientDisconnected(ClientId client)
		{
		}

		public bool Receive(JunctionStatePacket packet, ClientId client)
		{
			for (int i = 0; i < packet.JunctionState.Length; i++) SetJunction(ObjectId<Junction>.GetById(i), packet.JunctionState[i]);
			return true;
		}

		public bool Receive(JunctionSwitchedPacket packet, ClientId client)
		{
			return SetJunction(ObjectId<Junction>.GetById(packet.Junction), packet.SelectedBranch);
		}

		private bool SetJunction(Junction? junction, int branch)
		{
			if (junction is null) return false;
			if (branch < 0 || branch >= junction.outBranches.Count) return false;

			var sync = junction.GetComponent<JunctionSync>();
			if (sync) sync.SetSelected(branch);
			while (junction.selectedBranch != branch) junction.Switch(Junction.SwitchMode.FORCED);
			return true;
		}
	}

	/// <summary>
	/// Junction state change watcher
	/// </summary>
	public class JunctionSync : MonoBehaviour<JunctionSync>
	{
		private Junction _junction = null!;
		private int _lastSelected;
		private int _junctionId;

		void Awake()
		{
			_junction = GetComponent<Junction>();
			_junctionId = GetComponent<ObjectId>().Id;
			_lastSelected = _junction.selectedBranch;
		}

		void Update()
		{
			if (_junction.selectedBranch != _lastSelected)
			{
				NetworkManager.Send(new JunctionSwitchedPacket(){Junction = _junctionId, SelectedBranch = _junction.selectedBranch});
				_lastSelected = _junction.selectedBranch;
			}
		}

		internal void SetSelected(int selected) => _lastSelected = selected;
	}

	public class JunctionSwitchedPacket : AutoPacket
	{
		public int Junction;
		public int SelectedBranch;
	}

	public class JunctionStatePacket : AutoPacket
	{
		public int[] JunctionState = null!;
	}
}