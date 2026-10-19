using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingProposalService
    {
        private const string ProposalRpcName = "MarriageProposal";
        private const string ResponseRpcName = "MarriageProposalResponse";
        private const string PersistMarriageRpcName = "PersistMarriage";
        private const string MarriageRejectedRpcName = "MarriageRejected";
        private const string RememberedPartnerPrefix = "weddingtime_partner_";
        private const float ProposalRange = 5f;
        private const float ProposalLifetime = 120f;
        // The server drops a half-confirmed marriage after ProposalLifetime + 30 s; give its answer time to arrive.
        private const float ServerConfirmationGrace = ProposalLifetime + 60f;
        // Global keys arrive shortly after the character spawns; wait before concluding a marriage is gone.
        private const float AwayCheckDelay = 15f;
        // The divorce notice can arrive just after the updated keys; let it explain the change first.
        private const float MissingRecordGrace = 10f;

        private static CustomRPC _proposalRpc;
        private static CustomRPC _responseRpc;
        private static CustomRPC _persistMarriageRpc;
        private static CustomRPC _marriageRejectedRpc;
        private static PendingProposal _outgoing;
        private static PendingProposal _incoming;
        private static long _loadedWorldUid;
        private static long _trackedPlayerId;
        private static float _trackedSince;
        private static float _awaitingConfirmationSince;
        private static float _recordMissingSince;

        // Server only: a marriage is saved once BOTH partners have asked for it, so a client
        // cannot marry another player on its own.
        private static readonly Dictionary<string, PendingMarriage> PendingMarriages = new Dictionary<string, PendingMarriage>();

        // Set as soon as a proposal is accepted, before the server has saved the marriage.
        internal static long EngagedPartnerId { get; private set; }
        internal static string EngagedPartnerName { get; private set; } = string.Empty;

        // The partner only once the marriage is saved in the world; the Wedding Bond requires this.
        internal static long MarriedPartnerId => _awaitingConfirmationSince > 0f ? 0 : EngagedPartnerId;

        internal static void RegisterRpcs()
        {
            if (_proposalRpc != null) return;

            _proposalRpc = NetworkManager.Instance.AddRPC(ProposalRpcName, RPC_ReceiveProposal, RPC_ReceiveProposal);
            _responseRpc = NetworkManager.Instance.AddRPC(ResponseRpcName, RPC_ReceiveResponse, RPC_ReceiveResponse);
            _persistMarriageRpc = NetworkManager.Instance.AddRPC(PersistMarriageRpcName, RPC_ReceivePersistMarriage, RPC_ReceivePersistMarriage);
            _marriageRejectedRpc = NetworkManager.Instance.AddRPC(MarriageRejectedRpcName, RPC_IgnoreClientRequest, RPC_ReceiveMarriageRejected);
            Plugin.ModLogger.LogInfo("Marriage proposal and persistence CustomRPC handlers registered through Jotunn.");
        }

        // ---- Client state ------------------------------------------------------------------

        // Brings the local engagement in line with the world's marriage keys. Runs every couple of seconds,
        // so a lost server message (rejection, divorce) can never leave a client married on its own.
        internal static void Reconcile()
        {
            var player = Player.m_localPlayer;
            var zoneSystem = ZoneSystem.instance;
            if (player == null || zoneSystem == null || ZNet.instance == null) return;

            var worldUid = ZNet.instance.GetWorldUID();
            var playerId = player.GetPlayerID();
            if (_loadedWorldUid != worldUid || _trackedPlayerId != playerId)
            {
                _loadedWorldUid = worldUid;
                _trackedPlayerId = playerId;
                _trackedSince = Time.time;
                ClearEngagement();
                Plugin.ModLogger.LogInfo($"Loading marriage state for world {worldUid}.");
            }

            if (WeddingKeys.TryFindPartner(zoneSystem.GetGlobalKeys(), playerId, out var recordedPartner))
            {
                var justConfirmed = _awaitingConfirmationSince > 0f && EngagedPartnerId == recordedPartner;
                if (EngagedPartnerId != recordedPartner)
                {
                    EngagedPartnerId = recordedPartner;
                    EngagedPartnerName = string.Empty;
                    Plugin.ModLogger.LogInfo($"Restored marriage for player {playerId} and partner {recordedPartner} from this world's save.");
                }
                _awaitingConfirmationSince = 0f;
                _recordMissingSince = 0f;

                if (string.IsNullOrEmpty(EngagedPartnerName))
                {
                    EngagedPartnerName = WeddingProgression.TryGetOnlinePlayer(recordedPartner, out var onlineName, out _)
                        ? onlineName
                        : TryRecall(player, out _, out var rememberedName) ? rememberedName : string.Empty;
                }

                Remember(player, recordedPartner, EngagedPartnerName);
                if (justConfirmed) WeddingText.Show(WeddingText.T("marriage_recorded", EngagedPartnerName));
                return;
            }

            if (EngagedPartnerId != 0)
            {
                if (_awaitingConfirmationSince > 0f)
                {
                    if (Time.time - _awaitingConfirmationSince < ServerConfirmationGrace) return;
                    EndMarriageLocally();
                    WeddingText.Show(WeddingText.T("marriage_failed", WeddingText.KeyArg("reason_not_confirmed")));
                    return;
                }

                if (_recordMissingSince <= 0f) _recordMissingSince = Time.time;
                if (Time.time - _recordMissingSince < MissingRecordGrace) return;

                EndMarriageLocally();
                WeddingText.Show(WeddingText.T("marriage_missing"));
                return;
            }

            if (Time.time - _trackedSince >= AwayCheckDelay && TryRecall(player, out _, out var formerName))
            {
                Forget(player);
                WeddingText.Show(WeddingText.T("ended_while_away",
                    string.IsNullOrEmpty(formerName) ? WeddingText.KeyArg("your_partner") : formerName));
            }
        }

        internal static void ClearEngagement()
        {
            EngagedPartnerId = 0;
            EngagedPartnerName = string.Empty;
            _awaitingConfirmationSince = 0f;
            _recordMissingSince = 0f;
            _incoming = null;
            _outgoing = null;
        }

        // The marriage is over (divorce, rejection): also drop the record kept in the character file.
        internal static void EndMarriageLocally()
        {
            ClearEngagement();
            if (Player.m_localPlayer != null) Forget(Player.m_localPlayer);
            WeddingBond.Refresh();
        }

        // The character file remembers the partner per world, so a divorce finalized while this
        // character was offline can be reported the next time they join.
        private static string RememberedKey() => RememberedPartnerPrefix + _loadedWorldUid;

        private static void Remember(Player player, long partnerId, string partnerName)
        {
            var value = partnerId + "|" + (partnerName ?? string.Empty);
            if (!player.m_customData.TryGetValue(RememberedKey(), out var current) || current != value)
            {
                player.m_customData[RememberedKey()] = value;
            }
        }

        private static bool TryRecall(Player player, out long partnerId, out string partnerName)
        {
            partnerId = 0;
            partnerName = string.Empty;
            if (!player.m_customData.TryGetValue(RememberedKey(), out var value) || string.IsNullOrEmpty(value)) return false;

            var separator = value.IndexOf('|');
            var idText = separator >= 0 ? value.Substring(0, separator) : value;
            partnerName = separator >= 0 ? value.Substring(separator + 1) : string.Empty;
            return long.TryParse(idText, out partnerId) && partnerId != 0;
        }

        private static void Forget(Player player)
        {
            player.m_customData.Remove(RememberedKey());
        }

        // ---- Proposal flow (client) --------------------------------------------------------

        internal static bool Propose(string targetName, out string message)
        {
            var localPlayer = Player.m_localPlayer;
            if (_proposalRpc == null || localPlayer == null)
            {
                message = WeddingText.T("not_ready");
                return false;
            }

            if (EngagedPartnerId != 0)
            {
                message = WeddingText.T("already_engaged");
                return false;
            }

            if (_outgoing != null && Time.time < _outgoing.ExpiresAt)
            {
                message = WeddingText.T("proposal_pending");
                return false;
            }

            var matches = Player.GetAllPlayers()
                .Where(player => player != null
                    && player.GetPlayerID() != localPlayer.GetPlayerID()
                    && string.Equals(player.GetPlayerName(), targetName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                message = WeddingText.T("no_player_named", targetName);
                return false;
            }

            if (matches.Count > 1)
            {
                message = WeddingText.T("multiple_players_named", targetName);
                return false;
            }

            var target = matches[0];
            if (!IsWithinRange(target, localPlayer))
            {
                message = WeddingText.T("get_closer", ProposalRange.ToString("0"), target.GetPlayerName());
                return false;
            }

            var targetPlayerId = target.GetPlayerID();
            var targetPeerId = target.GetZDOID().UserID;
            _outgoing = new PendingProposal(targetPlayerId, target.GetPlayerName(), targetPeerId, Time.time + ProposalLifetime);
            var package = new ZPackage();
            package.Write(localPlayer.GetPlayerName());
            Plugin.ModLogger.LogInfo($"Sending marriage proposal from {localPlayer.GetPlayerName()} (player ID {localPlayer.GetPlayerID()}) to {target.GetPlayerName()} (player ID {targetPlayerId}, peer ID {targetPeerId}).");
            _proposalRpc.SendPackage(targetPeerId, package);

            message = WeddingText.T("proposal_sent", target.GetPlayerName());
            return true;
        }

        internal static bool Respond(bool accept, out string message)
        {
            if (_incoming == null || Time.time >= _incoming.ExpiresAt)
            {
                _incoming = null;
                message = WeddingText.T("no_pending_proposal");
                return false;
            }

            var localPlayer = Player.m_localPlayer;
            if (_responseRpc == null || localPlayer == null)
            {
                message = WeddingText.T("not_ready");
                return false;
            }

            if (accept && EngagedPartnerId != 0)
            {
                message = WeddingText.T("already_engaged_other");
                return false;
            }

            var proposal = _incoming;
            var proposer = FindPlayer(proposal.PlayerId);
            if (proposer == null || !IsWithinRange(proposer, localPlayer))
            {
                _incoming = null;
                message = WeddingText.T("proposer_gone");
                return false;
            }

            Plugin.ModLogger.LogInfo($"Sending marriage response from {localPlayer.GetPlayerName()} to peer {proposal.PeerId}: accepted={accept}.");
            SendResponse(proposal.PeerId, accept, localPlayer.GetPlayerName(), accept ? string.Empty : "reply_no");
            _incoming = null;

            if (!accept)
            {
                message = WeddingText.T("declined_self", proposal.PlayerName);
                return true;
            }

            BeginEngagement(localPlayer, proposal.PlayerId, proposal.PlayerName);
            message = WeddingText.T("accepted_self", proposal.PlayerName);
            return true;
        }

        private static void BeginEngagement(Player localPlayer, long partnerId, string partnerName)
        {
            EngagedPartnerId = partnerId;
            EngagedPartnerName = partnerName;
            _awaitingConfirmationSince = Time.time;
            PersistMarriage(localPlayer.GetPlayerID(), partnerId);
        }

        private static IEnumerator RPC_ReceiveProposal(long senderPeerId, ZPackage package)
        {
            yield return null;
            var proposerName = package.ReadString();
            Plugin.ModLogger.LogInfo($"Marriage proposal RPC received from peer {senderPeerId} ({proposerName}).");
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null)
            {
                Plugin.ModLogger.LogWarning($"Received a marriage proposal from peer {senderPeerId}, but the local player is not ready.");
                yield break;
            }

            if (EngagedPartnerId != 0)
            {
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "reply_busy");
                yield break;
            }

            var proposer = FindPlayerByPeerId(senderPeerId);
            if (proposer == null || !IsWithinRange(proposer, localPlayer))
            {
                Plugin.ModLogger.LogWarning($"Received a marriage proposal from peer {senderPeerId}, but the proposer could not be found nearby.");
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "reply_not_nearby");
                yield break;
            }

            if (_incoming != null && Time.time < _incoming.ExpiresAt)
            {
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "reply_other_waiting");
                WeddingText.Show(WeddingText.T("other_proposal_waiting"));
                yield break;
            }

            _incoming = new PendingProposal(proposer.GetPlayerID(), proposerName, senderPeerId, Time.time + ProposalLifetime);
            Plugin.ModLogger.LogInfo($"Received marriage proposal from {proposerName} (peer {senderPeerId}).");
            WeddingText.Show(WeddingText.T("proposal_received", proposerName));
        }

        private static IEnumerator RPC_ReceiveResponse(long senderPeerId, ZPackage package)
        {
            yield return null;
            var accepted = package.ReadBool();
            var responderName = package.ReadString();
            var reasonKey = package.ReadString();
            Plugin.ModLogger.LogInfo($"Marriage response RPC received from peer {senderPeerId} ({responderName}), accepted={accepted}.");
            if (_outgoing == null || Time.time >= _outgoing.ExpiresAt || _outgoing.PeerId != senderPeerId)
            {
                Plugin.ModLogger.LogWarning($"Ignoring marriage response from unexpected peer {senderPeerId}; expected {(_outgoing == null ? "no peer" : _outgoing.PeerId.ToString())}.");
                yield break;
            }

            var proposal = _outgoing;
            _outgoing = null;
            if (!accepted)
            {
                WeddingText.Show(WeddingText.T("reply", responderName, WeddingText.KeyArg(string.IsNullOrEmpty(reasonKey) ? "reply_no" : reasonKey)));
                yield break;
            }

            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || EngagedPartnerId != 0) yield break;

            BeginEngagement(localPlayer, proposal.PlayerId, responderName);
            WeddingText.Show(WeddingText.T("accepted_other", responderName));
        }

        private static void SendResponse(long peerId, bool accepted, string playerName, string reasonKey)
        {
            if (_responseRpc == null)
            {
                Plugin.ModLogger.LogError("Cannot send marriage response: the Jotunn RPC is not registered.");
                return;
            }

            var package = new ZPackage();
            package.Write(accepted);
            package.Write(playerName);
            package.Write(reasonKey ?? string.Empty);
            _responseRpc.SendPackage(peerId, package);
        }

        private static bool IsWithinRange(Player other, Player localPlayer)
        {
            return (other.transform.position - localPlayer.transform.position).sqrMagnitude <= ProposalRange * ProposalRange;
        }

        private static Player FindPlayer(long playerId)
        {
            return Player.GetAllPlayers().FirstOrDefault(player => player != null && player.GetPlayerID() == playerId);
        }

        private static Player FindPlayerByPeerId(long peerId)
        {
            return Player.GetAllPlayers().FirstOrDefault(player => player != null && player.GetZDOID().UserID == peerId);
        }

        // ---- Marriage persistence ----------------------------------------------------------

        private static void PersistMarriage(long firstPlayerId, long secondPlayerId)
        {
            if (firstPlayerId == 0 || secondPlayerId == 0 || firstPlayerId == secondPlayerId || _persistMarriageRpc == null)
            {
                return;
            }

            var first = Math.Min(firstPlayerId, secondPlayerId);
            var second = Math.Max(firstPlayerId, secondPlayerId);

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                ConfirmMarriage(firstPlayerId, first, second);
                return;
            }

            var serverPeer = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (serverPeer == null)
            {
                Plugin.ModLogger.LogWarning("Could not persist marriage: no server peer is available yet.");
                return;
            }

            var package = new ZPackage();
            package.Write(first);
            package.Write(second);
            _persistMarriageRpc.SendPackage(serverPeer.m_uid, package);
        }

        private static IEnumerator RPC_ReceivePersistMarriage(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) yield break;

            var first = package.ReadLong();
            var second = package.ReadLong();
            var senderPlayerId = WeddingProgression.GetPlayerIdForPeer(senderPeerId);
            if (senderPlayerId == 0 || (senderPlayerId != first && senderPlayerId != second))
            {
                Plugin.ModLogger.LogWarning($"Rejected marriage persistence request from peer {senderPeerId}: sender is not part of the requested pair.");
                yield break;
            }

            ConfirmMarriage(senderPlayerId, Math.Min(first, second), Math.Max(first, second));
        }

        private static void ConfirmMarriage(long confirmingPlayerId, long first, long second)
        {
            if (WeddingProgression.IsMarried(first) || WeddingProgression.IsMarried(second))
            {
                Plugin.ModLogger.LogWarning($"Rejected marriage {first}/{second}: one of the players is already married.");
                NotifyMarriageRejected(first, second, "reason_already_married");
                NotifyMarriageRejected(second, first, "reason_already_married");
                return;
            }

            PurgeExpiredMarriages();
            var pairKey = WeddingKeys.PairKey(first, second);
            if (!PendingMarriages.TryGetValue(pairKey, out var pending))
            {
                pending = new PendingMarriage { CreatedAt = Time.time };
                PendingMarriages[pairKey] = pending;
            }

            pending.Confirmed.Add(confirmingPlayerId);
            if (pending.Confirmed.Contains(first) && pending.Confirmed.Contains(second))
            {
                PendingMarriages.Remove(pairKey);
                SaveMarriageKey(first, second);
            }
            else
            {
                Plugin.ModLogger.LogInfo($"Waiting for the other partner to confirm marriage {first}/{second}.");
            }
        }

        private static void SaveMarriageKey(long first, long second)
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var key = WeddingKeys.MarriageKey(first, second);
            if (!ZoneSystem.instance.GetGlobalKey(key))
            {
                WeddingWorldKeys.Set(key);
            }
            Plugin.ModLogger.LogInfo($"Saved marriage pair {first}/{second} in the world save.");
        }

        // Server only: drop confirmations whose partner never answered and tell whoever did confirm.
        // Held in memory on purpose: both partners confirm within seconds of each other, and the client
        // side gives up on its own if the server restarts in between.
        internal static void PurgeExpiredMarriages()
        {
            if (PendingMarriages.Count == 0) return;

            var now = Time.time;
            foreach (var entry in PendingMarriages.Where(pair => now - pair.Value.CreatedAt > ProposalLifetime + 30f).ToList())
            {
                PendingMarriages.Remove(entry.Key);
                if (!WeddingKeys.TryParsePairKey(entry.Key, out var first, out var second)) continue;

                foreach (var confirmedId in entry.Value.Confirmed)
                {
                    NotifyMarriageRejected(confirmedId, confirmedId == first ? second : first, "reason_timeout");
                }
            }
        }

        // Server only: tell a client its optimistic engagement was not accepted.
        private static void NotifyMarriageRejected(long playerId, long partnerId, string reasonKey)
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
            {
                HandleMarriageRejected(partnerId, reasonKey);
                return;
            }

            if (_marriageRejectedRpc == null || !WeddingProgression.TryGetPeerIdForPlayer(playerId, out var peerId)) return;

            var package = new ZPackage();
            package.Write(partnerId);
            package.Write(reasonKey);
            _marriageRejectedRpc.SendPackage(peerId, package);
        }

        private static IEnumerator RPC_IgnoreClientRequest(long senderPeerId, ZPackage package)
        {
            yield return null;
            Plugin.ModLogger.LogWarning($"Ignored client-originated marriage rejection RPC from peer {senderPeerId}.");
        }

        private static IEnumerator RPC_ReceiveMarriageRejected(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) yield break;

            try
            {
                var partnerId = package.ReadLong();
                var reasonKey = package.ReadString();
                HandleMarriageRejected(partnerId, reasonKey);
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Could not process marriage rejection: {exception.Message}");
            }
        }

        private static void HandleMarriageRejected(long partnerId, string reasonKey)
        {
            // A confirmed marriage is never undone by a late rejection; only the pending engagement is.
            if (EngagedPartnerId != partnerId || _awaitingConfirmationSince <= 0f) return;

            EndMarriageLocally();
            WeddingText.Show(WeddingText.T("marriage_failed", WeddingText.KeyArg(reasonKey)));
        }

        private sealed class PendingMarriage
        {
            internal float CreatedAt;
            internal readonly HashSet<long> Confirmed = new HashSet<long>();
        }

        private sealed class PendingProposal
        {
            internal readonly long PlayerId;
            internal readonly long PeerId;
            internal readonly string PlayerName;
            internal readonly float ExpiresAt;

            internal PendingProposal(long playerId, string playerName, long peerId, float expiresAt)
            {
                PlayerId = playerId;
                PlayerName = playerName;
                PeerId = peerId;
                ExpiresAt = expiresAt;
            }
        }
    }
}
