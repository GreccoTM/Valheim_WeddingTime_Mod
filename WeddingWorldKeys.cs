using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace WeddingTime
{
    // The only way the mod writes its world keys. Vanilla lets any client set or remove global keys, which
    // would let a modified client forge a marriage or edit progress, so the server refuses every change to
    // a Wedding Time key that does not come from these helpers. On the server, SetGlobalKey/RemoveGlobalKey
    // are handled synchronously, so the authorized scope is still open when the vanilla handler runs.
    internal static class WeddingWorldKeys
    {
        private const string ModKeyPrefix = "weddingtime_";
        private static int _authorizedDepth;

        internal static void Set(string key) => Write(key, set: true);
        internal static void Remove(string key) => Write(key, set: false);

        private static void Write(string key, bool set)
        {
            if (ZoneSystem.instance == null) return;

            _authorizedDepth++;
            try
            {
                if (set) ZoneSystem.instance.SetGlobalKey(key);
                else ZoneSystem.instance.RemoveGlobalKey(key);
            }
            finally
            {
                _authorizedDepth--;
            }
        }

        [HarmonyPatch]
        private static class GuardGlobalKeyRpcs
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var name in new[] { "RPC_SetGlobalKey", "RPC_RemoveGlobalKey" })
                {
                    var method = AccessTools.Method(typeof(ZoneSystem), name, new[] { typeof(long), typeof(string) });
                    if (method != null) yield return method;
                }
            }

            private static bool Prefix(MethodBase __originalMethod, long __0, string __1)
            {
                if (_authorizedDepth > 0 || ZNet.instance == null || !ZNet.instance.IsServer()) return true;
                if (__1 == null || !__1.TrimStart().StartsWith(ModKeyPrefix, StringComparison.OrdinalIgnoreCase)) return true;

                Plugin.ModLogger.LogWarning($"Blocked {__originalMethod.Name} for '{__1}' from peer {__0}: Wedding Time keys can only be changed by the mod on the server.");
                return false;
            }
        }
    }
}
