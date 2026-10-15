using HarmonyLib;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WeddingTime
{
    internal sealed class RingUpgradeCrafterState
    {
        internal long CrafterId;
        internal string CrafterName;
        internal int OriginalQuality;
        internal int GridX;
        internal int GridY;
        internal Player Player;
    }

    [HarmonyPatch]
    internal static class WeddingRingUpgradeCrafterPatch
    {
        private static readonly FieldInfo UpgradeItemField = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
        private static RingUpgradeCrafterState _activeUpgrade;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(InventoryGui), "DoCrafting", new[] { typeof(Player) });
        }

        private static void Prefix(InventoryGui __instance, Player player, out RingUpgradeCrafterState __state)
        {
            __state = null;
            _activeUpgrade = null;

            if (__instance == null || player == null || UpgradeItemField == null
                || !(UpgradeItemField.GetValue(__instance) is ItemDrop.ItemData item)
                || item.m_dropPrefab == null || item.m_dropPrefab.name != "WeddingTime_Ring")
            {
                return;
            }

            __state = new RingUpgradeCrafterState
            {
                CrafterId = item.m_crafterID,
                CrafterName = item.m_crafterName,
                OriginalQuality = item.m_quality,
                GridX = item.m_gridPos.x,
                GridY = item.m_gridPos.y,
                Player = player
            };
            _activeUpgrade = __state;
            Plugin.ModLogger?.LogInfo($"Captured Wedding Ring crafter before quality upgrade: '{__state.CrafterName}' ({__state.CrafterId}), quality {__state.OriginalQuality}.");
        }

        private static void Postfix(RingUpgradeCrafterState __state)
        {
            if (__state == null)
            {
                return;
            }

            if (Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(RestoreUpgradedRingCrafter(__state));
            }
            else if (ReferenceEquals(_activeUpgrade, __state))
            {
                _activeUpgrade = null;
            }
        }

        private static IEnumerator RestoreUpgradedRingCrafter(RingUpgradeCrafterState state)
        {
            // The vanilla upgrade may replace the ItemData after DoCrafting returns.
            // Watch briefly so we restore the original crafter even when AddItem uses
            // an overload not covered by the immediate postfix below.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var inventory = state.Player != null ? state.Player.GetInventory() : null;
                var items = inventory?.GetAllItems();
                var upgradedRing = items?.FirstOrDefault(item => item?.m_dropPrefab != null
                    && item.m_dropPrefab.name == "WeddingTime_Ring"
                    && item.m_quality > state.OriginalQuality
                    && item.m_gridPos.x == state.GridX && item.m_gridPos.y == state.GridY);

                if (upgradedRing != null)
                {
                    upgradedRing.m_crafterID = state.CrafterId;
                    upgradedRing.m_crafterName = state.CrafterName;
                    Plugin.ModLogger?.LogInfo($"Restored Wedding Ring crafter '{state.CrafterName}' ({state.CrafterId}) after quality upgrade to {upgradedRing.m_quality}.");
                    break;
                }

                yield return new WaitForSeconds(0.1f);
            }

            if (ReferenceEquals(_activeUpgrade, state))
            {
                _activeUpgrade = null;
            }
        }

        [HarmonyPatch]
        internal static class InventoryAddItemPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                return typeof(Inventory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == "AddItem"
                        && method.ReturnType == typeof(ItemDrop.ItemData)
                        && method.GetParameters().Length > 0
                        && method.GetParameters()[0].ParameterType == typeof(string)
                        && method.GetParameters()[0].Name == "name"
                        && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(int)
                            && parameter.Name == "quality"));
            }

            private static void Postfix(object[] __args, ItemDrop.ItemData __result)
            {
                var state = _activeUpgrade;
                if (state == null || __result == null || __result.m_dropPrefab == null
                    || __result.m_dropPrefab.name != "WeddingTime_Ring"
                    || __result.m_quality <= state.OriginalQuality)
                {
                    return;
                }

                var args = __args;
                if (args == null || args.Length == 0 || !(args[0] is string prefabName)
                    || prefabName != "WeddingTime_Ring")
                {
                    return;
                }

                __result.m_crafterID = state.CrafterId;
                __result.m_crafterName = state.CrafterName;
                Plugin.ModLogger.LogInfo($"Preserved Wedding Ring crafter '{state.CrafterName}' ({state.CrafterId}) during upgrade.");
            }
        }
    }
}
