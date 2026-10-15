using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingDivorceService
    {
        private const string RequestRpcName = "DivorceRequest";
        private const string NoticeRpcName = "MarriageNotice";
        private const float ConfirmWindow = 60f;

        private const int ModeRequest = 0;
        private const int ModeCancel = 1;
        private const int ModeAdminForce = 2;

        private static readonly Dictionary<string, PendingDivorce> Pending = new Dictionary<string, PendingDivorce>();
        private static CustomRPC _requestRpc;
        private static CustomRPC _noticeRpc;
        private static float _armedUntil;

        internal static void RegisterRpcs()
        {
            if (_requestRpc != null) return;
            _requestRpc = NetworkManager.Instance.AddRPC(RequestRpcName, RPC_ReceiveRequest, RPC_IgnoreServerRequest);
            _noticeRpc = NetworkManager.Instance.AddRPC(NoticeRpcName, RPC_IgnoreClientNotice, RPC_ReceiveNotice);
        }

        // ---- Client side -------------------------------------------------------------------

        internal static bool Arm(out string message)
        {
            if (WeddingProposalService.EngagedPartnerId == 0)
            {
                message = "You are not married.";
                return false;
            }

            _armedUntil = Time.time + ConfirmWindow;
            var delay = Plugin.DivorceDelaySeconds.Value;
            var wait = delay > 0 ? $" The divorce becomes final {FormatDelay(delay)} after you confirm; either of you can cancel before then." : string.Empty;
            message = $"Divorcing {WeddingProposalService.EngagedPartnerName} ends your Wedding Bond and erases your shared progress.{wait} Type /divorce confirm within {ConfirmWindow:0} seconds to proceed.";
            return true;
        }

        internal static bool Confirm(out string message)
        {
            if (Time.time >= _armedUntil)
            {
                message = "Nothing to confirm. Type /divorce first.";
                return false;
            }

            _armedUntil = 0f;
            return Send(ModeRequest, 0, out message);
        }

        internal static bool Cancel(out string message)
        {
            _armedUntil = 0f;
            return Send(ModeCancel, 0, out message);
        }

        internal static bool AdminDivorce(string targetName, out string message)
        {
            var target = Player.GetAllPlayers()
                .Where(player => player != null && string.Equals(player.GetPlayerName(), targetName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (target.Count != 1)
            {
                message = target.Count == 0
                    ? $"No online player named '{targetName}' was found nearby."
                    : $"More than one player is named '{targetName}'.";
                return false;
            }

            return Send(ModeAdminForce, target[0].GetPlayerID(), out message);
        }

        private static bool Send(int mode, long targetPlayerId, out string message)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || ZNet.instance == null || _requestRpc == null)
            {
                message = "The world is not ready yet.";
                return false;
            }

            if (ZNet.instance.IsServer())
            {
                HandleRequest(localPlayer.GetPlayerID(), SynchronizationManager.Instance.PlayerIsAdmin, mode, targetPlayerId);
            }
            else
            {
                var serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null)
                {
                    message = "Not connected to the server.";
                    return false;
                }

                var package = new ZPackage();
                package.Write(mode);
                package.Write(targetPlayerId);
                _requestRpc.SendPackage(serverPeer.m_uid, package);
            }

            message = "Request sent.";
            return true;
        }

        private static IEnumerator RPC_IgnoreServerRequest(long senderPeerId, ZPackage package)
        {
            yield return null;
            Plugin.ModLogger.LogWarning($"Ignored server-originated divorce request from peer {senderPeerId}.");
        }

        private static IEnumerator RPC_ReceiveNotice(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) yield break;

            try
            {
                var ended = package.ReadBool();
                var message = package.ReadString();
                HandleNotice(ended, message);
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Could not process marriage notice: {exception.Message}");
            }
        }

        private static void HandleNotice(bool ended, string message)
        {
            if (ended)
            {
                WeddingProposalService.ClearEngagement();
                WeddingProgression.RefreshLocalLevel();
                Plugin.RefreshDebugBuff();
            }

            WeddingProposalService.ShowMessage(message);
        }

        // ---- Server side -------------------------------------------------------------------

        private static IEnumerator RPC_ReceiveRequest(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) yield break;

            int mode;
            long targetPlayerId;
            try
            {
                mode = package.ReadInt();
                targetPlayerId = package.ReadLong();
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Malformed divorce request from peer {senderPeerId}: {exception.Message}");
                yield break;
            }

            var senderPlayerId = WeddingProgression.GetPlayerIdForPeer(senderPeerId);
            if (senderPlayerId == 0)
            {
                Plugin.ModLogger.LogWarning($"Rejected divorce request from peer {senderPeerId}: no character found.");
                yield break;
            }

            HandleRequest(senderPlayerId, ZNet.instance.IsAdmin(senderPeerId), mode, targetPlayerId);
        }

        private static void HandleRequest(long senderPlayerId, bool senderIsAdmin, int mode, long targetPlayerId)
        {
            if (mode == ModeAdminForce)
            {
                if (!senderIsAdmin)
                {
                    Notify(senderPlayerId, false, "Only server admins can force a divorce.");
                    return;
                }

                if (!WeddingProgression.TryGetPartnerId(targetPlayerId, out var targetPartner))
                {
                    Notify(senderPlayerId, false, "That player is not married.");
                    return;
                }

                Execute(targetPlayerId, targetPartner, "A server admin ended this marriage.");
                return;
            }

            if (!WeddingProgression.TryGetPartnerId(senderPlayerId, out var partnerId))
            {
                Notify(senderPlayerId, false, "You are not married.");
                return;
            }

            var pairKey = Math.Min(senderPlayerId, partnerId) + "_" + Math.Max(senderPlayerId, partnerId);
            if (mode == ModeCancel)
            {
                if (Pending.Remove(pairKey))
                {
                    Notify(senderPlayerId, false, "The divorce was cancelled.");
                    Notify(partnerId, false, "The divorce was cancelled.");
                }
                else
                {
                    Notify(senderPlayerId, false, "There is no pending divorce.");
                }
                return;
            }

            if (mode != ModeRequest) return;

            if (Pending.ContainsKey(pairKey))
            {
                Notify(senderPlayerId, false, "A divorce is already pending. Use /divorce cancel to stop it.");
                return;
            }

            var delay = Plugin.DivorceDelaySeconds.Value;
            if (delay <= 0)
            {
                Execute(senderPlayerId, partnerId, "Your marriage has ended.");
                return;
            }

            Pending[pairKey] = new PendingDivorce { First = senderPlayerId, Second = partnerId, ExecuteAt = Time.time + delay };
            WeddingProgression.TryGetOnlinePlayer(senderPlayerId, out var senderName, out _);
            Notify(senderPlayerId, false, $"Divorce requested. It becomes final {FormatDelay(delay)} from now. Use /divorce cancel to stop it.");
            Notify(partnerId, false, $"{senderName ?? "Your partner"} requested a divorce. It becomes final {FormatDelay(delay)} from now. Use /divorce cancel to stop it.");
        }

        internal static void Tick()
        {
            if (Pending.Count == 0) return;

            var now = Time.time;
            foreach (var entry in Pending.Where(pair => now >= pair.Value.ExecuteAt).ToList())
            {
                Pending.Remove(entry.Key);
                if (WeddingProgression.TryGetPartnerId(entry.Value.First, out var partner) && partner == entry.Value.Second)
                {
                    Execute(entry.Value.First, entry.Value.Second, "Your marriage has ended.");
                }
            }
        }

        private static void Execute(long first, long second, string message)
        {
            Pending.Remove(Math.Min(first, second) + "_" + Math.Max(first, second));
            WeddingProgression.RemoveMarriage(first, second);
            Notify(first, true, message);
            Notify(second, true, message);
        }

        private static void Notify(long playerId, bool ended, string message)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
            {
                HandleNotice(ended, message);
                return;
            }

            if (_noticeRpc == null || !WeddingProgression.TryGetPeerIdForPlayer(playerId, out var peerId)) return;

            var package = new ZPackage();
            package.Write(ended);
            package.Write(message);
            _noticeRpc.SendPackage(peerId, package);
        }

        private static IEnumerator RPC_IgnoreClientNotice(long senderPeerId, ZPackage package)
        {
            yield return null;
            Plugin.ModLogger.LogWarning($"Ignored client-originated marriage notice from peer {senderPeerId}.");
        }

        private static string FormatDelay(int seconds)
        {
            return seconds >= 120 ? $"{seconds / 60} minutes" : seconds >= 60 ? "1 minute" : $"{seconds} seconds";
        }

        private sealed class PendingDivorce
        {
            internal long First;
            internal long Second;
            internal float ExecuteAt;
        }
    }
}
