using System;
using System.Collections;
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
            if (WeddingProposalService.MarriedPartnerId == 0)
            {
                message = WeddingText.T("not_married");
                return false;
            }

            _armedUntil = Time.time + ConfirmWindow;
            var delay = Plugin.DivorceDelaySeconds.Value;
            var wait = delay > 0 ? WeddingText.T("divorce_warning_wait", WeddingText.DurationArg(delay)) : string.Empty;
            var partnerName = string.IsNullOrEmpty(WeddingProposalService.EngagedPartnerName)
                ? WeddingText.KeyArg("your_partner")
                : WeddingProposalService.EngagedPartnerName;
            message = WeddingText.T("divorce_warning", partnerName, wait, ConfirmWindow.ToString("0"));
            return true;
        }

        internal static bool Confirm(out string message)
        {
            if (Time.time >= _armedUntil)
            {
                message = WeddingText.T("nothing_to_confirm");
                return false;
            }

            _armedUntil = 0f;
            return Send(ModeRequest, 0, string.Empty, out message);
        }

        internal static bool Cancel(out string message)
        {
            _armedUntil = 0f;
            return Send(ModeCancel, 0, string.Empty, out message);
        }

        // The server resolves the target, so it works for players far away (by name) or offline (by player ID).
        internal static bool AdminDivorce(string target, out string message)
        {
            return long.TryParse(target, out var playerId)
                ? Send(ModeAdminForce, playerId, string.Empty, out message)
                : Send(ModeAdminForce, 0, target, out message);
        }

        private static bool Send(int mode, long targetPlayerId, string targetName, out string message)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || ZNet.instance == null || _requestRpc == null)
            {
                message = WeddingText.T("not_ready");
                return false;
            }

            if (ZNet.instance.IsServer())
            {
                HandleRequest(localPlayer.GetPlayerID(), SynchronizationManager.Instance.PlayerIsAdmin, mode, targetPlayerId, targetName);
            }
            else
            {
                var serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer == null)
                {
                    message = WeddingText.T("not_connected");
                    return false;
                }

                var package = new ZPackage();
                package.Write(mode);
                package.Write(targetPlayerId);
                package.Write(targetName ?? string.Empty);
                _requestRpc.SendPackage(serverPeer.m_uid, package);
            }

            message = WeddingText.T("request_sent");
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
                var key = package.ReadString();
                var args = new string[package.ReadInt()];
                for (var index = 0; index < args.Length; index++) args[index] = package.ReadString();
                HandleNotice(ended, key, args);
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Could not process marriage notice: {exception.Message}");
            }
        }

        private static void HandleNotice(bool ended, string key, string[] args)
        {
            if (ended)
            {
                WeddingProposalService.EndMarriageLocally();
                WeddingProgression.RefreshLocalLevel();
            }

            WeddingText.Show(WeddingText.T(key, args));
        }

        // ---- Server side -------------------------------------------------------------------

        private static IEnumerator RPC_ReceiveRequest(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) yield break;

            int mode;
            long targetPlayerId;
            string targetName;
            try
            {
                mode = package.ReadInt();
                targetPlayerId = package.ReadLong();
                targetName = package.ReadString();
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

            HandleRequest(senderPlayerId, ZNet.instance.IsAdmin(senderPeerId), mode, targetPlayerId, targetName);
        }

        private static void HandleRequest(long senderPlayerId, bool senderIsAdmin, int mode, long targetPlayerId, string targetName)
        {
            if (mode == ModeAdminForce)
            {
                HandleAdminRequest(senderPlayerId, senderIsAdmin, targetPlayerId, targetName);
                return;
            }

            if (!WeddingProgression.TryGetPartnerId(senderPlayerId, out var partnerId))
            {
                Notify(senderPlayerId, false, "not_married");
                return;
            }

            var hasPending = HasPendingDivorce(senderPlayerId, partnerId);
            if (mode == ModeCancel)
            {
                if (!hasPending)
                {
                    Notify(senderPlayerId, false, "no_pending_divorce");
                    return;
                }

                RemovePendingKeys(senderPlayerId, partnerId);
                Notify(senderPlayerId, false, "divorce_cancelled");
                Notify(partnerId, false, "divorce_cancelled");
                return;
            }

            if (mode != ModeRequest) return;

            if (hasPending)
            {
                Notify(senderPlayerId, false, "divorce_already_pending");
                return;
            }

            var delay = Plugin.DivorceDelaySeconds.Value;
            if (delay <= 0)
            {
                Execute(senderPlayerId, partnerId, "marriage_ended");
                return;
            }

            // Real (UTC) time so world-time jumps from sleeping cannot skip the waiting period,
            // and a pending divorce survives a server restart.
            var executeAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + delay;
            WeddingWorldKeys.Set(WeddingKeys.DivorceKey(senderPlayerId, partnerId, executeAt));
            var senderName = WeddingProgression.TryGetOnlinePlayer(senderPlayerId, out var name, out _)
                ? name
                : WeddingText.KeyArg("your_partner");
            Notify(senderPlayerId, false, "divorce_requested", WeddingText.DurationArg(delay));
            Notify(partnerId, false, "divorce_requested_partner", senderName, WeddingText.DurationArg(delay));
            Plugin.ModLogger.LogInfo($"Divorce of couple {WeddingKeys.PairKey(senderPlayerId, partnerId)} scheduled in {delay} s.");
        }

        private static void HandleAdminRequest(long senderPlayerId, bool senderIsAdmin, long targetPlayerId, string targetName)
        {
            if (!senderIsAdmin)
            {
                Notify(senderPlayerId, false, "admin_only");
                return;
            }

            if (targetPlayerId == 0)
            {
                var matches = WeddingProgression.FindOnlinePlayerIdsByName(targetName);
                if (matches.Count != 1)
                {
                    Notify(senderPlayerId, false, matches.Count == 0 ? "target_not_found" : "target_ambiguous", targetName);
                    return;
                }
                targetPlayerId = matches[0];
            }

            if (!WeddingProgression.TryGetPartnerId(targetPlayerId, out var targetPartner))
            {
                Notify(senderPlayerId, false, "target_not_married");
                return;
            }

            Execute(targetPlayerId, targetPartner, "admin_ended");
            if (senderPlayerId != targetPlayerId && senderPlayerId != targetPartner)
            {
                Notify(senderPlayerId, false, "admin_done", string.IsNullOrEmpty(targetName) ? targetPlayerId.ToString() : targetName);
            }
        }

        // Server only: finalize divorces whose waiting period is over.
        internal static void Tick()
        {
            if (ZoneSystem.instance == null) return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var key in ZoneSystem.instance.GetGlobalKeys())
            {
                if (!WeddingKeys.TryParseDivorceKey(key, out var first, out var second, out var executeAt) || now < executeAt) continue;

                if (WeddingProgression.TryGetPartnerId(first, out var partner) && partner == second)
                {
                    Execute(first, second, "marriage_ended");
                }
                else
                {
                    WeddingWorldKeys.Remove(key);
                }
            }
        }

        private static bool HasPendingDivorce(long first, long second)
        {
            var pairKey = WeddingKeys.PairKey(first, second);
            return ZoneSystem.instance.GetGlobalKeys().Any(key =>
                WeddingKeys.TryParseDivorceKey(key, out var a, out var b, out _) && WeddingKeys.PairKey(a, b) == pairKey);
        }

        internal static void RemovePendingKeys(long first, long second)
        {
            if (ZoneSystem.instance == null) return;

            var pairKey = WeddingKeys.PairKey(first, second);
            foreach (var key in ZoneSystem.instance.GetGlobalKeys().ToList())
            {
                if (WeddingKeys.TryParseDivorceKey(key, out var a, out var b, out _) && WeddingKeys.PairKey(a, b) == pairKey)
                {
                    WeddingWorldKeys.Remove(key);
                }
            }
        }

        private static void Execute(long first, long second, string messageKey)
        {
            WeddingProgression.RemoveMarriage(first, second);
            Notify(first, true, messageKey);
            Notify(second, true, messageKey);
        }

        // Offline partners are not notified here; they learn about it from their character file on next join.
        private static void Notify(long playerId, bool ended, string key, params string[] args)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
            {
                HandleNotice(ended, key, args);
                return;
            }

            if (_noticeRpc == null || !WeddingProgression.TryGetPeerIdForPlayer(playerId, out var peerId)) return;

            var package = new ZPackage();
            package.Write(ended);
            package.Write(key);
            package.Write(args.Length);
            foreach (var arg in args) package.Write(arg ?? string.Empty);
            _noticeRpc.SendPackage(peerId, package);
        }

        private static IEnumerator RPC_IgnoreClientNotice(long senderPeerId, ZPackage package)
        {
            yield return null;
            Plugin.ModLogger.LogWarning($"Ignored client-originated marriage notice from peer {senderPeerId}.");
        }
    }
}
