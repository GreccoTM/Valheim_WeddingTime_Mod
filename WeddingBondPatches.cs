using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace WeddingTime
{
    // Character.Damage runs on the attacker's machine and only forwards the hit; RPC_Damage runs on the
    // victim's owner, which is the only client that knows whether its own Wedding Bond is active.
    [HarmonyPatch]
    internal static class WeddingBondDamagePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Character), "RPC_Damage", new[] { typeof(long), typeof(HitData) });
        }

        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit == null
                || !ReferenceEquals(__instance, Player.m_localPlayer)
                || WeddingProgression.CurrentLevel < 5
                || !WeddingBond.HasBond(Player.m_localPlayer))
            {
                return;
            }

            var multiplier = WeddingBond.IsEnhanced(Player.m_localPlayer) ? 0.50f : 0.75f;
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
            foreach (var name in new[] { "SetMaxHealth", "SetMaxStamina", "SetMaxEitr" })
            {
                var method = AccessTools.Method(typeof(Player), name, new[] { typeof(float), typeof(bool) });
                if (method != null) yield return method;
            }
        }

        private static void Prefix(Player __instance, ref float __0)
        {
            if (__0 > 0f
                && ReferenceEquals(__instance, Player.m_localPlayer)
                && WeddingProgression.CurrentLevel >= 9
                && WeddingBond.HasBond(__instance))
            {
                __0 += WeddingBond.IsEnhanced(__instance) ? 30f : 15f;
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

            if (WeddingProgression.CurrentLevel >= 8 && WeddingBond.HasBond(player))
            {
                __result += WeddingBond.IsEnhanced(player) ? 50f : 25f;
            }
        }
    }

    // SE_Stats appends a generic line for every stat it sets, which would repeat the bond's own
    // localized tooltip; show only the tooltip built in WeddingBond.Configure.
    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.GetTooltipString))]
    internal static class WeddingBondTooltipPatch
    {
        private static void Postfix(SE_Stats __instance, ref string __result)
        {
            if (WeddingBond.IsBondEffect(__instance)) __result = __instance.m_tooltip;
        }
    }

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
                WeddingBond.Refresh();
            }
        }
    }
}
