using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeddingTime
{
    // The Wedding Bond status effect on the local player, and the bond state other clients can see.
    internal static class WeddingBond
    {
        // Published on the owner's player ZDO: 0 = no bond, otherwise level + EnhancedFlag when upgraded.
        // Other clients cannot see a remote player's inventory or status effects, only its ZDO.
        private static readonly int StateZdoKey = StringExtensionMethods.GetStableHashCode("weddingtime_bond_state");
        private const int EnhancedFlag = 100;
        private static readonly MethodInfo UpdateFoodMethod = AccessTools.Method(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) });

        private static SE_Stats _buff;
        private static int _buffHash;
        private static int _configuredLevel = -1;
        private static bool _configuredEnhanced;
        private static Player _configuredFor;

        internal static bool DebugMarriageEnabled { get; private set; }

        internal static void RegisterStatusEffect(Sprite icon)
        {
            _buff = ScriptableObject.CreateInstance<SE_Stats>();
            _buff.name = "SE_WeddingTime_Bond";
            _buff.m_icon = icon;
            _buff.m_ttl = 0f;
            Configure(1, false);
            _buffHash = StringExtensionMethods.GetStableHashCode(_buff.name);

            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(_buff, false)))
            {
                Plugin.ModLogger.LogError("Could not register the Wedding Bond status effect.");
            }
        }

        internal static void SetDebugMarriage(bool enabled)
        {
            DebugMarriageEnabled = enabled;
            Refresh();
        }

        internal static bool IsBondEffect(StatusEffect effect) => effect != null && _buffHash != 0 && effect.NameHash() == _buffHash;

        // Only meaningful for the local player; other clients read the published state instead.
        internal static bool HasBond(Player player)
        {
            return player != null && player == Player.m_localPlayer && _buffHash != 0
                && player.GetSEMan() != null && player.GetSEMan().GetStatusEffect(_buffHash) != null;
        }

        internal static bool IsEnhanced(Player player) => player != null && player == Player.m_localPlayer && _configuredEnhanced;

        // Bond level encoded in a published state, or 0 when the bond is off.
        internal static int StateLevel(int state) => state > EnhancedFlag ? state - EnhancedFlag : Math.Max(0, state);

        // Adds or removes the bond on the local player. Equip/unequip patches call this immediately;
        // a slower loop calls it as a safety net for state that arrives later.
        internal static void Refresh()
        {
            var player = Player.m_localPlayer;
            if (player == null || _buffHash == 0 || player.GetInventory() == null || player.GetSEMan() == null) return;

            var partnerId = WeddingProposalService.MarriedPartnerId;
            var ringEquipped = false;
            var partnerRingEquipped = false;
            var enhancedRingEquipped = false;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (!item.m_equipped || !WeddingRing.IsWeddingRing(item)) continue;

                ringEquipped = true;
                // Mods that allow several utility items can equip two rings; the best qualifying one wins.
                var qualifies = partnerId != 0 && item.m_crafterID == partnerId;
                partnerRingEquipped |= qualifies;
                if ((qualifies || DebugMarriageEnabled) && item.m_quality >= 2) enhancedRingEquipped = true;
            }

            var shouldHaveBuff = partnerRingEquipped || (DebugMarriageEnabled && ringEquipped);
            var useEnhancedBuff = shouldHaveBuff && enhancedRingEquipped;
            var seMan = player.GetSEMan();
            var hasBuff = seMan.GetStatusEffect(_buffHash) != null;
            var level = Math.Max(1, WeddingProgression.CurrentLevel);
            var effectChanged = false;
            if (_configuredLevel != level || _configuredEnhanced != useEnhancedBuff || _configuredFor != player)
            {
                // Rebuilt per character as well, so the text follows the language chosen at the main menu.
                Configure(level, useEnhancedBuff);
                _configuredFor = player;
                if (hasBuff)
                {
                    seMan.RemoveStatusEffect(_buffHash);
                    hasBuff = false;
                    effectChanged = true;
                }
            }

            if (shouldHaveBuff && !hasBuff)
            {
                seMan.AddStatusEffect(_buffHash);
                effectChanged = true;
                Plugin.ModLogger.LogInfo(partnerRingEquipped
                    ? $"Wedding Bond activated for {player.GetPlayerName()} using their partner's ring."
                    : $"Debug Wedding Bond activated for {player.GetPlayerName()}.");
            }
            else if (!shouldHaveBuff && hasBuff)
            {
                seMan.RemoveStatusEffect(_buffHash);
                effectChanged = true;
                Plugin.ModLogger.LogInfo($"Wedding Bond removed from {player.GetPlayerName()}.");
            }

            if (effectChanged) UpdateFoodMethod?.Invoke(player, new object[] { 0f, true });
            PublishState(player, shouldHaveBuff ? level + (useEnhancedBuff ? EnhancedFlag : 0) : 0);
        }

        private static void PublishState(Player player, int state)
        {
            var view = player.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;

            var zdo = view.GetZDO();
            if (zdo.GetInt(StateZdoKey) == state) return;

            // The ZDO covers clients that load this player later; the RPC updates the ones that already have it.
            zdo.Set(StateZdoKey, state);
            view.InvokeRPC(ZNetView.Everybody, WeddingGlow.StateRpc, state);
        }

        internal static int ReadState(ZNetView view)
        {
            return view != null && view.IsValid() ? view.GetZDO().GetInt(StateZdoKey) : 0;
        }

        private static void Configure(int level, bool enhanced)
        {
            if (_buff == null) return;
            _configuredLevel = Math.Max(1, Math.Min(WeddingKeys.MaximumLevel, level));
            _configuredEnhanced = enhanced;
            var factor = enhanced ? 2f : 1f;
            _buff.m_staminaRegenMultiplier = _configuredLevel >= 1 ? 1f + 0.50f * factor : 1f;
            _buff.m_speedModifier = _configuredLevel >= 2 ? 0.20f * factor : 0f;
            _buff.m_addMaxCarryWeight = _configuredLevel >= 3 ? 50f * factor : 0f;
            _buff.m_healthRegenMultiplier = _configuredLevel >= 4 ? 1f + 0.20f * factor : 1f;
            _buff.m_eitrRegenMultiplier = _configuredLevel >= 6 ? 1f + 0.50f * factor : 1f;
            // SE_Stats adds base * modifier to the cost, so these are deltas (-0.25 = 25% cheaper), not multipliers.
            var staminaCostModifier = _configuredLevel >= 7 ? -0.25f * factor : 0f;
            _buff.m_runStaminaDrainModifier = staminaCostModifier;
            _buff.m_homeItemStaminaUseModifier = staminaCostModifier;
            _buff.m_attackStaminaUseModifier = staminaCostModifier;
            _buff.m_blockStaminaUseModifier = staminaCostModifier;
            _buff.m_dodgeStaminaUseModifier = staminaCostModifier;
            _buff.m_swimStaminaUseModifier = staminaCostModifier;
            _buff.m_jumpStaminaUseModifier = staminaCostModifier;
            _buff.m_sneakStaminaUseModifier = staminaCostModifier;
            _buff.m_staggerModifier = _configuredLevel >= 10 ? -0.50f * factor : 0f;

            // Per-level amounts before doubling, in the order of the effect_<level> text keys.
            var amounts = new[] { 50, 20, 50, 20, 25, 50, 25, 25, 15, 50 };
            var doubled = enhanced ? WeddingText.T("effect_doubled") : string.Empty;
            var effects = new List<string>();
            for (var effectLevel = 1; effectLevel <= _configuredLevel; effectLevel++)
            {
                var amount = (amounts[effectLevel - 1] * factor).ToString("0");
                effects.Add(WeddingText.T("effect_" + effectLevel, amount) + doubled);
            }

            _buff.m_name = WeddingText.T("bond_name") + "\n" + WeddingText.T("bond_level", _configuredLevel.ToString());
            _buff.m_tooltip = string.Join("\n", effects);
            Plugin.ModLogger.LogDebug($"Configured Wedding Bond effects for level {_configuredLevel}.");
        }
    }
}
