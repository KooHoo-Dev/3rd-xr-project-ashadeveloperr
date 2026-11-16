using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Untrodden.World
{
    /// <summary>Static set pieces: the sea, the destination village, hunter cabins, invisible world edges.</summary>
    public static class WorldDressing
    {
        public static void BuildSea(Transform parent, GameAssets assets)
        {
            var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.SetParent(parent, false);
            var depth = WorldMap.Length - (WorldMap.CoastZ - 60f) + 200f;
            sea.transform.position = new Vector3(WorldMap.Width * 0.5f, WorldMap.SeaLevel - 0.05f, WorldMap.CoastZ - 60f + depth * 0.5f);
            sea.transform.localScale = new Vector3((WorldMap.Width + 400f) / 10f, 1f, depth / 10f);
            var r = sea.GetComponent<MeshRenderer>();
            r.sharedMaterial = assets.sea;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public static void BuildBounds(Transform parent)
        {
            var root = new GameObject("World Bounds").transform;
            root.SetParent(parent, false);
            void Wall(Vector3 center, Vector3 size)
            {
                var go = new GameObject("Wall");
                go.transform.SetParent(root, false);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }

            const float h = 400f;
            Wall(new Vector3(-1f, 0f, WorldMap.Length * 0.5f), new Vector3(2f, h, WorldMap.Length));
            Wall(new Vector3(WorldMap.Width + 1f, 0f, WorldMap.Length * 0.5f), new Vector3(2f, h, WorldMap.Length));
            Wall(new Vector3(WorldMap.Width * 0.5f, 0f, -1f), new Vector3(WorldMap.Width, h, 2f));
            // A little way into the sea: you can wade to the shore but not swim off the map.
            Wall(new Vector3(WorldMap.Width * 0.5f, 0f, WorldMap.CoastZ + 25f), new Vector3(WorldMap.Width, h, 2f));
        }

        public static Transform BuildVillage(Transform parent, WorldMap world, GameAssets assets)
        {
            var root = new GameObject("Village").transform;
            root.SetParent(parent, false);
            var c = new Vector3(world.VillageXZ.x, world.VillageHeight, world.VillageXZ.y);
            root.position = c;

            // Houses in a loose ring facing the centre, lit windows visible through the fog.
            var rng = new System.Random(world.Seed + 17);
            for (var k = 0; k < 6; k++)
            {
                var a = k * Mathf.PI * 2f / 6f + (float)rng.NextDouble() * 0.4f;
                var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (WorldMap.VillageRadius - 4f);
                var yaw = Mathf.Atan2(c.x - p.x, c.z - p.z) * Mathf.Rad2Deg;
                BuildHouse(root, p, yaw, assets, k < 3);
            }

            // Tall beacon so the village reads from a distance.
            var pole = Meshes.Part(root, PrimitiveType.Cylinder, assets.wood, new Vector3(0f, 4f, 0f), new Vector3(0.25f, 4f, 0.25f), default, true);
            pole.name = "Beacon Pole";
            var lamp = Meshes.Part(root, PrimitiveType.Sphere, assets.windowGlow, new Vector3(0f, 8.3f, 0f), Vector3.one * 0.8f);
            lamp.name = "Beacon";
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.45f);
            light.range = 30f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;

            // A flat teleportable plaza so arrival is easy.
            var plaza = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plaza.name = "Plaza";
            plaza.transform.SetParent(root, false);
            plaza.transform.localPosition = new Vector3(0f, -0.08f, 0f);
            plaza.transform.localScale = new Vector3(WorldMap.VillageRadius * 2f, 0.1f, WorldMap.VillageRadius * 2f);
            plaza.GetComponent<MeshRenderer>().sharedMaterial = assets.snowGround;
            Object.DestroyImmediate(plaza.GetComponent<Collider>());
            plaza.AddComponent<MeshCollider>();
            plaza.AddComponent<TeleportationArea>().interactionLayers = InteractionLayerMask.GetMask("Teleport");

            return root;
        }

        public static void BuildCabin(Transform parent, Vector3 pos, float yaw, GameAssets assets)
        {
            BuildHouse(parent, pos, yaw, assets, false, 0.75f).name = "Hunter Cabin";
        }

        static Transform BuildHouse(Transform parent, Vector3 pos, float yaw, GameAssets assets, bool chimneyLight, float scale = 1f)
        {
            if (assets.cabinPrefab != null)
                return BuildPackCabin(parent, pos, yaw, assets, chimneyLight, scale);

            var house = new GameObject("House").transform;
            house.SetParent(parent, false);
            house.position = pos;
            house.rotation = Quaternion.Euler(0f, yaw, 0f);
            house.localScale = Vector3.one * scale;

            var body = Meshes.Part(house, PrimitiveType.Cube, assets.wall, new Vector3(0f, 1.4f, 0f), new Vector3(4f, 2.8f, 5f), default, true);
            body.name = "Walls";
            // Two slanted slabs make a pitched roof.
            Meshes.Part(house, PrimitiveType.Cube, assets.roof, new Vector3(-1.1f, 3.35f, 0f), new Vector3(2.6f, 0.2f, 5.6f), new Vector3(0f, 0f, 35f));
            Meshes.Part(house, PrimitiveType.Cube, assets.roof, new Vector3(1.1f, 3.35f, 0f), new Vector3(2.6f, 0.2f, 5.6f), new Vector3(0f, 0f, -35f));
            // Windows on the side facing +z (towards the centre / front).
            Meshes.Part(house, PrimitiveType.Cube, assets.windowGlow, new Vector3(-1f, 1.6f, 2.51f), new Vector3(0.8f, 0.7f, 0.05f));
            Meshes.Part(house, PrimitiveType.Cube, assets.windowGlow, new Vector3(1f, 1.6f, 2.51f), new Vector3(0.8f, 0.7f, 0.05f));

            if (chimneyLight)
            {
                var go = new GameObject("Window Light");
                go.transform.SetParent(house, false);
                go.transform.localPosition = new Vector3(0f, 1.6f, 3.2f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.4f);
                l.range = 6f;
                l.intensity = 1.5f;
                l.shadows = LightShadows.None;
            }

            return house;
        }

        /// <summary>The assembled log cabin from the Winter Cabin pack, ~6 m across at scale 1, with a solid collider.</summary>
        static Transform BuildPackCabin(Transform parent, Vector3 pos, float yaw, GameAssets assets, bool warmLight, float scale)
        {
            const float footprint = 6f;
            var size = assets.cabinSize;
            var fit = footprint / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));

            var house = Object.Instantiate(assets.cabinPrefab, parent).transform;
            house.name = "House";
            house.SetPositionAndRotation(pos - Vector3.up * 0.1f, Quaternion.Euler(0f, yaw, 0f));
            house.localScale = Vector3.one * fit * scale;

            // One box around the whole cabin keeps the player out of the walls without per-mesh colliders.
            var col = house.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, size.y * 0.5f, 0f);
            col.size = new Vector3(size.x * 0.9f, size.y, size.z * 0.9f);

            if (warmLight)
            {
                var go = new GameObject("Window Light");
                go.transform.SetParent(house, false);
                go.transform.localPosition = new Vector3(0f, size.y * 0.35f, size.z * 0.6f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.4f);
                l.range = 7f;
                l.intensity = 1.6f;
                l.shadows = LightShadows.None;
            }
            return house;
        }
    }
}
