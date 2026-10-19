using System;
using System.Collections.Generic;

namespace WeddingTime
{
    // World global keys that hold the mod's saved state. Kept free of game types so it can be unit tested.
    //   weddingtime_married_<first>_<second>
    //   weddingtime_shared_<first>_<second>_<centidays>
    //   weddingtime_divorce_<first>_<second>_<executeAtUnixSeconds>
    // <first> is always the smaller player ID. Valheim lowercases keys and splits on spaces, so keys
    // must stay lowercase with no spaces.
    internal static class WeddingKeys
    {
        internal const string MarriagePrefix = "weddingtime_married_";
        internal const string ProgressPrefix = "weddingtime_shared_";
        internal const string DivorcePrefix = "weddingtime_divorce_";
        internal const int HundredthsPerDay = 100;
        internal const int MaximumLevel = 10;

        internal static string PairKey(long first, long second)
        {
            return Math.Min(first, second) + "_" + Math.Max(first, second);
        }

        internal static bool TryParsePairKey(string pairKey, out long first, out long second)
        {
            first = 0;
            second = 0;
            var ids = pairKey?.Split('_');
            return ids != null && ids.Length == 2 && TryParsePair(ids[0], ids[1], out first, out second);
        }

        internal static string MarriageKey(long first, long second)
        {
            return MarriagePrefix + PairKey(first, second);
        }

        internal static bool TryParseMarriageKey(string key, out long first, out long second)
        {
            first = 0;
            second = 0;
            return TrySplit(key, MarriagePrefix, 2, out var parts) && TryParsePair(parts[0], parts[1], out first, out second);
        }

        internal static string ProgressKey(string pairKey, int centidays)
        {
            return ProgressPrefix + pairKey + "_" + centidays;
        }

        internal static bool TryParseProgressKey(string key, out string pairKey, out int centidays)
        {
            pairKey = null;
            centidays = 0;
            if (!TrySplit(key, ProgressPrefix, 3, out var parts)
                || !TryParsePair(parts[0], parts[1], out var first, out var second)
                || !int.TryParse(parts[2], out centidays) || centidays < 0)
            {
                return false;
            }

            pairKey = PairKey(first, second);
            return true;
        }

        internal static string DivorceKey(long first, long second, long executeAtUnixSeconds)
        {
            return DivorcePrefix + PairKey(first, second) + "_" + executeAtUnixSeconds;
        }

        internal static bool TryParseDivorceKey(string key, out long first, out long second, out long executeAtUnixSeconds)
        {
            first = 0;
            second = 0;
            executeAtUnixSeconds = 0;
            return TrySplit(key, DivorcePrefix, 3, out var parts)
                && TryParsePair(parts[0], parts[1], out first, out second)
                && long.TryParse(parts[2], out executeAtUnixSeconds);
        }

        internal static List<(long First, long Second)> GetMarriagePairs(IEnumerable<string> keys)
        {
            var pairs = new List<(long First, long Second)>();
            foreach (var key in keys)
            {
                if (TryParseMarriageKey(key, out var first, out var second)) pairs.Add((first, second));
            }
            return pairs;
        }

        internal static bool TryFindPartner(IEnumerable<string> keys, long playerId, out long partnerId)
        {
            partnerId = 0;
            if (playerId == 0) return false;
            foreach (var pair in GetMarriagePairs(keys))
            {
                if (pair.First == playerId) { partnerId = pair.Second; return true; }
                if (pair.Second == playerId) { partnerId = pair.First; return true; }
            }
            return false;
        }

        // Older versions could leave several progress keys for a couple; the highest one wins.
        internal static int ReadProgressCentidays(IEnumerable<string> keys, string pairKey)
        {
            var best = 0;
            foreach (var key in keys)
            {
                if (TryParseProgressKey(key, out var candidate, out var centidays) && candidate == pairKey)
                {
                    best = Math.Max(best, centidays);
                }
            }
            return best;
        }

        internal static int LevelFor(int centidays, int daysPerLevel)
        {
            var hundredthsPerLevel = Math.Max(1, daysPerLevel) * HundredthsPerDay;
            return Math.Max(1, Math.Min(MaximumLevel, 1 + Math.Max(0, centidays) / hundredthsPerLevel));
        }

        private static bool TrySplit(string key, string prefix, int count, out string[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(key) || !key.StartsWith(prefix, StringComparison.Ordinal)) return false;
            parts = key.Substring(prefix.Length).Split('_');
            return parts.Length == count;
        }

        private static bool TryParsePair(string firstText, string secondText, out long first, out long second)
        {
            first = 0;
            second = 0;
            if (!long.TryParse(firstText, out var a) || !long.TryParse(secondText, out var b) || a == 0 || b == 0 || a == b)
            {
                return false;
            }

            first = Math.Min(a, b);
            second = Math.Max(a, b);
            return true;
        }
    }
}
