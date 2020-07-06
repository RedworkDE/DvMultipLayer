using System;
using System.Collections;
using System.Linq;
using System.Threading;
using HarmonyLib;
using RedworkDE.DVMP.Networking;
using Steamworks;
using UnityEngine;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// Manage exchange of basic player information
	/// </summary>
	public class RemotePlayerManager : AutoCreateMonoBehaviour<RemotePlayerManager>, INotifyClientConnection
	{
		public static RemotePlayerManager Instance { get; private set; } = null!;
		
		void Start()
		{
			Instance = this;
		}

		void Awake()
		{
			NetworkManager.RegisterClientUpdates(this);
			MultiPlayerManager.Instance.OnMultiPlayerStarted += MultiPlayerStarted;
		}

		private void MultiPlayerStarted()
		{
			var remotePlayer = PlayerManager.PlayerTransform.gameObject.AddComponent<RemotePlayer>();
			remotePlayer.Init();

			MultiPlayerManager.Instance.Players.Add(MultiPlayerManager.Instance.OwnId, remotePlayer);
			MultiPlayerManager.Instance.LocalPlayer = remotePlayer;

			foreach (var map in RedworkDE.DVMP.Utils.Extensions.FindObjectsOfTypeAll<WorldMap>())
			{
				MultiPlayerManager.Instance.LocalPlayer.MapIndicators.Add(map.playerIndicator.gameObject);
				map.playerIndicator.GetComponent<MeshRenderer>().material.color = MultiPlayerManager.Instance.LocalPlayer.Color;
			}

			new Thread(() =>
			{
				try
				{
					SteamAPI.Init();
					MultiPlayerManager.Instance.LocalPlayer.Name = SteamFriends.GetPersonaName();
				}
				catch (Exception ex)
				{
					Logger.LogWarning("Failed to retrieve steam player name: " + ex);
				}
			}).Start();
		}

		void OnDestroy()
		{
			NetworkManager.UnregisterClientUpdates(this);
		}

		public void ClientConnected(ClientId client)
		{
			Logger.LogInfo($"Creating remote player: {client}");

			var playerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
			var remotePlayer = playerObject.AddComponent<RemotePlayer>();
			remotePlayer.Init(client);

			MultiPlayerManager.Instance.Players.Add(client, remotePlayer);
			MultiPlayerManager.Instance.RemotePlayers.Add(client, remotePlayer);

			Logger.LogInfo($"Now has {MultiPlayerManager.Instance.RemotePlayers.Count} remote players");

			InitMaps(remotePlayer);
			StartCoroutine(WaitForSendPlayerInfo(client));
		}

		private IEnumerator WaitForSendPlayerInfo(ClientId client)
		{
			yield return WaitFor.Seconds(1);
			MultiPlayerManager.Instance.LocalPlayer?.SendPlayerInfo(client);
		}
		
		private void InitMaps(RemotePlayer player)
		{
			var maps = RedworkDE.DVMP.Utils.Extensions.FindObjectsOfTypeAll<WorldMap>().ToList();

			Logger.LogDebug($"Adding player {player} to {maps.Count} maps");
			
			maps.ForEach(m => InitMap(player, m));
		}

		private void InitMap(RemotePlayer player, WorldMap map)
		{
			Logger.LogDebug($"Adding player {player} to map {map}");

			var indicator = Instantiate(map.playerIndicator.gameObject);
			indicator.transform.parent = map.playerIndicator.parent;
			indicator.transform.localPosition = map.playerIndicator.localPosition;
			indicator.SetActive(true);

			player.DestroyOnDisconnect.Add(indicator);
			player.MapIndicators.Add(indicator);
		}

		public void AddPlayersToMap(WorldMap map)
		{
			Logger.LogDebug($"Adding players to map {map}");

			foreach (var player in MultiPlayerManager.Instance.RemotePlayers.Values) InitMap(player, map);

			if (MultiPlayerManager.Instance.LocalPlayer)
			{
				MultiPlayerManager.Instance.LocalPlayer!.MapIndicators.Add(map.playerIndicator.gameObject);
				map.playerIndicator.GetComponent<MeshRenderer>().material.color = MultiPlayerManager.Instance.LocalPlayer!.Color;
			}
		}

		public void ClientDisconnected(ClientId client)
		{
			var remotePlayer = MultiPlayerManager.Instance.RemotePlayers[client];
			remotePlayer.Disconnect();
		}

		[HarmonyPatch(typeof(WorldMap), "Start")]
		[HarmonyPrefix]
		public static void WorldMap_Start_Patch(WorldMap __instance)
		{
			if (Instance is null)
			{
				System.Console.WriteLine("Map Start, but Instance does not exist");
				return;
			}

			Logger.LogDebug("Called WorldMap Start");

			Instance.AddPlayersToMap(__instance);
		}
	}
}