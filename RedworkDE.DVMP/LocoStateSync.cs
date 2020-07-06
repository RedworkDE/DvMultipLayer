using RedworkDE.DVMP.Networking;

namespace RedworkDE.DVMP
{
	public class LocoStateSync : TrainCarSync, IPacketReceiver<LocoStateUpdatePacket>
	{
		protected LocoControllerBase _controller = null!;

		protected override void Init()
		{
			base.Init();

			_controller = GetComponent<LocoControllerBase>();
		}

		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<LocoStateUpdatePacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<LocoStateUpdatePacket>(this);
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new LocoStateUpdatePacket()));
		}
		
		public bool Receive(LocoStateUpdatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			_controller.brake = packet.Brake;
			_controller.independentBrake = packet.IndependentBrake;
			_controller.throttle = packet.Throttle;
			//_controller.reverser = packet.Reverser;

			return true;
		}

		protected LocoStateUpdatePacket PopulateState(LocoStateUpdatePacket state)
		{
			base.PopulateState(state);
			state.Brake = _controller.brake;
			state.IndependentBrake = _controller.independentBrake;
			//state.Reverser = _controller.reverser;
			state.Throttle = _controller.throttle;
			return state;
		}
	}

	public class LocoStateUpdatePacket : TrainCarStatePacket
	{
		public float Brake;
		public float IndependentBrake;
		//public float Reverser;
		public float Throttle;
	}
}