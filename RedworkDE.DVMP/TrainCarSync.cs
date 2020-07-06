using System;
using DV.Logic.Job;
using HarmonyLib;
using RedworkDE.DVMP.Networking;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Synchronization of state shared by all train cars
	/// </summary>
	public class TrainCarSync : NetworkObject, IPacketReceiver<TrainCarStatePacket>, IPacketReceiver<CargoStateUpdatePacket>, IPacketReceiver<CoupleStateUpdatePacket>, IPacketReceiver<DerailedTrainCarPacket>
	{
		private const float COUPLER_UPDATE_DIST_SQ = 4;

		private CoupleStateUpdatePacket[] _coupleState = null!;
		private CargoStateUpdatePacket _cargoState= null!;
		private bool _derail;
		private TrainCar _car = null!;

		protected override void Init()
		{
			base.Init();

			_car = GetComponent<TrainCar>();

			if (Authoritative)
			{
				_cargoState = new CargoStateUpdatePacket {Id = Id};
			}
			else
			{
				GetComponent<DamageController>()?.SetupListeners(false);
				GetComponent<CarDamageModel>()?.SetupListeners(false);
				GetComponent<CargoDamageModel>()?.SetupListeners(false);
			}

			_coupleState = new CoupleStateUpdatePacket[_car.couplers.Length];
			for (var i = 0; i < _car.couplers.Length; i++)
			{
				_coupleState[i] = new CoupleStateUpdatePacket(){Id = Id};
				PopulateCoupleStatePacket(_coupleState[i], i);
			}
		}

		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<TrainCarStatePacket>(this);
			NetworkManager.RegisterReceiver<CargoStateUpdatePacket>(this);
			NetworkManager.RegisterReceiver<CoupleStateUpdatePacket>(this);
			NetworkManager.RegisterReceiver<DerailedTrainCarPacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<TrainCarStatePacket>(this);
			NetworkManager.UnregisterReceiver<CargoStateUpdatePacket>(this);
			NetworkManager.UnregisterReceiver<CoupleStateUpdatePacket>(this);
			NetworkManager.UnregisterReceiver<DerailedTrainCarPacket>(this);

		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new TrainCarStatePacket()));
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			CarSpawner.Instance.DeleteCar(gameObject);
		}

		public override void Update()
		{
			base.Update();

			if (Authoritative)
			{
				if (PopulateCargoStatePacket(_cargoState)) NetworkManager.Send(_cargoState);
			}

			for (int i = 0; i < _car.couplers.Length; i++)
			{
				if (Authoritative || (_car.couplers[i].transform.position - PlayerManager.PlayerTransform.position).sqrMagnitude < COUPLER_UPDATE_DIST_SQ)
				{
					if (PopulateCoupleStatePacket(_coupleState[i], i))
						NetworkManager.Send(_coupleState[i]);

				}
				else if (_car.couplers[i].springyCJ)
				{
					_car.couplers[i].springyCJ.breakForce = float.PositiveInfinity;
					_car.couplers[i].springyCJ.breakTorque = float.PositiveInfinity;
				}

			}
		}

		public TrainCarStatePacket PopulateState(TrainCarStatePacket state)
		{
			base.PopulateState(state);

			state.Velocity = _car.rb ? _car.GetForwardSpeed() : float.NaN;

			return state;
		}

		public bool Receive(TrainCarStatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			var carBogies = _car.Bogies;
			if (carBogies is null)
			{
				Logging<TrainCarSync>.LogError($"car {_car} has no bogies");
				return true;
			}

			if (carBogies.Length != 2)
			{
				Logging<TrainCarSync>.LogError($"number of bogies does not match data received from {client}");
			}

			return true;
		}

		private bool PopulateCargoStatePacket(CargoStateUpdatePacket state)
		{
			var logicCar = _car.logicCar;

			return SetStateValue(ref state.CargoType, logicCar.CurrentCargoTypeInCar) |
			       SetStateValue(ref state.LastUnloadedCargo, logicCar.LastUnloadedCargoType) |
			       SetStateValue(ref state.LoadedAmount, logicCar.LoadedCargoAmount);
		}
		
		public bool Receive(CargoStateUpdatePacket packet, ClientId client)
		{
			if (packet.Id != Id) return false;

			var logicCar = _car.logicCar;

			logicCar.LoadCargo(packet.LoadedAmount - logicCar.LoadedCargoAmount, packet.CargoType);
			logicCar.LoadedCargoAmount = packet.LoadedAmount;
			logicCar.CurrentCargoTypeInCar = packet.CargoType;
			logicCar.LastUnloadedCargoType = packet.LastUnloadedCargo;
			
			return true;
		}

		private bool PopulateCoupleStatePacket(CoupleStateUpdatePacket state, int couplerNum)
		{
			var coupler = _car.couplers[couplerNum];
			var otherCoupler = coupler.coupledTo ?? coupler.GetAirHoseConnectedTo();
			var otherCoupleNum = Array.IndexOf(otherCoupler?.train.couplers ?? Array.Empty<Coupler>(), otherCoupler);
			
			var flags = (CoupleStateFlags) couplerNum & CoupleStateFlags.OwnCouplerIdMask;
			flags |= (CoupleStateFlags) (otherCoupleNum << 4) & CoupleStateFlags.OtherCouplerIdMask;
			flags |= coupler.hoseAndCock.IsHoseConnected ? CoupleStateFlags.HoseConnected : 0;
			flags |= coupler.IsCockOpen ? CoupleStateFlags.CockOpen : 0;
			flags |= coupler.coupledTo ? CoupleStateFlags.ChainConnected : 0;

			return SetStateValue(ref state.OtherTrainCar, otherCoupler?.train.GetComponent<NetworkObject>().Id ?? default) |
				   SetStateValue(ref state.Flags, flags);
		}

		public bool Receive(CoupleStateUpdatePacket packet, ClientId client)
		{
			if (packet.Id != Id) return false;

			Logging<TrainCarSync>.LogDebug($"CoupleStateUpdatePacket: {packet.Flags & ~(CoupleStateFlags.OwnCouplerIdMask | CoupleStateFlags.OtherCouplerIdMask)} / {(int) (packet.Flags & CoupleStateFlags.OwnCouplerIdMask)} / {(int) (packet.Flags & CoupleStateFlags.OtherCouplerIdMask) >> 4} / {packet.OtherTrainCar}");

			var otherCar = packet.OtherTrainCar ? CarSpawner.Instance.allCars.Find(tc => tc.GetComponent<NetworkObject>()?.Id == packet.OtherTrainCar) : default;
			var otherCouplerNum = (int)(packet.Flags & CoupleStateFlags.OtherCouplerIdMask) >> 4;
			var otherCoupler = otherCar?.couplers[otherCouplerNum];
			var couplerNum = (int)(packet.Flags & CoupleStateFlags.OwnCouplerIdMask);
			var coupler = _car.couplers[couplerNum];

			if (otherCoupler && (packet.Flags & CoupleStateFlags.ChainConnected) != 0)
				coupler.CoupleTo(otherCoupler, viaChainInteraction: true);
			else
				coupler.Uncouple(viaChainInteraction: true);

			if (otherCoupler && (packet.Flags & CoupleStateFlags.HoseConnected) != 0)
				coupler.ConnectAirHose(otherCoupler, true);
			else
				coupler.DisconnectAirHose(true);

			coupler.IsCockOpen = (packet.Flags & CoupleStateFlags.CockOpen) != 0;

			return true;
		}

		[HarmonyPatch(typeof(TrainStress), nameof(TrainStress.Derail))]
		[HarmonyPrefix]
		public static bool TrainStress_Derail_Patch(TrainStress __instance)
		{
			Logging<TrainCarSync>.LogInfo($"Derailing car {__instance} / {__instance.GetComponent<TrainCarSync>()?.Id}");

			var sync = __instance.GetComponent<TrainCarSync>();
			if (!sync)
			{
				Logging<TrainCarSync>.LogDebug($"allowing, no sync");
				return true;
			}

			if (!sync.Authoritative)
			{
				if (sync._derail)
				{
					Logging<TrainCarSync>.LogDebug($"allowing, authoritative derail");
					return true;
				}
				Logging<TrainCarSync>.LogDebug($"suppressing, no authoritative derail");
				return false;
			}


			Logging<TrainCarSync>.LogDebug($"allowing, authority");
			NetworkManager.Send(new DerailedTrainCarPacket() {Id = sync.Id});
			return true;
		}


		public bool Receive(DerailedTrainCarPacket packet, ClientId client)
		{
			if (packet.Id != Id) return false;

			_derail = true;
			GetComponent<TrainStress>().Derail("multiplayer authority");
			_derail = false;

			return true;
		}
	}
	
	public class TrainCarStatePacket : NetworkObjectStatePacket
	{
		public float Velocity;
	}

	public class CargoStateUpdatePacket : NetworkObjectStatePacket
	{
		public CargoType CargoType;
		public float LoadedAmount;
		public CargoType LastUnloadedCargo;
	}

	public class CoupleStateUpdatePacket : NetworkObjectStatePacket
	{
		public MultiPlayerId OtherTrainCar;
		public CoupleStateFlags Flags;
	}

	[Flags]
	public enum CoupleStateFlags
	{
		OwnCouplerIdMask = 0x0f,
		OtherCouplerIdMask = 0xf0,
		ChainConnected = 0x100,
		Tightened = 0x200,
		HoseConnected = 0x400,
		CockOpen = 0x800,
	}

	public class DerailedTrainCarPacket : NetworkObjectStatePacket
	{

	}
}