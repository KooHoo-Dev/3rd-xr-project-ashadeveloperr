using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// 스키 산: 남쪽 정상에서 북쪽 바닷가 마을로 내려가는 굽이진 메인 슬로프,
    /// 슬로프 양옆의 빽빽한 숲, 깃발 관문, 출발·결승 아치.
    /// </summary>
    public static partial class UntroddenSceneBuilder
    {
        const float StartZ = 26f, FinishZ = 360f, VillageZ = 384f;
        static readonly float[] FireZ = { 140f, 255f };

        // ───────────────────────── 슬로프 모양 ─────────────────────────

        /// <summary>슬로프 중심선.</summary>
        static float RunX(float z) => 120f + 30f * Mathf.Sin(z / 62f) + 9f * Mathf.Sin(z / 23f + 1.3f);

        /// <summary>슬로프 반폭(나무가 없는 구간).</summary>
        static float RunHalfWidth(float z) => 10f + 2.5f * Mathf.Sin(z / 41f + 0.7f);

        static float RunEdge(float x, float z) => Mathf.Abs(x - RunX(z)) - RunHalfWidth(z);

        static Vector2 RunTangent(float z)
        {
            float dx = (RunX(z + 0.5f) - RunX(z - 0.5f));
            return new Vector2(dx, 1f).normalized;
        }

        static float[] s_Profile;

        /// <summary>z에 따른 경사각(도). 출발 평지, 모닥불 앞 완만한 턱, 결승 뒤 평지.</summary>
        static float SlopeDeg(float z)
        {
            float s = 17f + 4f * Mathf.Sin(z / 37f + 0.5f);
            s = Mathf.Lerp(3f, s, SmoothStep(StartZ + 2f, StartZ + 16f, z));
            foreach (var fz in FireZ)
                s = Mathf.Lerp(s, 5f, SmoothStep(18f, 8f, Mathf.Abs(z - fz)));
            s = Mathf.Lerp(s, 2f, SmoothStep(FinishZ + 4f, FinishZ + 16f, z));
            if (z > SeaZ - 12f) s = Mathf.Lerp(s, 9f, SmoothStep(SeaZ - 12f, SeaZ - 6f, z));
            return s;
        }

        static void BuildProfile()
        {
            int n = Mathf.CeilToInt(SizeZ) + 2;
            s_Profile = new float[n];
            s_Profile[0] = 0f;
            for (int z = 1; z < n; z++)
                s_Profile[z] = s_Profile[z - 1] - Mathf.Tan(SlopeDeg(z - 0.5f) * Mathf.Deg2Rad);
            // 마을 높이를 바다 위 3m로 맞춘다
            float offset = 3f - s_Profile[(int)VillageZ];
            for (int z = 0; z < n; z++) s_Profile[z] += offset;
        }

        static float Profile(float z)
        {
            z = Mathf.Clamp(z, 0f, s_Profile.Length - 1.001f);
            int i = (int)z;
            return Mathf.Lerp(s_Profile[i], s_Profile[i + 1], z - i);
        }

        static float HeightNoFlat(float x, float z)
        {
            float dx = Mathf.Abs(x - RunX(z));
            float hw = RunHalfWidth(z);
            float h = Profile(z);
            h += 0.004f * dx * dx; // 슬로프가 가운데로 오목한 골짜기
            float offPiste = SmoothStep(hw, hw + 14f, dx);
            h += (Mathf.PerlinNoise(x / 40f + 3.1f, z / 40f + 7.7f) - 0.5f) * 8f * offPiste;
            h += (Mathf.PerlinNoise(x / 9f + 11f, z / 9f + 5f) - 0.5f) * 0.5f; // 잔 둔덕
            h += 32f * SmoothStep(26f, 0f, x) + 32f * SmoothStep(SizeX - 26f, SizeX, x) + 26f * SmoothStep(16f, 0f, z);
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

        static void AddFlat(Vector2 c, float r) => s_Flats.Add((c, r, Mathf.Max(HeightNoFlat(c.x, c.y), SeaLevel + 1.2f)));

        // ───────────────────────── 코스 계획 ─────────────────────────

        static readonly List<(Vector2 c, float r)> s_Clearings = new();
        static readonly List<(Vector2 pos, Vector2 dir, int side)> s_Gates = new();

        static Vector2[] FirePositions()
        {
            var list = new Vector2[FireZ.Length];
            for (int i = 0; i < FireZ.Length; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                list[i] = new Vector2(RunX(FireZ[i]) + side * (RunHalfWidth(FireZ[i]) + 1.5f), FireZ[i]);
            }
            return list;
        }

        static void PlanCourse(Vector2 start, Vector2[] fires, Vector2 village)
        {
            s_Clearings.Clear();
            s_Gates.Clear();
            s_Clearings.Add((start, 12f));
            foreach (var f in fires) s_Clearings.Add((f, 7f));
            s_Clearings.Add((village, 30f));

            int side = 1;
            for (float z = StartZ + 24f; z < FinishZ - 10f; z += 22f)
            {
                bool nearFire = false;
                foreach (var fz in FireZ) nearFire |= Mathf.Abs(z - fz) < 14f;
                if (nearFire) continue;
                var c = new Vector2(RunX(z), z);
                var t = RunTangent(z);
                var right = new Vector2(t.y, -t.x);
                var pos = c + right * (side * RunHalfWidth(z) * 0.38f);
                s_Gates.Add((pos, t, side));
                side = -side;
            }
            Debug.Log($"[Untrodden] gates: {s_Gates.Count}");
        }

        static float ClearingEdge(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var (c, r) in s_Clearings) best = Mathf.Min(best, Vector2.Distance(c, p) - r);
            return best;
        }

        // ───────────────────────── 숲 ─────────────────────────

        static Dictionary<string, Material> s_SnowSwap;

        /// <summary>Synty 기본 재질을 눈 덮인 변형으로 바꾼다(윗면에 눈이 쌓인 모습).</summary>
        static void SnowCover(GameObject go)
        {
            if (s_SnowSwap == null)
            {
                s_SnowSwap = new Dictionary<string, Material>();
                void Map(string from, string to)
                {
                    var m = FindSyntyMaterial(to);
                    if (m != null) s_SnowSwap[from] = m;
                }
                Map("PolygonNature_01", "PolygonNature_Snow_01");
                Map("PolygonNature_Tree_01", "PolygonNature_Snow_01");
                Map("Leaves_Pine_01", "Leaves_Pine_01_Snow");
                Map("Leaves_Pine_LOD_01", "Leaves_Pine_LOD_01_Snow");
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && s_SnowSwap.TryGetValue(mats[i].name, out var snow))
                    {
                        mats[i] = snow;
                        changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }
        }

        static Material FindSyntyMaterial(string exactName)
        {
            foreach (var guid in AssetDatabase.FindAssets(exactName + " t:Material", new[] { "Assets/Synty" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == exactName)
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }

        static void BuildForest(Transform parent)
        {
            var root = new GameObject("Forest").transform;
            root.SetParent(parent);
            var pines = Prefabs("SM_Tree_Pine_02", "SM_Tree_Pine_Large_01", "SM_Tree_Pine_Large_02",
                "SM_Tree_PolyPine_01", "SM_Tree_PolyPine_02", "SM_Tree_PolyPine_03");
            var smallPines = Prefabs("SM_Tree_Pine_Small_02", "SM_Tree_PolyPine_Sparse_01");
            var dead = Prefabs("SM_Tree_Pine_Dead_01", "SM_Tree_Dead_01", "SM_Tree_Dead_03", "SM_Tree_Birch_Dead_01");
            var rocks = Prefabs("SM_Rock_01", "SM_Rock_02", "SM_Rock_03", "SM_Rock_04", "SM_Rock_Boulder_01");
            var bushes = Prefabs("SM_Plant_Bush_01", "SM_Plant_Bush_02", "SM_Plant_Fern_01");

            var lo = PlayMin - new Vector2(14f, 10f);
            var hi = new Vector2(PlayMax.x + 14f, SeaZ - 8f);
            const float spacing = 3.8f;
            int trees = 0;
            for (float z = lo.y; z < hi.y; z += spacing)
            for (float x = lo.x; x < hi.x; x += spacing)
            {
                var p = new Vector2(x + Rand(-1.4f, 1.4f), z + Rand(-1.4f, 1.4f));
                float edge = Mathf.Min(RunEdge(p.x, p.y), ClearingEdge(p));
                if (edge < 1f) continue;

                float roll = Rand(0f, 1f);
                // 슬로프 가장자리: 듬성듬성한 작은 나무·덤불
                if (edge < 4f)
                {
                    if (roll < 0.25f && bushes.Count > 0)
                        SnowCover(Place(Pick(bushes), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.3f)));
                    else if (roll < 0.5f && smallPines.Count > 0)
                    {
                        var t = Place(Pick(smallPines), root, Ground(p) + Vector3.down * 0.15f, Rand(0f, 360f), Rand(0.7f, 1.1f));
                        SnowCover(t);
                        AddTrunkCollider(t);
                    }
                    continue;
                }

                if (roll < 0.8f && pines.Count > 0)
                {
                    var t = Place(Pick(pines), root, Ground(p) + Vector3.down * 0.2f, Rand(0f, 360f), Rand(1.05f, 1.7f));
                    SnowCover(t);
                    AddTrunkCollider(t);
                    trees++;
                }
                else if (roll < 0.84f && dead.Count > 0)
                {
                    var t = Place(Pick(dead), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.2f));
                    SnowCover(t);
                    AddTrunkCollider(t);
                }
                else if (roll < 0.89f && rocks.Count > 0)
                {
                    var r = Place(Pick(rocks), root, Ground(p) + Vector3.down * 0.3f, Rand(0f, 360f), 1f);
                    FitToSize(r, Rand(1f, 2.5f));
                    SnowCover(r);
                    EnsureMeshColliders(r);
                }
                else if (bushes.Count > 0)
                {
                    SnowCover(Place(Pick(bushes), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.2f)));
                }
            }
            // 나무가 수천 그루라 정적 배칭(메시 합치기)은 메모리만 키운다. SRP Batcher에 맡긴다.
            Debug.Log($"[Untrodden] trees placed: {trees}");
        }

        // ───────────────────────── 관문·출발·결승 ─────────────────────────

        static Material s_FlagRed, s_FlagBlue, s_FlagGreen, s_FlagGray, s_SkiMat;
        static Sprite s_Ring;

        static Material UnlitMaterial(string name, Color color)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.enableInstancing = true;
            CreateOrReplace(m, $"{GenDir}/M_{name}.mat");
            return m;
        }

        static void CreateCourseMaterials()
        {
            // 안개 속에서도 눈에 띄도록 Unlit
            s_FlagRed = UnlitMaterial("FlagRed", new Color(0.9f, 0.12f, 0.1f));
            s_FlagBlue = UnlitMaterial("FlagBlue", new Color(0.1f, 0.3f, 0.95f));
            s_FlagGreen = UnlitMaterial("FlagGreen", new Color(0.2f, 0.85f, 0.3f));
            s_FlagGray = UnlitMaterial("FlagGray", new Color(0.45f, 0.45f, 0.48f));
            s_SkiMat = LitMaterial("Ski", new Color(0.12f, 0.32f, 0.85f));
            s_Ring = SpritePng("Ring", 96, (u, v) =>
            {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) / 0.5f;
                float ring = SmoothStep(0.8f, 0.86f, d) * SmoothStep(1f, 0.94f, d);
                float fill = SmoothStep(0.86f, 0.8f, d) * 0.25f;
                return new Color(1f, 1f, 1f, Mathf.Max(ring, fill));
            });
        }

        static SlalomGate[] BuildGates(Transform parent)
        {
            var root = new GameObject("Gates").transform;
            root.SetParent(parent);
            var gates = new List<SlalomGate>();
            for (int i = 0; i < s_Gates.Count; i++)
            {
                var (pos, dir, side) = s_Gates[i];
                var go = new GameObject($"Gate_{i:00}");
                go.transform.SetParent(root);
                go.transform.SetPositionAndRotation(Ground(pos), Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));
                var gate = go.AddComponent<SlalomGate>();
                gate.index = i;
                gate.halfWidth = 2.6f;
                gate.passedMaterial = s_FlagGreen;
                gate.missedMaterial = s_FlagGray;
                var flagMat = side > 0 ? s_FlagRed : s_FlagBlue;
                var flags = new List<Renderer>();
                foreach (float x in new[] { -gate.halfWidth, gate.halfWidth })
                {
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.name = "Pole";
                    Object.DestroyImmediate(pole.GetComponent<Collider>());
                    pole.transform.SetParent(go.transform, false);
                    pole.transform.localPosition = new Vector3(x, 0.9f, 0f);
                    pole.transform.localScale = new Vector3(0.05f, 0.9f, 0.05f);
                    pole.GetComponent<Renderer>().sharedMaterial = s_IvoryMat;
                    pole.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

                    var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    flag.name = "Flag";
                    Object.DestroyImmediate(flag.GetComponent<Collider>());
                    flag.transform.SetParent(go.transform, false);
                    flag.transform.localPosition = new Vector3(x - Mathf.Sign(x) * 0.32f, 1.45f, 0f);
                    flag.transform.localScale = new Vector3(0.6f, 0.5f, 0.02f);
                    var r = flag.GetComponent<Renderer>();
                    r.sharedMaterial = flagMat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    flags.Add(r);
                }
                gate.flags = flags.ToArray();
                gates.Add(gate);
            }
            return gates.ToArray();
        }

        /// <summary>슬로프를 가로지르는 아치(출발·결승).</summary>
        static void BuildArch(Transform parent, string name, float z, string label, Material bannerMat)
        {
            var c = new Vector2(RunX(z), z);
            var t = RunTangent(z);
            float hw = RunHalfWidth(z) + 0.8f;
            var root = new GameObject(name).transform;
            root.SetParent(parent);
            root.SetPositionAndRotation(Ground(c), Quaternion.LookRotation(new Vector3(t.x, 0f, t.y)));
            foreach (float x in new[] { -hw, hw })
            {
                // 기둥 아래가 경사에 묻히도록 지면 높이를 따로 맞춘다
                var local = new Vector3(x, 0f, 0f);
                var world = root.TransformPoint(local);
                float gy = GroundY(new Vector2(world.x, world.z)) - root.position.y;
                Box(root, "Post", new Vector3(x, gy + 2.2f, 0f), new Vector3(0.35f, 4.4f + 1f, 0.35f), Vector3.zero, s_DarkMat, true);
            }
            Box(root, "Banner", new Vector3(0f, 4.2f, 0f), new Vector3(hw * 2f, 0.9f, 0.12f), Vector3.zero, bannerMat, false);
            var canvas = WorldCanvas(name + "_Label", root, new Vector2(hw * 200f, 90f), 0.01f, false, null);
            canvas.transform.localPosition = new Vector3(0f, 4.2f, -0.08f);
            canvas.transform.localRotation = Quaternion.identity;
            Text(canvas.transform, label, 70, Vector2.zero, new Vector2(hw * 200f, 90f), Color.white, FontStyles.Bold);
            // 반대편에서도 읽히도록
            var back = WorldCanvas(name + "_LabelBack", root, new Vector2(hw * 200f, 90f), 0.01f, false, null);
            back.transform.localPosition = new Vector3(0f, 4.2f, 0.08f);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Text(back.transform, label, 70, Vector2.zero, new Vector2(hw * 200f, 90f), Color.white, FontStyles.Bold);
        }

        // ───────────────────────── 폴·스키 ─────────────────────────

        static SkiPole BuildPole(Transform controller, SkiPole.Side side)
        {
            const float length = 1.15f;
            var go = new GameObject(side == SkiPole.Side.Left ? "SkiPole_L" : "SkiPole_R");
            go.transform.SetParent(controller, false);
            // 폴은 컨트롤러 -forward(주먹 아래, 새끼손가락 쪽)로 뻗는다
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localPosition = Vector3.zero;
            var pole = go.AddComponent<SkiPole>();
            pole.side = side;
            pole.length = length;

            void Part(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
            {
                var p = GameObject.CreatePrimitive(type);
                p.name = name;
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = pos;
                p.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                p.transform.localScale = scale;
                var r = p.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            Part("Grip", PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.02f), new Vector3(0.032f, 0.07f, 0.032f), s_DarkMat);
            Part("Shaft", PrimitiveType.Cylinder, new Vector3(0f, 0f, length / 2f), new Vector3(0.016f, length / 2f, 0.016f), s_IvoryMat);
            Part("Basket", PrimitiveType.Cylinder, new Vector3(0f, 0f, length - 0.1f), new Vector3(0.09f, 0.006f, 0.09f), s_RedMat);
            return pole;
        }

        /// <summary>출발 지점 양옆의 폴 거치대. 폴을 잡으면(XRGrabInteractable) 그 손에 폴이 장착된다.</summary>
        static void BuildPoleRack(Transform parent, Vector2 start)
        {
            var rot = Quaternion.Euler(0f, StartYaw(), 0f);
            var right = rot * Vector3.right;
            var fwd = rot * Vector3.forward;
            var root = new GameObject("PoleRack").transform;
            root.SetParent(parent);
            var basePos = Ground(start) + fwd * 0.7f;
            root.SetPositionAndRotation(basePos, rot);

            foreach (float side in new[] { -1f, 1f })
            {
                // 거치대 기둥 + 가로대
                var standPos = new Vector3(side * 0.75f, 0f, 0f);
                Box(root, "RackPost", standPos + new Vector3(0f, 0.55f, 0.12f), new Vector3(0.08f, 1.1f, 0.08f), Vector3.zero, s_WoodMat, false);
                Box(root, "RackBar", standPos + new Vector3(0f, 1.0f, 0.06f), new Vector3(0.3f, 0.06f, 0.14f), Vector3.zero, s_WoodMat, false);

                // 세워 둔 폴: 손잡이가 위(지면에서 1.15m), 끝이 눈에 닿음
                const float length = 1.15f;
                var go = new GameObject(side < 0 ? "RackPole_L" : "RackPole_R");
                go.transform.SetParent(root, false);
                go.transform.localPosition = standPos + new Vector3(0f, length, 0f);
                go.transform.localRotation = Quaternion.Euler(4f, 0f, side * 3f);
                void Part(string name, Vector3 pos, Vector3 scale, Material mat)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    p.name = name;
                    Object.DestroyImmediate(p.GetComponent<Collider>());
                    p.transform.SetParent(go.transform, false);
                    p.transform.localPosition = pos;
                    p.transform.localScale = scale;
                    var r = p.GetComponent<Renderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                Part("Grip", new Vector3(0f, -0.02f, 0f), new Vector3(0.032f, 0.07f, 0.032f), s_DarkMat);
                Part("Shaft", new Vector3(0f, -length / 2f, 0f), new Vector3(0.016f, length / 2f, 0.016f), s_IvoryMat);
                Part("Basket", new Vector3(0f, -length + 0.1f, 0f), new Vector3(0.09f, 0.006f, 0.09f), s_RedMat);

                // 잡기 판정은 손잡이 쪽을 넉넉하게
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 1;
                col.height = 0.6f;
                col.radius = 0.06f;
                col.center = new Vector3(0f, -0.25f, 0f);
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var grab = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                go.AddComponent<PoleRackItem>();
            }

            // 안내 문구
            var canvas = WorldCanvas("PoleRackHint", root, new Vector2(1000, 70), 0.0012f, false, null);
            canvas.transform.localPosition = new Vector3(0f, 1.32f, 0.1f);
            canvas.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            Text(canvas.transform, "그립 버튼으로 양손에 폴을 하나씩 잡으세요", 40, Vector2.zero, new Vector2(1000, 70), new Color(1f, 0.9f, 0.6f), FontStyles.Bold);
        }

        static Transform BuildSkis()
        {
            var root = new GameObject("Skis").transform;
            foreach (float x in new[] { -0.13f, 0.13f })
            {
                Box(root, "Ski", new Vector3(x, 0.015f, 0.1f), new Vector3(0.09f, 0.03f, 1.6f), Vector3.zero, s_SkiMat, false);
                Box(root, "Tip", new Vector3(x, 0.05f, 0.92f), new Vector3(0.09f, 0.03f, 0.14f), new Vector3(-30f, 0f, 0f), s_SkiMat, false);
                Box(root, "Binding", new Vector3(x, 0.05f, 0f), new Vector3(0.1f, 0.06f, 0.28f), Vector3.zero, s_DarkMat, false);
            }
            return root;
        }
    }
}
