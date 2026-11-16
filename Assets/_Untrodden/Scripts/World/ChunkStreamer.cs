using System.Collections.Generic;
using Untrodden.Items;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Untrodden.World
{
    /// <summary>
    /// Builds terrain only around the player. Each chunk is a 32 m square with:
    /// terrain mesh + collider + teleport area, river water, instanced trees and rocks,
    /// tree colliders, and deterministic pickups. Fog hides the edge of the loaded area.
    /// </summary>
    public class ChunkStreamer : MonoBehaviour
    {
        public const float ChunkSize = 32f;
        const int k_Res = 16;               // quads per side (2 m)
        const float k_Step = ChunkSize / k_Res;

        [SerializeField] int m_LoadRadius = 3;
        [SerializeField] int m_UnloadRadius = 4;
        [SerializeField] int m_ChunksPerFrame = 2;

        WorldMap m_World;
        GameAssets m_Assets;
        Transform m_Viewer;
        ItemFactory m_Items;
        int m_TeleportMask;

        readonly Dictionary<Vector2Int, Chunk> m_Chunks = new Dictionary<Vector2Int, Chunk>();
        readonly List<Vector2Int> m_Pending = new List<Vector2Int>();
        readonly List<Vector2Int> m_ToRemove = new List<Vector2Int>();
        readonly HashSet<long> m_Taken = new HashSet<long>();

        Mesh m_TrunkMesh, m_ConeMesh, m_RockMesh;
        InstancedSet m_PackTrees, m_PackRocks, m_PackProps;
        bool m_UsePack;

        class Chunk
        {
            public GameObject root;
            public Mesh terrain;
            public Mesh water;
            public Bounds bounds;
            public readonly List<Matrix4x4> trunks = new List<Matrix4x4>();
            public readonly List<Matrix4x4> crowns = new List<Matrix4x4>();
            public readonly List<Matrix4x4> rocks = new List<Matrix4x4>();
            public readonly List<WorldItem> items = new List<WorldItem>();
            public List<Matrix4x4>[] packTrees, packRocks, packProps;
        }

        /// <summary>Several multi-part models drawn instanced: one matrix list per (model, part).</summary>
        sealed class InstancedSet
        {
            public readonly PackModel[] models;
            readonly int[] m_Offsets;
            readonly int m_Total;

            public InstancedSet(PackModel[] models)
            {
                this.models = models ?? new PackModel[0];
                m_Offsets = new int[this.models.Length];
                for (var i = 0; i < this.models.Length; i++)
                {
                    m_Offsets[i] = m_Total;
                    m_Total += this.models[i].meshes.Length;
                }
            }

            public bool Empty => models.Length == 0;

            public List<Matrix4x4>[] NewLists()
            {
                var lists = new List<Matrix4x4>[m_Total];
                for (var i = 0; i < m_Total; i++)
                    lists[i] = new List<Matrix4x4>();
                return lists;
            }

            /// <summary>Place a model with its bounding-box bottom on <paramref name="ground"/>.</summary>
            public void Add(List<Matrix4x4>[] lists, int model, Vector3 ground, Quaternion rot, float scale)
            {
                var m = models[model];
                var lift = -m.boundsMin.y * scale;
                var root = Matrix4x4.TRS(ground + Vector3.up * lift, rot, Vector3.one * scale);
                for (var p = 0; p < m.parts.Length; p++)
                    lists[m_Offsets[model] + p].Add(root * m.parts[p]);
            }

            public void Draw(List<Matrix4x4>[] lists, Material mat, Bounds bounds)
            {
                if (lists == null || mat == null)
                    return;
                var rp = new RenderParams(mat)
                {
                    worldBounds = bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                for (var i = 0; i < models.Length; i++)
                {
                    var m = models[i];
                    for (var p = 0; p < m.meshes.Length; p++)
                    {
                        var list = lists[m_Offsets[i] + p];
                        if (list.Count == 0)
                            continue;
                        for (var sub = 0; sub < m.meshes[p].subMeshCount; sub++)
                            Graphics.RenderMeshInstanced(rp, m.meshes[p], sub, list);
                    }
                }
            }
        }

        public int LoadedCount => m_Chunks.Count;

        public void Init(WorldMap world, GameAssets assets, Transform viewer, ItemFactory items)
        {
            m_World = world;
            m_Assets = assets;
            m_Viewer = viewer;
            m_Items = items;
            m_TeleportMask = InteractionLayerMask.GetMask("Teleport");
            m_TrunkMesh = Meshes.Primitive(PrimitiveType.Cylinder);
            m_ConeMesh = Meshes.Cone;
            m_RockMesh = Meshes.Primitive(PrimitiveType.Sphere);

            m_UsePack = assets.HasPackTrees;
            if (m_UsePack)
            {
                m_PackTrees = new InstancedSet(Valid(assets.trees));
                m_PackRocks = new InstancedSet(Valid(assets.rocks));
                m_PackProps = new InstancedSet(Valid(assets.props));
                m_UsePack = !m_PackTrees.Empty;
            }
        }

        static PackModel[] Valid(PackModel[] models) =>
            models == null ? new PackModel[0] : System.Array.FindAll(models, m => m != null && m.Valid);

        /// <summary>Build the chunks around a point right now (used before the player can fall).</summary>
        public void LoadImmediate(Vector3 position, int radius)
        {
            var c = ToChunk(position);
            for (var dz = -radius; dz <= radius; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var key = new Vector2Int(c.x + dx, c.y + dz);
                if (IsValid(key) && !m_Chunks.ContainsKey(key))
                    m_Chunks[key] = BuildChunk(key);
            }
        }

        public void MarkTaken(long id) => m_Taken.Add(id);

        void Update()
        {
            if (m_World == null || m_Viewer == null)
                return;

            var center = ToChunk(m_Viewer.position);

            // unload
            m_ToRemove.Clear();
            foreach (var pair in m_Chunks)
            {
                var d = pair.Key - center;
                if (Mathf.Abs(d.x) > m_UnloadRadius || Mathf.Abs(d.y) > m_UnloadRadius)
                    m_ToRemove.Add(pair.Key);
            }
            foreach (var key in m_ToRemove)
            {
                DestroyChunk(m_Chunks[key]);
                m_Chunks.Remove(key);
            }

            // load, nearest first
            m_Pending.Clear();
            for (var dz = -m_LoadRadius; dz <= m_LoadRadius; dz++)
            for (var dx = -m_LoadRadius; dx <= m_LoadRadius; dx++)
            {
                var key = new Vector2Int(center.x + dx, center.y + dz);
                if (IsValid(key) && !m_Chunks.ContainsKey(key))
                    m_Pending.Add(key);
            }
            m_Pending.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
            for (var i = 0; i < m_Pending.Count && i < m_ChunksPerFrame; i++)
                m_Chunks[m_Pending[i]] = BuildChunk(m_Pending[i]);

            DrawInstanced();
        }

        void OnDestroy()
        {
            foreach (var chunk in m_Chunks.Values)
                DestroyChunk(chunk);
            m_Chunks.Clear();
        }

        void DrawInstanced()
        {
            foreach (var chunk in m_Chunks.Values)
            {
                if (m_UsePack)
                {
                    m_PackTrees.Draw(chunk.packTrees, m_Assets.packMaterial, chunk.bounds);
                    m_PackRocks.Draw(chunk.packRocks, m_Assets.packMaterial, chunk.bounds);
                    m_PackProps.Draw(chunk.packProps, m_Assets.packMaterial, chunk.bounds);
                    continue;
                }
                Draw(chunk, m_TrunkMesh, m_Assets.trunk, chunk.trunks);
                Draw(chunk, m_ConeMesh, m_Assets.pine, chunk.crowns);
                Draw(chunk, m_RockMesh, m_Assets.rock, chunk.rocks);
            }
        }

        static void Draw(Chunk chunk, Mesh mesh, Material mat, List<Matrix4x4> matrices)
        {
            if (matrices.Count == 0 || mat == null)
                return;
            var rp = new RenderParams(mat)
            {
                worldBounds = chunk.bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderMeshInstanced(rp, mesh, 0, matrices);
        }

        // ------------------------------------------------------------------ build

        Chunk BuildChunk(Vector2Int key)
        {
            var origin = new Vector3(key.x * ChunkSize, 0f, key.y * ChunkSize);
            var chunk = new Chunk
            {
                root = new GameObject($"Chunk {key.x},{key.y}"),
            };
            chunk.root.transform.SetParent(transform, false);
            chunk.root.transform.position = origin;

            var heights = SampleHeights(origin, out var minH, out var maxH);
            chunk.terrain = BuildTerrainMesh(heights);
            chunk.root.AddComponent<MeshFilter>().sharedMesh = chunk.terrain;
            var mr = chunk.root.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_Assets.snowGround;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            chunk.root.AddComponent<MeshCollider>().sharedMesh = chunk.terrain;

            // Collider must exist before the interactable is added (it gathers colliders in Awake).
            var area = chunk.root.AddComponent<TeleportationArea>();
            area.interactionLayers = m_TeleportMask;

            chunk.water = BuildWater(chunk, origin);

            chunk.bounds = new Bounds(origin + new Vector3(ChunkSize * 0.5f, (minH + maxH) * 0.5f + 8f, ChunkSize * 0.5f),
                new Vector3(ChunkSize + 8f, (maxH - minH) + 40f, ChunkSize + 8f));

            Scatter(chunk, key, origin);
            SpawnItems(chunk, key, origin);
            return chunk;
        }

        float[,] SampleHeights(Vector3 origin, out float minH, out float maxH)
        {
            // one extra ring for normals
            var n = k_Res + 3;
            var h = new float[n, n];
            minH = float.MaxValue;
            maxH = float.MinValue;
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var v = m_World.Height(origin.x + (x - 1) * k_Step, origin.z + (z - 1) * k_Step);
                h[x, z] = v;
                if (x >= 1 && z >= 1 && x <= k_Res + 1 && z <= k_Res + 1)
                {
                    minH = Mathf.Min(minH, v);
                    maxH = Mathf.Max(maxH, v);
                }
            }
            return h;
        }

        Mesh BuildTerrainMesh(float[,] h)
        {
            var n = k_Res + 1;
            var verts = new Vector3[n * n];
            var normals = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var i = z * n + x;
                verts[i] = new Vector3(x * k_Step, h[x + 1, z + 1], z * k_Step);
                normals[i] = new Vector3(h[x, z + 1] - h[x + 2, z + 1], 2f * k_Step, h[x + 1, z] - h[x + 1, z + 2]).normalized;
                uvs[i] = new Vector2(x * k_Step, z * k_Step) * 0.25f;
            }

            var tris = new int[k_Res * k_Res * 6];
            var t = 0;
            for (var z = 0; z < k_Res; z++)
            for (var x = 0; x < k_Res; x++)
            {
                var a = z * n + x;
                var b = a + 1;
                var c = a + n;
                var d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }

            var mesh = new Mesh { name = "Terrain" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        Mesh BuildWater(Chunk chunk, Vector3 origin)
        {
            if (origin.z > WorldMap.CoastZ - 40f)
                return null; // the sea plane covers this area

            var n = k_Res + 1;
            var wet = new bool[n * n];
            var surf = new float[n * n];
            var any = false;
            for (var z = 0; z < n; z++)
            for (var x = 0; x < n; x++)
            {
                var wx = origin.x + x * k_Step;
                var wz = origin.z + z * k_Step;
                var r = m_World.NearestRiver(wx, wz);
                var i = z * n + x;
                surf[i] = r.valid ? r.surface : 0f;
                // Reach well under the banks (which rise above the surface): the terrain then cuts
                // the water plane along a smooth line instead of the 2 m grid showing as saw teeth.
                wet[i] = r.valid && r.distance < r.halfWidth + 4f;
                any |= wet[i];
            }
            if (!any)
                return null;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var map = new int[n * n];
            for (var i = 0; i < map.Length; i++)
                map[i] = -1;

            int V(int x, int z)
            {
                var i = z * n + x;
                if (map[i] < 0)
                {
                    map[i] = verts.Count;
                    verts.Add(new Vector3(x * k_Step, surf[i] - 0.05f, z * k_Step));
                }
                return map[i];
            }

            for (var z = 0; z < k_Res; z++)
            for (var x = 0; x < k_Res; x++)
            {
                var a = z * n + x;
                if (!(wet[a] || wet[a + 1] || wet[a + n] || wet[a + n + 1]))
                    continue;
                int va = V(x, z), vb = V(x + 1, z), vc = V(x, z + 1), vd = V(x + 1, z + 1);
                tris.Add(va); tris.Add(vc); tris.Add(vb);
                tris.Add(vb); tris.Add(vc); tris.Add(vd);
            }

            var mesh = new Mesh { name = "Water" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Water");
            go.transform.SetParent(chunk.root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_Assets.water;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mesh;
        }

        void Scatter(Chunk chunk, Vector2Int key, Vector3 origin)
        {
            var rng = m_World.ChunkRandom(key.x, key.y, 1);
            float R() => (float)rng.NextDouble();
            var colliderRoot = new GameObject("Tree Colliders").transform;
            colliderRoot.SetParent(chunk.root.transform, false);

            if (m_UsePack)
            {
                chunk.packTrees = m_PackTrees.NewLists();
                chunk.packRocks = m_PackRocks.NewLists();
                chunk.packProps = m_PackProps.NewLists();
            }

            const float spacing = 4f;
            var start = new Vector2(m_World.StartPosition.x, m_World.StartPosition.z);
            for (var gz = 0; gz < ChunkSize / spacing; gz++)
            for (var gx = 0; gx < ChunkSize / spacing; gx++)
            {
                var x = origin.x + (gx + R()) * spacing;
                var z = origin.z + (gz + R()) * spacing;
                var roll = R();
                var yaw = R() * 360f;
                var sizeRoll = R();
                var widthRoll = R();
                var pick = R();

                if (Vector2.Distance(new Vector2(x, z), start) < 6f)
                    continue;
                if (m_World.TryGetWater(x, z, out _))
                    continue;

                var biome = m_World.GetBiome(x, z);
                var treeChance = TreeDensity(biome);
                var rockChance = RockDensity(biome);
                var y = m_World.Height(x, z);
                var pos = new Vector3(x, y, z);
                var rot = Quaternion.Euler(0f, yaw, 0f);

                if (roll < treeChance)
                {
                    var height = biome == Biome.Ridge ? Mathf.Lerp(4f, 8f, sizeRoll) : Mathf.Lerp(7f, 15f, sizeRoll);
                    if (m_UsePack)
                    {
                        var model = Mathf.Min((int)(pick * m_PackTrees.models.Length), m_PackTrees.models.Length - 1);
                        m_PackTrees.Add(chunk.packTrees, model, pos, rot, height / m_PackTrees.models[model].Height);
                        AddTreeCollider(colliderRoot, pos);
                        continue;
                    }
                    var width = Mathf.Lerp(1.6f, 2.6f, widthRoll) * height / 11f;
                    chunk.trunks.Add(Matrix4x4.TRS(pos + Vector3.up * height * 0.2f, rot, new Vector3(0.35f, height * 0.2f, 0.35f)));
                    chunk.crowns.Add(Matrix4x4.TRS(pos + Vector3.up * height * 0.25f, rot, new Vector3(width, height * 0.45f, width)));
                    chunk.crowns.Add(Matrix4x4.TRS(pos + Vector3.up * height * 0.5f, rot, new Vector3(width * 0.7f, height * 0.5f, width * 0.7f)));

                    AddTreeCollider(colliderRoot, pos);
                }
                else if (m_UsePack && !m_PackProps.Empty && roll >= treeChance + rockChance && roll < treeChance + rockChance + PropDensity(biome))
                {
                    // dead bushes, snow mounds, stumps: knee-high clutter that makes the snow read as ground
                    var model = Mathf.Min((int)(pick * m_PackProps.models.Length), m_PackProps.models.Length - 1);
                    var size = m_PackProps.models[model].Size;
                    var fit = Mathf.Lerp(0.8f, 1.6f, sizeRoll) / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
                    m_PackProps.Add(chunk.packProps, model, pos - Vector3.up * 0.05f, rot, fit);
                }
                else if (roll < treeChance + rockChance)
                {
                    var s = Mathf.Lerp(0.8f, 2.2f, sizeRoll);
                    if (m_UsePack && !m_PackRocks.Empty)
                    {
                        var model = Mathf.Min((int)(pick * m_PackRocks.models.Length), m_PackRocks.models.Length - 1);
                        var size = m_PackRocks.models[model].Size;
                        var fit = s / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
                        m_PackRocks.Add(chunk.packRocks, model, pos - Vector3.up * 0.15f, rot, fit);
                        continue;
                    }
                    chunk.rocks.Add(Matrix4x4.TRS(pos - Vector3.up * 0.2f, rot, new Vector3(s, s * Mathf.Lerp(0.4f, 0.8f, widthRoll), s * 0.9f)));
                }
            }
        }

        void SpawnItems(Chunk chunk, Vector2Int key, Vector3 origin)
        {
            if (m_Items == null)
                return;
            var rng = m_World.ChunkRandom(key.x, key.y, 2);
            float R() => (float)rng.NextDouble();
            var index = 0;

            Vector3 RandomPoint() => new Vector3(origin.x + 2f + R() * (ChunkSize - 4f), 0f, origin.z + 2f + R() * (ChunkSize - 4f));

            void Spawn(ItemKind kind, Vector3 p)
            {
                var id = ((long)(key.x + 1000) << 40) | ((long)(key.y + 1000) << 20) | (long)index++;
                if (m_Taken.Contains(id))
                    return;
                p.y = m_World.Height(p.x, p.z) + 0.15f;
                var item = m_Items.Create(kind, p, Quaternion.Euler(0f, R() * 360f, 0f));
                item.Bind(id, this);
                chunk.items.Add(item);
            }

            // firewood
            for (var k = 0; k < 6; k++)
            {
                var p = RandomPoint();
                var chance = R();
                if (m_World.TryGetWater(p.x, p.z, out _))
                    continue;
                var b = m_World.GetBiome(p.x, p.z);
                var want = b == Biome.Forest ? 0.5f : b == Biome.Valley ? 0.4f : b == Biome.Ridge || b == Biome.Coast ? 0.2f : 0f;
                if (chance < want)
                    Spawn(ItemKind.Firewood, p);
                else
                    index++;
            }

            // fish on river banks: more downstream
            for (var k = 0; k < 4; k++)
            {
                var p = RandomPoint();
                var chance = R();
                var r = m_World.NearestRiver(p.x, p.z);
                if (!r.valid || r.distance < r.halfWidth + 0.8f || r.distance > r.halfWidth + 4f)
                {
                    index++;
                    continue;
                }
                var b = m_World.GetBiome(p.x, p.z);
                var want = b == Biome.Downstream ? 0.55f : b == Biome.River ? 0.35f : b == Biome.Stream ? 0.12f : 0f;
                if (chance < want)
                    Spawn(ItemKind.Fish, p);
                else
                    index++;
            }

            // abandoned hunter cabin: canned food and ammo
            var cabinRoll = R();
            var cabinPos = new Vector3(origin.x + ChunkSize * 0.5f, 0f, origin.z + ChunkSize * 0.5f);
            var cabinBiome = m_World.GetBiome(cabinPos.x, cabinPos.z);
            var farFromStart = Vector3.Distance(cabinPos, m_World.StartPosition) > 60f;
            if (cabinRoll < 0.07f && farFromStart && (cabinBiome == Biome.Forest || cabinBiome == Biome.Valley))
            {
                cabinPos.y = m_World.Height(cabinPos.x, cabinPos.z);
                WorldDressing.BuildCabin(chunk.root.transform, cabinPos, R() * 360f, m_Assets);
                Spawn(ItemKind.Can, cabinPos + new Vector3(2.4f, 0f, 0.4f));
                Spawn(ItemKind.Can, cabinPos + new Vector3(2.5f, 0f, -0.3f));
                Spawn(ItemKind.AmmoBox, cabinPos + new Vector3(2.2f, 0f, -1f));
            }
        }

        static void AddTreeCollider(Transform parent, Vector3 pos)
        {
            var col = new GameObject("T").AddComponent<CapsuleCollider>();
            col.transform.SetParent(parent, false);
            col.transform.position = pos + Vector3.up * 1.5f;
            col.radius = 0.3f;
            col.height = 3f;
        }

        static float PropDensity(Biome b)
        {
            switch (b)
            {
                case Biome.Forest:
                case Biome.Valley: return 0.05f;
                case Biome.Ridge:
                case Biome.Coast: return 0.08f;
                case Biome.Stream:
                case Biome.River:
                case Biome.Downstream: return 0.04f;
                default: return 0f;
            }
        }

        static float TreeDensity(Biome b)
        {
            switch (b)
            {
                case Biome.Forest: return 0.38f;
                case Biome.Valley: return 0.22f;
                case Biome.Ridge: return 0.1f;
                case Biome.Coast: return 0.06f;
                case Biome.Stream: return 0.05f;
                case Biome.River: return 0.02f;
                case Biome.Downstream: return 0.01f;
                default: return 0f;
            }
        }

        static float RockDensity(Biome b)
        {
            switch (b)
            {
                case Biome.Ridge: return 0.08f;
                case Biome.Stream:
                case Biome.River: return 0.05f;
                case Biome.Forest: return 0.02f;
                default: return 0.01f;
            }
        }

        void DestroyChunk(Chunk chunk)
        {
            foreach (var item in chunk.items)
            {
                if (item != null && !item.Taken)
                    Destroy(item.gameObject);
            }
            if (chunk.terrain != null) Destroy(chunk.terrain);
            if (chunk.water != null) Destroy(chunk.water);
            if (chunk.root != null) Destroy(chunk.root);
        }

        static Vector2Int ToChunk(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.z / ChunkSize));

        static bool IsValid(Vector2Int key) =>
            key.x >= 0 && key.y >= 0 &&
            key.x * ChunkSize < WorldMap.Width &&
            key.y * ChunkSize < WorldMap.CoastZ + 60f;
    }
}
