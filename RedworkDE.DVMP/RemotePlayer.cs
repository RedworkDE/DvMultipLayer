using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RedworkDE.DVMP.Networking;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace RedworkDE.DVMP
{
	/// <summary>
	/// A player, instances exist for each player inclusing the local one
	/// </summary>
	public class RemotePlayer : NetworkObject, IPacketReceiver<PlayerPositionPacket>, IPacketReceiver<PlayerInfoPacket>
	{
		private static WorldMap? _map;
		private static WorldMap Map => _map ??= FindObjectOfType<WorldMap>();

		public override float UpdateRate => 0;

		public Vector3 Position;
		public Quaternion Rotation;
		public readonly List<Object> DestroyOnDisconnect = new List<Object>();
		public List<GameObject> MapIndicators = new List<GameObject>();

		private TextMeshPro _playerNameField = null!;

		private Color _color = Random.ColorHSV(0, 1, 1, 1, 1, 1, 1, 1);
		private string _name = "Player";
		private bool _connected = true;

		public string Name
		{
			get => _name;
			set => MaybeUpdate(ref _name, value);
		}

		public Color Color
		{
			get => _color;
			set => MaybeUpdate(ref _color, value, SetIndicatorColor);
		}

		public bool Connected
		{
			get => _connected;
		}

		private void MaybeUpdate<T>(ref T backing, T newValue, Action<T>? update = null) where T : IEquatable<T>
		{
			if (!Authoritative) return; // cant set props of remote players

			if (backing is null)
			{
				if (newValue is null) return;
				backing = newValue;
				update?.Invoke(newValue);
				DoUpdate(default);
			}
			else if (!backing.Equals(newValue))
			{
				backing = newValue;
				update?.Invoke(newValue);
				DoUpdate(default);
			}
		}

		public void SendPlayerInfo(ClientId clientId)
		{
			DoUpdate(clientId);
		}

		private void DoUpdate(ClientId target)
		{
			NetworkManager.Send(new PlayerInfoPacket() {PlayerName = Name, Color = Color}, target);
		}

		private void SetIndicatorColor(Color color)
		{
			foreach (var indicator in MapIndicators) 
				indicator.GetComponent<MeshRenderer>().material.color = color;
		}

		public override void RegisterReceiver(bool registerState = false)
		{
			base.RegisterReceiver();
			if (registerState) NetworkManager.RegisterReceiver<PlayerPositionPacket>(this);
			NetworkManager.RegisterReceiver<PlayerInfoPacket>(this);
		}

		public override void UnregisterReceiver()
		{
			base.UnregisterReceiver();
			NetworkManager.UnregisterReceiver<PlayerPositionPacket>(this);
			NetworkManager.UnregisterReceiver<PlayerInfoPacket>(this);
		}

		public PlayerPositionPacket PopulateState(PlayerPositionPacket state)
		{
			state.Position = PlayerManager.PlayerTransform.position - WorldMover.currentMove;
			state.Rotation = PlayerManager.PlayerTransform.rotation;

			return state;
		}

		public override void SendPacket()
		{
			NetworkManager.Send(PopulateState(new PlayerPositionPacket()));
		}

		public new void Init()
		{
			base.Init((MultiPlayerId)MultiPlayerManager.Instance.OwnId);
			Authoritative = true;
		}

		public void Init(ClientId client)
		{
			base.Init((MultiPlayerId) client);

			gameObject.name = Name = "Remote Player " + client;
			DontDestroyOnLoad(gameObject);
			Destroy(GetComponent<Collider>());

			var go = new GameObject("PlayerName");
			go.transform.parent = transform;
			go.transform.localPosition = Vector3.up;
			_playerNameField = go.AddComponent<TextMeshPro>();
			_playerNameField.text = Name;
			_playerNameField.alignment = (TextAlignmentOptions) ((int )_HorizontalAlignmentOptions.Center | (int) _VerticalAlignmentOptions.Bottom);
			_playerNameField.autoSizeTextContainer = true;
			_playerNameField.enableAutoSizing = true;
			_playerNameField.isOverlay = true;
			_playerNameField.outlineColor = new Color32(0, 0, 0, 255);
			_playerNameField.outlineWidth = 10;
			
			_playerNameField.transform.localPosition = Vector3.up * 1.2f;
			_playerNameField.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);

			DestroyOnDisconnect.Add(go);
			DestroyOnDisconnect.Add(gameObject);
		}

		public override void Update()
		{
			base.Update();

			if (!Authoritative)
			{
				var angle = PlayerManager.PlayerCamera.transform.eulerAngles;
				angle.z = 0;
				_playerNameField.transform.eulerAngles = angle;
				
				for (int i = 0; i < MapIndicators.Count; i++)
				{
					var indicator = MapIndicators[i];

					if (!indicator || !indicator.transform)
					{
						MapIndicators.RemoveAt(i);
						i--;
						continue;
					}

					if (!Map)
					{
						continue;
					}
					
					Vector3 position = Position;
					var x = Mathf.Lerp(-Map.triggerExtentsXZ.x, Map.triggerExtentsXZ.x, Mathf.InverseLerp(0.0f, LevelInfo.Instance.worldSize, position.x));
					var z = Mathf.Lerp(-Map.triggerExtentsXZ.y, Map.triggerExtentsXZ.y, Mathf.InverseLerp(0.0f, LevelInfo.Instance.worldSize, position.z));
					var y = indicator.transform.localPosition.y;
					indicator.transform.localPosition = new Vector3(x, y, z);

					Vector3 normalized = Vector3.ProjectOnPlane(Rotation * Vector3.forward, Vector3.up).normalized;
					if (normalized != Vector3.zero) indicator.transform.localRotation = Quaternion.LookRotation(normalized);
				}
			}
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			Destroy(_playerNameField.gameObject);
		}

		public bool Receive(PlayerPositionPacket packet, ClientId client)
		{
			if ((MultiPlayerId) client != Id) return false;

			Position = packet.Position;
			Rotation = packet.Rotation;

			transform.rotation = packet.Rotation;
			transform.position = packet.Position + WorldMover.currentMove + Vector3.up;

			return true;
		}

		public bool Receive(PlayerInfoPacket packet, ClientId client)
		{
			if ((MultiPlayerId) client != Id) return false;

			_name = packet.PlayerName;
			_color = packet.Color;

			_playerNameField.text = Name;
			GetComponent<MeshRenderer>().material.color = Color;

			for (int i = 0; i < MapIndicators.Count; i++) 
				MapIndicators[i].GetComponent<MeshRenderer>().material.color = Color;

			return true;
		}

		public void Disconnect()
		{
			_connected = false;
			foreach (var obj in DestroyOnDisconnect) Destroy(obj);
		}
	}
	
	public class PlayerPositionPacket : AutoPacket
	{
		public Vector3 Position;
		public Quaternion Rotation;
	}

	public class PlayerInfoPacket : AutoPacket
	{
		[MarshalAs(UnmanagedType.BStr)]
		public string PlayerName = null!;
		public Color Color = Color.white;

	}
}
