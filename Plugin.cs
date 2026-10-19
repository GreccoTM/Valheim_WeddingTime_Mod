using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Extensions;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Collections;
using UnityEngine;

namespace WeddingTime
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ice.weddingtime";
        public const string PluginName = "Wedding Time";
        // Generated from <Version> in WeddingTime.csproj.
        public const string PluginVersion = PluginInfo.Version;

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource ModLogger;
        internal static ConfigEntry<int> DaysTogetherPerLevel { get; private set; }
        internal static ConfigEntry<int> DivorceDelaySeconds { get; private set; }
        private static ConfigEntry<int> _upgradeCoinCost;
        private static ConfigEntry<int> _upgradeJadeCost;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            ModLogger = Logger;
            DaysTogetherPerLevel = Config.BindConfig("Progression", "DaysTogetherPerLevel", 5,
                "Shared in-game days the married couple must spend online together to gain each Wedding Bond level. Synced from the server.",
                true, acceptableValues: new AcceptableValueRange<int>(1, 100));
            DivorceDelaySeconds = Config.BindConfig("Marriage", "DivorceDelaySeconds", 300,
                "Seconds between /divorce confirm and the divorce becoming final. Either partner can cancel with /divorce cancel during this time. 0 makes it immediate. Synced from the server.",
                true, acceptableValues: new AcceptableValueRange<int>(0, 3600));
            _upgradeCoinCost = Config.BindConfig("Upgrade", "CoinCost", 3000,
                "Coins required to upgrade the Wedding Ring at the Forge. Requires a restart after editing.",
                false, acceptableValues: new AcceptableValueRange<int>(0, 100000));
            _upgradeJadeCost = Config.BindConfig("Upgrade", "JadeCost", 1,
                "Jade required to upgrade the Wedding Ring at the Forge. Requires a restart after editing.",
                false, acceptableValues: new AcceptableValueRange<int>(0, 100));

            WeddingText.Register();
            PrefabManager.OnVanillaPrefabsAvailable += RegisterWeddingRing;
            WeddingProposalService.RegisterRpcs();
            WeddingDivorceService.RegisterRpcs();
            WeddingCelebrationService.RegisterRpcs();
            WeddingCommands.Register();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            StartCoroutine(WatchBondState());
            StartCoroutine(WeddingProgression.Watch());
            ModLogger.LogInfo($"Wedding Time {PluginVersion} loaded; waiting for vanilla prefabs to register the wedding ring.");
        }

        private void RegisterWeddingRing()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterWeddingRing;
            WeddingRing.Register(_upgradeCoinCost.Value, _upgradeJadeCost.Value);
        }

        // Equip/unequip patches refresh the bond immediately; this slower loop is a safety net
        // for state that arrives later (world keys, partner changes, quality upgrades).
        private static IEnumerator WatchBondState()
        {
            var wait = new WaitForSeconds(2f);
            while (true)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    WeddingProposalService.PurgeExpiredMarriages();
                    WeddingDivorceService.Tick();
                }
                WeddingProposalService.Reconcile();
                WeddingBond.Refresh();
                yield return wait;
            }
        }

        private void OnDestroy()
        {
            WeddingProgression.Flush();
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterWeddingRing;
            _harmony?.UnpatchSelf();
        }
    }
}
