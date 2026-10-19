using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingCelebrationService
    {
        private const string LevelUpRpcName = "WeddingBondLevelUp";
        private static readonly FieldInfo FireworksEffectsField = AccessTools.Field(typeof(Fireplace), "m_fireworksEffects");
        private static readonly string[] FireworksPrefabNames = { "piece_firepit", "piece_firepit_iron", "fire_pit" };
        private static CustomRPC _levelUpRpc;
        private static object _fireworksEffects;

        internal static void RegisterRpcs()
        {
            if (_levelUpRpc != null) return;
            _levelUpRpc = NetworkManager.Instance.AddRPC(LevelUpRpcName, RPC_IgnoreClientRequest, RPC_ReceiveLevelUp);
            Plugin.ModLogger.LogInfo("Wedding Bond celebration RPC registered.");
        }

        internal static void AnnounceLevelUp(long firstPlayerId, long secondPlayerId, int level)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || _levelUpRpc == null) return;

            if (!WeddingProgression.TryGetOnlinePlayer(firstPlayerId, out var firstName, out var firstPosition))
            {
                firstName = $"Player {firstPlayerId}";
            }
            if (!WeddingProgression.TryGetOnlinePlayer(secondPlayerId, out var secondName, out var secondPosition))
            {
                secondName = $"Player {secondPlayerId}";
            }

            ShowCelebration(firstName, secondName, level, firstPosition, secondPosition);

            var package = new ZPackage();
            package.Write(firstName);
            package.Write(secondName);
            package.Write(level);
            package.Write(firstPosition);
            package.Write(secondPosition);
            var peers = ZNet.instance.GetPeers();
            if (peers != null && peers.Count > 0)
            {
                _levelUpRpc.SendPackage(peers, package);
            }
        }

        private static IEnumerator RPC_IgnoreClientRequest(long senderPeerId, ZPackage package)
        {
            yield return null;
            Plugin.ModLogger.LogWarning($"Ignored client-originated Wedding Bond celebration RPC from peer {senderPeerId}.");
        }

        private static IEnumerator RPC_ReceiveLevelUp(long senderPeerId, ZPackage package)
        {
            yield return null;
            if (ZNet.instance == null || ZNet.instance.IsServer()) yield break;

            try
            {
                var firstName = package.ReadString();
                var secondName = package.ReadString();
                var level = package.ReadInt();
                var firstPosition = package.ReadVector3();
                var secondPosition = package.ReadVector3();
                ShowCelebration(firstName, secondName, level, firstPosition, secondPosition);
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Could not process Wedding Bond level-up celebration: {exception.Message}");
            }
        }

        private static void ShowCelebration(string firstName, string secondName, int level, Vector3 firstPosition, Vector3 secondPosition)
        {
            WeddingText.Show(WeddingText.T("celebration", firstName, secondName, level.ToString()));

            CreateFireworks(firstPosition);
            CreateFireworks(secondPosition);
            Plugin.ModLogger.LogInfo($"Announced Wedding Bond level {level} for {firstName} and {secondName}.");
        }

        private static void CreateFireworks(Vector3 position)
        {
            try
            {
                if (_fireworksEffects == null)
                {
                    foreach (var prefabName in FireworksPrefabNames)
                    {
                        var fireplacePrefab = PrefabManager.Instance.GetPrefab(prefabName);
                        var fireplace = fireplacePrefab != null ? fireplacePrefab.GetComponent<Fireplace>() : null;
                        var effects = fireplace != null && FireworksEffectsField != null
                            ? FireworksEffectsField.GetValue(fireplace)
                            : null;
                        if (effects == null) continue;

                        _fireworksEffects = effects;
                        Plugin.ModLogger.LogInfo($"Loaded vanilla fireplace fireworks effects from '{prefabName}'.");
                        break;
                    }
                }

                if (_fireworksEffects == null)
                {
                    Plugin.ModLogger.LogWarning("The vanilla fireplace fireworks effect list could not be found.");
                    return;
                }

                var createMethods = _fireworksEffects.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == "Create");
                foreach (var method in createMethods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length < 2 || parameters[0].ParameterType != typeof(Vector3)
                        || parameters[1].ParameterType != typeof(Quaternion)) continue;

                    var arguments = new object[parameters.Length];
                    for (var index = 0; index < parameters.Length; index++)
                    {
                        var type = parameters[index].ParameterType;
                        if (type == typeof(Vector3)) arguments[index] = position + Vector3.up * 2f;
                        else if (type == typeof(Quaternion)) arguments[index] = Quaternion.identity;
                        else if (parameters[index].HasDefaultValue) arguments[index] = parameters[index].DefaultValue;
                        else arguments[index] = type.IsValueType ? Activator.CreateInstance(type) : null;
                    }

                    method.Invoke(_fireworksEffects, arguments);
                    return;
                }

                Plugin.ModLogger.LogWarning("No compatible Create method was found for the vanilla fireplace fireworks effect list.");
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogWarning($"Could not create vanilla fireworks: {exception.Message}");
            }
        }
    }
}
