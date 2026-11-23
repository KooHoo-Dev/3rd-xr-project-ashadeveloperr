using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// 스테이지별 코스 설정과 스테이지 전용 요소(바위·자갈·점프대·코스 위 나무).
    /// 스테이지마다 씬을 따로 만들어 한 맵에 모든 요소를 넣지 않는다(성능).
    /// </summary>
    public static partial class UntroddenSceneBuilder
    {
        class StageSpec
        {
            public string file;
            public int seed;
            // 슬로프 중심선: x = 120 + ampA·sin(z/freqA) + ampB·sin(z/freqB + phaseB)
            public float ampA = 30f, freqA = 62f, ampB = 9f, freqB = 23f, phaseB = 1.3f;
            public float halfWidth = 10f, halfWidthVar = 2.5f;
            public float gateSpacing = 22f;
            public bool obstacles;       // 바위·자갈
            public float obstacleStep = 22f;
            public bool jumps;           // 점프대
            public bool forestOnCourse;  // 코스 위 나무
            [Tooltip("제한시간 = 코스 길이 / 이 평균 속도(m/s), 5초 단위 올림")]
            public float limitSpeed = 5.5f;
        }

        static readonly StageSpec[] Specs =
        {
            new StageSpec { file = "Stage1_Tutorial", seed = 20261123, limitSpeed = 5.5f },
            new StageSpec { file = "Stage2_Obstacles", seed = 7311, ampA = 34f, freqA = 56f, ampB = 7f, freqB = 27f, phaseB = 0.4f,
                            halfWidth = 11f, gateSpacing = 32f, obstacles = true, obstacleStep = 21f, limitSpeed = 6.5f },
            new StageSpec { file = "Stage3_Glide", seed = 5523, ampA = 16f, freqA = 85f, ampB = 5f, freqB = 31f, phaseB = 2.1f,
                            halfWidth = 12f, halfWidthVar = 1.5f, gateSpacing = 36f, obstacles = true, obstacleStep = 40f, jumps = true, limitSpeed = 7.5f },
            new StageSpec { file = "Stage4_Forest", seed = 9187, ampA = 28f, freqA = 70f, ampB = 10f, freqB = 21f, phaseB = 0.9f,
                            halfWidth = 13f, halfWidthVar = 2f, gateSpacing = 45f, forestOnCourse = true, limitSpeed = 5f },
        };

        static StageSpec s_Stage = Specs[0];

        static string StageScenePath(int i) => $"{Root}/Scenes/{Specs[i].file}.unity";

        // ───────────────────────── 계획 데이터 ─────────────────────────

        // 점프대: 립(끝) 위치의 z. 코스 중앙에 놓는다.
        static readonly float[] JumpZ = { 85f, 150f, 215f, 280f };
        // 립 각도 = atan(2·높이/길이) ≈ 28°(경사 17° 기준 수평보다 약 11° 위). 시속 36km면 약 13m, 45km면 약 18m를 난다.
        const float RampLength = 9f, RampHeight = 2.4f, RampWidth = 5f;

        static readonly List<float> s_JumpLips = new();
        static readonly List<(Vector2 c, float r)> s_Gravel = new();
        static readonly List<(Vector2 c, float size)> s_Boulders = new();
        static readonly List<Vector2> s_CourseTrees = new();
        static readonly List<(Vector2 c, float r, float depth)> s_Dips = new();

        static void ClearStagePlan()
        {
            s_JumpLips.Clear();
            s_Gravel.Clear();
            s_Boulders.Clear();
            s_CourseTrees.Clear();
            s_Dips.Clear();
        }

        static Vector2 CoursePoint(float z, float lateral)
        {
            var t = RunTangent(z);
            var right = new Vector2(t.y, -t.x);
            return new Vector2(RunX(z), z) + right * lateral;
        }

        static bool NearJump(float z, float before, float after)
        {
            foreach (var lz in s_JumpLips)
                if (z > lz - before && z < lz + after) return true;
            return false;
        }

        static bool NearGate(Vector2 p, float r)
        {
            foreach (var g in s_Gates) if (Near(g.pos, p, r)) return true;
            return false;
        }

        /// <summary>점프대 위치를 먼저 정한다(관문·장애물이 피해 가도록).</summary>
        static void PlanJumps()
        {
            if (!s_Stage.jumps) return;
            foreach (var z in JumpZ)
            {
                s_JumpLips.Add(z);
                var t = RunTangent(z);
                // 립 바로 뒤 움푹 꺼진 골: 점프대가 '끊긴 언덕'처럼 보이고, 느리게 떨어지면 골에 빠진다
                var dipC = new Vector2(RunX(z), z) + t * 5f;
                s_Dips.Add((dipC, 5f, 1.6f));
                // 점프하지 않고 옆으로 돌아가면 자갈, 너무 느리게 뛰면 골 안 바위·자갈에 떨어진다.
                // 자갈은 립에서 약 11m까지. 시속 36km 이상으로 뛰면 그 너머(약 13m)에 넉넉히 착지한다.
                float hw = RunHalfWidth(z);
                foreach (float side in new[] { -1f, 1f })
                {
                    s_Gravel.Add((CoursePoint(z + 4f, side * hw * 0.62f), 3.8f));
                    s_Gravel.Add((CoursePoint(z + 8f, side * hw * 0.55f), 3.5f));
                }
                s_Gravel.Add((CoursePoint(z + 8.5f, 0f), 2.2f));
                s_Boulders.Add((CoursePoint(z + 5.5f, -1.4f), 1.3f));
                s_Boulders.Add((CoursePoint(z + 6f, 1.6f), 1.1f));
            }
        }

        /// <summary>바위 무리와 자갈밭을 코스에 번갈아 놓는다. 항상 지나갈 틈(5m 이상)을 남긴다.</summary>
        static void PlanObstacles()
        {
            if (!s_Stage.obstacles) return;
            int kind = 0;
            for (float z = StartZ + 45f; z < FinishZ - 25f; z += s_Stage.obstacleStep + Rand(-4f, 4f))
            {
                if (NearJump(z, 22f, 34f)) continue; // 착지 구간(립에서 약 30m)까지 비워 둔다
                if (NearGate(new Vector2(RunX(z), z), 9f)) z += 9f;
                float hw = RunHalfWidth(z);
                if (kind % 2 == 0)
                {
                    // 바위 무리: 틈 하나를 비워 두고 나머지 폭을 바위로 막는다
                    float gap = Rand(-hw + 3.5f, hw - 3.5f);
                    for (float x = -hw + 1.2f; x < hw - 1f; x += Rand(2.6f, 3.6f))
                    {
                        if (Mathf.Abs(x - gap) < 2.8f) continue;
                        if (Rand(0f, 1f) < 0.25f) continue; // 가끔 빠져서 틈이 더 생김
                        s_Boulders.Add((CoursePoint(z + Rand(-1.5f, 1.5f), x), Rand(1.4f, 2.4f)));
                    }
                }
                else
                {
                    // 자갈밭: 한쪽으로 치우쳐 놓아 반대편으로 돌아가거나, 짧게 가로지를지 고르게 한다
                    float side = Rand(0f, 1f) < 0.5f ? -1f : 1f;
                    float off = side * Rand(hw * 0.15f, hw * 0.45f);
                    float r = Rand(3.5f, 5f);
                    s_Gravel.Add((CoursePoint(z, off), r));
                    if (Rand(0f, 1f) < 0.5f) s_Gravel.Add((CoursePoint(z + r * 0.9f, off - side * 2f), r * 0.8f));
                }
                kind++;
            }
        }

        /// <summary>숲 스테이지: 코스 위에도 나무를 흩어 심는다. 나무끼리 4.5m 이상 떨어뜨려 늘 빠져나갈 틈(줄기 사이 약 3.8m)이 있다.</summary>
        static void PlanCourseTrees()
        {
            if (!s_Stage.forestOnCourse) return;
            for (float z = StartZ + 28f; z < FinishZ - 14f; z += 2.6f)
            {
                float hw = RunHalfWidth(z);
                float density = Mathf.Lerp(0.14f, 0.3f, Mathf.InverseLerp(StartZ + 28f, FinishZ - 14f, z)); // 내려갈수록 빽빽
                for (float x = -hw + 1f; x < hw - 1f; x += 2.6f)
                {
                    if (Rand(0f, 1f) > density) continue;
                    var p = CoursePoint(z + Rand(-1f, 1f), x + Rand(-1f, 1f));
                    if (NearGate(p, 6f)) continue;
                    bool crowded = false;
                    foreach (var q in s_CourseTrees) if (Near(p, q, 4.5f)) { crowded = true; break; }
                    if (crowded) continue;
                    s_CourseTrees.Add(p);
                }
            }
            Debug.Log($"[Untrodden] course trees: {s_CourseTrees.Count}");
        }

        /// <summary>자갈밭이 지형에 칠해지는 정도(0~1).</summary>
        static float GravelWeight(float x, float z)
        {
            float w = 0f;
            var p = new Vector2(x, z);
            foreach (var (c, r) in s_Gravel)
            {
                float d = Vector2.Distance(c, p);
                w = Mathf.Max(w, SmoothStep(r + 0.6f, r - 0.4f, d));
            }
            return w;
        }

        static float DipOffset(float x, float z)
        {
            float h = 0f;
            var p = new Vector2(x, z);
            foreach (var (c, r, depth) in s_Dips)
            {
                float d = Vector2.Distance(c, p);
                if (d < r) h -= depth * SmoothStep(r, r * 0.35f, d);
            }
            return h;
        }

        // ───────────────────────── 배치 ─────────────────────────

        static void BuildStageFeatures(Transform parent)
        {
            if (s_Boulders.Count > 0) BuildBoulders(parent);
            if (s_Gravel.Count > 0) BuildGravel(parent);
            if (s_JumpLips.Count > 0) BuildRamps(parent);
            if (s_CourseTrees.Count > 0) BuildCourseTrees(parent);
        }

        static void BuildBoulders(Transform parent)
        {
            var root = new GameObject("Boulders").transform;
            root.SetParent(parent);
            var rocks = Prefabs("SM_Rock_01", "SM_Rock_02", "SM_Rock_03", "SM_Rock_04", "SM_Rock_Boulder_01");
            foreach (var (c, size) in s_Boulders)
            {
                GameObject r;
                if (rocks.Count > 0) r = Place(Pick(rocks), root, Ground(c) + Vector3.down * 0.25f, Rand(0f, 360f), 1f);
                else
                {
                    r = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    r.transform.SetParent(root);
                    r.transform.position = Ground(c);
                }
                FitToSize(r, size);
                SnowCover(r);
                EnsureMeshColliders(r);
            }
            Debug.Log($"[Untrodden] boulders: {s_Boulders.Count}");
        }

        static void BuildGravel(Transform parent)
        {
            var root = new GameObject("Gravel").transform;
            root.SetParent(parent);
            var pebbles = Prefabs("SM_Rock_Small_01", "SM_Rock_Small_02", "SM_Rock_Small_03", "SM_Rock_01", "SM_Rock_02");
            int i = 0;
            foreach (var (c, r) in s_Gravel)
            {
                var go = new GameObject($"GravelPatch_{i++:00}");
                go.transform.SetParent(root);
                go.transform.position = Ground(c);
                go.AddComponent<GravelPatch>().radius = r;
                // 눈 위로 드러난 자갈(충돌 없음, 눈 덮이지 않은 어두운 돌)
                int n = Mathf.RoundToInt(r * r * 1.4f);
                for (int k = 0; k < n && pebbles.Count > 0; k++)
                {
                    float a = Rand(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rand(0f, 1f)) * r;
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    var peb = Place(Pick(pebbles), go.transform, Ground(p) + Vector3.down * 0.05f, Rand(0f, 360f), 1f);
                    FitToSize(peb, Rand(0.15f, 0.4f));
                    foreach (var col in peb.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                }
            }
            Debug.Log($"[Untrodden] gravel patches: {s_Gravel.Count}");
        }

        static void BuildCourseTrees(Transform parent)
        {
            var root = new GameObject("CourseTrees").transform;
            root.SetParent(parent);
            var pines = Prefabs("SM_Tree_Pine_02", "SM_Tree_PolyPine_01", "SM_Tree_PolyPine_02", "SM_Tree_PolyPine_03", "SM_Tree_Pine_Small_02");
            foreach (var p in s_CourseTrees)
            {
                if (pines.Count == 0) break;
                var t = Place(Pick(pines), root, Ground(p) + Vector3.down * 0.2f, Rand(0f, 360f), Rand(0.85f, 1.25f));
                SnowCover(t);
                AddTrunkCollider(t);
            }
        }

        /// <summary>
        /// 점프대: 지면을 따라 휘어 오르는 판(립 각도 ≈ 22°, 높이 1.8m). 립 뒤로는 지형이 움푹 꺼져(골) 끊긴 언덕처럼 보인다.
        /// 위면은 MeshCollider라 캐릭터 컨트롤러가 타고 올라가고, 립에서 땅이 사라지면 SkiController가 이륙시킨다.
        /// </summary>
        static void BuildRamps(Transform parent)
        {
            var root = new GameObject("Ramps").transform;
            root.SetParent(parent);
            for (int j = 0; j < s_JumpLips.Count; j++)
            {
                float lz = s_JumpLips[j];
                var lip = new Vector2(RunX(lz), lz);
                var t = RunTangent(lz);
                var right = new Vector2(t.y, -t.x);
                const int n = 14;
                var verts = new List<Vector3>();
                var tris = new List<int>();

                Vector3 P(float along, float lateral, bool top)
                {
                    var xz = lip + t * along + right * lateral;
                    float g = GroundY(xz);
                    float k = Mathf.Clamp01((along + RampLength) / RampLength);
                    return new Vector3(xz.x, top ? g + RampHeight * k * k : g - 0.6f, xz.y);
                }
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
                {
                    int s = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                    tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }
                float hw = RampWidth / 2f;
                for (int i = 0; i < n; i++)
                {
                    float a0 = -RampLength + RampLength * i / n, a1 = -RampLength + RampLength * (i + 1) / n;
                    // 위면 (진행 방향으로 감기도록 순서를 맞춘다)
                    Quad(P(a0, -hw, true), P(a1, -hw, true), P(a1, hw, true), P(a0, hw, true));
                    // 옆면
                    Quad(P(a0, -hw, false), P(a1, -hw, false), P(a1, -hw, true), P(a0, -hw, true));
                    Quad(P(a1, hw, false), P(a0, hw, false), P(a0, hw, true), P(a1, hw, true));
                }
                // 립 앞면(수직으로 떨어지는 벽)
                Quad(P(0f, hw, true), P(0f, -hw, true), P(0f, -hw, false), P(0f, hw, false));

                var mesh = new Mesh { name = $"Ramp_{j}" };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                CreateOrReplace(mesh, $"{GenDir}/{s_Stage.file}_Ramp_{j}.asset");

                var go = new GameObject($"Ramp_{j}");
                go.transform.SetParent(root);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = s_RampMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;

                // 립 양끝 표시 깃발(멀리서도 점프대 위치가 보이게)
                foreach (float side in new[] { -1f, 1f })
                {
                    var p = P(0f, side * (hw + 0.2f), true);
                    var flag = new GameObject("LipMarker").transform;
                    flag.SetParent(go.transform);
                    flag.position = p;
                    flag.rotation = Quaternion.LookRotation(new Vector3(t.x, 0f, t.y));
                    Box(flag, "Pole", new Vector3(0f, 0.6f, 0f), new Vector3(0.06f, 1.2f, 0.06f), Vector3.zero, s_DarkMat, false);
                    Box(flag, "Flag", new Vector3(0f, 1.05f, 0f), new Vector3(0.5f, 0.35f, 0.02f), Vector3.zero, s_FlagRed, false);
                }
            }
            Debug.Log($"[Untrodden] ramps: {s_JumpLips.Count}");
        }

        // ───────────────────────── 코스 경로·제한시간 ─────────────────────────

        static Vector3[] BuildCoursePoints()
        {
            var pts = new List<Vector3>();
            for (float z = StartZ; z <= FinishZ + 8f; z += 4f) pts.Add(Ground(new Vector2(RunX(z), z)));
            return pts.ToArray();
        }

        static float CourseLength()
        {
            float len = 0f;
            var prev = new Vector2(RunX(StartZ), StartZ);
            for (float z = StartZ + 1f; z <= FinishZ; z += 1f)
            {
                var p = new Vector2(RunX(z), z);
                len += Vector2.Distance(prev, p);
                prev = p;
            }
            return len;
        }

        static float StageTimeLimit()
        {
            float raw = CourseLength() / s_Stage.limitSpeed;
            return Mathf.Ceil(raw / 5f) * 5f;
        }
    }
}
