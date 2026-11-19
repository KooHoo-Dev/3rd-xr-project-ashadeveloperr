using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// Untrodden 플레이 씬을 코드로 생성한다. (메뉴: Untrodden > Build Scene)
    /// 남쪽이 높고 북쪽이 바다인 지형, 강, 숲, 모닥불 체크포인트, 바위 그늘, 마을, XR 리그와 UI를 배치한다.
    /// 다시 실행하면 씬과 생성 애셋을 새로 만든다.
    /// </summary>
    public static class UntroddenSceneBuilder
    {
        const string Root = "Assets/Untrodden";
        const string GenDir = Root + "/Generated";
        const string ScenePath = Root + "/Scenes/Untrodden.unity";
        const string RigPrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string VignettePrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";
        const string FontPath = "Assets/99. External Assets/Font/Noto_Sans_KR/NotoSansKR-Regular SDF.asset";
        const string CreatorName = "채경희";

        // 지형 치수 (월드 좌표, 미터)
        const float SizeX = 240f, SizeZ = 440f, TerrainBase = -10f, TerrainHeight = 80f;
        const float SeaZ = 402f, SeaLevel = 0.4f;
        const int HeightRes = 513, AlphaRes = 256;

        static readonly Color FogColor = new(0.72f, 0.78f, 0.85f);

        static Terrain s_Terrain;
        static TMP_FontAsset s_Font;
        static Sprite s_White, s_Vignette;
        static Material s_SnowMat, s_SparkMat, s_WoodMat, s_DarkMat, s_RedMat, s_IvoryMat;
        static readonly List<(Vector2 c, float r, float h)> s_Flats = new();
        static System.Random s_Rng;

        // ───────────────────────── 진입점 ─────────────────────────

        [MenuItem("Untrodden/Build Scene")]
        public static void Build()
        {
            s_Rng = new System.Random(20261123);
            s_Flats.Clear();
            EnsureDir(GenDir);
            EnsureDir(Root + "/Scenes");

            CreateSharedAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 주요 지점
            var start = new Vector2(RiverX(22f) + 20f, 22f);
            var fires = new[]
            {
                new Vector2(start.x - 3f, start.y + 3f),
                new Vector2(RiverX(140f) - 18f, 140f),
                new Vector2(RiverX(262f) + 18f, 262f),
            };
            var village = new Vector2(RiverX(386f) + 30f, 386f);

            AddFlat(start, 9f);
            foreach (var f in fires) AddFlat(f, 7f);
            AddFlat(village, 30f);

            SetupLighting();
            BuildTerrain();

            var env = new GameObject("Environment").transform;
            BuildWater(env);
            BuildForest(env, start, fires, village);
            var shelters = BuildShelters(env);
            BuildMushrooms(env);
            BuildVillage(env, village);

            var startPoint = Marker("StartPoint", start, 0f);
            var goalPoint = Marker("GoalPoint", village, 0f);

            var gameplay = new GameObject("Gameplay").transform;
            for (int i = 0; i < fires.Length; i++)
                BuildCampfireSite(gameplay, i, fires[i], i == 0 ? startPoint : null, i == 0);

            var rig = BuildRig(start);
            var cam = rig.Camera;
            BuildSystems(rig, cam, startPoint, goalPoint, start);

            Debug.Log($"[Untrodden] shelters: {shelters}");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Untrodden] Scene built: " + ScenePath);
        }

        /// <summary>배치 모드용: 씬을 만들고 바로 APK까지 빌드한다.</summary>
        public static void BuildSceneAndApk()
        {
            Build();
            QuestBuild.BuildApk();
        }

        // ───────────────────────── 지형 ─────────────────────────

        static float RiverX(float z) => 120f + 32f * Mathf.Sin(z / 70f) + 9f * Mathf.Sin(z / 23f + 1.3f);

        static float BaseHeight(float x, float z)
        {
            float slope = z < 400f
                ? 2.5f + 38f * Mathf.Pow(1f - z / 400f, 1.2f)
                : Mathf.Lerp(2.5f, -3f, Mathf.Clamp01((z - 400f) / 10f));

            float n = (Mathf.PerlinNoise(x / 55f + 3.1f, z / 55f + 7.7f) - 0.5f) * 10f
                    + (Mathf.PerlinNoise(x / 14f + 11f, z / 14f + 5f) - 0.5f) * 2.5f;
            n *= Mathf.Clamp01((SeaZ - z) / 25f); // 해안 가까이는 평평하게

            float walls = 32f * SmoothStep(26f, 0f, x) + 32f * SmoothStep(SizeX - 26f, SizeX, x)
                        + 26f * SmoothStep(16f, 0f, z);
            return slope + n + walls;
        }

        static float RiverDistance(float x, float z) => Mathf.Abs(x - RiverX(z));

        static float HeightNoFlat(float x, float z)
        {
            float h = BaseHeight(x, z);
            float d = RiverDistance(x, z);
            if (d < 13f)
            {
                float center = BaseHeight(RiverX(z), z);
                float bed = center - 2.6f * (1f - Mathf.Clamp01(d / 8f) * Mathf.Clamp01(d / 8f));
                h = Mathf.Lerp(bed, h, SmoothStep(6f, 13f, d));
            }
            if (z > SeaZ) h = Mathf.Min(h, -1.5f);
            return h;
        }

        static float Height(float x, float z)
        {
            float h = HeightNoFlat(x, z);
            foreach (var (c, r, target) in s_Flats)
            {
                float d = Vector2.Distance(c, new Vector2(x, z));
                if (d < r) h = Mathf.Lerp(h, target, SmoothStep(r, r * 0.55f, d));
            }
            return h;
        }

        static float RiverWaterY(float z) => BaseHeight(RiverX(z), z) - 1.3f;

        static void AddFlat(Vector2 c, float r) => s_Flats.Add((c, r, Mathf.Max(HeightNoFlat(c.x, c.y), SeaLevel + 1.2f)));

        static void BuildTerrain()
        {
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = AlphaRes };
            data.size = new Vector3(SizeX, TerrainHeight, SizeZ);

            var heights = new float[HeightRes, HeightRes];
            for (int iz = 0; iz < HeightRes; iz++)
            for (int ix = 0; ix < HeightRes; ix++)
            {
                float x = ix / (float)(HeightRes - 1) * SizeX;
                float z = iz / (float)(HeightRes - 1) * SizeZ;
                heights[iz, ix] = Mathf.Clamp01((Height(x, z) - TerrainBase) / TerrainHeight);
            }
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[]
            {
                Layer("Snow", new Color(0.93f, 0.95f, 0.98f), new Color(0.82f, 0.86f, 0.92f)),
                Layer("Rock", new Color(0.45f, 0.46f, 0.48f), new Color(0.32f, 0.33f, 0.35f)),
                Layer("RiverBank", new Color(0.42f, 0.37f, 0.3f), new Color(0.6f, 0.6f, 0.58f)),
            };

            var alpha = new float[AlphaRes, AlphaRes, 3];
            for (int iz = 0; iz < AlphaRes; iz++)
            for (int ix = 0; ix < AlphaRes; ix++)
            {
                float nx = ix / (float)(AlphaRes - 1), nz = iz / (float)(AlphaRes - 1);
                float x = nx * SizeX, z = nz * SizeZ;
                float steep = data.GetSteepness(nx, nz);
                float rock = SmoothStep(26f, 38f, steep);
                float bank = SmoothStep(11f, 7f, RiverDistance(x, z)) + (z > SeaZ - 8f ? 1f : 0f);
                bank = Mathf.Clamp01(bank) * (1f - rock);
                float snow = Mathf.Max(0f, 1f - rock - bank);
                alpha[iz, ix, 0] = snow;
                alpha[iz, ix, 1] = rock;
                alpha[iz, ix, 2] = bank;
            }
            data.SetAlphamaps(0, 0, alpha);

            CreateOrReplace(data, GenDir + "/TerrainData.asset");

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(0f, TerrainBase, 0f);
            s_Terrain = go.GetComponent<Terrain>();
            s_Terrain.heightmapPixelError = 8;
            s_Terrain.basemapDistance = 60f;
            s_Terrain.drawInstanced = true;
            s_Terrain.shadowCastingMode = ShadowCastingMode.Off;
            var rp = GraphicsSettings.defaultRenderPipeline;
            if (rp != null && rp.defaultTerrainMaterial != null) s_Terrain.materialTemplate = rp.defaultTerrainMaterial;

            var area = go.AddComponent<TeleportationArea>();
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        static TerrainLayer Layer(string name, Color a, Color b)
        {
            var tex = NoiseTexture(a, b, name.GetHashCode());
            CreateOrReplace(tex, $"{GenDir}/Tex_{name}.asset");
            var layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(6f, 6f) };
            CreateOrReplace(layer, $"{GenDir}/Layer_{name}.terrainlayer");
            return layer;
        }

        static float GroundY(Vector2 p) => s_Terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + TerrainBase;
        static Vector3 Ground(Vector2 p) => new(p.x, GroundY(p), p.y);

        // ───────────────────────── 물 ─────────────────────────

        static void BuildWater(Transform parent)
        {
            var water = FindMaterial("SyntyStudios_Water", "Water");

            // 강: 중심선을 따라 내려가는 띠 메시
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const float half = 8.5f;
            int i = 0;
            for (float z = -4f; z <= SeaZ + 6f; z += 2f, i++)
            {
                float cx = RiverX(z);
                float y = Mathf.Max(RiverWaterY(z), SeaLevel);
                verts.Add(new Vector3(cx - half, y, z));
                verts.Add(new Vector3(cx + half, y, z));
                uvs.Add(new Vector2(0f, z / 10f));
                uvs.Add(new Vector2(1f, z / 10f));
                if (i > 0)
                {
                    int b = (i - 1) * 2;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = "River" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            CreateOrReplace(mesh, GenDir + "/RiverMesh.asset");
            MeshObject("River", parent, mesh, water);

            // 바다
            var sea = new Mesh { name = "Sea" };
            sea.SetVertices(new[]
            {
                new Vector3(-200f, SeaLevel, SeaZ - 6f), new Vector3(SizeX + 200f, SeaLevel, SeaZ - 6f),
                new Vector3(-200f, SeaLevel, SeaZ + 400f), new Vector3(SizeX + 200f, SeaLevel, SeaZ + 400f),
            });
            sea.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(0, 40), new Vector2(40, 40) });
            sea.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            sea.RecalculateNormals();
            CreateOrReplace(sea, GenDir + "/SeaMesh.asset");
            MeshObject("Sea", parent, sea, water);

            // 바다로 걸어 들어가지 못하게 막는 벽
            var wall = new GameObject("SeaWall");
            wall.transform.SetParent(parent);
            wall.transform.position = new Vector3(SizeX / 2f, 5f, SeaZ + 4f);
            wall.AddComponent<BoxCollider>().size = new Vector3(SizeX, 20f, 1f);
        }

        // ───────────────────────── 숲·바위 ─────────────────────────

        static void BuildForest(Transform parent, Vector2 start, Vector2[] fires, Vector2 village)
        {
            var root = new GameObject("Forest").transform;
            root.SetParent(parent);
            var pines = Prefabs("SM_Tree_Pine_02", "SM_Tree_Pine_Large_01", "SM_Tree_Pine_Large_02", "SM_Tree_Pine_Small_02",
                "SM_Tree_PolyPine_01", "SM_Tree_PolyPine_02", "SM_Tree_PolyPine_03", "SM_Tree_PolyPine_Sparse_01");
            var dead = Prefabs("SM_Tree_Pine_Dead_01", "SM_Tree_Dead_01", "SM_Tree_Dead_03", "SM_Tree_Birch_Dead_01");
            var rocks = Prefabs("SM_Rock_01", "SM_Rock_02", "SM_Rock_03", "SM_Rock_04", "SM_Rock_Boulder_01");
            var bushes = Prefabs("SM_Plant_Bush_01", "SM_Plant_Bush_02", "SM_Plant_Fern_01");

            const float spacing = 8f;
            int trees = 0;
            for (float z = 4f; z < SeaZ - 6f; z += spacing)
            for (float x = 4f; x < SizeX - 4f; x += spacing)
            {
                var p = new Vector2(x + Rand(-3f, 3f), z + Rand(-3f, 3f));
                float river = RiverDistance(p.x, p.y);
                if (river < 12f) continue;
                if (Near(p, start, 12f) || Near(p, village, 38f)) continue;
                bool nearFire = false;
                foreach (var f in fires) nearFire |= Near(p, f, 11f);
                if (nearFire) continue;

                float roll = Rand(0f, 1f);
                if (roll < 0.52f && pines.Count > 0)
                {
                    var t = Place(Pick(pines), root, Ground(p) + Vector3.down * 0.2f, Rand(0f, 360f), Rand(0.8f, 1.3f));
                    AddTrunkCollider(t);
                    trees++;
                }
                else if (roll < 0.58f && dead.Count > 0 && river < 30f)
                {
                    var t = Place(Pick(dead), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.2f));
                    AddTrunkCollider(t);
                }
                else if (roll < 0.64f && rocks.Count > 0)
                {
                    var r = Place(Pick(rocks), root, Ground(p) + Vector3.down * 0.3f, Rand(0f, 360f), 1f);
                    FitToSize(r, Rand(1f, 2.5f));
                    EnsureMeshColliders(r);
                }
                else if (roll < 0.7f && bushes.Count > 0)
                {
                    Place(Pick(bushes), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.2f));
                }
            }
            MarkStatic(root.gameObject);
            Debug.Log($"[Untrodden] trees placed: {trees}");
        }

        static int BuildShelters(Transform parent)
        {
            var root = new GameObject("Shelters").transform;
            root.SetParent(parent);
            var big = Prefabs("SM_Rock_Cluster_Large_01", "SM_Rock_Cluster_Large_02", "SM_Rock_Cluster_Large_03", "SM_Rock_Cluster_Large_04");
            float[] zs = { 75f, 112f, 195f, 228f, 318f, 350f };
            int n = 0;
            foreach (var z in zs)
            {
                float side = n % 2 == 0 ? 1f : -1f;
                var p = new Vector2(RiverX(z) + side * 21f, z);
                if (big.Count > 0)
                {
                    var rock = Place(Pick(big), root, Ground(p) + Vector3.down * 0.5f, Rand(0f, 360f), 1f);
                    FitToSize(rock, 7f);
                    EnsureMeshColliders(rock);
                    MarkStatic(rock);
                }
                var zone = new GameObject($"ShelterZone_{n}");
                zone.transform.SetParent(root);
                zone.transform.position = Ground(p);
                zone.AddComponent<ShelterZone>().radius = 6.5f;
                n++;
            }
            return n;
        }

        static void BuildMushrooms(Transform parent)
        {
            var root = new GameObject("Food").transform;
            root.SetParent(parent);
            for (int i = 0; i < 14; i++)
            {
                float z = Mathf.Lerp(40f, 370f, i / 13f) + Rand(-8f, 8f);
                float side = Rand(0f, 1f) < 0.5f ? -1f : 1f;
                var p = new Vector2(RiverX(z) + side * Rand(9.5f, 12f), z);
                var go = new GameObject($"Mushroom_{i}");
                go.transform.SetParent(root);
                var (_, thick) = GrabVisual(go.transform, "SM_Plant_Mushrooms_01", 0.14f, false, PrimitiveType.Sphere, s_RedMat);
                go.AddComponent<SphereCollider>().radius = 0.06f;
                MakeGrabbable(go, 0.2f);
                var e = go.AddComponent<Edible>();
                e.displayName = "버섯";
                e.nutrition = 35f;
                go.transform.position = Ground(p) + Vector3.up * 0.15f;
            }
        }

        static void BuildVillage(Transform parent, Vector2 center)
        {
            var root = new GameObject("Village").transform;
            root.SetParent(parent);
            var houses = Prefabs("SM_Gen_Bld_Background_01", "SM_Gen_Bld_Background_02", "SM_Gen_Bld_Background_03",
                "SM_Gen_Bld_Background_04", "SM_Gen_Bld_Background_05", "SM_Gen_Bld_Background_06", "SM_Gen_Bld_Background_07");
            var c3 = Ground(center);
            int n = 0;
            for (float a = 25f; a <= 335f; a += 38f)
            {
                if (a > 140f && a < 220f) continue; // 남쪽(플레이어가 오는 방향)은 비워 둔다
                var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad));
                var p = new Vector2(center.x + dir.x * 17f, center.y + dir.z * 17f);
                if (houses.Count == 0) break;
                var h = Place(houses[n % houses.Count], root, Ground(p), 0f, 1f);
                h.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                FitToSize(h, 11f);
                EnsureMeshColliders(h);
                n++;
            }

            // 마을 불빛: 횃불 기둥 + 따뜻한 점광원
            var torch = Prefab("SM_Prop_TorchStick_01");
            for (int i = 0; i < 4; i++)
            {
                float a = 45f + i * 90f;
                var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad));
                var p = new Vector2(center.x + dir.x * 9f, center.y + dir.z * 9f);
                var pos = Ground(p);
                if (torch) FitToSize(Place(torch, root, pos, 0f, 1f), 2.2f);
                var l = new GameObject("VillageLight").AddComponent<Light>();
                l.transform.SetParent(root);
                l.transform.position = pos + Vector3.up * 2.4f;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.7f, 0.4f);
                l.range = 14f;
                l.intensity = 3f;
                l.shadows = LightShadows.None;
            }

            // 마을 한가운데 큰 모닥불 (도착 지점 표시)
            var fireFx = Prefab("FX_Fire_01");
            if (fireFx) Place(fireFx, root, c3 + Vector3.up * 0.1f, 0f, 1.6f);
            var bl = new GameObject("BonfireLight").AddComponent<Light>();
            bl.transform.SetParent(root);
            bl.transform.position = c3 + Vector3.up * 1.5f;
            bl.type = LightType.Point;
            bl.color = new Color(1f, 0.55f, 0.2f);
            bl.range = 20f;
            bl.intensity = 4f;
            var crackle = AudioObject("BonfireAudio", root, c3, ProceduralAudioSource.Kind.Crackle, true, 1f, 0.9f);
            crackle.maxDistance = 60f;

            MarkStatic(root.gameObject);
        }

        // ───────────────────────── 모닥불 자리 ─────────────────────────

        static void BuildCampfireSite(Transform parent, int index, Vector2 pos2, Transform respawnOverride, bool withCompass)
        {
            var site = new GameObject($"CampfireSite_{index}").transform;
            site.SetParent(parent);
            var center = Ground(pos2);
            site.position = center;

            var fireGo = new GameObject("Campfire");
            fireGo.transform.SetParent(site, false);
            var fire = fireGo.AddComponent<Campfire>();

            // 돌 테두리
            var stone = Prefab("SM_Rock_Small_01");
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.55f;
                GameObject s;
                if (stone) s = Place(stone, fireGo.transform, p, Rand(0f, 360f), 1f);
                else
                {
                    s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    s.transform.SetParent(fireGo.transform);
                    s.transform.position = p;
                }
                FitToSize(s, 0.28f);
                foreach (var col in s.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            }

            // 장작 소켓 3개 (원뿔 모양으로 기대어 세움)
            var sockets = new List<FirewoodSocket>();
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var sgo = new GameObject($"LogSocket_{i}");
                sgo.transform.SetParent(fireGo.transform);
                sgo.transform.position = center + radial * 0.16f + Vector3.up * 0.24f;
                sgo.transform.rotation = Quaternion.AngleAxis(25f, Vector3.Cross(radial, Vector3.up));
                var sc = sgo.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.radius = 0.2f;
                sockets.Add(sgo.AddComponent<FirewoodSocket>());
            }
            fire.sockets = sockets.ToArray();
            fire.logsRequired = 2;

            var fx = Prefab("FX_Fire_01");
            if (fx)
            {
                var f = Place(fx, fireGo.transform, center + Vector3.up * 0.05f, 0f, 0.7f);
                fire.fireEffect = f;
            }
            var light = new GameObject("FireLight").AddComponent<Light>();
            light.transform.SetParent(fireGo.transform);
            light.transform.position = center + Vector3.up * 0.7f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 9f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            fire.fireLight = light;
            fire.crackle = AudioObject("Crackle", fireGo.transform, center + Vector3.up * 0.3f, ProceduralAudioSource.Kind.Crackle, false, 1f, 0.7f);

            // 리스폰 지점: 불 남쪽 2.2m에서 북쪽(진행 방향)을 바라봄
            if (respawnOverride != null) fire.respawnPoint = respawnOverride;
            else fire.respawnPoint = Marker("RespawnPoint", new Vector2(pos2.x, pos2.y - 2.2f), 0f, site);

            // 장작 더미
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(pos2.x + 1.9f + i * 0.18f, pos2.y - 0.6f + (i % 2) * 0.25f);
                var log = new GameObject($"Firewood_{i}");
                log.transform.SetParent(site);
                var (_, thick) = GrabVisual(log.transform, "SM_Tree_Log_01", 0.5f, true, PrimitiveType.Cylinder, s_WoodMat);
                var cap = log.AddComponent<CapsuleCollider>();
                cap.direction = 1;
                cap.height = 0.5f;
                cap.radius = Mathf.Clamp(thick * 0.5f, 0.03f, 0.08f);
                MakeGrabbable(log, 0.8f);
                log.AddComponent<Firewood>();
                log.transform.position = Ground(p) + Vector3.up * 0.12f;
                log.transform.rotation = Quaternion.Euler(0f, Rand(-20f, 20f), 90f);
            }

            // 그루터기 위의 파이어스틱 (+ 시작 지점에는 나침반)
            var stumpPos = new Vector2(pos2.x - 1.6f, pos2.y - 1.1f);
            var stumpPrefab = Prefab("SM_Tree_Stump_01");
            float top = GroundY(stumpPos) + 0.45f;
            if (stumpPrefab)
            {
                var stump = Place(stumpPrefab, site, Ground(stumpPos), Rand(0f, 360f), 1f);
                FitToSize(stump, 0.8f);
                var b = WorldBounds(stump);
                top = b.max.y;
                EnsureMeshColliders(stump);
            }
            for (int i = 0; i < 2; i++)
            {
                var stick = BuildFireStick(site, i);
                stick.transform.position = new Vector3(stumpPos.x, top + 0.04f, stumpPos.y - 0.06f + i * 0.12f);
                stick.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            }
            if (withCompass)
            {
                var compass = BuildCompass(site);
                compass.transform.position = new Vector3(stumpPos.x + 0.15f, top + 0.03f, stumpPos.y + 0.18f);
            }
        }

        static GameObject BuildFireStick(Transform parent, int i)
        {
            var go = new GameObject($"FireStick_{i}");
            go.transform.SetParent(parent);
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.name = "Visual";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localScale = new Vector3(0.028f, 0.18f, 0.028f);
            vis.GetComponent<Renderer>().sharedMaterial = i == 0 ? s_WoodMat : s_IvoryMat;

            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.height = 0.36f;
            cap.radius = 0.016f;

            var zone = new GameObject("StrikeZone");
            zone.transform.SetParent(go.transform, false);
            var trig = zone.AddComponent<CapsuleCollider>();
            trig.isTrigger = true;
            trig.direction = 1;
            trig.height = 0.42f;
            trig.radius = 0.04f;

            MakeGrabbable(go, 0.3f);
            var stick = go.AddComponent<FireStick>();
            stick.sparks = SparkSystem(go.transform);
            stick.strikeAudio = AudioObject("StrikeAudio", go.transform, go.transform.position, ProceduralAudioSource.Kind.Knock, false, 1f, 0.8f);
            return go;
        }

        static GameObject BuildCompass(Transform parent)
        {
            var go = new GameObject("Compass");
            go.transform.SetParent(parent);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.09f, 0.008f, 0.09f);
            body.GetComponent<Renderer>().sharedMaterial = s_DarkMat;

            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.name = "Face";
            face.transform.SetParent(go.transform, false);
            face.transform.localPosition = new Vector3(0f, 0.0085f, 0f);
            face.transform.localScale = new Vector3(0.08f, 0.001f, 0.08f);
            face.GetComponent<Renderer>().sharedMaterial = s_IvoryMat;

            var pivot = new GameObject("Needle").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0f, 0.012f, 0f);
            Needle(pivot, 0.017f, s_RedMat);
            Needle(pivot, -0.017f, s_DarkMat);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.09f, 0.025f, 0.09f);
            MakeGrabbable(go, 0.2f);
            go.AddComponent<Compass>().needle = pivot;
            return go;
        }

        static void Needle(Transform pivot, float z, Material m)
        {
            var n = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(n.GetComponent<Collider>());
            n.transform.SetParent(pivot, false);
            n.transform.localPosition = new Vector3(0f, 0f, z);
            n.transform.localScale = new Vector3(0.007f, 0.003f, 0.034f);
            n.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ───────────────────────── XR 리그 ─────────────────────────

        static XROrigin BuildRig(Vector2 start)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Ground(start), Quaternion.identity);
            var rig = go.GetComponent<XROrigin>();

            var cam = rig.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.farClipPlane = 80f;
            cam.nearClipPlane = 0.02f;

            // 연속 이동 중에만 비네팅
            var vigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VignettePrefab);
            var move = go.GetComponentInChildren<ContinuousMoveProvider>(true);
            if (vigPrefab != null)
            {
                var vig = (GameObject)PrefabUtility.InstantiatePrefab(vigPrefab, cam.transform);
                vig.transform.localPosition = Vector3.zero;
                vig.transform.localRotation = Quaternion.identity;
                var ctrl = vig.GetComponent<TunnelingVignetteController>();
                if (ctrl != null && move != null)
                {
                    ctrl.locomotionVignetteProviders.Clear();
                    ctrl.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = move, enabled = true });
                    EditorUtility.SetDirty(ctrl);
                }
            }
            return rig;
        }

        // ───────────────────────── 시스템·UI ─────────────────────────

        static void BuildSystems(XROrigin rig, Camera cam, Transform startPoint, Transform goalPoint, Vector2 start)
        {
            var systems = new GameObject("Systems");

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<XRUIInputModule>();

            var gm = systems.AddComponent<GameManager>();
            gm.rig = rig;
            gm.startPoint = startPoint;
            gm.goalPoint = goalPoint;
            gm.goalRadius = 10f;
            systems.AddComponent<PlayerVitals>();

            // 눈보라: 머리 위를 따라다니는 눈 파티클 + 바람 소리
            var blizzard = systems.AddComponent<Blizzard>();
            blizzard.playerCamera = cam;
            blizzard.sun = RenderSettings.sun;
            var snowGo = new GameObject("Snowfall");
            var follow = snowGo.AddComponent<BodyFollower>();
            follow.head = cam.transform;
            follow.heightBelowHead = -7f;
            follow.yawFollowSpeed = 0f;
            blizzard.snow = SnowSystem(snowGo.transform);
            blizzard.wind = AudioObject("Wind", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Wind, true, 0f, 0.2f);

            // 허리 소켓 2개 (나침반·파이어스틱 보관)
            var belt = new GameObject("Belt");
            var bf = belt.AddComponent<BodyFollower>();
            bf.head = cam.transform;
            bf.heightBelowHead = 0.6f;
            foreach (var x in new[] { -0.22f, 0.22f })
            {
                var s = new GameObject(x < 0 ? "BeltSocket_L" : "BeltSocket_R");
                s.transform.SetParent(belt.transform, false);
                s.transform.localPosition = new Vector3(x, 0f, 0.12f);
                var c = s.AddComponent<SphereCollider>();
                c.isTrigger = true;
                c.radius = 0.12f;
                s.AddComponent<XRSocketInteractor>();
            }

            // 시작 화면
            var title = WorldCanvas("TitleCanvas", null, new Vector2(1000, 720), 0.0016f, true, cam);
            title.transform.SetPositionAndRotation(Ground(start) + new Vector3(0f, 1.6f, 2.6f), Quaternion.identity);
            Panel(title.transform, new Color(0.05f, 0.07f, 0.1f, 0.82f));
            Text(title.transform, "Untrodden", 120, new Vector2(0, 230), new Vector2(950, 150), Color.white, FontStyles.Bold);
            Text(title.transform, "눈보라와 곰을 피해 얼어붙은 숲을 지나\n바닷가 마을까지 살아서 도착하세요", 36, new Vector2(0, 85), new Vector2(950, 120), new Color(0.85f, 0.9f, 1f));
            Text(title.transform, $"제작  {CreatorName}", 32, new Vector2(0, 0), new Vector2(900, 50), new Color(1f, 0.8f, 0.5f));
            Text(title.transform, "왼쪽 스틱 이동 · 오른쪽 스틱 회전 / 앞으로 밀어 순간이동\n그립 버튼으로 잡기 · 나침반 빨간 바늘이 북쪽(바다)", 26, new Vector2(0, -85), new Vector2(950, 90), new Color(0.75f, 0.8f, 0.88f));
            var startBtn = Button(title.transform, "시작하기", new Vector2(0, -235));
            UnityEventTools.AddPersistentListener(startBtn.onClick, new UnityAction(gm.StartGame));
            gm.titleCanvas = title.gameObject;

            // 결과 화면
            var result = WorldCanvas("ResultCanvas", null, new Vector2(900, 620), 0.0016f, true, cam);
            Panel(result.transform, new Color(0.05f, 0.07f, 0.1f, 0.85f));
            Text(result.transform, "마을에 도착했어요!", 70, new Vector2(0, 200), new Vector2(850, 110), new Color(1f, 0.85f, 0.55f), FontStyles.Bold);
            var stats = Text(result.transform, "", 44, new Vector2(0, 30), new Vector2(800, 220), Color.white);
            var rs = result.gameObject.AddComponent<ResultScreen>();
            rs.statsText = stats;
            var again = Button(result.transform, "다시 하기", new Vector2(0, -200));
            UnityEventTools.AddPersistentListener(again.onClick, new UnityAction(rs.OnRestart));
            gm.resultScreen = rs;

            // 손목 HUD (왼손)
            var left = FindDeep(rig.transform, "Left Controller");
            var hud = WorldCanvas("WristHUD", left != null ? left : cam.transform, new Vector2(320, 250), 0.0005f, false, cam);
            hud.transform.localPosition = left != null ? new Vector3(0f, 0.035f, -0.11f) : new Vector3(-0.2f, -0.2f, 0.5f);
            hud.transform.localRotation = left != null ? Quaternion.Euler(62f, 0f, 0f) : Quaternion.identity;
            Panel(hud.transform, new Color(0.04f, 0.05f, 0.08f, 0.78f));
            var w = hud.gameObject.AddComponent<WristHUD>();
            w.timeText = Text(hud.transform, "00:00", 30, new Vector2(95, 95), new Vector2(120, 40), Color.white);
            w.healthFill = Bar(hud.transform, "체력", 50, new Color(0.9f, 0.25f, 0.25f));
            w.warmthFill = Bar(hud.transform, "체온", 5, new Color(1f, 0.6f, 0.2f));
            w.foodFill = Bar(hud.transform, "허기", -40, new Color(0.85f, 0.75f, 0.3f));
            w.statusText = Text(hud.transform, "", 22, new Vector2(0, -78), new Vector2(300, 30), Color.white);
            w.noticeText = Text(hud.transform, "", 18, new Vector2(0, -108), new Vector2(300, 40), new Color(1f, 0.9f, 0.7f));
            w.alertAudio = AudioObject("AlertBeep", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Beep, false, 0f, 0.5f);

            // 시야 아래쪽 알림
            var toast = WorldCanvas("ToastCanvas", cam.transform, new Vector2(800, 120), 0.0012f, false, cam);
            toast.transform.localPosition = new Vector3(0f, -0.28f, 1.2f);
            toast.transform.localRotation = Quaternion.identity;
            w.toastText = Text(toast.transform, "", 40, Vector2.zero, new Vector2(780, 110), new Color(1f, 0.95f, 0.85f));
            w.toastText.outlineWidth = 0.2f;
            w.toastText.outlineColor = new Color32(0, 0, 0, 200);

            // 피해 연출 (붉은 테두리)
            var dmg = WorldCanvas("DamageVignette", cam.transform, new Vector2(1000, 1000), 0.0012f, false, cam);
            dmg.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            dmg.transform.localRotation = Quaternion.identity;
            var img = Image(dmg.transform, s_Vignette, new Color(0.8f, 0.05f, 0.05f, 0f));
            Stretch(img.rectTransform);
            dmg.gameObject.AddComponent<DamageVignette>().image = img;
        }

        static Image Bar(Transform parent, string label, float y, Color color)
        {
            Text(parent, label, 24, new Vector2(-115, y), new Vector2(70, 34), Color.white);
            var bg = Image(parent, s_White, new Color(1f, 1f, 1f, 0.15f));
            bg.rectTransform.anchoredPosition = new Vector2(35, y);
            bg.rectTransform.sizeDelta = new Vector2(210, 22);
            var fill = Image(bg.transform, s_White, color);
            Stretch(fill.rectTransform);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
            return fill;
        }

        static Canvas WorldCanvas(string name, Transform parent, Vector2 size, float scale, bool interactive, Camera cam)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent) go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>().enabled = false;
                go.AddComponent<TrackedDeviceGraphicRaycaster>();
            }
            return canvas;
        }

        static void Panel(Transform parent, Color color)
        {
            var img = Image(parent, s_White, color);
            img.raycastTarget = true;
            Stretch(img.rectTransform);
            img.transform.SetAsFirstSibling();
        }

        static Image Image(Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject("Image", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 pos, Vector2 box, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (s_Font) t.font = s_Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.richText = true;
            var rt = t.rectTransform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            return t;
        }

        static Button Button(Transform parent, string label, Vector2 pos)
        {
            var img = Image(parent, s_White, new Color(0.95f, 0.55f, 0.2f));
            img.name = "Button";
            img.raycastTarget = true;
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = new Vector2(380, 96);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 0.85f, 0.7f);
            colors.pressedColor = new Color(0.8f, 0.5f, 0.3f);
            b.colors = colors;
            Text(img.transform, label, 44, Vector2.zero, new Vector2(370, 90), Color.white, FontStyles.Bold);
            return b;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ───────────────────────── 조명·파티클·오디오 ─────────────────────────

        static void SetupLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            sun.color = new Color(0.85f, 0.9f, 1f);
            sun.intensity = 0.9f;
            sun.shadows = LightShadows.None;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = 8f;
            RenderSettings.fogEndDistance = 65f;
            RenderSettings.sun = sun;
        }

        static ParticleSystem SnowSystem(Transform parent)
        {
            var go = new GameObject("Snow");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 7f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 5000;
            main.gravityModifier = 0.12f;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = 60f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 1f, 34f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.6f, -0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_SnowMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static ParticleSystem SparkSystem(Transform parent)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.4f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.6f;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_SparkMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static AudioSource AudioObject(string name, Transform parent, Vector3 pos, ProceduralAudioSource.Kind kind, bool loopPlay, float spatial, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.loop = loopPlay || kind == ProceduralAudioSource.Kind.Crackle || kind == ProceduralAudioSource.Kind.Wind;
            src.playOnAwake = loopPlay;
            src.spatialBlend = spatial;
            src.volume = volume;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = 25f;
            go.AddComponent<ProceduralAudioSource>().kind = kind;
            return src;
        }

        // ───────────────────────── 공용 애셋 ─────────────────────────

        static void CreateSharedAssets()
        {
            s_Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            s_White = SpritePng("White", 8, (u, v) => Color.white);
            s_Vignette = SpritePng("Vignette", 128, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.5f;
                return new Color(1f, 1f, 1f, SmoothStep(0.55f, 1.05f, d));
            });

            var dot = SpritePng("SoftDot", 32, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.5f;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d));
            }).texture;
            s_SnowMat = ParticleMaterial("Snow", dot, new Color(1f, 1f, 1f, 0.9f));
            s_SparkMat = ParticleMaterial("Spark", dot, Color.white);

            s_WoodMat = LitMaterial("Wood", new Color(0.45f, 0.3f, 0.18f));
            s_DarkMat = LitMaterial("Dark", new Color(0.15f, 0.15f, 0.17f));
            s_RedMat = LitMaterial("Red", new Color(0.85f, 0.15f, 0.12f));
            s_IvoryMat = LitMaterial("Ivory", new Color(0.92f, 0.88f, 0.78f));
        }

        static Sprite SpritePng(string name, int size, System.Func<float, float, Color> fn)
        {
            string path = $"{GenDir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, fn((x + 0.5f) / size, (y + 0.5f) / size));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite sp) sprite = sp;
            if (sprite == null) Debug.LogError("[Untrodden] sprite import failed: " + path);
            return sprite;
        }

        static Material LitMaterial(string name, Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.2f);
            m.enableInstancing = true;
            CreateOrReplace(m, $"{GenDir}/M_{name}.mat");
            return m;
        }

        static Material ParticleMaterial(string name, Texture tex, Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            CreateOrReplace(m, $"{GenDir}/M_Particle_{name}.mat");
            return m;
        }

        static Texture2D NoiseTexture(Color a, Color b, int seed)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
            float ox = (seed & 0xff) * 0.37f, oy = ((seed >> 8) & 0xff) * 0.53f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 타일링되도록 원통 좌표로 노이즈를 샘플링
                float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
                float n = Mathf.PerlinNoise(ox + Mathf.Cos(u) * 1.5f + Mathf.Sin(v) * 1.5f, oy + Mathf.Sin(u) * 1.5f + Mathf.Cos(v) * 1.5f) * 0.6f
                        + Mathf.PerlinNoise(ox + Mathf.Cos(u) * 6f + Mathf.Sin(v) * 6f, oy + Mathf.Sin(u) * 6f + Mathf.Cos(v) * 6f) * 0.4f;
                tex.SetPixel(x, y, Color.Lerp(a, b, n));
            }
            tex.Apply(true);
            return tex;
        }

        // ───────────────────────── 배치 헬퍼 ─────────────────────────

        static GameObject Prefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Synty" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            Debug.LogWarning("[Untrodden] prefab not found: " + name);
            return null;
        }

        static List<GameObject> Prefabs(params string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var p = Prefab(n);
                if (p) list.Add(p);
            }
            return list;
        }

        static Material FindMaterial(params string[] names)
        {
            foreach (var name in names)
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Material", new[] { "Assets/Synty" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m) return m;
            }
            return LitMaterial("WaterFallback", new Color(0.25f, 0.4f, 0.5f));
        }

        static GameObject Place(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation);
            go.transform.localScale *= scale;
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            bool has = false;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!has) b.size = Vector3.one * 0.1f;
            return b;
        }

        static void FitToSize(GameObject go, float size)
        {
            var b = WorldBounds(go);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest > 1e-4f) go.transform.localScale *= size / longest;
        }

        /// <summary>잡을 물체의 겉모습: Synty 모델을 길이에 맞춰 축소하고 긴 축을 로컬 Y로 맞춘다. 없으면 기본 도형.</summary>
        static (GameObject visual, float thickness) GrabVisual(Transform root, string prefabName, float length, bool alignLongToY, PrimitiveType fallback, Material fallbackMat)
        {
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;
            var src = Prefab(prefabName);
            GameObject vis;
            if (src != null)
            {
                vis = Object.Instantiate(src, root);
                foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var lod in vis.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            }
            else
            {
                vis = GameObject.CreatePrimitive(fallback);
                Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(root, false);
                vis.GetComponent<Renderer>().sharedMaterial = fallbackMat;
            }
            vis.name = "Visual";
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            var b = WorldBounds(vis);
            var s = b.size;
            float longest = Mathf.Max(s.x, s.y, s.z);
            vis.transform.localScale *= length / Mathf.Max(longest, 1e-4f);
            if (alignLongToY)
            {
                if (s.x >= s.y && s.x >= s.z) vis.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                else if (s.z >= s.x && s.z >= s.y) vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var b2 = WorldBounds(vis);
            vis.transform.position -= b2.center;
            float thick = alignLongToY ? Mathf.Min(b2.size.x, b2.size.z) : Mathf.Min(b2.size.x, b2.size.z, b2.size.y);
            return (vis, thick);
        }

        static void MakeGrabbable(GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = true;
            grab.useDynamicAttach = true;
        }

        static void AddTrunkCollider(GameObject tree)
        {
            if (tree.GetComponentInChildren<Collider>() != null) return;
            var c = tree.AddComponent<CapsuleCollider>();
            float s = tree.transform.lossyScale.y;
            c.radius = 0.35f / Mathf.Max(s, 0.01f);
            c.height = 5f / Mathf.Max(s, 0.01f);
            c.center = new Vector3(0f, c.height / 2f, 0f);
        }

        static void EnsureMeshColliders(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>() != null) return;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.Contains("LOD") && !mf.name.Contains("LOD0")) continue;
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
        }

        static void MarkStatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<ParticleSystem>() || t.GetComponent<Light>() || t.GetComponent<AudioSource>()) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        static void MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Transform Marker(string name, Vector2 p, float yaw, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            if (parent) t.SetParent(parent);
            t.SetPositionAndRotation(Ground(p), Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r) return r;
            }
            return null;
        }

        static void CreateOrReplace(Object asset, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }

        static bool Near(Vector2 a, Vector2 b, float r) => (a - b).sqrMagnitude < r * r;
        static float Rand(float a, float b) => a + (float)s_Rng.NextDouble() * (b - a);
        static T Pick<T>(List<T> list) => list[s_Rng.Next(list.Count)];

        static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
