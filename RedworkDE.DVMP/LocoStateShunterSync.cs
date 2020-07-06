using System;
using RedworkDE.DVMP.Networking;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Synchronize Shunter specific input
	/// </summary>
	public class LocoStateShunterSync : LocoStateSync, IPacketReceiver<LocoShunterStateUpdatePacket>, IPacketReceiver<LocoShunterActionPacket>
	{
		private new LocoControllerShunter _controller = null!;
		private LocoShunterActionPacket _action = null!;

		protected override void Init()
		{
			base.Init();

			_controller = GetComponent<LocoControllerShunter>();
			_action = new LocoShunterActionPacket() {Id = Id};
		}

		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<LocoShunterStateUpdatePacket>(this);
			NetworkManager.RegisterReceiver<LocoShunterActionPacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<LocoShunterStateUpdatePacket>(this);
			NetworkManager.UnregisterReceiver<LocoShunterActionPacket>(this);
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new LocoShunterStateUpdatePacket()));
		}

		public override void Update()
		{
			base.Update();

			if (Authoritative || PlayerManager.Car == _controller.train)
				if (PopulateActionPacket(_action)) NetworkManager.Send(_action);
		}

		public bool Receive(LocoShunterStateUpdatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			return true;
		}

		protected LocoShunterStateUpdatePacket PopulateState(LocoShunterStateUpdatePacket state)
		{
			base.PopulateState(state);
			
			return state;
		}

		private bool PopulateActionPacket(LocoShunterActionPacket action)
		{
			var flags = ShunterStateFlags.None;

			flags |= _controller.GetEngineRunning() ? ShunterStateFlags.EngineOn : 0;
			flags |= _controller.GetSandersOn() ? ShunterStateFlags.SandOn : 0;
			flags |= _controller.backlight ? ShunterStateFlags.LightOn : 0;
			flags |= _controller.fan ? ShunterStateFlags.FanOn : 0;
			flags |= _controller.headlights.activeSelf ? ShunterStateFlags.HeadlightOn : 0;

			return SetStateValue(ref action.Flags, flags) |
			       SetStateValue(ref action.TargetThrottle, _controller.targetThrottle) |
			       SetStateValue(ref action.TargetBrake, _controller.targetBrake) |
			       SetStateValue(ref action.TargetIndependentBrake, _controller.targetIndependentBrake) |
				   SetStateValue(ref action.Reverser, _controller.reverser);
		}

		public bool Receive(LocoShunterActionPacket packet, ClientId client)
		{
			if (packet.Id != Id) return false;

			Logging<TrainCarSync>.LogDebug($"LocoShunterActionPacket: {packet.Flags} / {packet.TargetThrottle} / {packet.TargetBrake} / {packet.TargetIndependentBrake}");

			_controller.SetThrottle(packet.TargetThrottle);
			_controller.SetBrake(packet.TargetBrake);
			_controller.SetIndependentBrake(packet.TargetIndependentBrake);
			_controller.SetReverser(packet.Reverser);
			_controller.SetEngineRunning((packet.Flags & ShunterStateFlags.EngineOn) != 0);
			_controller.SetSandersOn((packet.Flags & ShunterStateFlags.SandOn) != 0);
			_controller.backlight = (packet.Flags & ShunterStateFlags.LightOn) != 0;
			_controller.fan = (packet.Flags & ShunterStateFlags.FanOn) != 0;
			_controller.headlights.SetActive((packet.Flags & ShunterStateFlags.HeadlightOn) != 0);

			return true;
		}
	}

	public class LocoShunterStateUpdatePacket : LocoStateUpdatePacket
	{

	}

	public class LocoShunterActionPacket : NetworkObjectStatePacket
	{
		public ShunterStateFlags Flags;
		public float TargetThrottle;
		public float TargetBrake;
		public float TargetIndependentBrake;
		public float Reverser;
	}

	[Flags]
	public enum ShunterStateFlags
	{
		None = 0,
		EngineOn = 0x01,
		SandOn = 0x02,
		LightOn = 0x04,
		FanOn = 0x08,
		HeadlightOn = 0x10,
	}
}