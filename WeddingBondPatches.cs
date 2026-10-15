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

        private static void Postfix(ref float __result)
        {
            var player = Player.m_localPlayer;
            if (WeddingProgression.GetLevelForPlayer(player) >= 8 && Plugin.HasWeddingBond(player))
            {
                __result += Plugin.IsWeddingBondEnhanced(player) ? 50f : 25f;
            }
        }
    }
}
