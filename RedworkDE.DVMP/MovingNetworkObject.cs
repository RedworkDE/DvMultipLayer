using RedworkDE.DVMP.Networking;
using UnityEngine;

namespace RedworkDE.DVMP
{
	public class MovingNetworkObject : NetworkObject, IPacketReceiver<MovingNetworkObjectStatePacket>
	{
		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<MovingNetworkObjectStatePacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<MovingNetworkObjectStatePacket>(this);
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new MovingNetworkObjectStatePacket()));
		}

		public MovingNetworkObjectStatePacket PopulateState(MovingNetworkObjectStatePacket state)
		{
			base.PopulateState(state);

			state.Position = transform.position - WorldMover.currentMove;
			state.Rotation = transform.rotation;

			return state;
		}

		public bool Receive(MovingNetworkObjectStatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			transform.position = packet.Position + WorldMover.currentMove;
			transform.rotation = packet.Rotation;

			return true;
		}
	}

	public class MovingNetworkObjectStatePacket : NetworkObjectStatePacket
	{
		public Vector3 Position;
		public Quaternion Rotation;
	}
}