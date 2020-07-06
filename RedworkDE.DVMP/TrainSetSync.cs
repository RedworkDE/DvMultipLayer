using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RedworkDE.DVMP.Networking;
using RedworkDE.DVMP.Utils;
using UnityEngine;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Fix positions of entire trainsets when they deviate too much
	/// </summary>
	public class TrainSetSync : NetworkObject, IPacketReceiver<TrainSetStatePacket>
	{
		private const double TELEPORT_THRESHOLD = 20;

		private static readonly GameObject _container = new GameObject();
		private static readonly Dictionary<Trainset, TrainSetSync> _syncs = new Dictionary<Trainset, TrainSetSync>();

		private Trainset _trainset = null!;
		private double[] _offsets = null!;
		private double[] _dampVel = null!;
		private double[] _trackPosition = null!;
		private float[] _vels = null!;
		private RailTrack[] _tracks = null!;
		private Vector3[] _bogiePos = null!;

		private BogiePosition[] _bogiePosition = null!;

		private Coroutine? _coro;

		public override float UpdateRate => 1;

		public void Init(Trainset set, MultiPlayerId id, List<TrainCar>? carOrder)
		{
			_syncs.Add(set, this);

			_trainset = set;

			if (Authoritative)
			{
				_bogiePosition = new BogiePosition[set.cars.Sum(c => c.Bogies.Length)];
			}
			else
			{
				_offsets = new double[set.cars.Sum(c => c.Bogies.Length)];
				_trackPosition = new double[_offsets.Length];
				_dampVel = new double[_offsets.Length];
				_vels = new float[_offsets.Length];
				_tracks = new RailTrack[_offsets.Length];
				_bogiePos = new Vector3[set.cars.Max(c => c.Bogies.Length)];
			}

			if (carOrder is {})
			{
				for (int i = 0; i < carOrder.Count; i++)
				{
					if (carOrder[i].indexInTrainset != i)
					{
						Logging<TrainSetSync>.LogInfo($"trainset in wrong order: {carOrder[i]} is at index {carOrder[i].indexInTrainset} but should be at {i}");
					}
					carOrder[i].indexInTrainset = i;
				}
				set.cars.Clear();
				set.cars.AddRange(carOrder);
				set.locoIndices.Clear();
				for (int index = 0; index < carOrder.Count; ++index)
				{
					if (carOrder[index].IsLoco)
						set.locoIndices.Add(index);
				}
				set.firstCar = carOrder[0];
				set.lastCar = carOrder[carOrder.Count - 1];
			}

			base.Init(id);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			_syncs.Remove(_trainset);

			Logging<TrainSetSync>.LogInfo($"Destroying TrainsSetSync, from cars {string.Join(", ", _trainset.cars.Select(c => c.GetComponent<NetworkObject>()?.Id))}");
		}


		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<TrainSetStatePacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<TrainSetStatePacket>(this);
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new TrainSetStatePacket()));
		}

		public TrainSetStatePacket PopulateState(TrainSetStatePacket state)
		{
			base.PopulateState(state);

			// not sure why this fail to be set property
			_bogiePosition ??= new BogiePosition[_trainset.cars.Sum(c => c.Bogies.Length)];

			int i = -1;
			foreach (var car in _trainset.cars)
			{
				foreach (var bogie in car.Bogies)
				{
					i++;

					_bogiePosition[i].Track = bogie.track?.GetComponent<ObjectId>()?.Id ?? -1;
					_bogiePosition[i].TrackPosition = bogie.traveller.Span;
					_bogiePosition[i].Velocity = bogie.rb?.velocity.magnitude ?? 0;
				}
			}

			state.Bogies = _bogiePosition;
			
			return state;
		}

		public bool Receive(TrainSetStatePacket packet, ClientId client)
		{
			if (!base.Receive(packet, client)) return false;

			if (_coro is {}) StopCoroutine(_coro);

			int i = -1;
			foreach (var car in _trainset.cars)
			{
				foreach (var bogie in car.Bogies)
				{
					i++;

					var pos = packet.Bogies[i];

					var track = ObjectId<RailTrack>.GetById(pos.Track);
					if (!track || track is null)
					{
						// derailed no sync

						_offsets[i] = double.NaN;
						continue;
					}

					_tracks[i] = track;
					_trackPosition[i] = pos.TrackPosition;
					_vels[i] = pos.Velocity;

					if (track != bogie.track)
					{
						if (bogie.track.inBranch?.track == track)
						{
							var dist = -bogie.traveller.Span;
							if (bogie.track.inBranch.first) _offsets[i] = dist - pos.TrackPosition;
							else _offsets[i] = dist - bogie.track.inBranch.track.GetPointSet().span + pos.TrackPosition;
						}
						else if (AdjacentToJunction(bogie.track.inJunction, track, out var length))
						{
							var dist = -bogie.traveller.Span;
							if (length == 0) _offsets[i] = dist - pos.TrackPosition;
							else _offsets[i] = dist - length + pos.TrackPosition;
						}
						else if (bogie.track.outBranch?.track == track)
						{
							var dist = bogie.track.GetPointSet().span - bogie.traveller.Span;
							if (bogie.track.outBranch.first) _offsets[i] = dist + pos.TrackPosition;
							else _offsets[i] = dist + bogie.track.outBranch.track.GetPointSet().span - pos.TrackPosition;
						}
						else if (AdjacentToJunction(bogie.track.outJunction, track, out length))
						{
							var dist = bogie.track.GetPointSet().span - bogie.traveller.Span;
							if (length == 0) _offsets[i] = dist + pos.TrackPosition;
							else _offsets[i] = dist + length - pos.TrackPosition;
						}
						else
						{
							// bogie went down the wrong branch of a junction
							// (or skipped over a track entirely
							_offsets[i] = double.PositiveInfinity;
						}

						//Logging<TrainSetSync>.LogDebug($"{i - 1}: settrack for {pos.Track}({track}) at {pos.TrackPosition}");
						//bogie.SetTrack(track, pos.TrackPosition);
					}
					else
					{
						_offsets[i] = pos.TrackPosition - bogie.traveller.Span;

					}

					//Logging<TrainSetSync>.LogDebug($"should be: {track}@{pos.TrackPosition} is {bogie.track}@{bogie.traveller.Span}, delta: {_offsets[i]}");
				}
			}

			//Logging<TrainSetSync>.LogDebug($"offsets: {string.Join(",", _offsets)}");

			_coro = StartCoroutine(AdjustTrain());
			return true;
		}

		private bool AdjacentToJunction(Junction junction, RailTrack track, out double length)
		{
			length = double.NaN;
			if (!junction) return false;

			if (junction.inBranch?.track == track)
			{
				if (junction.inBranch.first) length = 0;
				else length = junction.inBranch.track.GetPointSet().span;
				return true;
			}
			foreach (var branch in junction.outBranches)
			{
				if (branch.track == track)
				{
					if (branch.first) length = 0;
					else length = branch.track.GetPointSet().span;
					return true;
				}
			}

			return false;
		}

		private IEnumerator AdjustTrain()
		{
			var max = _offsets.Max(Mathd.Abs);
			var vmax = _vels.Max();

			if (max <= 0.1) yield break;

			var teleport = vmax > 20 ? max > 10 :
				           vmax > 5 ? max > 5 :
						   vmax > 1 ? max > 1 :
						   false;

			if (teleport)
			{
				Logging<TrainSetSync>.LogWarning($"Excessive desync ({(double.IsInfinity(max) ? "wrong track" : max + "m")}), teleporting train");
			}
			else if (vmax < 0.5)
			{
				Logging<TrainSetSync>.LogWarning($"Desync {max}m, adjusting train");
			}
			else
			{
				// gradual resync fucks the train speed, only do it when stopped
				yield break;
			}

			while (_offsets.Any(o => Mathd.Abs(o) > 0.1))
			{
				yield return WaitFor.FixedUpdate;

				int i = -1;
				foreach (var car in _trainset.cars)
				{
					var offsetBefore = Vector3.Lerp(car.Bogies[0].transform.position, car.Bogies[car.Bogies.Length - 1].transform.position, 0.5f);

					var tmp = i;

					for (var j = 0; j < car.Bogies.Length; j++)
					{
						var bogie = car.Bogies[j];
						i++;

						if (bogie.HasDerailed || double.IsNaN(_offsets[i])) continue; // no actual position, or derailed => skip


						if (teleport)
						{
							_offsets[i] = 0;
							bogie.ResetBogiesToStartPosition();
							bogie.SetTrack(_tracks[i], _trackPosition[i]);
						}
						else
						{
							var v = Mathd.Max(bogie.rb.velocity.magnitude * 0.1, 0.5);
							//var delta = Mathd.Min(_offsets[i], v * Time.deltaTime * Mathd.Sign(_offsets[i]));
							//_offsets[i] -= delta;
							var c = Mathd.SmoothDamp(_offsets[i], 0, ref _dampVel[i], UpdateRate, v);
							var delta = _offsets[i] - c;
							_offsets[i] = c;

							double num2 = bogie.traveller.Span + (double) delta;
							double span = bogie.track.GetPointSet().span;

							if (num2 >= 0.0 && num2 < span)
							{
								bogie.traveller.Travel(delta);
							}
							else
							{
								bool first = num2 < 0.0;
								Junction.Branch branch = first ? bogie.track.GetInBranch() : bogie.track.GetOutBranch();
								if (branch == null || branch.track == null)
								{
									bogie.Derail("Reached end of track");
									continue;
								}

								RailTrack track = branch.track;
								bogie.SwitchJunctionIfNeeded(bogie.track, first);
								bogie.SetTrack(track, branch.first);
								bogie.traveller.Travel(Mathd.Abs(first ? num2 : num2 - span) * (branch.first ? 1.0 : -1.0));
							}
						}

						bogie.point1 = bogie.traveller.curPoint;
						bogie.point2 = bogie.traveller.pointSet.points[bogie.traveller.curPoint.index + 1];

						//bogie.rb.velocity = Vector3.Project(bogie.rb.velocity, (Vector3) (bogie.point2.position - bogie.point1.position));
						//bogie.rb.velocity = bogie.rb.velocity.magnitude * ((Vector3) (bogie.point2.position - bogie.point1.position)).normalized;
						if (Bogie.useMicrobumps)
							_bogiePos[j] = (Vector3) bogie.traveller.worldPosition + bogie.GetMicrobumpOffset() * bogie.simManager.microbumpScale + WorldMover.currentMove;
						else
							_bogiePos[j] = (Vector3) bogie.traveller.worldPosition + WorldMover.currentMove;
					}

					if (true||teleport)
					{
						var sum = _vels[tmp + 1];
						for (int j = tmp + 2; j <= i; j++) sum += _vels[j];

						car.SetForwardSpeed(sum / car.Bogies.Length);
					}
					car.transform.position += Vector3.Lerp(_bogiePos[0], _bogiePos[car.Bogies.Length - 1], 0.5f) - offsetBefore;
					car.transform.rotation = Quaternion.LookRotation(_bogiePos[car.Bogies.Length - 1] - _bogiePos[0]);

					for (var j = 0; j < car.Bogies.Length; j++)
					{
						var bogie = car.Bogies[j];
						bogie.transform.position = _bogiePos[j];
						if (bogie.constrainNodeUp)
						{
							bogie.rb.angularVelocity = Vector3.zero;
							bogie.UpdateRotation();
						}
					}
				}
			}
		}
		
		public static TrainSetSync CreateLocal(Trainset trainset)
		{
			Logging<TrainSetSync>.LogInfo($"Create Local TrainsSetSync, from cars {string.Join(", ", trainset.cars.Select(c => c.GetComponent<NetworkObject>()?.Id))}");

			var set = _container.AddComponent<TrainSetSync>();
			set.Init(trainset, default, null);
			return set;
		}
		
		public static void CreateRemote(List<TrainCar> car, MultiPlayerId id)
		{
			Logging<TrainSetSync>.LogInfo($"Create Remote TrainsSetSync, from cars {string.Join(", ", car.Select(c => c.GetComponent<NetworkObject>()?.Id))}");

			var wait = WaitForCouple(car, id);

			// only create a coroutine if the action has to wait
			if (wait.MoveNext()) CoroutineManager.Instance.StartCoroutine(wait);
		}

		private static IEnumerator WaitForCouple(List<TrainCar> cars, MultiPlayerId id)
		{
			nextCheck:
			var set = cars[0].trainset;

			// if there some cars have a different trainset, they have not yet been coupled locally
			for (int i = 1; i < cars.Count; i++)
			{
				if (set != cars[i].trainset)
				{
					yield return null;
					goto nextCheck;
				}
			}

			// if there are more cars in the set than what we got, they have not been uncouple locally
			if (set.cars.Count != cars.Count)
			{
				yield return null;
				goto nextCheck;
			}

			// local and remote sets are synchronized
			_container.AddComponent<TrainSetSync>().Init(set, id, cars);
		}
		
		public TrainSetInformationPacket Info()
		{
			var info = new TrainSetInformationPacket()
			{
				Id = Id,
				Cars = _trainset.cars.Select(c => c.GetComponent<NetworkObject>().Id).ToArray(),
			};


			Logging<TrainSetSync>.LogInfo($"Send set: {string.Join(", ", info.Cars)}");

			return info;
		}

		[HarmonyPatch(typeof(Trainset), nameof(Trainset.Merge))]
		[HarmonyPrefix]
		public static void Trainset_Merge_Prefix(TrainCar a, TrainCar b, ref bool __state)
		{
			if (_syncs.TryGetValue(a.trainset, out var sa)) Destroy(sa);
			if (_syncs.TryGetValue(b.trainset, out var sb)) Destroy(sb);

			__state = sa && sb ? (sa.Id < sb.Id ? sa.Authoritative : sb.Authoritative) : sa ? sa.Authoritative : sb && sb.Authoritative;
		}


		[HarmonyPatch(typeof(Trainset), nameof(Trainset.Merge))]
		[HarmonyPostfix]
		public static void Trainset_Merge_Postfix(bool __state)
		{
			CheckCreateSet(Trainset.allSets[Trainset.allSets.Count - 1], __state);
		}

		[HarmonyPatch(typeof(Trainset), nameof(Trainset.Split))]
		[HarmonyPrefix]
		public static void Trainset_Split_Prefix(TrainCar carA, TrainCar carB, ref bool __state)
		{
			if (_syncs.TryGetValue(carA.trainset, out var sa)) Destroy(sa);
			if (_syncs.TryGetValue(carB.trainset, out var sb)) Destroy(sb);

			__state = sa ? sa.Authoritative : sb && sb.Authoritative;
		}

		[HarmonyPatch(typeof(Trainset), nameof(Trainset.Split))]
		[HarmonyPostfix]
		public static void Trainset_Split_Postfix(bool __state)
		{
			CheckCreateSet(Trainset.allSets[Trainset.allSets.Count - 2], __state);
			CheckCreateSet(Trainset.allSets[Trainset.allSets.Count - 1], __state);
		}

		private static void CheckCreateSet(Trainset set, bool auth)
		{
			if (!MultiPlayerManager.Instance.IsBeforeMultiPlayer) return;

			if (auth)
			{
				var sync = CreateLocal(set);
				NetworkManager.Send(sync.Info());
			}

			// todo: id to client mapping doesnt work properly yet
			//var ids = set.cars.Select(c => c.GetComponent<NetworkObject>()).Where(no => no).GroupBy(no => NetworkManager.MultiPlayerIdToClient(no.Id)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();

			//if (ids is null) return;

			//var owner = ids.Key;

			//if (owner == MultiPlayerManager.Instance.OwnId)
			//{
			//	var sync = CreateLocal(set);
			//	NetworkManager.Send(sync.Info());
			//}
		}
	}

	public class TrainSetStatePacket : NetworkObjectStatePacket
	{
		public BogiePosition[] Bogies = null!;
	}

	public struct BogiePosition
	{
		public int Track;
		public double TrackPosition;
		public float Velocity;
	}
}