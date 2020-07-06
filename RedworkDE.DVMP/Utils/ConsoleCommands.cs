using System;
using System.Net;
using CommandTerminal;
using RedworkDE.DVMP.Networking;
using UnityEngine;
using Ping = RedworkDE.DVMP.Networking.Ping;

namespace RedworkDE.DVMP.Utils
{
	public class ConsoleCommands : AutoLoad<ConsoleCommands>
	{
		static ConsoleCommands()
		{
			ConsoleCommandHelper.RegisterCommand("mp.authority", args =>
			{
				if (MultiPlayerManager.Instance.IsMultiPlayer)
				{
					Terminal.Log(TerminalLogType.Error, "Cannot set authority when already connected");
					return;
				}
				MultiPlayerManager.Instance.HasAuthority = true;
			});
			ConsoleCommandHelper.RegisterCommand("mp.listen", new CommandInfo()
			{
				min_arg_count = 0,
				max_arg_count = 1,
				proc = args =>
				{
					var port = args.Length == 1 ? args[0].Int : 2000;

					if (!NetworkManager.Listen(port)) Terminal.Log(TerminalLogType.Warning, "already listening");
				}
			});
			ConsoleCommandHelper.RegisterCommand("mp.connect", new CommandInfo()
			{
				min_arg_count = 0,
				max_arg_count = 2,
				proc = args =>
				{
					var remote = IPAddress.Loopback;
					var port = 2000;
					if (args.Length == 1)
					{
						if (int.TryParse(args[0].String, out var p)) port = p;
						else if (IPAddress.TryParse(args[0].String, out var ip)) remote = ip;
						else
						{
							Terminal.Shell.IssueErrorMessage("Incorrect type for {0}, expected <{1}>", args[0].String, "int or IPAddress");
							return;
						}
					}
					else if (args.Length == 2)
					{
						if (!IPAddress.TryParse(args[0].String, out remote))
						{
							Terminal.Shell.IssueErrorMessage("Incorrect type for {0}, expected <{1}>", args[0].String, "PAddress");
							return;
						}
						if (!int.TryParse(args[1].String, out port))
						{
							Terminal.Shell.IssueErrorMessage("Incorrect type for {0}, expected <{1}>", args[1].String, "int");
							return;
						}

					}

					if (!NetworkManager.Connect(remote, port)) Terminal.Log(TerminalLogType.Warning, "already connecting");
				},
			});


			NetworkManager.RegisterReceiver(new DefaultPacketReceiver<BroadcastMessagePacket>((packet, i) =>
			{
				Logger.LogMessage($"Broadcast: {i}, {packet.Message}");
				Terminal.Log("{0}: {1}", i, packet);
				return true;
			}));
			ConsoleCommandHelper.RegisterCommand("mp.broadcast", args =>
			{
				var message = string.Join(" ", args);
				NetworkManager.Send(new BroadcastMessagePacket(){Message = message});
			});

			ConsoleCommandHelper.RegisterCommand("mp.username", args =>
			{
				if (MultiPlayerManager.Instance.LocalPlayer == null) return;

				if (args.Length == 0)
				{
					Terminal.Log(MultiPlayerManager.Instance.LocalPlayer.Name);
				}
				else
				{
					MultiPlayerManager.Instance.LocalPlayer.Name = args[0].String;
					Terminal.Log("Updated player name");
				}
			});
			ConsoleCommandHelper.RegisterCommand("mp.playercolor", args =>
			{
				if (MultiPlayerManager.Instance.LocalPlayer == null) return;

				if (args.Length == 0)
				{
					var color = MultiPlayerManager.Instance.LocalPlayer.Color;
					Terminal.Log($"RGB {color.r * 255:F0} {color.g * 255:F0} {color.b * 255:F0}");
				}
				else if (args.Length == 3)
				{
					var r = Mathf.Clamp01(args[0].Int / 255f);
					var g = Mathf.Clamp01(args[1].Int / 255f);
					var b = Mathf.Clamp01(args[2].Int / 255f);

					MultiPlayerManager.Instance.LocalPlayer.Color = new Color(r, g, b);
					Terminal.Log("Updated player color");
				}
				else
				{
					Terminal.Log(TerminalLogType.Error, "Command takes either 0 or 3 arguments");
				}
			});

			static void PingResponse(Guid guid, ClientId clientId, TimeSpan time)
			{
				MultiPlayerManager.Instance.Players.TryGetValue(clientId, out var player);
				Terminal.Log($"Ping Response from {(player != null ? $"{player.Name}({clientId})" : clientId.ToString())} after {time.TotalMilliseconds:F1}ms");
			}
			bool addedPingHandler = false;
			ConsoleCommandHelper.RegisterCommand("mp.ping", args =>
			{
				if (!addedPingHandler) Ping.Instance.PingResponse += PingResponse;
				addedPingHandler = true;

				Ping.Instance.SendPing(default);
			});
		}
	}
}