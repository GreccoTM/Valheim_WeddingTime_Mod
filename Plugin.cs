using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Extensions;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
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
        public const string PluginVersion = "0.5.11";
        internal const string RingPrefabName = "WeddingTime_Ring";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource ModLogger;
        internal static bool DebugMarriageEnabled { get; private set; }
        internal static int WeddingBuffHash { get; private set; }
        internal static SE_Stats WeddingBuff { get; private set; }
        internal static bool WeddingBuffEnhanced => _configuredBuffEnhanced;
        internal static ConfigEntry<int> DaysTogetherPerLevel { get; private set; }
        private static ConfigEntry<int> _upgradeCoinCost;
        private static ConfigEntry<int> _upgradeJadeCost;
        private static readonly MethodInfo UpdateFoodMethod = AccessTools.Method(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) });
        private Harmony _harmony;
        private static readonly System.Collections.Generic.Dictionary<long, GameObject> WeddingGlows = new System.Collections.Generic.Dictionary<long, GameObject>();
        private bool _weddingRingRegistered;
        private static int _configuredBuffLevel = -1;
        private static bool _configuredBuffEnhanced;

        private void Awake()
        {
            Instance = this;
            ModLogger = Logger;
            DaysTogetherPerLevel = Config.BindConfig("Progression", "DaysTogetherPerLevel", 5,
                "Shared in-game days the married couple must spend online together to gain each Wedding Bond level. Synced from the server.",
                true, acceptableValues: new AcceptableValueRange<int>(1, 100));
            _upgradeCoinCost = Config.BindConfig("Upgrade", "CoinCost", 3000,
                "Coins required to upgrade the Wedding Ring at the Forge. Requires a restart after editing.",
                false, acceptableValues: new AcceptableValueRange<int>(0, 100000));
            _upgradeJadeCost = Config.BindConfig("Upgrade", "JadeCost", 1,
                "Jade required to upgrade the Wedding Ring at the Forge. Requires a restart after editing.",
                false, acceptableValues: new AcceptableValueRange<int>(0, 100));
            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += RegisterWeddingRing;
            CommandManager.Instance.AddConsoleCommand(new DebugMarriageCommand());
            CommandManager.Instance.AddConsoleCommand(new DebugWeddingLevelCommand());
            CommandManager.Instance.AddConsoleCommand(new WeddingTimeCommand());
            CommandManager.Instance.AddConsoleCommand(new DoYouMarryMeCommand());
            CommandManager.Instance.AddConsoleCommand(new IDoCommand());
            CommandManager.Instance.AddConsoleCommand(new IDoNotCommand());
            WeddingProposalService.RegisterRpcs();
            WeddingCelebrationService.RegisterRpcs();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            StartCoroutine(WatchBondState());
            StartCoroutine(WeddingProgression.Watch());
            StartCoroutine(WatchWeddingGlows());
            ModLogger.LogInfo("Wedding Time loaded; waiting for vanilla prefabs to register the wedding ring.");
            ModLogger.LogInfo($"Wedding Bond progression is configured for {DaysTogetherPerLevel.Value} shared in-game days per level.");
        }

        private static void AddLocalization()
        {
            var localization = LocalizationManager.Instance.GetLocalization();
            var english = "English";
            localization.AddTranslation(english, new System.Collections.Generic.Dictionary<string, string>
            {
                { "weddingtime_ring_name", "Wedding Ring" },
                { "weddingtime_ring_description", "Crafted for someone special. After accepting a proposal, equip the ring made by your partner to share the Wedding Bond." }
            });
            var portuguese = "Portuguese_Brazilian";
            localization.AddTranslation(portuguese, new System.Collections.Generic.Dictionary<string, string>
            {
                { "weddingtime_ring_name", "Aliança de Casamento" },
                { "weddingtime_ring_description", "Feita para alguém especial. Depois de aceitar um pedido, equipe a aliança feita pelo seu parceiro para compartilhar o Laço do Casamento." }
            });
        }

        private void RegisterWeddingRing()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterWeddingRing;
            if (_weddingRingRegistered)
            {
                return;
            }

            var config = new ItemConfig
            {
                Name = "$weddingtime_ring_name",
                Description = "$weddingtime_ring_description",
                StackSize = 1,
                CraftingStation = "forge",
                Requirements = new[]
                {
                    new RequirementConfig("Bronze", 1),
                    new RequirementConfig("Amber", 1)
                }
            };
            config.Icon = LoadRingIcon();

            WeddingBuff = ScriptableObject.CreateInstance<SE_Stats>();
            WeddingBuff.name = "SE_WeddingTime_Bond";
            WeddingBuff.m_name = "Wedding Bond";
            WeddingBuff.m_icon = config.Icon;
            WeddingBuff.m_ttl = 0f;
            ConfigureWeddingBuff(1, false);
            WeddingBuffHash = StringExtensionMethods.GetStableHashCode(WeddingBuff.name);

            if (!ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(WeddingBuff, false)))
            {
                ModLogger.LogError("Could not register the Wedding Bond status effect.");
            }

            var ring = new CustomItem(RingPrefabName, "Amber", config);
            ring.ItemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;
            ring.ItemDrop.m_itemData.m_shared.m_maxQuality = 2;
            // Zero value keeps traders (e.g. Haldor) from listing the ring as sellable.
            ring.ItemDrop.m_itemData.m_shared.m_value = 0;
            var upgradedIcon = LoadUpgradedRingIcon();
            if (upgradedIcon != null)
            {
                ring.ItemDrop.m_itemData.m_shared.m_icons = new[] { config.Icon, upgradedIcon };
            }
            SetDroppedRingModel(ring.ItemPrefab);
            if (ItemManager.Instance.AddItem(ring))
            {
                _weddingRingRegistered = true;
                StartCoroutine(ConfigureRingUpgradeWhenObjectDbReady(ring.ItemPrefab.name));
                ModLogger.LogInfo($"Wedding Ring registered as a quality 2 Utility item. Its Forge upgrade costs {_upgradeCoinCost.Value} Coins and {_upgradeJadeCost.Value} Jade; the upgraded ring doubles Wedding Bond effects.");
            }
            else
            {
                ModLogger.LogError("Could not register the Wedding Ring.");
            }
        }

        private IEnumerator ConfigureRingUpgradeWhenObjectDbReady(string prefabName)
        {
            const int maxAttempts = 120;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var database = ObjectDB.instance;
                if (database != null && database.m_recipes != null)
                {
                    var recipe = database.m_recipes.Find(candidate => candidate != null
                        && candidate.m_item != null
                        && candidate.m_item.name == prefabName);
                    var coinsPrefab = database.GetItemPrefab("Coins");
                    var jadePrefab = database.GetItemPrefab("GemstoneGreen");
                    var coins = coinsPrefab != null ? coinsPrefab.GetComponent<ItemDrop>() : null;
                    var jade = jadePrefab != null ? jadePrefab.GetComponent<ItemDrop>() : null;

                    if (recipe != null && coins != null && jade != null)
                    {
                        var requirements = new System.Collections.Generic.List<Piece.Requirement>(recipe.m_resources ?? Array.Empty<Piece.Requirement>());
                        requirements.RemoveAll(requirement => requirement.m_resItem == coins || requirement.m_resItem == jade);
                        requirements.Add(new Piece.Requirement
                        {
                            m_resItem = coins,
                            m_amount = 0,
                            m_amountPerLevel = _upgradeCoinCost.Value,
                            m_recover = false
                        });
                        requirements.Add(new Piece.Requirement
                        {
                            m_resItem = jade,
                            m_amount = 0,
                            m_amountPerLevel = _upgradeJadeCost.Value,
                            m_recover = false
                        });
                        recipe.m_resources = requirements.ToArray();
                        ModLogger.LogInfo($"Wedding Ring upgrade requirements applied to the live ObjectDB recipe: {_upgradeCoinCost.Value} Coins and {_upgradeJadeCost.Value} Jade for quality 2.");
                        yield break;
                    }
                }

                yield return new WaitForSeconds(0.5f);
            }

            ModLogger.LogError("Could not find the live Wedding Ring recipe, Coins, or Jade in ObjectDB; the ring upgrade will have no configured material cost.");
        }

        internal static void SetDebugMarriage(bool enabled)
        {
            DebugMarriageEnabled = enabled;
            RefreshDebugBuff();
        }

        // Equip/unequip patches refresh the bond immediately; this slower loop is a safety net
        // for state that arrives later (world keys, partner changes, quality upgrades).
        private IEnumerator WatchBondState()
        {
            var wait = new WaitForSeconds(2f);
            while (true)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) WeddingProposalService.PurgeExpiredMarriages();
                WeddingProposalService.RestoreFromWorld();
                RefreshDebugBuff();
                yield return wait;
            }
        }

        private IEnumerator WatchWeddingGlows()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                var activePlayers = new System.Collections.Generic.HashSet<long>();
                foreach (var player in Player.GetAllPlayers())
                {
                    if (player == null) continue;
                    var playerId = player.GetPlayerID();
                    var shouldGlow = WeddingProgression.GetLevelForPlayer(player) >= 10 && HasWeddingBond(player);
                    if (!shouldGlow)
                    {
                        if (WeddingGlows.TryGetValue(playerId, out var oldGlow))
                        {
                            if (oldGlow != null) Destroy(oldGlow);
                            WeddingGlows.Remove(playerId);
                        }
                        continue;
                    }

                    activePlayers.Add(playerId);
                    if (WeddingGlows.TryGetValue(playerId, out var existingGlow) && existingGlow != null) continue;

                    var glowObject = new GameObject("WeddingTime_Lv10_Glow");
                    glowObject.transform.SetParent(player.transform, false);
                    glowObject.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                    var glow = glowObject.AddComponent<Light>();
                    glow.type = LightType.Point;
                    glow.color = new Color(1f, 0.84f, 0.64f, 1f);
                    glow.intensity = 1.35f;
                    glow.range = 9f;
                    glow.shadows = LightShadows.None;
                    WeddingGlows[playerId] = glowObject;
                    ModLogger.LogInfo($"Level 10 Wedding Bond glow enabled for {player.GetPlayerName()}.");
                }

                foreach (var playerId in WeddingGlows.Keys.Where(playerId => !activePlayers.Contains(playerId)).ToList())
                {
                    var glowObject = WeddingGlows[playerId];
                    if (glowObject != null) Destroy(glowObject);
                    WeddingGlows.Remove(playerId);
                }

                yield return wait;
            }
        }

        private void OnDestroy()
        {
            WeddingProgression.Flush();
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterWeddingRing;
            _harmony?.UnpatchSelf();
            foreach (var glow in WeddingGlows.Values)
            {
                if (glow != null) Destroy(glow);
            }
            WeddingGlows.Clear();
        }

        internal static bool IsWeddingRing(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == RingPrefabName;
        }

        internal static bool HasWeddingBond(Player player)
        {
            return player != null && ((WeddingBuffHash != 0 && player.GetSEMan() != null
                && player.GetSEMan().GetStatusEffect(WeddingBuffHash) != null)
                || WeddingProgression.HasPartnerRingEquipped(player));
        }

        internal static bool IsWeddingBondEnhanced(Player player)
        {
            return player == Player.m_localPlayer
                ? _configuredBuffEnhanced
                : WeddingProgression.HasEnhancedPartnerRingEquipped(player);
        }

        internal static void RefreshDebugBuff()
        {
            var player = Player.m_localPlayer;
            if (player == null || WeddingBuffHash == 0 || player.GetInventory() == null || player.GetSEMan() == null)
            {
                return;
            }

            var ringEquipped = false;
            var partnerRingEquipped = false;
            var enhancedRingEquipped = false;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (item.m_equipped && IsWeddingRing(item))
                {
                    ringEquipped = true;
                    if (WeddingProposalService.EngagedPartnerId != 0
                        && item.m_crafterID == WeddingProposalService.EngagedPartnerId)
                    {
                        partnerRingEquipped = true;
                        enhancedRingEquipped = item.m_quality >= 2;
                    }
                    else if (DebugMarriageEnabled)
                    {
                        enhancedRingEquipped = item.m_quality >= 2;
                    }
                }
            }

            var shouldHaveBuff = (WeddingProposalService.EngagedPartnerId != 0 && partnerRingEquipped)
                || (DebugMarriageEnabled && ringEquipped);
            var hasBuff = player.GetSEMan().GetStatusEffect(WeddingBuffHash) != null;
            var level = Math.Max(1, WeddingProgression.CurrentLevel);
            var useEnhancedBuff = shouldHaveBuff && enhancedRingEquipped;
            var effectChanged = false;
            if (_configuredBuffLevel != level || _configuredBuffEnhanced != useEnhancedBuff)
            {
                ConfigureWeddingBuff(level, useEnhancedBuff);
                if (hasBuff)
                {
                    player.GetSEMan().RemoveStatusEffect(WeddingBuffHash);
                    hasBuff = false;
                    effectChanged = true;
                }
            }

            if (shouldHaveBuff && !hasBuff)
            {
                player.GetSEMan().AddStatusEffect(WeddingBuffHash);
                effectChanged = true;
                ModLogger.LogInfo(partnerRingEquipped
                    ? $"Wedding Bond activated for {player.GetPlayerName()} using their partner's ring."
                    : $"Debug Wedding Bond activated for {player.GetPlayerName()}.");
            }
            else if (!shouldHaveBuff && hasBuff)
            {
                player.GetSEMan().RemoveStatusEffect(WeddingBuffHash);
                effectChanged = true;
                ModLogger.LogInfo($"Wedding Bond removed from {player.GetPlayerName()}.");
            }

            if (effectChanged) RefreshMaxStats(player);
        }

        private static void RefreshMaxStats(Player player)
        {
            UpdateFoodMethod?.Invoke(player, new object[] { 0f, true });
        }

        private static void ConfigureWeddingBuff(int level, bool enhanced)
        {
            if (WeddingBuff == null) return;
            _configuredBuffLevel = Math.Max(1, Math.Min(10, level));
            _configuredBuffEnhanced = enhanced;
            var factor = enhanced ? 2f : 1f;
            WeddingBuff.m_staminaRegenMultiplier = _configuredBuffLevel >= 1 ? 1f + 0.50f * factor : 1f;
            WeddingBuff.m_speedModifier = _configuredBuffLevel >= 2 ? 0.20f * factor : 0f;
            WeddingBuff.m_addMaxCarryWeight = _configuredBuffLevel >= 3 ? 50f * factor : 0f;
            WeddingBuff.m_healthRegenMultiplier = _configuredBuffLevel >= 4 ? 1f + 0.20f * factor : 1f;
            WeddingBuff.m_eitrRegenMultiplier = _configuredBuffLevel >= 6 ? 1f + 0.50f * factor : 1f;
            var staminaCostMultiplier = _configuredBuffLevel >= 7 ? 1f - 0.25f * factor : 1f;
            WeddingBuff.m_runStaminaDrainModifier = staminaCostMultiplier;
            WeddingBuff.m_homeItemStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_attackStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_blockStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_dodgeStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_swimStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_jumpStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_sneakStaminaUseModifier = staminaCostMultiplier;
            WeddingBuff.m_staggerModifier = _configuredBuffLevel >= 10 ? -0.50f * factor : 0f;
            var effects = new System.Collections.Generic.List<string>();
            var doubleText = enhanced ? " (doubled by the upgraded ring)" : string.Empty;
            if (_configuredBuffLevel >= 1) effects.Add($"Lv1: +{50 * factor}% stamina regeneration{doubleText}");
            if (_configuredBuffLevel >= 2) effects.Add($"Lv2: +{20 * factor}% movement speed{doubleText}");
            if (_configuredBuffLevel >= 3) effects.Add($"Lv3: +{50 * factor} carry weight{doubleText}");
            if (_configuredBuffLevel >= 4) effects.Add($"Lv4: +{20 * factor}% health regeneration{doubleText}");
            if (_configuredBuffLevel >= 5) effects.Add($"Lv5: {25 * factor}% less fire, frost, lightning, poison, and spirit damage{doubleText}");
            if (_configuredBuffLevel >= 6) effects.Add($"Lv6: +{50 * factor}% Eitr regeneration{doubleText}");
            if (_configuredBuffLevel >= 7) effects.Add($"Lv7: {25 * factor}% less stamina cost{doubleText}");
            if (_configuredBuffLevel >= 8) effects.Add($"Lv8: +{25 * factor} to all skills{doubleText}");
            if (_configuredBuffLevel >= 9) effects.Add($"Lv9: +{15 * factor} maximum health, stamina, and Eitr{doubleText}");
            if (_configuredBuffLevel >= 10) effects.Add($"Lv10: {50 * factor}% less stagger{doubleText}");
            WeddingBuff.m_name = $"Wedding Bond\nLv. {_configuredBuffLevel}/10";
            WeddingBuff.m_tooltip = string.Join("\n", effects);
            Plugin.ModLogger.LogDebug($"Configured Wedding Bond effects for level {_configuredBuffLevel}.");
        }

        private static void SetDroppedRingModel(GameObject itemPrefab)
        {
            var originalRenderer = itemPrefab.GetComponentInChildren<MeshRenderer>(true);
            var material = originalRenderer != null && originalRenderer.sharedMaterial != null
                ? new Material(originalRenderer.sharedMaterial)
                : null;

            foreach (var renderer in itemPrefab.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            if (material != null)
            {
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", new Color(0.83f, 0.62f, 0.22f, 1f));
                }

                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", 0.72f);
                }
            }
            else
            {
                var shader = Shader.Find("Standard");
                if (shader != null)
                {
                    material = new Material(shader);
                    material.color = new Color(0.83f, 0.62f, 0.22f, 1f);
                    material.SetFloat("_Metallic", 0.72f);
                }
            }

            var model = new GameObject("WeddingTime_Ring_Model");
            model.transform.SetParent(itemPrefab.transform, false);
            var filter = model.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateRingMesh();
            var meshRenderer = model.AddComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }
            else
            {
                ModLogger.LogWarning("No compatible shader was found for the custom Wedding Ring world model.");
            }

            ModLogger.LogInfo("Created a custom gold ring model for the dropped item.");
        }

        private static Mesh CreateRingMesh()
        {
            const int segments = 48;
            const int sides = 12;
            const float radius = 0.12f;
            const float tubeRadius = 0.022f;
            var vertices = new Vector3[(segments + 1) * (sides + 1)];
            var triangles = new int[segments * sides * 6];

            for (var segment = 0; segment <= segments; segment++)
            {
                var around = 2f * Mathf.PI * segment / segments;
                var cosAround = Mathf.Cos(around);
                var sinAround = Mathf.Sin(around);
                for (var side = 0; side <= sides; side++)
                {
                    var tubeAngle = 2f * Mathf.PI * side / sides;
                    var tube = radius + tubeRadius * Mathf.Cos(tubeAngle);
                    vertices[segment * (sides + 1) + side] = new Vector3(
                        tube * cosAround,
                        tubeRadius * Mathf.Sin(tubeAngle),
                        tube * sinAround);
                }
            }

            var triangle = 0;
            for (var segment = 0; segment < segments; segment++)
            {
                for (var side = 0; side < sides; side++)
                {
                    var current = segment * (sides + 1) + side;
                    var nextSegment = (segment + 1) * (sides + 1) + side;
                    triangles[triangle++] = current;
                    triangles[triangle++] = current + 1;
                    triangles[triangle++] = nextSegment;
                    triangles[triangle++] = current + 1;
                    triangles[triangle++] = nextSegment + 1;
                    triangles[triangle++] = nextSegment;
                }
            }

            var mesh = new Mesh { name = "WeddingTime_Ring_Mesh" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Sprite LoadRingIcon()
        {
            return LoadIcon("WeddingTime.wedding_ring_icon.png", "WeddingTime_Ring_Icon");
        }

        private Sprite LoadUpgradedRingIcon()
        {
            return LoadIcon("WeddingTime.wedding_ring_icon_upgraded.png", "WeddingTime_Ring_Upgraded_Icon");
        }

        private Sprite LoadIcon(string resourceName, string textureName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    ModLogger.LogError($"Embedded asset not found: {resourceName}");
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    ModLogger.LogError("Could not decode the Wedding Ring icon.");
                    return null;
                }

                texture.name = textureName;
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
