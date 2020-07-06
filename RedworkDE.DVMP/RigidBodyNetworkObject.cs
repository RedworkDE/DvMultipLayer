using RedworkDE.DVMP.Networking;
using UnityEngine;

namespace RedworkDE.DVMP
{
	public class RigidBodyNetworkObject : MovingNetworkObject, IPacketReceiver<RigidBodyNetworkObjectStatePacket>
	{
		private Rigidbody _rb = null!;

		protected override void Init()
		{
			base.Init();

			_rb = GetComponent<Rigidbody>();
		}

		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<RigidBodyNetworkObjectStatePacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<RigidBodyNetworkObjectStatePacket>(this);
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new RigidBodyNetworkObjectStatePacket()));
		}
		
		public RigidBodyNetworkObjectStatePacket PopulateState(RigidBodyNetworkObjectStatePacket state)
		{
			base.PopulateState(state);

			state.Velocity = _rb.velocity;
			state.AngularVelocity = _rb.angularVelocity;

			return state;
		}

		public bool Receive(RigidBodyNetworkObjectStatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			_rb.velocity = packet.Velocity;
			_rb.angularVelocity = packet.AngularVelocity;

			return true;
		}
	}

	public class RigidBodyNetworkObjectStatePacket : MovingNetworkObjectStatePacket
	{
		public Vector3 Velocity;
		public Vector3 AngularVelocity;
	}
}