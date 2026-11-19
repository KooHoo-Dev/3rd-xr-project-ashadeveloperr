using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// 숲길 네트워크: 주 통로(시작 → 모닥불 → 모닥불 → 마을), 바위 그늘로 가는 갈림길, 막다른 샛길, 다리.
    /// 숲은 이 길과 공터를 피해서 빽빽하게 채운다.
    /// </summary>
    public static partial class UntroddenSceneBuilder
    {
        class Trail
        {
            public string name;
            public float halfWidth;
            public readonly List<Vector2> points = new();
        }

        static readonly List<Trail> s_Trails = new();
        static readonly List<(Vector2 c, float r)> s_Clearings = new();
        static readonly List<Vector2> s_DeadEnds = new();
        static readonly List<(Vector2 a, Vector2 b)> s_Bridges = new();
        static float[,] s_TrailEdge; // 1m 격자: 가장 가까운 길 가장자리까지 거리(길 안쪽은 음수)
        const float EdgeFieldMax = 14f;

        const float MainHalfWidth = 1.8f, BranchHalfWidth = 1.3f, DeadEndHalfWidth = 1.1f;
        const float Crossing1Z = 85f, Crossing2Z = 205f;

        /// <summary>주 통로가 강의 어느 쪽에 있는지(+1 동쪽, -1 서쪽).</summary>
        static float MainSide(float z) => z < Crossing1Z ? 1f : z < Crossing2Z ? -1f : 1f;

        static Vector2 Bank(float z, float side, float offset) => new(RiverX(z) + side * offset, z);

        /// <summary>바위 그늘 위치: 주 통로와 같은 쪽, 길에서 조금 들어간 곳(갈림길로 연결).</summary>
        static Vector2[] ShelterPositions()
        {
            float[] zs = { 70f, 118f, 172f, 240f, 312f, 352f };
            var list = new Vector2[zs.Length];
            for (int i = 0; i < zs.Length; i++)
            {
                var p = Bank(zs[i], MainSide(zs[i]), 48f + (i % 2) * 4f);
                p.x = Mathf.Clamp(p.x, PlayMin.x + 7f, PlayMax.x - 7f);
                list[i] = p;
            }
            return list;
        }

        static void PlanTrails(Vector2 start, Vector2[] fires, Vector2 village, Vector2[] shelters)
        {
            s_Trails.Clear();
            s_Clearings.Clear();
            s_DeadEnds.Clear();
            s_Bridges.Clear();

            // ── 주 통로 ──
            var main = new Trail { name = "Main", halfWidth = MainHalfWidth };
            var e1 = Bank(Crossing1Z, 1f, 11f);
            var w1 = Bank(Crossing1Z, -1f, 11f);
            var w2 = Bank(Crossing2Z, -1f, 11f);
            var e2 = Bank(Crossing2Z, 1f, 11f);
            AppendCurve(main.points, new[] { start, Bank(45f, 1f, 30f), Bank(68f, 1f, 26f), e1 }, 3f, 101);
            AppendLine(main.points, e1, w1); // 다리 1
            AppendCurve(main.points, new[] { w1, Bank(102f, -1f, 26f), Bank(122f, -1f, 32f), fires[1] }, 3f, 202);
            AppendCurve(main.points, new[] { fires[1], Bank(160f, -1f, 32f), Bank(186f, -1f, 26f), w2 }, 3f, 303);
            AppendLine(main.points, w2, e2); // 다리 2
            AppendCurve(main.points, new[] { e2, Bank(222f, 1f, 26f), Bank(244f, 1f, 32f), fires[2] }, 3f, 404);
            AppendCurve(main.points, new[] { fires[2], Bank(290f, 1f, 34f), Bank(325f, 1f, 28f), Bank(355f, 1f, 34f), village }, 3.5f, 505);
            s_Trails.Add(main);
            s_Bridges.Add((e1, w1));
            s_Bridges.Add((w2, e2));

            // ── 바위 그늘 갈림길 ──
            for (int i = 0; i < shelters.Length; i++)
            {
                var s = shelters[i];
                var from = NearestOn(main.points, s);
                var dir = (from - s).normalized;
                var end = s + dir * 5.5f; // 바위 바로 앞에서 끝
                var mid = Vector2.Lerp(from, end, 0.5f) + Perp(dir) * Rand(-4f, 4f);
                var t = new Trail { name = $"Shelter_{i}", halfWidth = BranchHalfWidth };
                AppendCurve(t.points, new[] { from, mid, end }, 1.5f, 600 + i);
                s_Trails.Add(t);
                s_Clearings.Add((s, 8.5f));
            }

            // ── 막다른 샛길 (끝에 작은 공터와 버섯) ──
            float[] dz = { 38f, 150f, 268f, 372f };
            for (int i = 0; i < dz.Length; i++)
            {
                float z = dz[i];
                float side = MainSide(z);
                var from = NearestOn(main.points, Bank(z, side, 30f));
                // 강 반대쪽(바깥 벽 방향)으로 뻗었다가 휘어진다
                var end = new Vector2(Mathf.Clamp(from.x + side * Rand(24f, 32f), PlayMin.x + 8f, PlayMax.x - 8f), z + Rand(-14f, 14f));
                var mid = Vector2.Lerp(from, end, 0.5f) + new Vector2(0f, Rand(-8f, 8f));
                var t = new Trail { name = $"DeadEnd_{i}", halfWidth = DeadEndHalfWidth };
                AppendCurve(t.points, new[] { from, mid, end }, 2f, 700 + i);
                s_Trails.Add(t);
                s_Clearings.Add((end, 5.5f));
                s_DeadEnds.Add(end);
            }

            // ── 공터 ──
            s_Clearings.Add((start, 11f));
            foreach (var f in fires) s_Clearings.Add((f, 9f));
            s_Clearings.Add((village, 36f));

            BuildTrailEdgeField();
            int n = 0;
            foreach (var t in s_Trails) n += t.points.Count;
            Debug.Log($"[Untrodden] trails: {s_Trails.Count}, points: {n}");
        }

        /// <summary>제어점을 지나는 Catmull-Rom 곡선 + 펄린 노이즈로 굽이치게. 양 끝은 흔들지 않는다.</summary>
        static void AppendCurve(List<Vector2> pts, Vector2[] ctrl, float wiggle, int seed)
        {
            var raw = new List<Vector2>();
            for (int i = 0; i < ctrl.Length - 1; i++)
            {
                var p0 = ctrl[Mathf.Max(i - 1, 0)];
                var p1 = ctrl[i];
                var p2 = ctrl[i + 1];
                var p3 = ctrl[Mathf.Min(i + 2, ctrl.Length - 1)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 0.5f));
                for (int s = 0; s < steps; s++)
                    raw.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
            }
            raw.Add(ctrl[^1]);

            float total = 0f;
            var cum = new float[raw.Count];
            for (int i = 1; i < raw.Count; i++) cum[i] = total += Vector2.Distance(raw[i - 1], raw[i]);

            for (int i = 0; i < raw.Count; i++)
            {
                var fwd = raw[Mathf.Min(i + 1, raw.Count - 1)] - raw[Mathf.Max(i - 1, 0)];
                var n = Perp(fwd.normalized);
                float taper = Mathf.Clamp01(Mathf.Min(cum[i], total - cum[i]) / 12f);
                float off = (Mathf.PerlinNoise(seed * 0.37f, cum[i] / 22f) - 0.5f) * 2f * wiggle * taper;
                pts.Add(raw[i] + n * off);
            }
        }

        static void AppendLine(List<Vector2> pts, Vector2 a, Vector2 b)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.5f));
            for (int s = 0; s <= steps; s++) pts.Add(Vector2.Lerp(a, b, s / (float)steps));
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        static Vector2 Perp(Vector2 v) => new(-v.y, v.x);

        static Vector2 NearestOn(List<Vector2> pts, Vector2 p)
        {
            var best = pts[0];
            float bd = float.MaxValue;
            foreach (var q in pts)
            {
                float d = (q - p).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        static void BuildTrailEdgeField()
        {
            int w = Mathf.CeilToInt(SizeX) + 1, h = Mathf.CeilToInt(SizeZ) + 1;
            s_TrailEdge = new float[h, w];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                s_TrailEdge[z, x] = EdgeFieldMax;

            foreach (var t in s_Trails)
            {
                int r = Mathf.CeilToInt(t.halfWidth + EdgeFieldMax);
                foreach (var p in t.points)
                {
                    int cx = Mathf.RoundToInt(p.x), cz = Mathf.RoundToInt(p.y);
                    for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(h - 1, cz + r); z++)
                    for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                    {
                        float d = Vector2.Distance(p, new Vector2(x, z)) - t.halfWidth;
                        if (d < s_TrailEdge[z, x]) s_TrailEdge[z, x] = d;
                    }
                }
            }
        }

        /// <summary>길 가장자리까지 거리(m). 길 안쪽이면 음수. 격자를 쌍선형 보간한다.</summary>
        static float TrailEdge(float x, float z)
        {
            if (s_TrailEdge == null) return EdgeFieldMax;
            int h = s_TrailEdge.GetLength(0), w = s_TrailEdge.GetLength(1);
            x = Mathf.Clamp(x, 0f, w - 1.001f);
            z = Mathf.Clamp(z, 0f, h - 1.001f);
            int x0 = (int)x, z0 = (int)z;
            float fx = x - x0, fz = z - z0;
            float a = Mathf.Lerp(s_TrailEdge[z0, x0], s_TrailEdge[z0, x0 + 1], fx);
            float b = Mathf.Lerp(s_TrailEdge[z0 + 1, x0], s_TrailEdge[z0 + 1, x0 + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }

        /// <summary>공터 가장자리까지 거리(m). 공터 안이면 음수.</summary>
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

            // 플레이 영역보다 조금 넓게 심어 경계 너머도 숲으로 보이게 한다
            var lo = PlayMin - new Vector2(14f, 10f);
            var hi = new Vector2(PlayMax.x + 14f, SeaZ - 8f);
            const float spacing = 3.8f;
            int trees = 0, small = 0;
            for (float z = lo.y; z < hi.y; z += spacing)
            for (float x = lo.x; x < hi.x; x += spacing)
            {
                var p = new Vector2(x + Rand(-1.4f, 1.4f), z + Rand(-1.4f, 1.4f));
                float river = RiverDistance(p.x, p.y);
                float edge = Mathf.Min(TrailEdge(p.x, p.y), ClearingEdge(p));
                if (river < 8.5f || edge < 0.9f) continue;

                float roll = Rand(0f, 1f);
                // 길·공터 가장자리: 덤불과 작은 나무만 (길이 막히지 않으면서 숲 벽처럼 보이게)
                if (edge < 3f || river < 10f)
                {
                    if (roll < 0.35f && bushes.Count > 0)
                        SnowCover(Place(Pick(bushes), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.3f)));
                    else if (roll < 0.6f && smallPines.Count > 0)
                    {
                        var t = Place(Pick(smallPines), root, Ground(p) + Vector3.down * 0.15f, Rand(0f, 360f), Rand(0.7f, 1.1f));
                        SnowCover(t);
                        AddTrunkCollider(t);
                        small++;
                    }
                    else if (roll < 0.68f && dead.Count > 0 && river < 16f)
                    {
                        var t = Place(Pick(dead), root, Ground(p), Rand(0f, 360f), Rand(0.8f, 1.1f));
                        SnowCover(t);
                        AddTrunkCollider(t);
                    }
                    continue;
                }

                // 숲 안쪽: 빽빽한 침엽수
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
            Debug.Log($"[Untrodden] trees placed: {trees} (+{small} small)");
        }

        /// <summary>강을 건너는 통나무 다리(난간 포함).</summary>
        static void BuildBridges(Transform parent)
        {
            int i = 0;
            foreach (var (a2, b2) in s_Bridges)
            {
                var dir2 = (b2 - a2).normalized;
                var a = Ground(a2 - dir2 * 1.5f);
                var b = Ground(b2 + dir2 * 1.5f);
                var mid = (a + b) / 2f;
                float midZ = (a2.y + b2.y) / 2f;
                mid.y = Mathf.Max(mid.y, RiverWaterY(midZ) + 0.6f) + 0.08f;
                var root = new GameObject($"Bridge_{i++}").transform;
                root.SetParent(parent);
                root.position = mid;
                root.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0f, b.z - a.z));
                float len = Vector2.Distance(a2, b2) + 3f;

                Box(root, "Deck", Vector3.zero, new Vector3(2.4f, 0.18f, len), Vector3.zero, s_WoodMat, true);
                for (float z = -len / 2f + 0.3f; z < len / 2f; z += 0.6f)
                    Box(root, "Plank", new Vector3(0f, 0.1f, z), new Vector3(2.5f, 0.05f, 0.5f), new Vector3(0f, Rand(-2f, 2f), 0f), s_WoodMat, false);
                foreach (float x in new[] { -1.2f, 1.2f })
                {
                    Box(root, "Rail", new Vector3(x, 0.95f, 0f), new Vector3(0.12f, 0.12f, len - 2f), Vector3.zero, s_WoodMat, true);
                    for (float z = -len / 2f + 1f; z <= len / 2f - 1f; z += 2.5f)
                        Box(root, "Post", new Vector3(x, 0.5f, z), new Vector3(0.14f, 1f, 0.14f), Vector3.zero, s_DarkMat, false);
                }
                // 양 끝은 지형에 살짝 묻어 턱이 생기지 않게
                float slope = Mathf.Atan2(b.y - a.y, len) * Mathf.Rad2Deg;
                root.Rotate(-slope, 0f, 0f, Space.Self);
            }
        }
    }
}
