using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingProgression
    {
        private const string DaysPerLevelPrefix = "weddingtime_days_per_level_";
        private const string ProgressSchemaKey = "weddingtime_progress_schema_2";
        private const int LegacySecondsPerDay = 86400;
        private const int DefaultGameDayLengthSeconds = 1800;
        private const double PersistIntervalDays = 0.25;

        private static readonly Dictionary<string, double> SharedSeconds = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> PersistedSeconds = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> LastOnlineWorldTime = new Dictionary<string, double>();
        private static readonly HashSet<string> WaitingForPlayersLog = new HashSet<string>();
        private static long _worldUid;
        private static bool _worldInitialized;
        private static int _localLevel = 1;
        private static int _debugLevel;

        internal static int CurrentLevel => _debugLevel > 0 ? _debugLevel : _localLevel;

        internal static void SetDebugLevel(int level)
        {
            _debugLevel = level < 1 || level > WeddingKeys.MaximumLevel ? 0 : level;
            WeddingBond.Refresh();
        }

        internal static string GetLocalProgressSummary()
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZoneSystem.instance == null)
            {
                return WeddingText.T("progress_unavailable");
            }

            EnsureWorldLoaded();
            var keys = ZoneSystem.instance.GetGlobalKeys();
            if (!WeddingKeys.TryFindPartner(keys, Player.m_localPlayer.GetPlayerID(), out var partnerId))
            {
                return WeddingText.T("progress_not_married");
            }

            var partnerName = WeddingProposalService.MarriedPartnerId == partnerId && !string.IsNullOrEmpty(WeddingProposalService.EngagedPartnerName)
                ? WeddingProposalService.EngagedPartnerName
                : WeddingText.KeyArg("your_partner");
            var daysPerLevel = DaysPerLevel();
            var centidays = WeddingKeys.ReadProgressCentidays(keys, WeddingKeys.PairKey(Player.m_localPlayer.GetPlayerID(), partnerId));
            var level = WeddingKeys.LevelFor(centidays, daysPerLevel);
            if (level >= WeddingKeys.MaximumLevel)
            {
                return WeddingText.T("progress_max", partnerName, FormatDays(centidays));
            }

            var hundredthsPerLevel = daysPerLevel * WeddingKeys.HundredthsPerDay;
            var progressThisLevel = centidays % hundredthsPerLevel;
            return WeddingText.T("progress", partnerName, level.ToString(), FormatDays(progressThisLevel),
                daysPerLevel.ToString(), FormatDays(hundredthsPerLevel - progressThisLevel), (level + 1).ToString());
        }

        private static string FormatDays(int centidays)
        {
            return (centidays / (double)WeddingKeys.HundredthsPerDay).ToString("0.00");
        }

        // A dedicated server only has Player objects for characters near it, so resolve
        // connected characters through the peer roster as well.
        internal static bool TryGetOnlinePlayer(long playerId, out string name, out Vector3 position)
        {
            name = null;
            position = Vector3.zero;
            var player = Player.GetAllPlayers()?.FirstOrDefault(candidate => candidate != null && candidate.GetPlayerID() == playerId);
            if (player != null)
            {
                name = player.GetPlayerName();
                position = player.transform.position;
                return true;
            }

            var peer = FindPeerByPlayerId(playerId);
            if (peer == null) return false;
            name = peer.m_playerName;
            position = peer.m_refPos;
            return true;
        }

        internal static List<long> FindOnlinePlayerIdsByName(string name)
        {
            var ids = new HashSet<long>();
            if (string.IsNullOrEmpty(name)) return ids.ToList();

            foreach (var player in Player.GetAllPlayers() ?? new List<Player>())
            {
                if (player != null && string.Equals(player.GetPlayerName(), name, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(player.GetPlayerID());
                }
            }

            foreach (var peer in ZNet.instance?.GetPeers() ?? new List<ZNetPeer>())
            {
                if (peer == null || !string.Equals(peer.m_playerName, name, StringComparison.OrdinalIgnoreCase)) continue;
                var playerId = GetPlayerIdForCharacter(peer.m_characterID);
                if (playerId != 0) ids.Add(playerId);
            }

            return ids.ToList();
        }

        internal static long GetPlayerIdForPeer(long peerId)
        {
            var player = Player.GetAllPlayers()?.FirstOrDefault(candidate => candidate != null && candidate.GetZDOID().UserID == peerId);
            if (player != null) return player.GetPlayerID();

            var peer = ZNet.instance?.GetPeer(peerId);
            return peer != null ? GetPlayerIdForCharacter(peer.m_characterID) : 0;
        }

        internal static bool TryGetPeerIdForPlayer(long playerId, out long peerId)
        {
            var peer = FindPeerByPlayerId(playerId);
            peerId = peer != null ? peer.m_uid : 0;
            return peer != null;
        }

        private static ZNetPeer FindPeerByPlayerId(long playerId)
        {
            return ZNet.instance?.GetPeers()?.FirstOrDefault(peer => peer != null && GetPlayerIdForCharacter(peer.m_characterID) == playerId);
        }

        private static long GetPlayerIdForCharacter(ZDOID characterId)
        {
            if (characterId.IsNone() || ZDOMan.instance == null) return 0;
            var character = ZDOMan.instance.GetZDO(characterId);
            return character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0;
        }

        internal static bool TryGetPartnerId(long playerId, out long partnerId)
        {
            partnerId = 0;
            return ZoneSystem.instance != null && WeddingKeys.TryFindPartner(ZoneSystem.instance.GetGlobalKeys(), playerId, out partnerId);
        }

        internal static bool IsMarried(long playerId)
        {
            return TryGetPartnerId(playerId, out _);
        }

        // Server only: erase the marriage, any pending divorce and all of the couple's saved progress.
        internal static void RemoveMarriage(long first, long second)
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var pairKey = WeddingKeys.PairKey(first, second);
            foreach (var key in ZoneSystem.instance.GetGlobalKeys())
            {
                if (key == WeddingKeys.MarriageKey(first, second)
                    || (WeddingKeys.TryParseProgressKey(key, out var progressPair, out _) && progressPair == pairKey))
                {
                    WeddingWorldKeys.Remove(key);
                }
            }
            WeddingDivorceService.RemovePendingKeys(first, second);

            SharedSeconds.Remove(pairKey);
            PersistedSeconds.Remove(pairKey);
            LastOnlineWorldTime.Remove(pairKey);
            WaitingForPlayersLog.Remove(pairKey);
            Plugin.ModLogger.LogInfo($"Removed marriage and Wedding Bond progress for couple {pairKey}.");
        }

        internal static IEnumerator Watch()
        {
            var wait = new WaitForSeconds(10f);
            while (true)
            {
                TickServer();
                RefreshLocalLevel();
                yield return wait;
            }
        }

        internal static void Flush()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null) return;

            foreach (var pair in SharedSeconds.Keys.ToList())
            {
                PersistProgress(pair);
            }
        }

        internal static void RefreshLocalLevel()
        {
            if (ZNet.instance == null || ZoneSystem.instance == null || Player.m_localPlayer == null) return;

            EnsureWorldLoaded();
            var keys = ZoneSystem.instance.GetGlobalKeys();
            var localId = Player.m_localPlayer.GetPlayerID();
            if (!WeddingKeys.TryFindPartner(keys, localId, out var partnerId))
            {
                SetLocalLevel(1);
                return;
            }

            var centidays = WeddingKeys.ReadProgressCentidays(keys, WeddingKeys.PairKey(localId, partnerId));
            SetLocalLevel(WeddingKeys.LevelFor(centidays, DaysPerLevel()));
        }

        private static void TickServer()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null)
            {
                _worldInitialized = false;
                LastOnlineWorldTime.Clear();
                return;
            }

            EnsureWorldLoaded();
            MigrateLegacyProgress();
            RemoveLegacyDaysPerLevelKeys();
            var now = ZNet.instance.GetTimeSeconds();
            var players = GetOnlinePlayerIds();
            var keys = ZoneSystem.instance.GetGlobalKeys();
            var onlinePairs = new HashSet<string>();

            foreach (var pair in WeddingKeys.GetMarriagePairs(keys))
            {
                var key = WeddingKeys.PairKey(pair.First, pair.Second);
                if (!SharedSeconds.ContainsKey(key))
                {
                    var storedSeconds = WeddingKeys.ReadProgressCentidays(keys, key) / (double)WeddingKeys.HundredthsPerDay * GetGameSecondsPerDay();
                    SharedSeconds[key] = storedSeconds;
                    PersistedSeconds[key] = storedSeconds;
                }

                if (!players.Contains(pair.First) || !players.Contains(pair.Second))
                {
                    if (LastOnlineWorldTime.Remove(key))
                    {
                        PersistProgress(key);
                        Plugin.ModLogger.LogInfo($"Paused shared Wedding Bond time for couple {key}; both partners are no longer online.");
                    }
                    if (WaitingForPlayersLog.Add(key))
                    {
                        Plugin.ModLogger.LogInfo($"Waiting to count Wedding Bond time for couple {key}; server currently sees player IDs: {string.Join(",", players.OrderBy(playerId => playerId))}.");
                    }
                    continue;
                }

                onlinePairs.Add(key);
                WaitingForPlayersLog.Remove(key);
                if (LastOnlineWorldTime.TryGetValue(key, out var lastTime))
                {
                    var delta = Math.Max(0.0, now - lastTime);
                    SharedSeconds[key] = SharedSeconds.TryGetValue(key, out var current) ? current + delta : delta;
                }
                else
                {
                    Plugin.ModLogger.LogInfo($"Started counting shared Wedding Bond time for couple {key}.");
                }
                LastOnlineWorldTime[key] = now;

                var persisted = PersistedSeconds.TryGetValue(key, out var saved) ? saved : 0.0;
                if (SharedSeconds.TryGetValue(key, out var total)
                    && total - persisted >= GetGameSecondsPerDay() * PersistIntervalDays)
                {
                    PersistProgress(key);
                }
            }

            foreach (var key in LastOnlineWorldTime.Keys.Where(key => !onlinePairs.Contains(key)).ToList())
            {
                LastOnlineWorldTime.Remove(key);
            }
        }

        private static HashSet<long> GetOnlinePlayerIds()
        {
            var playerIds = new HashSet<long>();
            foreach (var player in Player.GetAllPlayers() ?? new List<Player>())
            {
                var playerId = player != null ? player.GetPlayerID() : 0;
                if (playerId != 0) playerIds.Add(playerId);
            }

            // A dedicated server may not expose every connected character as a local
            // Player component. Its peer roster contains each character ZDO instead.
            foreach (var peer in ZNet.instance?.GetPeers() ?? new List<ZNetPeer>())
            {
                var playerId = peer != null ? GetPlayerIdForCharacter(peer.m_characterID) : 0;
                if (playerId != 0) playerIds.Add(playerId);
            }

            return playerIds;
        }

        private static void EnsureWorldLoaded()
        {
            var uid = ZNet.instance.GetWorldUID();
            if (_worldInitialized && _worldUid == uid) return;

            _worldUid = uid;
            _worldInitialized = true;
            SharedSeconds.Clear();
            PersistedSeconds.Clear();
            LastOnlineWorldTime.Clear();
            WaitingForPlayersLog.Clear();

            var keys = ZoneSystem.instance.GetGlobalKeys();
            foreach (var pair in WeddingKeys.GetMarriagePairs(keys))
            {
                var pairKey = WeddingKeys.PairKey(pair.First, pair.Second);
                var seconds = WeddingKeys.ReadProgressCentidays(keys, pairKey) / (double)WeddingKeys.HundredthsPerDay * GetGameSecondsPerDay();
                SharedSeconds[pairKey] = seconds;
                PersistedSeconds[pairKey] = seconds;
            }

            Plugin.ModLogger.LogInfo($"Wedding progression loaded for world {uid}.");
        }

        private static void PersistProgress(string pairKey)
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()
                || !SharedSeconds.TryGetValue(pairKey, out var seconds))
            {
                return;
            }

            var centidays = ToCentidays(seconds);
            var saved = PersistedSeconds.TryGetValue(pairKey, out var previous) ? ToCentidays(previous) : 0;
            if (centidays == saved || centidays <= 0) return;

            // Write the new value before removing the old ones, so a crash in between never loses progress
            // (readers take the highest value).
            var newKey = WeddingKeys.ProgressKey(pairKey, centidays);
            WeddingWorldKeys.Set(newKey);
            foreach (var oldKey in ZoneSystem.instance.GetGlobalKeys())
            {
                if (oldKey != newKey && WeddingKeys.TryParseProgressKey(oldKey, out var oldPair, out _) && oldPair == pairKey)
                {
                    WeddingWorldKeys.Remove(oldKey);
                }
            }

            PersistedSeconds[pairKey] = centidays / (double)WeddingKeys.HundredthsPerDay * GetGameSecondsPerDay();
            var oldLevel = WeddingKeys.LevelFor(saved, DaysPerLevel());
            var newLevel = WeddingKeys.LevelFor(centidays, DaysPerLevel());
            if (newLevel > oldLevel && WeddingKeys.TryParsePairKey(pairKey, out var firstId, out var secondId))
            {
                Plugin.ModLogger.LogInfo($"Wedding Bond progression for couple {pairKey}: level {oldLevel} -> {newLevel} ({FormatDays(centidays)} shared world days).");
                WeddingCelebrationService.AnnounceLevelUp(firstId, secondId, newLevel);
            }
        }

        private static int ToCentidays(double seconds)
        {
            return (int)Math.Floor(seconds / GetGameSecondsPerDay() * WeddingKeys.HundredthsPerDay);
        }

        // DaysTogetherPerLevel is a server-synced config entry, so clients already hold the server's value.
        private static int DaysPerLevel()
        {
            var configuredDays = Plugin.DaysTogetherPerLevel != null ? Plugin.DaysTogetherPerLevel.Value : 5;
            return Math.Max(1, Math.Min(100, configuredDays));
        }

        private static double GetGameSecondsPerDay()
        {
            return EnvMan.instance != null
                ? Math.Max(1.0, EnvMan.instance.m_dayLengthSec)
                : DefaultGameDayLengthSeconds;
        }

        private static void MigrateLegacyProgress()
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var keys = ZoneSystem.instance.GetGlobalKeys();
            if (keys.Contains(ProgressSchemaKey)) return;

            var legacyProgress = new Dictionary<string, int>();
            foreach (var key in keys)
            {
                if (WeddingKeys.TryParseProgressKey(key, out var pairKey, out var centidays))
                {
                    legacyProgress[pairKey] = Math.Max(legacyProgress.TryGetValue(pairKey, out var previous) ? previous : 0, centidays);
                }
            }

            foreach (var key in keys.Where(key => key.StartsWith(WeddingKeys.ProgressPrefix, StringComparison.Ordinal)))
            {
                WeddingWorldKeys.Remove(key);
            }

            foreach (var pair in legacyProgress)
            {
                // Versions 0.5.6-0.5.7 treated 86,400 world-clock seconds as one game day.
                // Convert the saved values using the world's current actual day length.
                var migratedCentidays = (int)Math.Round(
                    pair.Value * LegacySecondsPerDay / GetGameSecondsPerDay(),
                    MidpointRounding.AwayFromZero);
                if (migratedCentidays > 0)
                {
                    WeddingWorldKeys.Set(WeddingKeys.ProgressKey(pair.Key, migratedCentidays));
                }
            }

            WeddingWorldKeys.Set(ProgressSchemaKey);
            _worldInitialized = false;
            EnsureWorldLoaded();
            Plugin.ModLogger.LogInfo($"Migrated Wedding Bond progress for {legacyProgress.Count} couple(s) to the corrected in-game day length.");
        }

        // Versions before 0.5.11 mirrored the config value into a world global key.
        private static void RemoveLegacyDaysPerLevelKeys()
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;

            foreach (var key in ZoneSystem.instance.GetGlobalKeys()
                .Where(key => key.StartsWith(DaysPerLevelPrefix, StringComparison.Ordinal)))
            {
                WeddingWorldKeys.Remove(key);
            }
        }

        private static void SetLocalLevel(int level)
        {
            level = Math.Max(1, Math.Min(WeddingKeys.MaximumLevel, level));
            if (_localLevel == level) return;
            _localLevel = level;
            Plugin.ModLogger.LogInfo($"Local Wedding Bond level is now {level}.");
        }
    }
}
