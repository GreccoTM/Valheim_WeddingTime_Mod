using System.Reflection;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace WeddingTime
{
    internal static class WeddingRing
    {
        internal const string PrefabName = "WeddingTime_Ring";
        private static readonly Color Gold = new Color(0.83f, 0.62f, 0.22f, 1f);
        private static bool _registered;

        internal static bool IsWeddingRing(ItemDrop.ItemData item)
        {
            return item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName;
        }

        internal static void Register(int upgradeCoinCost, int upgradeJadeCost)
        {
            if (_registered) return;

            var icon = LoadIcon("WeddingTime.wedding_ring_icon.png", "WeddingTime_Ring_Icon");
            // Amount applies to crafting (quality 1); AmountPerLevel to each quality upgrade. A zero
            // amount is hidden by the crafting UI, so crafting shows only Bronze and Amber and the
            // upgrade only Coins and Jade.
            var config = new ItemConfig
            {
                Name = "$weddingtime_ring_name",
                Description = "$weddingtime_ring_description",
                StackSize = 1,
                CraftingStation = "forge",
                Icon = icon,
                Requirements = new[]
                {
                    new RequirementConfig("Bronze", 1),
                    new RequirementConfig("Amber", 1),
                    new RequirementConfig("Coins", 0, upgradeCoinCost, false),
                    new RequirementConfig("GemstoneGreen", 0, upgradeJadeCost, false)
                }
            };

            WeddingBond.RegisterStatusEffect(icon);

            var ring = new CustomItem(PrefabName, "Amber", config);
            var shared = ring.ItemDrop.m_itemData.m_shared;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Utility;
            shared.m_maxQuality = 2;
            // Zero value keeps traders (e.g. Haldor) from listing the ring as sellable.
            shared.m_value = 0;
            var upgradedIcon = LoadIcon("WeddingTime.wedding_ring_icon_upgraded.png", "WeddingTime_Ring_Upgraded_Icon");
            if (icon != null && upgradedIcon != null)
            {
                shared.m_icons = new[] { icon, upgradedIcon };
            }
            SetDroppedRingModel(ring.ItemPrefab);

            if (!ItemManager.Instance.AddItem(ring))
            {
                Plugin.ModLogger.LogError("Could not register the Wedding Ring.");
                return;
            }

            _registered = true;
            Plugin.ModLogger.LogInfo($"Wedding Ring registered as a quality 2 Utility item. Its Forge upgrade costs {upgradeCoinCost} Coins and {upgradeJadeCost} Jade; the upgraded ring doubles Wedding Bond effects.");
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
                if (material.HasProperty("_Color")) material.SetColor("_Color", Gold);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.72f);
            }
            else
            {
                var shader = Shader.Find("Standard");
                if (shader != null)
                {
                    material = new Material(shader) { color = Gold };
                    material.SetFloat("_Metallic", 0.72f);
                }
            }

            var model = new GameObject("WeddingTime_Ring_Model");
            model.transform.SetParent(itemPrefab.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = CreateRingMesh();
            var meshRenderer = model.AddComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }
            else
            {
                Plugin.ModLogger.LogWarning("No compatible shader was found for the custom Wedding Ring world model.");
            }
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

        private static Sprite LoadIcon(string resourceName, string textureName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Plugin.ModLogger.LogError($"Embedded asset not found: {resourceName}");
                    return null;
                }

                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0) break;
                    read += count;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes))
                {
                    Plugin.ModLogger.LogError($"Could not decode the embedded icon {resourceName}.");
                    return null;
                }

                texture.name = textureName;
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
