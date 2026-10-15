using System;
using System.Collections;
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
        private const string MarriageKeyPrefix = "weddingtime_married_";
        private const float ProposalRange = 5f;
        private const float ProposalLifetime = 120f;

        private static CustomRPC _proposalRpc;
        private static CustomRPC _responseRpc;
        private static CustomRPC _persistMarriageRpc;
        private static PendingProposal _outgoing;
        private static PendingProposal _incoming;
        private static long _loadedWorldUid;

        internal static long EngagedPartnerId { get; private set; }
        internal static string EngagedPartnerName { get; private set; }

        internal static void RegisterRpcs()
        {
            if (_proposalRpc != null && _responseRpc != null)
            {
                return;
            }

            _proposalRpc = NetworkManager.Instance.AddRPC(ProposalRpcName, RPC_ReceiveProposal, RPC_ReceiveProposal);
            _responseRpc = NetworkManager.Instance.AddRPC(ResponseRpcName, RPC_ReceiveResponse, RPC_ReceiveResponse);
            _persistMarriageRpc = NetworkManager.Instance.AddRPC(PersistMarriageRpcName, RPC_ReceivePersistMarriage, RPC_ReceivePersistMarriage);
            Plugin.ModLogger.LogInfo("Marriage proposal and persistence CustomRPC handlers registered through Jotunn.");
        }

        internal static void RestoreFromWorld()
        {
            var player = Player.m_localPlayer;
            var zoneSystem = ZoneSystem.instance;
            if (player == null || zoneSystem == null || ZNet.instance == null)
            {
                return;
            }

            var worldUid = ZNet.instance.GetWorldUID();
            if (_loadedWorldUid != worldUid)
            {
                _loadedWorldUid = worldUid;
                EngagedPartnerId = 0;
                EngagedPartnerName = string.Empty;
                Plugin.ModLogger.LogInfo($"Loading marriage state for world {worldUid}.");
            }

            if (EngagedPartnerId != 0) return;

            var playerId = player.GetPlayerID();
            foreach (var key in zoneSystem.GetGlobalKeys())
            {
                if (TryReadMarriageKey(key, playerId, out var partnerId))
                {
                    EngagedPartnerId = partnerId;
                    var partner = FindPlayer(partnerId);
                    EngagedPartnerName = partner != null ? partner.GetPlayerName() : string.Empty;
                    Plugin.ModLogger.LogInfo($"Restored marriage for player {playerId} and partner {partnerId} from this world's save.");
                    return;
                }
            }
        }

        private static bool TryReadMarriageKey(string key, long playerId, out long partnerId)
        {
            partnerId = 0;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(MarriageKeyPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var ids = key.Substring(MarriageKeyPrefix.Length).Split('_');
            if (ids.Length != 2 || !long.TryParse(ids[0], out var first) || !long.TryParse(ids[1], out var second))
            {
                return false;
            }

            if (first == playerId) partnerId = second;
            else if (second == playerId) partnerId = first;
            return partnerId != 0;
        }

        private static void PersistMarriage(long firstPlayerId, long secondPlayerId)
        {
            if (firstPlayerId == 0 || secondPlayerId == 0 || firstPlayerId == secondPlayerId || _persistMarriageRpc == null)
            {
                return;
            }

            var first = Math.Min(firstPlayerId, secondPlayerId);
            var second = Math.Max(firstPlayerId, secondPlayerId);
            var package = new ZPackage();
            package.Write(first);
            package.Write(second);

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                SaveMarriageKey(first, second);
                return;
            }

            var serverPeer = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (serverPeer == null)
            {
                Plugin.ModLogger.LogWarning("Could not persist marriage: no server peer is available yet.");
                return;
            }

            _persistMarriageRpc.SendPackage(serverPeer.m_uid, package);
        }

        private static void SaveMarriageKey(long firstPlayerId, long secondPlayerId)
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            var key = MarriageKeyPrefix + firstPlayerId + "_" + secondPlayerId;
            if (!ZoneSystem.instance.GetGlobalKey(key))
            {
                ZoneSystem.instance.SetGlobalKey(key);
            }
            Plugin.ModLogger.LogInfo($"Saved marriage pair {firstPlayerId}/{secondPlayerId} in the world save.");
        }

        private static IEnumerator RPC_ReceivePersistMarriage(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) yield break;

            var first = package.ReadLong();
            var second = package.ReadLong();
            var sender = FindPlayerByPeerId(senderPeerId);
            if (sender == null || (sender.GetPlayerID() != first && sender.GetPlayerID() != second))
            {
                Plugin.ModLogger.LogWarning($"Rejected marriage persistence request from peer {senderPeerId}: sender is not part of the requested pair.");
                yield break;
            }

            SaveMarriageKey(Math.Min(first, second), Math.Max(first, second));
        }

        internal static bool Propose(string targetName, out string message)
        {
            var localPlayer = Player.m_localPlayer;
            if (_proposalRpc == null || localPlayer == null)
            {
                message = "The world is not ready for marriage proposals yet.";
                return false;
            }

            if (EngagedPartnerId != 0)
            {
                message = "You are already engaged. Complete or cancel that relationship before proposing again.";
                return false;
            }

            if (_outgoing != null && Time.time < _outgoing.ExpiresAt)
            {
                message = "You already have a proposal awaiting an answer.";
                return false;
            }

            var matches = Player.GetAllPlayers()
                .Where(player => player != null
                    && player.GetPlayerID() != localPlayer.GetPlayerID()
                    && string.Equals(player.GetPlayerName(), targetName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                message = $"No nearby player named '{targetName}' was found. Player names must match exactly.";
                return false;
            }

            if (matches.Count > 1)
            {
                message = $"More than one nearby player is named '{targetName}'. Ask them to use distinct character names.";
                return false;
            }

            var target = matches[0];
            if ((target.transform.position - localPlayer.transform.position).sqrMagnitude > ProposalRange * ProposalRange)
            {
                message = $"Get within {ProposalRange:0} meters of {target.GetPlayerName()} before proposing.";
                return false;
            }

            var targetPlayerId = target.GetPlayerID();
            if (targetPlayerId == EngagedPartnerId)
            {
                message = "You are already engaged to this player.";
                return false;
            }

            var targetPeerId = target.GetZDOID().UserID;
            _outgoing = new PendingProposal(targetPlayerId, target.GetPlayerName(), targetPeerId, Time.time + ProposalLifetime);
            var package = new ZPackage();
            package.Write(localPlayer.GetPlayerName());
            Plugin.ModLogger.LogInfo($"Sending marriage proposal from {localPlayer.GetPlayerName()} (player ID {localPlayer.GetPlayerID()}) to {target.GetPlayerName()} (player ID {targetPlayerId}, peer ID {targetPeerId}).");
            _proposalRpc.SendPackage(targetPeerId, package);

            message = $"You asked {target.GetPlayerName()}, ‘Will you marry me?’ They have two minutes to answer with /i_do or /i_do_not.";
            return true;
        }

        internal static bool Respond(bool accept, out string message)
        {
            if (_incoming == null || Time.time >= _incoming.ExpiresAt)
            {
                _incoming = null;
                message = "You do not have a pending marriage proposal.";
                return false;
            }

            if (_responseRpc == null)
            {
                message = "The marriage network is not ready yet.";
                return false;
            }

            var proposal = _incoming;
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null)
            {
                message = "The local player is not ready.";
                return false;
            }

            if (accept && EngagedPartnerId != 0)
            {
                message = "You are already engaged to another player.";
                return false;
            }

            var proposer = FindPlayer(proposal.PlayerId);
            if (proposer == null || (proposer.transform.position - localPlayer.transform.position).sqrMagnitude > ProposalRange * ProposalRange)
            {
                _incoming = null;
                message = "The proposer is no longer nearby. The proposal expired.";
                return false;
            }

            var responseText = accept
                ? "I do!"
                : "I do not.";
            var package = new ZPackage();
            package.Write(accept);
            package.Write(localPlayer.GetPlayerName());
            package.Write(responseText);
            Plugin.ModLogger.LogInfo($"Sending marriage response from {localPlayer.GetPlayerName()} to peer {proposal.PeerId}: accepted={accept}.");
            _responseRpc.SendPackage(proposal.PeerId, package);
            _incoming = null;

            if (accept)
            {
                EngagedPartnerId = proposal.PlayerId;
                EngagedPartnerName = proposal.PlayerName;
                PersistMarriage(localPlayer.GetPlayerID(), proposal.PlayerId);
                message = $"You said ‘I do’ to {proposal.PlayerName}! You are engaged. Each of you can now craft a Wedding Ring for the other.";
            }
            else
            {
                message = $"You declined {proposal.PlayerName}'s proposal.";
            }

            return true;
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
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "I cannot accept another proposal right now.");
                yield break;
            }

            var proposer = FindPlayerByPeerId(senderPeerId);
            if (proposer == null || (proposer.transform.position - localPlayer.transform.position).sqrMagnitude > ProposalRange * ProposalRange)
            {
                Plugin.ModLogger.LogWarning($"Received a marriage proposal from peer {senderPeerId}, but the proposer could not be found nearby.");
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "The proposer is no longer nearby.");
                yield break;
            }

            if (_incoming != null && Time.time < _incoming.ExpiresAt)
            {
                SendResponse(senderPeerId, false, localPlayer.GetPlayerName(), "I already have another proposal waiting for an answer.");
                ShowMessage("You already have another proposal waiting for an answer.");
                yield break;
            }

            var proposerPlayerId = proposer.GetPlayerID();
            _incoming = new PendingProposal(proposerPlayerId, proposerName, senderPeerId, Time.time + ProposalLifetime);
            Plugin.ModLogger.LogInfo($"Received marriage proposal from {proposerName} (peer {senderPeerId}).");
            ShowMessage($"{proposerName} asks: ‘Will you marry me?’ Answer with /i_do or /i_do_not within two minutes.");
        }

        private static IEnumerator RPC_ReceiveResponse(long senderPeerId, ZPackage package)
        {
            yield return null;
            var accepted = package.ReadBool();
            var responderName = package.ReadString();
            var responseText = package.ReadString();
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
                ShowMessage($"{responderName} replied: ‘{responseText}’");
                yield break;
            }

            EngagedPartnerId = proposal.PlayerId;
            EngagedPartnerName = responderName;
            var localPlayer = Player.m_localPlayer;
            if (localPlayer != null)
            {
                PersistMarriage(localPlayer.GetPlayerID(), proposal.PlayerId);
            }
            ShowMessage($"{responderName} said ‘I do’! You are engaged. Each of you can now craft a Wedding Ring for the other.");
        }

        private static void SendResponse(long peerId, bool accepted, string playerName, string responseText)
        {
            if (_responseRpc == null)
            {
                Plugin.ModLogger.LogError("Cannot send marriage response: the Jotunn RPC is not registered.");
                return;
            }

            var package = new ZPackage();
            package.Write(accepted);
            package.Write(playerName);
            package.Write(responseText);
            _responseRpc.SendPackage(peerId, package);
        }

        private static Player FindPlayer(long playerId)
        {
            return Player.GetAllPlayers().FirstOrDefault(player => player != null && player.GetPlayerID() == playerId);
        }

        private static Player FindPlayerByPeerId(long peerId)
        {
            return Player.GetAllPlayers().FirstOrDefault(player => player != null && player.GetZDOID().UserID == peerId);
        }

        private static void ShowMessage(string text)
        {
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            }

            if (Chat.instance != null)
            {
                Chat.instance.AddString(text);
            }
        }

        private sealed class PendingProposal
        {
            internal readonly long PlayerId;
            internal readonly long PeerId;
            internal readonly string PlayerName;
            internal readonly float ExpiresAt;

            internal PendingProposal(long playerId, string playerName, float expiresAt)
                : this(playerId, playerName, playerId, expiresAt)
            {
            }

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
