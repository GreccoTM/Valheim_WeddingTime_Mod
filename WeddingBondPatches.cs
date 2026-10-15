using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace WeddingTime
{
    [HarmonyPatch]
    internal static class WeddingBondDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Character), "Damage", new[] { typeof(HitData) });
        }

        private static void Prefix(Character __instance, ref HitData hit)
        {
            if (!(__instance is Player player)
                || WeddingProgression.GetLevelForPlayer(player) < 5
                || !Plugin.HasWeddingBond(player))
            {
                return;
            }

            var multiplier = Plugin.IsWeddingBondEnhanced(player) ? 0.50f : 0.75f;
            hit.m_damage.m_fire *= multiplier;
            hit.m_damage.m_frost *= multiplier;
            hit.m_damage.m_lightning *= multiplier;
            hit.m_damage.m_poison *= multiplier;
            hit.m_damage.m_spirit *= multiplier;
        }
    }

    [HarmonyPatch]
    internal static class WeddingBondMaxStatsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new[] { "SetMaxHealth", "SetMaxStamina", "SetMaxEitr" };
            foreach (var name in names)
            {
                var method = AccessTools.Method(typeof(Player), name, new[] { typeof(float), typeof(bool) });
                if (method != null) yield return method;
            }
        }

        private static void Prefix(Player __instance, ref float __0)
        {
            if (__0 > 0f && WeddingProgression.GetLevelForPlayer(__instance) >= 9 && Plugin.HasWeddingBond(__instance))
            {
                __0 += Plugin.IsWeddingBondEnhanced(__instance) ? 30f : 15f;
            }
        }
    }

    [HarmonyPatch]
    internal static class WeddingBondSkillsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Skills), "GetSkillLevel", new[] { typeof(Skills.SkillType) });
        }

        private static void Postfix(Skills __instance, ref float __result)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.GetSkills() != __instance) return;

            if (WeddingProgression.GetLevelForPlayer(player) >= 8 && Plugin.HasWeddingBond(player))
            {
                __result += Plugin.IsWeddingBondEnhanced(player) ? 50f : 25f;
            }
        }
    }
}

namespace WeddingTime
{
    // Refresh the Wedding Bond as soon as a ring is equipped or removed instead of waiting for the poll.
    [HarmonyPatch]
    internal static class WeddingBondEquipPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "EquipItem", "UnequipItem" })
            {
                var method = AccessTools.Method(typeof(Humanoid), name);
                if (method != null) yield return method;
            }
        }

        private static void Postfix(Humanoid __instance)
        {
            if (__instance != null && __instance == Player.m_localPlayer)
            {
                Plugin.RefreshDebugBuff();
            }
        }
    }
}
