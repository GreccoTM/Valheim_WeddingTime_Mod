using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeddingTime
{
    // A warm light around every player whose Wedding Bond is active at level 10, visible to all clients.
    // Event driven, with no polling: each player object reads the published bond state once when it
    // spawns on a client, and the owner broadcasts an RPC on that object whenever the state changes.
    internal sealed class WeddingGlow : MonoBehaviour
    {
        internal const string StateRpc = "WeddingTime_BondState";
        private static readonly bool Headless = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        // Fraction of the Wisplight's mist-clearing radius.
        private const float DemisterRangeFactor = 0.6f;
        private static ParticleSystemForceField _wisplightField;

        private ZNetView _view;
        private GameObject _light;

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class AttachToPlayer
        {
            private static void Postfix(Player __instance)
            {
                var view = __instance.GetComponent<ZNetView>();
                // The character preview at the main menu has no network object.
                if (view == null || view.GetZDO() == null) return;

                var glow = __instance.gameObject.AddComponent<WeddingGlow>();
                glow._view = view;
                // Registered on servers too, so the broadcast never logs a missing RPC there.
                view.Register<int>(StateRpc, glow.RPC_BondState);
            }
        }

        private void Start()
        {
            if (_view != null && _view.IsValid()) Apply(WeddingBond.ReadState(_view));
        }

        private void RPC_BondState(long sender, int state)
        {
            if (_view != null && _view.IsValid() && sender == _view.GetZDO().GetOwner()) Apply(state);
        }

        private void Apply(int state)
        {
            if (Headless) return;

            var enabled = WeddingBond.StateLevel(state) >= WeddingKeys.MaximumLevel;
            if (enabled && _light == null) _light = CreateLight();
            if (_light != null && _light.activeSelf != enabled) _light.SetActive(enabled);
        }

        private GameObject CreateLight()
        {
            var glowObject = new GameObject("WeddingTime_Lv10_Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            var glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.84f, 0.64f, 1f);
            glow.intensity = 1.35f;
            glow.range = 9f;
            glow.shadows = LightShadows.None;
            AddDemister(glowObject.transform);
            return glowObject;
        }

        // Clears Mistlands mist like a smaller Wisplight. The field is copied from the Wisplight's own
        // demister so it pushes the mist particles the same way; deactivating the glow also unregisters it.
        private static void AddDemister(Transform parent)
        {
            var template = FindWisplightField();
            if (template == null) return;

            var fieldObject = new GameObject("WeddingTime_Lv10_Demister") { layer = template.gameObject.layer };
            fieldObject.transform.SetParent(parent, false);
            var field = fieldObject.AddComponent<ParticleSystemForceField>();
            field.shape = template.shape;
            field.startRange = template.startRange * DemisterRangeFactor;
            field.endRange = template.endRange * DemisterRangeFactor;
            field.length = template.length * DemisterRangeFactor;
            field.directionX = template.directionX;
            field.directionY = template.directionY;
            field.directionZ = template.directionZ;
            field.gravity = template.gravity;
            field.gravityFocus = template.gravityFocus;
            field.rotationSpeed = template.rotationSpeed;
            field.rotationAttraction = template.rotationAttraction;
            field.rotationRandomness = template.rotationRandomness;
            field.drag = template.drag;
            field.multiplyDragByParticleSize = template.multiplyDragByParticleSize;
            field.multiplyDragByParticleVelocity = template.multiplyDragByParticleVelocity;
            // Demister.Awake picks up the force field, so it must be added after it.
            fieldObject.AddComponent<Demister>();
        }

        private static ParticleSystemForceField FindWisplightField()
        {
            if (_wisplightField != null || ObjectDB.instance == null) return _wisplightField;

            foreach (var effect in ObjectDB.instance.m_StatusEffects)
            {
                var demister = effect is SE_Demister wisplight && wisplight.m_ballPrefab != null
                    ? wisplight.m_ballPrefab.GetComponentInChildren<Demister>(true)
                    : null;
                var field = demister != null ? demister.GetComponent<ParticleSystemForceField>() : null;
                if (field == null) continue;

                _wisplightField = field;
                Plugin.ModLogger.LogDebug($"Level 10 demister uses {DemisterRangeFactor:P0} of the Wisplight range ({field.endRange * DemisterRangeFactor:0.#} m).");
                break;
            }

            if (_wisplightField == null) Plugin.ModLogger.LogWarning("Wisplight demister not found; the level 10 glow will not clear mist.");
            return _wisplightField;
        }
    }
}
