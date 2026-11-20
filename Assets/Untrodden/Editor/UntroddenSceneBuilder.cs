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
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// Untrodden 플레이 씬을 코드로 생성한다. (메뉴: Untrodden > Build Scene)
    /// 남쪽이 높고 북쪽이 바다인 지형, 강, 숲, 모닥불 체크포인트, 바위 그늘, 마을, XR 리그와 UI를 배치한다.
    /// 다시 실행하면 씬과 생성 애셋을 새로 만든다.
    /// </summary>
    public static partial class UntroddenSceneBuilder
    {
        const string Root = "Assets/Untrodden";
        const string GenDir = Root + "/Generated";
        const string ScenePath = Root + "/Scenes/Untrodden.unity";
        const string RigPrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string VignettePrefab = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";
        const string FontPath = "Assets/99. External Assets/Font/Noto_Sans_KR/NotoSansKR-Regular SDF.asset";
        const string CreatorName = "채경희";

        // 지형 치수 (월드 좌표, 미터)
        const float SizeX = 240f, SizeZ = 440f, TerrainBase = -10f, TerrainHeight = 180f;
        const float SeaZ = 402f, SeaLevel = 0.4f;
        const int HeightRes = 513, AlphaRes = 512;
        // 플레이어가 다닐 수 있는 영역: 양옆·남쪽 벽 비탈과 바다를 뺀 안쪽
        static readonly Vector2 PlayMin = new(30f, 16f), PlayMax = new(SizeX - 30f, SeaZ + 1f);

        static readonly Color FogColor = new(0.63f, 0.69f, 0.77f);
        static readonly Color StormFogColor = new(0.8f, 0.83f, 0.87f);
        // 시야: 안개가 끝나는 거리 바로 뒤에서 카메라가 렌더링을 끊는다(최적화)
        const float CalmFogStart = 4f, CalmFogEnd = 42f, StormFogEnd = 16f, CameraFar = 46f;

        static Terrain s_Terrain;
        static TMP_FontAsset s_Font;
        static Sprite s_White, s_Vignette;
        static Material s_SnowMat, s_FlakeMat, s_GustMat, s_SparkMat, s_WoodMat, s_DarkMat, s_RedMat, s_IvoryMat, s_WindowMat;
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
            BuildProfile();
            var start = new Vector2(RunX(StartZ), StartZ);
            var fires = FirePositions();
            var village = new Vector2(RunX(VillageZ), VillageZ);

            AddFlat(start, 10f);
            foreach (var f in fires) AddFlat(f, 5f);
            AddFlat(village, 26f);
            PlanCourse(start, fires, village);
            CreateCourseMaterials();

            SetupLighting();
            BuildTerrain();

            var env = new GameObject("Environment").transform;
            BuildWater(env);
            BuildBoundary(env);
            BuildForest(env);
            BuildVillage(env, village);

            var startPoint = Marker("StartPoint", start, StartYaw());
            var goal = new Vector2(RunX(FinishZ), FinishZ);
            var goalPoint = Marker("GoalPoint", goal, 0f);

            var gameplay = new GameObject("Gameplay").transform;
            for (int i = 0; i < fires.Length; i++)
                BuildCampfireSite(gameplay, i, fires[i]);
            var gates = BuildGates(gameplay);
            BuildArch(gameplay, "StartArch", StartZ + 11f, "START", s_FlagBlue);
            BuildArch(gameplay, "FinishArch", FinishZ, "FINISH", s_FlagRed);

            var rig = BuildRig(start);
            var cam = rig.Camera;
            BuildSystems(rig, cam, startPoint, goalPoint, start, gates);

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

        static void BuildTerrain()
        {
            var data = new TerrainData { heightmapResolution = HeightRes, alphamapResolution = AlphaRes };
            data.size = new Vector3(SizeX, TerrainHeight, SizeZ);
            // 에셋을 먼저 만든 뒤 데이터를 채워야 스플랫(알파맵) 텍스처가 함께 저장된다
            CreateOrReplace(data, GenDir + "/TerrainData.asset");

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
                Layer("Snow", new Color(0.93f, 0.95f, 0.98f), new Color(0.82f, 0.86f, 0.92f)),   // 숲속 자연설
                Layer("Rock", new Color(0.45f, 0.46f, 0.48f), new Color(0.32f, 0.33f, 0.35f)),
                Layer("Piste", new Color(0.8f, 0.84f, 0.9f), new Color(0.7f, 0.74f, 0.81f)),   // 정설된 슬로프
            };

            var alpha = new float[AlphaRes, AlphaRes, 3];
            for (int iz = 0; iz < AlphaRes; iz++)
            for (int ix = 0; ix < AlphaRes; ix++)
            {
                float nx = ix / (float)(AlphaRes - 1), nz = iz / (float)(AlphaRes - 1);
                float x = nx * SizeX, z = nz * SizeZ;
                float steep = data.GetSteepness(nx, nz);
                float rock = SmoothStep(30f, 42f, steep);
                bool onCourse = z > StartZ - 14f && z < FinishZ + 30f;
                float piste = onCourse ? SmoothStep(0.8f, -0.8f, RunEdge(x, z)) * (1f - rock) : 0f;
                float snow = Mathf.Max(0f, 1f - rock - piste);
                alpha[iz, ix, 0] = snow;
                alpha[iz, ix, 1] = rock;
                alpha[iz, ix, 2] = piste;
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);

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

        }

        /// <summary>플레이 영역 둘레의 보이지 않는 벽. 걸어서 밖으로 나가지 못하게 막는다.</summary>
        static void BuildBoundary(Transform parent)
        {
            var root = new GameObject("PlayAreaBoundary").transform;
            root.SetParent(parent);
            var c = (PlayMin + PlayMax) / 2f;
            var size = PlayMax - PlayMin;
            const float y = 30f, h = 120f, t = 1f;
            void Wall(string name, Vector3 pos, Vector3 sz)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root);
                go.transform.position = pos;
                go.AddComponent<BoxCollider>().size = sz;
            }
            Wall("South", new Vector3(c.x, y, PlayMin.y - t / 2f), new Vector3(size.x + 2f, h, t));
            Wall("North", new Vector3(c.x, y, PlayMax.y + t / 2f), new Vector3(size.x + 2f, h, t));
            Wall("West", new Vector3(PlayMin.x - t / 2f, y, c.y), new Vector3(t, h, size.y + 2f));
            Wall("East", new Vector3(PlayMax.x + t / 2f, y, c.y), new Vector3(t, h, size.y + 2f));
        }

        // ───────────────────────── 숲·바위 ─────────────────────────

        static void BuildVillage(Transform parent, Vector2 center)
        {
            var root = new GameObject("Village").transform;
            root.SetParent(parent);
            // 임시 통나무집 (팩에 어울리는 시골집 모델이 없어 기본 도형으로 제작)
            var c3 = Ground(center);
            int n = 0;
            for (float a = 25f; a <= 335f; a += 38f)
            {
                if (a > 140f && a < 220f) continue; // 남쪽(플레이어가 오는 방향)은 비워 둔다
                var dir = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad));
                var p = new Vector2(center.x + dir.x * 16f, center.y + dir.z * 16f);
                BuildCabin(root, Ground(p) + Vector3.down * 0.2f, -dir, Rand(0.9f, 1.15f), n++);
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

        static void BuildCabin(Transform parent, Vector3 pos, Vector3 facing, float scale, int index)
        {
            var cabin = new GameObject($"Cabin_{index}").transform;
            cabin.SetParent(parent);
            cabin.SetPositionAndRotation(pos, Quaternion.LookRotation(facing, Vector3.up));
            cabin.localScale = Vector3.one * scale;

            Box(cabin, "Body", new Vector3(0f, 1.6f, 0f), new Vector3(5f, 3.2f, 4f), Vector3.zero, s_WoodMat, true);
            // 박공지붕: 기울인 판 두 장 + 눈 덮인 윗면
            Box(cabin, "RoofL", new Vector3(-1.35f, 3.85f, 0f), new Vector3(3.2f, 0.18f, 4.6f), new Vector3(0f, 0f, 35f), s_DarkMat, false);
            Box(cabin, "RoofR", new Vector3(1.35f, 3.85f, 0f), new Vector3(3.2f, 0.18f, 4.6f), new Vector3(0f, 0f, -35f), s_DarkMat, false);
            Box(cabin, "SnowL", new Vector3(-1.32f, 3.97f, 0f), new Vector3(3.1f, 0.08f, 4.5f), new Vector3(0f, 0f, 35f), s_IvoryMat, false);
            Box(cabin, "SnowR", new Vector3(1.32f, 3.97f, 0f), new Vector3(3.1f, 0.08f, 4.5f), new Vector3(0f, 0f, -35f), s_IvoryMat, false);
            Box(cabin, "Gable", new Vector3(0f, 3.6f, 0f), new Vector3(1.6f, 0.9f, 3.9f), Vector3.zero, s_WoodMat, false);
            // 앞면: 문과 불 켜진 창문
            Box(cabin, "Door", new Vector3(0f, 1f, 2.01f), new Vector3(1f, 2f, 0.06f), Vector3.zero, s_DarkMat, false);
            Box(cabin, "WindowL", new Vector3(-1.6f, 1.8f, 2.01f), new Vector3(0.9f, 0.7f, 0.05f), Vector3.zero, s_WindowMat, false);
            Box(cabin, "WindowR", new Vector3(1.6f, 1.8f, 2.01f), new Vector3(0.9f, 0.7f, 0.05f), Vector3.zero, s_WindowMat, false);
            Box(cabin, "WindowSide", new Vector3(2.51f, 1.8f, 0f), new Vector3(0.05f, 0.7f, 0.9f), Vector3.zero, s_WindowMat, false);
            Box(cabin, "Chimney", new Vector3(1.4f, 4.4f, -0.8f), new Vector3(0.5f, 1.4f, 0.5f), Vector3.zero, s_DarkMat, false);
            MarkStatic(cabin.gameObject);
        }

        static void Box(Transform parent, string name, Vector3 localPos, Vector3 size, Vector3 euler, Material mat, bool keepCollider)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            if (!keepCollider) Object.DestroyImmediate(b.GetComponent<Collider>());
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localRotation = Quaternion.Euler(euler);
            b.transform.localScale = size;
            var r = b.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ───────────────────────── 모닥불 자리 ─────────────────────────

        static void BuildCampfireSite(Transform parent, int index, Vector2 pos2)
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

            // 리스폰 지점: 불 옆 슬로프 가장자리에서 아래(진행 방향)를 바라봄
            float side = Mathf.Sign(RunX(pos2.y) - pos2.x);
            var rp = new Vector2(pos2.x + side * 3.5f, pos2.y + 1f);
            var t = RunTangent(rp.y);
            fire.respawnPoint = Marker("RespawnPoint", rp, Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg, site);

            // 장작더미·통나무 의자 장식
            var logPrefab = Prefab("SM_Tree_Log_01");
            if (logPrefab)
            {
                var log = Place(logPrefab, site, Ground(new Vector2(pos2.x - side * 1.6f, pos2.y)), 90f, 1f);
                FitToSize(log, 1.6f);
                SnowCover(log);
                foreach (var col in log.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            }
        }

        // ───────────────────────── XR 리그 ─────────────────────────

        static XROrigin BuildRig(Vector2 start)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Ground(start), Quaternion.Euler(0f, StartYaw(), 0f));
            var rig = go.GetComponent<XROrigin>();

            var cam = rig.Camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogColor;
            cam.farClipPlane = CameraFar;
            cam.nearClipPlane = 0.02f;

            // 이동 방식: 스키(폴) + 멈췄을 때만 왼쪽 스틱 걷기·오른쪽 스틱 회전. 순간이동은 쓰지 않는다.
            ConfigureLocomotion(go);
            var move = go.GetComponentInChildren<ContinuousMoveProvider>(true);
            var turn = go.GetComponentInChildren<ContinuousTurnProvider>(true);
            if (move != null)
            {
                move.moveSpeed = 1.3f; // 스키 신고 걷기
                EditorUtility.SetDirty(move);
            }

            // 스키 폴·스키·스키 이동
            var left = FindDeep(go.transform, "Left Controller");
            var right = FindDeep(go.transform, "Right Controller");
            var host = move != null ? move.gameObject : go;
            var ski = host.AddComponent<SkiController>();
            ski.rig = rig;
            if (left != null) ski.leftPole = BuildPole(left, SkiPole.Side.Left);
            if (right != null) ski.rightPole = BuildPole(right, SkiPole.Side.Right);
            ski.skis = BuildSkis();
            ski.walkProviders = new Behaviour[] { move, turn };
            ski.windAudio = AudioObject("SkiWind", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Wind, true, 0f, 0f);
            ski.crashAudio = AudioObject("CrashThud", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Knock, false, 0f, 0.9f);
            SpeedFlakes(cam.transform);

            // 스키·걷기·회전 중에만 비네팅
            var vigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VignettePrefab);
            if (vigPrefab != null)
            {
                var vig = (GameObject)PrefabUtility.InstantiatePrefab(vigPrefab, cam.transform);
                vig.transform.localPosition = Vector3.zero;
                vig.transform.localRotation = Quaternion.identity;
                var ctrl = vig.GetComponent<TunnelingVignetteController>();
                if (ctrl != null && move != null)
                {
                    ctrl.locomotionVignetteProviders.Clear();
                    ctrl.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = ski, enabled = true });
                    ctrl.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = move, enabled = true });
                    if (turn != null)
                        ctrl.locomotionVignetteProviders.Add(new LocomotionVignetteProvider { locomotionProvider = turn, enabled = true });
                    EditorUtility.SetDirty(ctrl);
                }
            }
            return rig;
        }

        const float TurnSpeed = 50f; // 초당 회전 각도 (부드러운 회전)

        /// <summary>출발 방향: 슬로프 아래쪽.</summary>
        static float StartYaw()
        {
            var t = RunTangent(StartZ + 8f);
            return Mathf.Atan2(t.x, t.y) * Mathf.Rad2Deg;
        }

        static void ConfigureLocomotion(GameObject rigGo)
        {
            // 컨트롤러별 입력 관리자(Starter Assets): 순간이동 입력을 끊고, 회전은 스냅 대신 부드러운 회전
            foreach (var mb in rigGo.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "ControllerInputActionManager") continue;
                var so = new SerializedObject(mb);
                so.FindProperty("m_TeleportMode").objectReferenceValue = null;
                so.FindProperty("m_TeleportModeCancel").objectReferenceValue = null;
                so.FindProperty("m_TeleportInteractor").objectReferenceValue = null;
                so.FindProperty("m_SmoothTurnEnabled").boolValue = true; // 한 번에 꺾이지 않고 천천히 도는 회전
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 순간이동 인터랙터는 꺼 둔다(레이가 보이지 않도록)
            foreach (var t in rigGo.GetComponentsInChildren<Transform>(true))
                if (t.name == "Teleport Interactor") t.gameObject.SetActive(false);

            var turn = rigGo.GetComponentInChildren<ContinuousTurnProvider>(true);
            if (turn != null)
            {
                turn.turnSpeed = TurnSpeed;
                EditorUtility.SetDirty(turn);
            }
            else Debug.LogWarning("[Untrodden] ContinuousTurnProvider not found on rig");
        }

        // ───────────────────────── 시스템·UI ─────────────────────────

        static void BuildSystems(XROrigin rig, Camera cam, Transform startPoint, Transform goalPoint, Vector2 start, SlalomGate[] gates)
        {
            var systems = new GameObject("Systems");

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<XRUIInputModule>();

            var gm = systems.AddComponent<GameManager>();
            gm.rig = rig;
            gm.startPoint = startPoint;
            gm.goalPoint = goalPoint;
            gm.goalRadius = RunHalfWidth(FinishZ) + 3f;
            systems.AddComponent<PlayerVitals>();
            var course = systems.AddComponent<CourseTracker>();
            course.gates = gates;
            course.missPenalty = 5f;
            course.passAudio = AudioObject("GatePass", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Beep, false, 0f, 0.25f);
            var playArea = systems.AddComponent<PlayArea>();
            playArea.min = PlayMin;
            playArea.max = PlayMax;
            systems.AddComponent<ReturnToCampfireInput>();

            // 눈보라: 머리 위를 따라다니는 눈 파티클 + 바람 소리
            var blizzard = systems.AddComponent<Blizzard>();
            blizzard.playerCamera = cam;
            blizzard.sun = RenderSettings.sun;
            blizzard.calmFog = FogColor;
            blizzard.stormFog = StormFogColor;
            blizzard.calmFogStart = CalmFogStart;
            blizzard.calmFogEnd = CalmFogEnd;
            blizzard.stormFogEnd = StormFogEnd;
            var snowGo = new GameObject("Snowfall");
            var follow = snowGo.AddComponent<BodyFollower>();
            follow.head = cam.transform;
            follow.heightBelowHead = -7f;
            follow.yawFollowSpeed = 0f;
            // 눈: Blizzard 셰이더 3겹 (원본 데모의 Far 레이어는 안개에 가려 Quest에서는 생략)
            //  Mid = 주된 눈 / Near = 가끔 얼굴 앞을 스치는 큰 흐린 눈 / Gusts = 눈보라 때만 휩쓸리는 눈가루 덩어리
            var mid = FlakeLayer(snowGo.transform, "Snow_Mid", s_FlakeMat, new Vector3(0f, -4f, 0f), new Vector3(30f, 8f, 30f),
                                 new Vector2(0.04f, 0.08f), new Vector2(4f, 6f), 15000, true);
            var near = FlakeLayer(snowGo.transform, "Snow_Near", s_FlakeMat, new Vector3(0f, -6.5f, 0f), new Vector3(6f, 3f, 6f),
                                  new Vector2(0.05f, 0.1f), new Vector2(3f, 4f), 800, true);
            var gusts = FlakeLayer(snowGo.transform, "Snow_Gusts", s_GustMat, new Vector3(0f, -6f, 0f), new Vector3(40f, 5f, 40f),
                                   new Vector2(6f, 12f), new Vector2(6f, 8f), 100, false);
            var gustMain = gusts.main;
            gustMain.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.2f), new Color(1f, 1f, 1f, 0.4f));
            blizzard.snowLayers = new[]
            {
                new Blizzard.SnowLayer { system = mid, calmRate = 300f, stormRate = 2500f, windScale = 1f },
                new Blizzard.SnowLayer { system = near, calmRate = 15f, stormRate = 150f, windScale = 1f },
                new Blizzard.SnowLayer { system = gusts, calmRate = 0f, stormRate = 6f, windScale = 1.4f },
            };
            blizzard.wind = AudioObject("Wind", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Wind, true, 0f, 0.2f);

            // 입김
            var breathGo = new GameObject("Breath");
            breathGo.transform.SetParent(systems.transform);
            breathGo.AddComponent<BreathPuff>().head = cam.transform;
            BreathSystem(breathGo);

            // 시작 화면
            var title = WorldCanvas("TitleCanvas", null, new Vector2(1000, 720), 0.0016f, true, cam);
            var startRot = Quaternion.Euler(0f, StartYaw(), 0f);
            title.transform.SetPositionAndRotation(Ground(start) + Vector3.up * 1.6f + startRot * Vector3.forward * 2.6f, startRot);
            Panel(title.transform, new Color(0.05f, 0.07f, 0.1f, 0.82f));
            Text(title.transform, "Untrodden", 120, new Vector2(0, 230), new Vector2(950, 150), Color.white, FontStyles.Bold);
            Text(title.transform, "눈 덮인 산을 스키로 내려가며 깃발 관문을 통과하고\n바닷가 마을까지 가장 빠르게 도착하세요", 36, new Vector2(0, 85), new Vector2(950, 120), new Color(0.85f, 0.9f, 1f));
            Text(title.transform, $"제작  {CreatorName}", 32, new Vector2(0, 0), new Vector2(900, 50), new Color(1f, 0.8f, 0.5f));
            Text(title.transform, "폴 끝이 눈에 닿으면 톡 떨림 · 닿은 채 뒤로 밀기: 출발 · 양손을 돌린 쪽으로 회전 · 옆으로 꺾으면 감속\n멈췄을 때 왼쪽 스틱 걷기·오른쪽 스틱 회전 · 막히면 왼쪽 메뉴 버튼 길게: 모닥불로\n놓친 관문 +5초 · 부딪히거나 눈보라를 맞으면 체력 감소 · 모닥불을 지나면 회복", 24, new Vector2(0, -95), new Vector2(980, 120), new Color(0.75f, 0.8f, 0.88f));
            var startBtn = Button(title.transform, "시작하기", new Vector2(0, -235));
            UnityEventTools.AddPersistentListener(startBtn.onClick, new UnityAction(gm.StartGame));
            gm.titleCanvas = title.gameObject;
            var titleFollow = title.gameObject.AddComponent<TitleFollow>();
            titleFollow.head = cam.transform;
            titleFollow.distance = 2.6f;

            // 결과 화면
            var result = WorldCanvas("ResultCanvas", null, new Vector2(900, 620), 0.0016f, true, cam);
            Panel(result.transform, new Color(0.05f, 0.07f, 0.1f, 0.85f));
            Text(result.transform, "결승선 통과!", 70, new Vector2(0, 210), new Vector2(850, 110), new Color(1f, 0.85f, 0.55f), FontStyles.Bold);
            var stats = Text(result.transform, "", 40, new Vector2(0, 25), new Vector2(840, 260), Color.white);
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
            w.timeText = Text(hud.transform, "0:00.0", 30, new Vector2(-60, 95), new Vector2(190, 40), Color.white, FontStyles.Bold);
            w.speedText = Text(hud.transform, "0 km/h", 22, new Vector2(100, 95), new Vector2(110, 36), new Color(0.8f, 0.9f, 1f));
            w.healthFill = Bar(hud.transform, "체력", 55, new Color(0.9f, 0.25f, 0.25f));
            w.gateText = Text(hud.transform, "관문 0/0", 24, new Vector2(-65, 8), new Vector2(180, 34), Color.white);
            w.statusText = Text(hud.transform, "", 20, new Vector2(-65, -30), new Vector2(180, 30), Color.white);
            w.noticeText = Text(hud.transform, "", 17, new Vector2(0, -95), new Vector2(305, 50), new Color(1f, 0.9f, 0.7f));

            // 나침반: 위쪽 = 지금 바라보는 방향. N은 북쪽(바다), 노란 화살표는 다음 관문
            var ring = Image(hud.transform, s_Ring, new Color(1f, 1f, 1f, 0.7f));
            ring.rectTransform.anchoredPosition = new Vector2(100, -12);
            ring.rectTransform.sizeDelta = new Vector2(88, 88);
            RectTransform Dial(string name)
            {
                var d = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                d.SetParent(ring.rectTransform, false);
                d.anchoredPosition = Vector2.zero;
                d.sizeDelta = new Vector2(88, 88);
                return d;
            }
            w.compassNorth = Dial("North");
            Text(w.compassNorth, "N", 20, new Vector2(0, 33), new Vector2(30, 26), new Color(1f, 0.35f, 0.3f), FontStyles.Bold);
            w.compassTarget = Dial("Target");
            Text(w.compassTarget, "▲", 26, new Vector2(0, 18), new Vector2(34, 30), new Color(1f, 0.85f, 0.2f));
            w.alertAudio = AudioObject("AlertBeep", cam.transform, cam.transform.position, ProceduralAudioSource.Kind.Beep, false, 0f, 0.5f);

            // 알림: 고개에 붙어 다니므로 시야 중심 가까이(약 5° 아래) 둔다.
            // 너무 아래에 두면 고개를 숙여도 따라 내려가서 눈만 아래로 굴려야 하고, 렌즈 가장자리라 흐릿하게 보인다.
            // 거리는 렌즈 초점 거리(약 1.5m 이상)에 맞춘다.
            var toast = WorldCanvas("ToastCanvas", cam.transform, new Vector2(800, 120), 0.0016f, false, cam);
            toast.transform.localPosition = new Vector3(0f, -0.14f, 1.6f);
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
            // 낮게 뜬 겨울 해: 약간 따뜻한 빛 + 차가운 하늘빛 그늘
            sun.transform.rotation = Quaternion.Euler(16f, -40f, 0f);
            sun.color = new Color(1f, 0.92f, 0.82f);
            sun.intensity = 0.75f;
            sun.shadows = LightShadows.None;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.68f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.45f, 0.52f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = CalmFogStart;
            RenderSettings.fogEndDistance = CalmFogEnd;
            RenderSettings.sun = sun;
        }

        static void BreathSystem(GameObject go)
        {
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new Color(1f, 1f, 1f, 0.22f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_SnowMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Blizzard 셰이더용 머티리얼. 원본 데모 기본값에서 VR(가까운 거리, 모바일 GPU)에 맞게 조금 줄였다.</summary>
        static Material BlizzardMaterial(string name, bool gust)
        {
            var shader = Shader.Find("Untrodden/Blizzard");
            if (shader == null) { Debug.LogError("[Untrodden] Untrodden/Blizzard shader not found"); return s_SnowMat; }
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Opacity", gust ? 0.85f : 0.9f);
            m.SetFloat("_AmbientStrength", gust ? 1.1f : 1f);
            m.SetFloat("_LightStrength", gust ? 0.4f : 0.6f);
            m.SetFloat("_ForwardScatter", gust ? 1f : 1.5f);
            m.SetFloat("_EdgeSoftness", gust ? 1f : 0.7f);
            m.SetFloat("_Irregularity", gust ? 0.3f : 0.18f);
            m.SetFloat("_Twinkle", gust ? 0f : 0.25f);
            m.SetFloat("_Wispiness", gust ? 0.85f : 0f);
            m.SetFloat("_NoiseScale", gust ? 1.3f : 2f);
            m.SetFloat("_NoiseScroll", gust ? 0.3f : 0.4f);
            m.SetFloat("_StretchAmount", gust ? 0.12f : 0.018f);
            m.SetFloat("_MaxStretch", gust ? 2.5f : 3.5f);
            m.SetFloat("_FocusDistance", 6f);
            m.SetFloat("_FocusRange", 3f);
            m.SetFloat("_BlurDistance", 25f);
            m.SetFloat("_BokehGrow", gust ? 0f : 1.5f); // 원본 2.5 → 크게 번진 눈이 겹쳐 그려지는 양(오버드로)을 줄임
            m.SetFloat("_SoftParticles", 0f);
            m.DisableKeyword("_SOFTPARTICLES_ON");
            m.SetFloat("_SoftParticleDistance", gust ? 2f : 0.3f);
            m.SetFloat("_NearFadeStart", gust ? 0.5f : 0.35f);
            m.SetFloat("_NearFadeRange", gust ? 3f : 0.6f);
            m.renderQueue = (int)RenderQueue.Transparent;
            CreateOrReplace(m, $"{GenDir}/M_{name}.mat");
            return m;
        }

        /// <summary>Blizzard 셰이더를 쓰는 눈 파티클 한 겹(원본 BlizzardDemoBuilder.CreateSnowLayer 기반).</summary>
        static ParticleSystem FlakeLayer(Transform parent, string name, Material mat, Vector3 localPos, Vector3 box,
                                         Vector2 size, Vector2 lifetime, int maxParticles, bool prewarm)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 10f;
            main.loop = true;
            main.prewarm = prewarm;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.55f), Color.white);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            var emission = ps.emission;
            emission.rateOverTime = 0f; // 양은 Blizzard가 평소/눈보라에 따라 정한다
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            vel.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.6f;
            noise.octaveCount = 1;
            noise.damping = true;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.sortMode = ParticleSystemSortMode.None;
            r.minParticleSize = 0f;
            r.maxParticleSize = 10f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // 셰이더 Attributes와 순서가 맞아야 한다
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomXY,
                ParticleSystemVertexStream.Center,
                ParticleSystemVertexStream.SizeX,
                ParticleSystemVertexStream.Velocity,
            });
            ps.Play();
            return ps;
        }

        /// <summary>
        /// 속도감용 눈가루: 시야 앞쪽 가까이에 공중에 멈춘 눈가루를 '이동 거리'에 비례해 뿌린다.
        /// 서 있으면 거의 안 생기고, 빠를수록 많이 생겨 옆으로 휙휙 지나간다.
        /// </summary>
        static void SpeedFlakes(Transform cam)
        {
            var go = new GameObject("SpeedFlakes");
            go.transform.SetParent(cam, false);
            go.transform.localPosition = new Vector3(0f, -0.3f, 6f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.gravityModifier = 0.02f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 30f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 3f, 7f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_SnowMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        static ParticleSystem SparkSystem(Transform parent)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
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
            s_FlakeMat = BlizzardMaterial("SnowFlake", false);
            s_GustMat = BlizzardMaterial("SnowGust", true);

            s_WoodMat = LitMaterial("Wood", new Color(0.45f, 0.3f, 0.18f));
            s_DarkMat = LitMaterial("Dark", new Color(0.15f, 0.15f, 0.17f));
            s_RedMat = LitMaterial("Red", new Color(0.85f, 0.15f, 0.12f));
            s_IvoryMat = LitMaterial("Ivory", new Color(0.92f, 0.88f, 0.78f));

            // 불 켜진 창문: 안개 속에서도 보이도록 Unlit
            s_WindowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Window" };
            s_WindowMat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.38f));
            CreateOrReplace(s_WindowMat, $"{GenDir}/M_Window.mat");
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
                // URP 지형은 알파를 매끄러움으로 쓰므로 낮게 둬서 눈이 번들거리지 않게 한다
                var c = Color.Lerp(a, b, n);
                c.a = 0.12f;
                tex.SetPixel(x, y, c);
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
