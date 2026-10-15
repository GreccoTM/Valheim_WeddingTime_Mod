using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingProgression
    {
        private const string MarriagePrefix = "weddingtime_married_";
        private const string ProgressPrefix = "weddingtime_shared_";
        private const string DaysPerLevelPrefix = "weddingtime_days_per_level_";
        private const string ProgressSchemaKey = "weddingtime_progress_schema_2";
        private const int LegacySecondsPerDay = 86400;
        private const int DefaultGameDayLengthSeconds = 1800;
        private const double PersistIntervalDays = 0.25;
        private const int HundredthsPerDay = 100;
        private const int MaximumLevel = 10;

        private static readonly Dictionary<string, double> SharedSeconds = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> PersistedSeconds = new Dictionary<string, double>();
        private static readonly Dictionary<string, double> LastOnlineWorldTime = new Dictionary<string, double>();
        private static readonly Dictionary<long, int> PlayerLevels = new Dictionary<long, int>();
        private static readonly Dictionary<long, long> PartnerIds = new Dictionary<long, long>();
        private static readonly HashSet<string> WaitingForPlayersLog = new HashSet<string>();
        private static long _worldUid;
        private static bool _worldInitialized;
        private static int _localLevel = 1;
        private static int _debugLevel;

        internal static int CurrentLevel => _debugLevel > 0 ? _debugLevel : _localLevel;

        internal static void SetDebugLevel(int level)
        {
            _debugLevel = level < 1 || level > MaximumLevel ? 0 : level;
            Plugin.RefreshDebugBuff();
        }

        internal static int GetLevelForPlayer(Player player)
        {
            if (player == null) return 1;
            if (player == Player.m_localPlayer) return CurrentLevel;
            return PlayerLevels.TryGetValue(player.GetPlayerID(), out var level) ? level : 1;
        }

        internal static string GetLocalProgressSummary()
        {
            if (Player.m_localPlayer == null || ZNet.instance == null || ZoneSystem.instance == null)
            {
                return "Wedding Bond progress is unavailable right now. Enter a world first.";
            }

            EnsureWorldLoaded();
            var localId = Player.m_localPlayer.GetPlayerID();
            var keys = ZoneSystem.instance.GetGlobalKeys().ToList();
            var marriage = GetMarriagePairs(keys)
                .FirstOrDefault(pair => pair.First == localId || pair.Second == localId);
            if (marriage.First == 0 || marriage.Second == 0)
            {
                return "You are not married in this world. Wedding Bond progress has not started.";
            }

            var daysPerLevel = GetHundredthsPerLevel(keys) / HundredthsPerDay;
            var centidays = ReadProgressCentidays(keys, MakePairKey(marriage.First, marriage.Second));
            var level = Math.Min(MaximumLevel, 1 + centidays / (daysPerLevel * HundredthsPerDay));
            if (level >= MaximumLevel)
            {
                return $"Wedding Bond: Level 10/10 — maximum level reached ({centidays / (double)HundredthsPerDay:0.00} shared days).";
            }

            var progressThisLevel = centidays % (daysPerLevel * HundredthsPerDay);
            var daysIntoLevel = progressThisLevel / (double)HundredthsPerDay;
            var daysRemaining = (daysPerLevel * HundredthsPerDay - progressThisLevel) / (double)HundredthsPerDay;
            return $"Wedding Bond: Level {level}/10 — {daysIntoLevel:0.00}/{daysPerLevel} shared days; {daysRemaining:0.00} days until Level {level + 1}.";
        }

        internal static bool HasPartnerRingEquipped(Player player)
        {
            if (player == null || player.GetInventory() == null
                || !PartnerIds.TryGetValue(player.GetPlayerID(), out var partnerId)) return false;

            return player.GetInventory().GetAllItems().Any(item => item.m_equipped
                && item.m_dropPrefab != null
                && item.m_dropPrefab.name == "WeddingTime_Ring"
                && item.m_crafterID == partnerId);
        }

        internal static bool HasEnhancedPartnerRingEquipped(Player player)
        {
            if (player == null || player.GetInventory() == null
                || !PartnerIds.TryGetValue(player.GetPlayerID(), out var partnerId)) return false;

            return player.GetInventory().GetAllItems().Any(item => item.m_equipped
                && item.m_dropPrefab != null
                && item.m_dropPrefab.name == "WeddingTime_Ring"
                && item.m_crafterID == partnerId
                && item.m_quality >= 2);
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
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null)
            {
                return;
            }

            foreach (var pair in SharedSeconds.Keys.ToList())
            {
                PersistProgress(pair);
            }
        }

        internal static void RefreshLocalLevel()
        {
            if (ZNet.instance == null || ZoneSystem.instance == null || Player.m_localPlayer == null)
            {
                return;
            }

            EnsureWorldLoaded();
            var localId = Player.m_localPlayer.GetPlayerID();
            var keys = ZoneSystem.instance.GetGlobalKeys().ToList();
            var marriagePairs = GetMarriagePairs(keys);
            RefreshPlayerLevels(marriagePairs, keys);
            var matching = marriagePairs
                .FirstOrDefault(pair => pair.First == localId || pair.Second == localId);

            if (matching.First == 0 || matching.Second == 0)
            {
                SetLocalLevel(1);
                return;
            }

            var key = MakePairKey(matching.First, matching.Second);
            var centidays = ReadProgressCentidays(ZoneSystem.instance.GetGlobalKeys(), key);
            var level = Math.Min(MaximumLevel, 1 + centidays / GetHundredthsPerLevel(keys));
            SetLocalLevel(level);
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
            SyncDaysPerLevelSetting();
            var now = ZNet.instance.GetTimeSeconds();
            var players = GetOnlinePlayerIds();
            var keys = ZoneSystem.instance.GetGlobalKeys().ToList();
            var marriagePairs = GetMarriagePairs(keys);
            RefreshPlayerLevels(marriagePairs, keys);
            var onlinePairs = new HashSet<string>();

            foreach (var pair in marriagePairs)
            {
                var key = MakePairKey(pair.First, pair.Second);
                if (!SharedSeconds.ContainsKey(key))
                {
                    var centidays = ReadProgressCentidays(keys, key);
                    var storedSeconds = centidays / (double)HundredthsPerDay * GetGameSecondsPerDay();
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
                if (!LastOnlineWorldTime.ContainsKey(key))
                {
                    Plugin.ModLogger.LogInfo($"Started counting shared Wedding Bond time for couple {key}.");
                }
                if (LastOnlineWorldTime.TryGetValue(key, out var lastTime))
                {
                    var delta = Math.Max(0.0, now - lastTime);
                    SharedSeconds[key] = SharedSeconds.TryGetValue(key, out var current) ? current + delta : delta;
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
            var allPlayers = Player.GetAllPlayers();
            if (allPlayers != null)
            {
                foreach (var player in allPlayers)
                {
                    if (player == null) continue;
                    var playerId = player.GetPlayerID();
                    if (playerId != 0) playerIds.Add(playerId);
                }
            }

            // A dedicated server may not expose every connected character as a local
            // Player component. Its peer roster contains each character ZDO instead.
            if (ZNet.instance != null && ZDOMan.instance != null)
            {
                var peers = ZNet.instance.GetPeers();
                if (peers != null)
                {
                    foreach (var peer in peers)
                    {
                        if (peer == null || peer.m_characterID.IsNone()) continue;
                        var character = ZDOMan.instance.GetZDO(peer.m_characterID);
                        if (character == null) continue;
                        var playerId = character.GetLong(ZDOVars.s_playerID, 0L);
                        if (playerId != 0) playerIds.Add(playerId);
                    }
                }
            }

            return playerIds;
        }

        private static void EnsureWorldLoaded()
        {
            var uid = ZNet.instance.GetWorldUID();
            if (_worldInitialized && _worldUid == uid)
            {
                return;
            }

            _worldUid = uid;
            _worldInitialized = true;
            SharedSeconds.Clear();
            PersistedSeconds.Clear();
            LastOnlineWorldTime.Clear();
            PlayerLevels.Clear();
            PartnerIds.Clear();
            WaitingForPlayersLog.Clear();

            foreach (var key in ZoneSystem.instance.GetGlobalKeys())
            {
                if (TryParseProgressKey(key, out var pairKey, out var centidays))
                {
                    var seconds = centidays / (double)HundredthsPerDay * GetGameSecondsPerDay();
                    SharedSeconds[pairKey] = seconds;
                    PersistedSeconds[pairKey] = seconds;
                }
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

            var centidays = (int)Math.Floor(seconds / GetGameSecondsPerDay() * HundredthsPerDay);
            var saved = PersistedSeconds.TryGetValue(pairKey, out var previous)
                ? (int)Math.Floor(previous / GetGameSecondsPerDay() * HundredthsPerDay)
                : 0;
            if (centidays == saved)
            {
                return;
            }

            var prefix = ProgressPrefix + pairKey + "_";
            foreach (var oldKey in ZoneSystem.instance.GetGlobalKeys()
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                ZoneSystem.instance.RemoveGlobalKey(oldKey);
            }

            if (centidays > 0)
            {
                ZoneSystem.instance.SetGlobalKey(prefix + centidays);
            }

            PersistedSeconds[pairKey] = centidays / (double)HundredthsPerDay * GetGameSecondsPerDay();
            var hundredthsPerLevel = GetHundredthsPerLevel(ZoneSystem.instance.GetGlobalKeys());
            var oldLevel = Math.Min(MaximumLevel, 1 + saved / hundredthsPerLevel);
            var newLevel = Math.Min(MaximumLevel, 1 + centidays / hundredthsPerLevel);
            if (newLevel > oldLevel)
            {
                Plugin.ModLogger.LogInfo($"Wedding Bond progression for couple {pairKey}: level {oldLevel} -> {newLevel} ({centidays / (double)HundredthsPerDay:0.00} shared world days).");
                var ids = pairKey.Split('_');
                if (ids.Length == 2 && long.TryParse(ids[0], out var firstId) && long.TryParse(ids[1], out var secondId))
                {
                    WeddingCelebrationService.AnnounceLevelUp(firstId, secondId, newLevel);
                }
            }
        }

        private static int ReadProgressCentidays(IEnumerable<string> keys, string pairKey)
        {
            var prefix = ProgressPrefix + pairKey + "_";
            var best = 0;
            foreach (var key in keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(key.Substring(prefix.Length), out var value))
                {
                    best = Math.Max(best, value);
                }
            }
            return best;
        }

        private static void RefreshPlayerLevels(IEnumerable<(long First, long Second)> pairs, IEnumerable<string> keys)
        {
            PlayerLevels.Clear();
            PartnerIds.Clear();
            var keyList = keys.ToList();
            foreach (var pair in pairs)
            {
                var pairKey = MakePairKey(pair.First, pair.Second);
            var level = Math.Min(MaximumLevel, 1 + ReadProgressCentidays(keyList, pairKey) / GetHundredthsPerLevel(keyList));
                PlayerLevels[pair.First] = level;
                PlayerLevels[pair.Second] = level;
                PartnerIds[pair.First] = pair.Second;
                PartnerIds[pair.Second] = pair.First;
            }
        }

        private static int GetHundredthsPerLevel(IEnumerable<string> keys)
        {
            var configuredDays = Plugin.DaysTogetherPerLevel != null ? Plugin.DaysTogetherPerLevel.Value : 5;
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                foreach (var key in keys)
                {
                    if (key.StartsWith(DaysPerLevelPrefix, StringComparison.Ordinal)
                        && int.TryParse(key.Substring(DaysPerLevelPrefix.Length), out var savedDays)
                        && savedDays >= 1 && savedDays <= 100)
                    {
                        configuredDays = savedDays;
                        break;
                    }
                }
            }

            return Math.Max(1, Math.Min(100, configuredDays)) * HundredthsPerDay;
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

            var keys = ZoneSystem.instance.GetGlobalKeys().ToList();
            if (keys.Contains(ProgressSchemaKey)) return;

            var legacyProgress = new Dictionary<string, int>();
            foreach (var key in keys)
            {
                if (TryParseProgressKey(key, out var pairKey, out var centidays))
                {
                    legacyProgress[pairKey] = Math.Max(
                        legacyProgress.TryGetValue(pairKey, out var previous) ? previous : 0,
                        centidays);
                }
            }

            foreach (var key in keys.Where(key => key.StartsWith(ProgressPrefix, StringComparison.Ordinal)).ToList())
            {
                ZoneSystem.instance.RemoveGlobalKey(key);
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
                    ZoneSystem.instance.SetGlobalKey(ProgressPrefix + pair.Key + "_" + migratedCentidays);
                }
            }

            ZoneSystem.instance.SetGlobalKey(ProgressSchemaKey);
            _worldInitialized = false;
            EnsureWorldLoaded();
            Plugin.ModLogger.LogInfo($"Migrated Wedding Bond progress for {legacyProgress.Count} couple(s) to the corrected in-game day length.");
        }

        private static void SyncDaysPerLevelSetting()
        {
            if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var configuredDays = Math.Max(1, Math.Min(100, Plugin.DaysTogetherPerLevel.Value));
            var desiredKey = DaysPerLevelPrefix + configuredDays;
            var existingKeys = ZoneSystem.instance.GetGlobalKeys()
                .Where(key => key.StartsWith(DaysPerLevelPrefix, StringComparison.Ordinal)).ToList();
            if (existingKeys.Count == 1 && existingKeys[0] == desiredKey) return;

            foreach (var key in existingKeys) ZoneSystem.instance.RemoveGlobalKey(key);
            ZoneSystem.instance.SetGlobalKey(desiredKey);
            Plugin.ModLogger.LogInfo($"Wedding progression setting synchronized to world: {configuredDays} shared in-game days per level.");
        }

        private static bool TryParseProgressKey(string key, out string pairKey, out int centidays)
        {
            pairKey = null;
            centidays = 0;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var valueStart = key.LastIndexOf('_');
            if (valueStart <= ProgressPrefix.Length || !int.TryParse(key.Substring(valueStart + 1), out centidays))
            {
                return false;
            }

            pairKey = key.Substring(ProgressPrefix.Length, valueStart - ProgressPrefix.Length);
            return pairKey.Length > 0 && centidays >= 0;
        }

        private static List<(long First, long Second)> GetMarriagePairs(IEnumerable<string> keys)
        {
            var pairs = new List<(long First, long Second)>();
            foreach (var key in keys)
            {
                if (!key.StartsWith(MarriagePrefix, StringComparison.Ordinal)) continue;
                var ids = key.Substring(MarriagePrefix.Length).Split('_');
                if (ids.Length != 2 || !long.TryParse(ids[0], out var first) || !long.TryParse(ids[1], out var second)) continue;
                pairs.Add((Math.Min(first, second), Math.Max(first, second)));
            }
            return pairs;
        }

        private static string MakePairKey(long first, long second)
        {
            return Math.Min(first, second) + "_" + Math.Max(first, second);
        }

        private static void SetLocalLevel(int level)
        {
            level = Math.Max(1, Math.Min(MaximumLevel, level));
            if (_localLevel == level) return;
            _localLevel = level;
            Plugin.ModLogger.LogInfo($"Local Wedding Bond level is now {level}.");
        }
    }
}
