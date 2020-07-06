using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using RedworkDE.DVMP.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Manage spawning of train cars
	/// </summary>
	public class TrainCarSpawnManager : AutoCreateMonoBehaviour<TrainCarSpawnManager>, IPacketReceiver<TrainCarInformationPacket>, IPacketReceiver<DestroyTrainCarPacket>, IPacketReceiver<TrainSetInformationPacket>, INotifyClientConnection
	{
		private readonly List<TrainCar> _ownedCars = new List<TrainCar>();
		private bool _spawningCar = false;

		void Awake()
		{
			CarSpawner.CarSpawned += CarSpawned;
			
			NetworkManager.RegisterReceiver<TrainCarInformationPacket>(this);
			NetworkManager.RegisterReceiver<TrainSetInformationPacket>(this);
			NetworkManager.RegisterReceiver<DestroyTrainCarPacket>(this);

			MultiPlayerManager.Instance.OnMultiPlayerStarted += () =>
			{
				//if (!MultiPlayerManager.Instance.HasAuthority)
				//{
					SaveGameManager.data = null;
					foreach (var controller in Object.FindObjectsOfType<StationProceduralJobsController>())
					foreach (var chain in controller.jobChainControllers)
						chain.DestroyChain();
					CarSpawner.Instance.allCars.ToList().ForEach(CarSpawner.DeleteCar);
				//}
				//else
				//{
				//	//_ownedCars.AddRange(CarSpawner.Instance.allCars);
				//}

				
			};
		}

		[HarmonyPatch(typeof(StationProceduralJobsController), nameof(StationProceduralJobsController.TryToGenerateJobs))]
		[HarmonyPrefix]
		public static bool StationProceduralJobsController_TryToGenerateJobs_Patch()
		{
			return false;
		}

		[HarmonyPatch(typeof(StationLocoSpawner), nameof(StationLocoSpawner.Update))]
		[HarmonyPrefix]
		public static bool StationLocoSpawner_Update_Patch()
		{
			return false;
		}

		void OnDestroy()
		{
			CarSpawner.CarSpawned -= CarSpawned;

			NetworkManager.UnregisterReceiver<TrainCarInformationPacket>(this);
			NetworkManager.UnregisterReceiver<TrainSetInformationPacket>(this);
			NetworkManager.UnregisterReceiver<DestroyTrainCarPacket>(this);
		}

		private void CarSpawned(TrainCar car)
		{
			if (_spawningCar) return;
			if (!MultiPlayerManager.Instance.IsMultiPlayer) return;

			Logger.LogInfo($"Spawned {car} at {car.transform.position - WorldMover.currentMove} moved by {WorldMover.currentMove}");

			if (car.GetComponent<LocoControllerShunter>()) car.gameObject.AddComponent<LocoStateShunterSync>().Init();
			//else if (car.GetComponent<LocoControllerDiesel>()) car.gameObject.AddComponent<LocoStateDieselSync>().Init();
			//else if (car.GetComponent<LocoControllerSteam>()) car.gameObject.AddComponent<LocoStateSteamSync>().Init();
			//else if (car.GetComponent<LocoControllerHandcar>()) car.gameObject.AddComponent<LocoStateHandcarSync>().Init();
			else if (car.GetComponent<LocoControllerBase>()) 
			{
				CarSpawner.DeleteCar(car);
				return;
				//car.gameObject.AddComponent<LocoStateSync>().Init();
			}
			else car.gameObject.AddComponent<TrainCarSync>().Init();

			_ownedCars.Add(car);
			SendCarInformation(car, default);
			SendSetInformation(TrainSetSync.CreateLocal(car._trainset), default);
		}
		
		public CarSpawnDisable SpawningCar => new CarSpawnDisable(this);

		public struct CarSpawnDisable : IDisposable
		{
			private TrainCarSpawnManager _tcsm;

			public CarSpawnDisable(TrainCarSpawnManager tcsm)
			{
				_tcsm = tcsm;
				tcsm._spawningCar = true;
			}

			public void Dispose()
			{
				_tcsm._spawningCar = false;
				_tcsm = null!;
			}
		}

		public bool Receive(TrainCarInformationPacket packet, ClientId client)
		{
			var player = MultiPlayerManager.Instance.RemotePlayers[client];
			Logger.LogInfo($"Spawning remote car {packet.CarType} at {packet.Position} moved by {WorldMover.currentMove} for {player}");

			var prefab = CarTypes.GetCarPrefab(packet.CarType);
			var (rail, _) = RailTrack.GetClosest(packet.Position + WorldMover.currentMove);
			TrainCar train;
			using (SpawningCar)
				train = CarSpawner.SpawnCar(prefab, rail, packet.Position + WorldMover.currentMove, packet.Forward);

			if (train.GetComponent<LocoControllerShunter>()) train.gameObject.AddComponent<LocoStateShunterSync>().Init(packet.Id);
			//else if (train.GetComponent<LocoControllerDiesel>()) train.gameObject.AddComponent<LocoStateDieselSync>().Init(packet.Id);
			//else if (train.GetComponent<LocoControllerSteam>()) train.gameObject.AddComponent<LocoStateHandcarSync>().Init(packet.Id);
			//else if (train.GetComponent<LocoControllerHandcar>()) train.gameObject.AddComponent<LocoStateHandcarSync>().Init(packet.Id);
			//else if (train.GetComponent<LocoControllerBase>()) train.gameObject.AddComponent<LocoStateSync>().Init(packet.Id);
			else train.gameObject.AddComponent<TrainCarSync>().Init(packet.Id);

			train.logicCar.ID = packet.Name;

			return true;
		}

		public bool Receive(TrainSetInformationPacket packet, ClientId client)
		{
			Logger.LogInfo($"Receive set: {string.Join(", ", packet.Cars)}");

			var cars = packet.Cars.Select(id => CarSpawner.Instance.allCars.FirstOrDefault(c => c.GetComponent<NetworkObject>().Id == id)).ToList();

			TrainSetSync.CreateRemote(cars, packet.Id);
			return true;
		}

		public bool Receive(DestroyTrainCarPacket packet, ClientId client)
		{
			foreach (var car in CarSpawner.Instance.allCars)
				if (car.GetComponent<NetworkObject>().Id == packet.Id)
				{
					CarSpawner.DeleteCar(car);
					_ownedCars.Remove(car);
				}

			return true;
		}

		public void ClientConnected(ClientId client)
		{
			foreach (var car in _ownedCars)
				SendCarInformation(car, client);
		}

		public void ClientDisconnected(ClientId client)
		{
		}
		
		private void SendCarInformation(TrainCar car, ClientId client)
		{
			var id = car.GetComponent<NetworkObject>();

			NetworkManager.Send(new TrainCarInformationPacket()
			{
				Id = id.Id,
				CarType = car.carType,
				Position = car.transform.position - WorldMover.currentMove,
				Forward = car.transform.rotation * Vector3.forward,
				Name = car.logicCar.ID,
			}, client);
		}

		private void SendSetInformation(TrainSetSync set, ClientId client)
		{
			NetworkManager.Send(set.Info() , client);
		}
	}

	public class TrainCarInformationPacket : NetworkObjectStatePacket
	{
		public TrainCarType CarType;
		public Vector3 Position;
		public Vector3 Forward;
		[MarshalAs(UnmanagedType.AnsiBStr)]
		public string Name = "";
	}

	public class TrainSetInformationPacket : NetworkObjectStatePacket
	{
		public MultiPlayerId[] Cars = null!;
	}


	public class DestroyTrainCarPacket : NetworkObjectStatePacket
	{
	}
}