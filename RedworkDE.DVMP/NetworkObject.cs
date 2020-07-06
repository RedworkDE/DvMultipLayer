using RedworkDE.DVMP.Networking;
using UnityEngine;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Base class for network synchronized objects
	/// </summary>
	public class NetworkObject : MonoBehaviour, IPacketReceiver<NetworkObjectStatePacket>
	{
		public MultiPlayerId Id { get; private set; }
		public bool Authoritative { get; protected set; }

		public virtual float UpdateRate => 0.1f;
		private float _lastUpdate;

		protected virtual void Init()
		{
			RegisterReceiver(true);
		}

		public void Init(MultiPlayerId id = default)
		{
			Authoritative = !id;
			Id = id ? id : MultiPlayerManager.Instance.NewId();

			Init();
		}

		public virtual void OnDestroy()
		{
			UnregisterReceiver();
		}

		public virtual void Update()
		{
			if (Authoritative)
			{
				if (_lastUpdate >= UpdateRate)
				{
					SendPacket();
					// ReSharper disable once CompareOfFloatsByEqualityOperator
					if (UpdateRate == 0)
						_lastUpdate = 0;
					else
						_lastUpdate -= UpdateRate;
				}

				_lastUpdate += Time.deltaTime;
			}
		}

		protected bool SetStateValueObject<T>(ref T store, T value) where T : class
		{
			if (Equals(store, value)) return false;

			store = value;
			return true;
		}

		protected unsafe bool SetStateValue<T>(ref T store, T value) where T : unmanaged
		{
			fixed (T* ptr = &store)
			{
				var a = (byte*) ptr;
				var b = (byte*)&value;

				for (int i = 0; i < sizeof(T); i++)
					if (a[i] != b[i])
						goto dostore;

				return false;
			}

			dostore:
			store = value;
			return true;
		}

		public NetworkObjectStatePacket PopulateState(NetworkObjectStatePacket state)
		{
			state.Id = Id;
			return state;
		}

		public virtual void RegisterReceiver(bool registerState = false)
		{
			if (registerState) NetworkManager.RegisterReceiver(this);
		}

		public virtual void UnregisterReceiver()
		{
			NetworkManager.UnregisterReceiver(this);
		}

		public virtual void SendPacket()
		{
			NetworkManager.Send(PopulateState(new NetworkObjectStatePacket()));
		}

		public bool Receive(NetworkObjectStatePacket packet, ClientId client)
		{
			return Id == packet.Id;
		}
	}


	public class NetworkObjectStatePacket : AutoPacket
	{
		public MultiPlayerId Id;

		
	}
}