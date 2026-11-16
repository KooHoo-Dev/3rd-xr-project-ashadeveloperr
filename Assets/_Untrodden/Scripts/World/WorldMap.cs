using System.Collections.Generic;
using UnityEngine;

namespace Untrodden.World
{
    public enum Biome { Forest, Ridge, Valley, Stream, River, Downstream, Coast, Sea, Village }

    public struct RiverHit
    {
        public bool valid;
        public float distance;   // to the river centre line (m)
        public float surface;    // water surface height (m)
        public float halfWidth;  // half of the channel width (m)
        public float flow;       // upstream drainage (cells)
        public Vector2 point;    // nearest centre-line point (world xz, un-warped)
    }

    /// <summary>
    /// The world skeleton. Built once per seed:
    /// 1. coarse heightfield sloping from the southern highlands to the northern sea,
    /// 2. priority-flood so every cell drains to the sea,
    /// 3. D8-style flow directions and drainage accumulation,
    /// 4. river segments rasterised into a fine grid with a nearest-segment transform.
    /// After that, Height / water / biome can be queried anywhere, which is all chunk streaming needs.
    /// </summary>
    public class WorldMap
    {
        public const float Width = 800f;
        public const float Length = 1200f;
        public const float CoastZ = 1100f;
        public const float SeaLevel = 0f;
        public const float MacroCell = 20f;
        public const float FineCell = 4f;
        public const float StreamFlow = 15f;
        public const float RiverFlow = 60f;
        public const float VillageRadius = 18f;
        const float k_FloodEpsilon = 0.05f;
        const float k_Warp = 7f;

        public readonly int Seed;
        public Vector3 StartPosition { get; private set; }
        public Vector2 VillageXZ { get; private set; }
        public float VillageHeight { get; private set; }
        public Vector2 MouthXZ { get; private set; }

        readonly int m_GW, m_GL;
        readonly float[] m_Raw, m_Filled, m_Flow, m_Relief;
        readonly int[] m_Receiver;

        readonly int m_FW, m_FL;
        readonly int[] m_FineSeg;

        // river segments: one per river cell, from its centre to its receiver's centre
        readonly List<Vector2> m_SegA = new List<Vector2>();
        readonly List<Vector2> m_SegB = new List<Vector2>();
        readonly List<float> m_SegSurfA = new List<float>();
        readonly List<float> m_SegSurfB = new List<float>();
        readonly List<float> m_SegHalfWidth = new List<float>();
        readonly List<float> m_SegFlow = new List<float>();

        readonly Vector2 m_HillOffset, m_DetailOffset, m_WarpOffset;

        public WorldMap(int seed)
        {
            Seed = seed;
            var rng = new System.Random(seed);
            m_HillOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
            m_DetailOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
            m_WarpOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));

            m_GW = Mathf.CeilToInt(Width / MacroCell);
            m_GL = Mathf.CeilToInt(Length / MacroCell);
            var n = m_GW * m_GL;
            m_Raw = new float[n];
            m_Filled = new float[n];
            m_Flow = new float[n];
            m_Relief = new float[n];
            m_Receiver = new int[n];

            m_FW = Mathf.CeilToInt(Width / FineCell);
            m_FL = Mathf.CeilToInt(Length / FineCell);
            m_FineSeg = new int[m_FW * m_FL];

            BuildRawHeights();
            PriorityFlood();
            FlowAndAccumulate();
            for (var c = 0; c < n; c++)
                m_Relief[c] = Relief(c);
            RasteriseRivers();
            NearestSegmentTransform();
            PlaceVillage();
            PlaceStart();
        }

        // ------------------------------------------------------------------ queries

        public float Height(float x, float z)
        {
            var baseH = BaseHeight(x, z);
            var h = baseH;

            var r = NearestRiver(x, z);
            if (r.valid)
            {
                var bank = 5f + r.halfWidth * 1.5f;
                if (r.distance < r.halfWidth + bank)
                {
                    // Continuous profile: bed → water line at the channel edge → bank → base terrain.
                    // A step here makes the shoreline follow the 2 m mesh grid as saw teeth.
                    var depth = 0.6f + r.halfWidth * 0.15f;
                    var rim = r.surface + 0.35f;
                    if (r.distance < r.halfWidth)
                    {
                        var u = r.distance / r.halfWidth;
                        h = r.surface - depth * (1f - u * u);
                    }
                    else
                    {
                        var t = (r.distance - r.halfWidth) / bank;
                        var lip = Mathf.Lerp(r.surface, rim, Mathf.Clamp01(t * 4f)); // rises to the rim in the first quarter
                        h = Mathf.Lerp(lip, Mathf.Max(baseH, rim), Smooth01(t));
                    }
                }
            }

            var coast = Smooth01((z - (CoastZ - 25f)) / 60f);
            if (coast > 0f)
                h = Mathf.Lerp(h, Mathf.Min(h, -3f), coast);

            var dv = Vector2.Distance(new Vector2(x, z), VillageXZ);
            if (dv < VillageRadius + 15f)
                h = Mathf.Lerp(VillageHeight, h, Smooth01((dv - VillageRadius) / 15f));

            return h;
        }

        public bool TryGetWater(float x, float z, out float surface)
        {
            var r = NearestRiver(x, z);
            if (r.valid && r.distance < r.halfWidth + 0.2f)
            {
                surface = r.surface;
                return true;
            }

            if (z > CoastZ - 40f && Height(x, z) < SeaLevel)
            {
                surface = SeaLevel;
                return true;
            }

            surface = 0f;
            return false;
        }

        public RiverHit NearestRiver(float x, float z)
        {
            // Domain warp so straight 20 m segments read as meandering streams.
            var wx = x + (Mathf.PerlinNoise(x * 0.015f + m_WarpOffset.x, z * 0.015f + m_WarpOffset.y) - 0.5f) * 2f * k_Warp;
            var wz = z + (Mathf.PerlinNoise(x * 0.015f + m_WarpOffset.y, z * 0.015f + m_WarpOffset.x) - 0.5f) * 2f * k_Warp;
            var p = new Vector2(wx, wz);

            var fi = Mathf.Clamp((int)(wx / FineCell), 0, m_FW - 1);
            var fj = Mathf.Clamp((int)(wz / FineCell), 0, m_FL - 1);

            var best = new RiverHit { distance = float.MaxValue };
            var bestScore = float.MaxValue;
            for (var dj = -1; dj <= 1; dj++)
            for (var di = -1; di <= 1; di++)
            {
                var i = fi + di;
                var j = fj + dj;
                if (i < 0 || j < 0 || i >= m_FW || j >= m_FL)
                    continue;
                var s = m_FineSeg[j * m_FW + i];
                if (s < 0)
                    continue;

                var d = SegmentDistance(s, p, out var t, out var q);
                // Prefer the segment whose bank we are inside, relative to its width.
                var score = d - m_SegHalfWidth[s];
                if (score < bestScore)
                {
                    bestScore = score;
                    best.valid = true;
                    best.distance = d;
                    best.halfWidth = m_SegHalfWidth[s];
                    best.surface = Mathf.Max(Mathf.Lerp(m_SegSurfA[s], m_SegSurfB[s], t), SeaLevel);
                    best.flow = m_SegFlow[s];
                    best.point = q;
                }
            }

            return best;
        }

        public Biome GetBiome(float x, float z)
        {
            var h = Height(x, z);
            if (z > CoastZ - 5f && h < 0.2f)
                return Biome.Sea;
            if (Vector2.Distance(new Vector2(x, z), VillageXZ) < VillageRadius + 8f)
                return Biome.Village;

            var r = NearestRiver(x, z);
            if (r.valid && r.distance < r.halfWidth + 7f)
            {
                if (r.flow < RiverFlow)
                    return Biome.Stream;
                return z > CoastZ * 0.6f ? Biome.Downstream : Biome.River;
            }

            if (z > CoastZ - 45f)
                return Biome.Coast;

            // Interpolated between cell centres so zone borders follow the terrain, not the 20 m grid.
            var relief = SampleCells(m_Relief, x, z);
            var flow = SampleCells(m_Flow, x, z);
            if (relief > 2.5f && flow < 4f)
                return Biome.Ridge;
            if (flow >= 5f)
                return Biome.Valley;
            return Biome.Forest;
        }

        /// <summary>0 at the start, 1 at the coast.</summary>
        public static float Downstream(float z) => Mathf.Clamp01(z / CoastZ);

        public bool IsInsideVillage(Vector3 p) =>
            Vector2.Distance(new Vector2(p.x, p.z), VillageXZ) < VillageRadius;

        /// <summary>Deterministic per-chunk random generator.</summary>
        public System.Random ChunkRandom(int cx, int cz, int salt)
        {
            unchecked
            {
                var h = Seed;
                h = h * 486187739 + cx;
                h = h * 486187739 + cz;
                h = h * 486187739 + salt;
                return new System.Random(h);
            }
        }

        // ------------------------------------------------------------------ build

        void BuildRawHeights()
        {
            for (var j = 0; j < m_GL; j++)
            for (var i = 0; i < m_GW; i++)
            {
                var x = (i + 0.5f) * MacroCell;
                var z = (j + 0.5f) * MacroCell;
                var idx = j * m_GW + i;

                if (z > CoastZ)
                {
                    m_Raw[idx] = -4f - (z - CoastZ) * 0.04f;
                    continue;
                }

                var t = z / CoastZ;
                var hills = (Fbm(x * 0.0035f + m_HillOffset.x, z * 0.0035f + m_HillOffset.y, 4) - 0.5f) * 2f;
                var h = 95f * (1f - t) + hills * 35f * (0.35f + 0.65f * (1f - t)) + 3f;

                // Raise the side edges so rivers do not run along the world boundary.
                var edge = Mathf.Min(x, Width - x);
                var e = Mathf.Max(0f, 140f - edge) / 140f;
                h += e * e * 70f;
                m_Raw[idx] = h;
            }
        }

        void PriorityFlood()
        {
            var n = m_Raw.Length;
            var visited = new bool[n];
            var heap = new MinHeap(n);
            for (var k = 0; k < n; k++)
            {
                m_Receiver[k] = -1;
                if (IsSeaCell(k))
                {
                    m_Filled[k] = m_Raw[k];
                    visited[k] = true;
                    heap.Push(m_Filled[k], k);
                }
            }

            while (heap.Count > 0)
            {
                var c = heap.Pop();
                var ci = c % m_GW;
                var cj = c / m_GW;
                for (var dj = -1; dj <= 1; dj++)
                for (var di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0)
                        continue;
                    var i = ci + di;
                    var j = cj + dj;
                    if (i < 0 || j < 0 || i >= m_GW || j >= m_GL)
                        continue;
                    var nb = j * m_GW + i;
                    if (visited[nb])
                        continue;
                    visited[nb] = true;
                    m_Filled[nb] = Mathf.Max(m_Raw[nb], m_Filled[c] + k_FloodEpsilon);
                    m_Receiver[nb] = c;
                    heap.Push(m_Filled[nb], nb);
                }
            }
        }

        void FlowAndAccumulate()
        {
            var n = m_Raw.Length;
            var land = new List<int>(n);
            for (var c = 0; c < n; c++)
            {
                if (IsSeaCell(c))
                {
                    m_Receiver[c] = -1;
                    continue;
                }

                // Steepest lower neighbour (falls back to the flood parent).
                var ci = c % m_GW;
                var cj = c / m_GW;
                var best = m_Receiver[c];
                var bestDrop = best >= 0 ? (m_Filled[c] - m_Filled[best]) / NeighbourDistance(c, best) : 0f;
                for (var dj = -1; dj <= 1; dj++)
                for (var di = -1; di <= 1; di++)
                {
                    if (di == 0 && dj == 0)
                        continue;
                    var i = ci + di;
                    var j = cj + dj;
                    if (i < 0 || j < 0 || i >= m_GW || j >= m_GL)
                        continue;
                    var nb = j * m_GW + i;
                    var drop = (m_Filled[c] - m_Filled[nb]) / ((di != 0 && dj != 0) ? 1.414f : 1f);
                    if (m_Filled[nb] < m_Filled[c] && drop > bestDrop)
                    {
                        bestDrop = drop;
                        best = nb;
                    }
                }

                m_Receiver[c] = best;
                land.Add(c);
            }

            land.Sort((a, b) => m_Filled[b].CompareTo(m_Filled[a]));
            for (var c = 0; c < n; c++)
                m_Flow[c] = IsSeaCell(c) ? 0f : 1f;
            foreach (var c in land)
            {
                var r = m_Receiver[c];
                if (r >= 0)
                    m_Flow[r] += m_Flow[c];
            }
        }

        void RasteriseRivers()
        {
            for (var k = 0; k < m_FineSeg.Length; k++)
                m_FineSeg[k] = -1;

            for (var c = 0; c < m_Raw.Length; c++)
            {
                if (IsSeaCell(c) || m_Flow[c] < StreamFlow)
                    continue;
                var r = m_Receiver[c];
                if (r < 0)
                    continue;

                var a = CellCenter(c);
                var b = CellCenter(r);
                var seg = m_SegA.Count;
                m_SegA.Add(a);
                m_SegB.Add(b);
                m_SegSurfA.Add(m_Filled[c]);
                m_SegSurfB.Add(IsSeaCell(r) ? SeaLevel : m_Filled[r]);
                m_SegHalfWidth.Add(HalfWidth(m_Flow[c]));
                m_SegFlow.Add(m_Flow[c]);

                var steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 1.5f);
                for (var s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)steps);
                    var fi = (int)(p.x / FineCell);
                    var fj = (int)(p.y / FineCell);
                    if (fi < 0 || fj < 0 || fi >= m_FW || fj >= m_FL)
                        continue;
                    var f = fj * m_FW + fi;
                    var existing = m_FineSeg[f];
                    if (existing < 0 || m_SegHalfWidth[existing] < m_SegHalfWidth[seg])
                        m_FineSeg[f] = seg;
                }
            }
        }

        void NearestSegmentTransform()
        {
            // Two-pass chamfer sweep that carries the nearest segment index.
            var dist = new float[m_FineSeg.Length];
            for (var k = 0; k < dist.Length; k++)
                dist[k] = m_FineSeg[k] >= 0 ? 0f : float.MaxValue;

            void Relax(int i, int j, int ni, int nj)
            {
                if (ni < 0 || nj < 0 || ni >= m_FW || nj >= m_FL)
                    return;
                var s = m_FineSeg[nj * m_FW + ni];
                if (s < 0)
                    return;
                var c = j * m_FW + i;
                var p = new Vector2((i + 0.5f) * FineCell, (j + 0.5f) * FineCell);
                var d = SegmentDistance(s, p, out _, out _);
                if (d < dist[c])
                {
                    dist[c] = d;
                    m_FineSeg[c] = s;
                }
            }

            for (var j = 0; j < m_FL; j++)
            for (var i = 0; i < m_FW; i++)
            {
                Relax(i, j, i - 1, j);
                Relax(i, j, i - 1, j - 1);
                Relax(i, j, i, j - 1);
                Relax(i, j, i + 1, j - 1);
            }

            for (var j = m_FL - 1; j >= 0; j--)
            for (var i = m_FW - 1; i >= 0; i--)
            {
                Relax(i, j, i + 1, j);
                Relax(i, j, i + 1, j + 1);
                Relax(i, j, i, j + 1);
                Relax(i, j, i - 1, j + 1);
            }
        }

        void PlaceVillage()
        {
            // The main river mouth: a big river that reaches the sea, preferring ones near the middle
            // of the coast so the village is not tucked into a corner.
            var mouth = -1;
            var bestScore = float.MinValue;
            for (var c = 0; c < m_Raw.Length; c++)
            {
                if (IsSeaCell(c))
                    continue;
                var r = m_Receiver[c];
                if (r < 0 || !IsSeaCell(r))
                    continue;
                var centrality = 1f - Mathf.Abs(CellCenter(c).x - Width * 0.5f) / (Width * 0.5f);
                var score = m_Flow[c] * (0.35f + 0.65f * centrality);
                if (score > bestScore)
                {
                    bestScore = score;
                    mouth = c;
                }
            }

            var m = mouth >= 0 ? CellCenter(mouth) : new Vector2(Width * 0.5f, CoastZ - 10f);
            MouthXZ = m;
            var hw = mouth >= 0 ? HalfWidth(m_Flow[mouth]) : 4f;

            // Beside the mouth, on the side facing the middle of the map, clear of any river.
            var side = m.x < Width * 0.5f ? 1f : -1f;
            var chosen = new Vector2(m.x + side * (hw + 35f), CoastZ - 45f);
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var offset = hw + 30f + (attempt / 2) * 10f;
                var s = attempt % 2 == 0 ? side : -side;
                var candidate = new Vector2(Mathf.Clamp(m.x + s * offset, 60f, Width - 60f), CoastZ - 45f);
                var r = NearestRiver(candidate.x, candidate.y);
                if (!r.valid || r.distance > VillageRadius + r.halfWidth + 10f)
                {
                    chosen = candidate;
                    break;
                }
            }

            VillageXZ = chosen;
            VillageHeight = Mathf.Max(BaseHeight(chosen.x, chosen.y), 2f);
        }

        void PlaceStart()
        {
            var bestScore = float.MinValue;
            var best = new Vector2(Width * 0.5f, 60f);
            for (var z = 50f; z <= 110f; z += 10f)
            for (var x = Width * 0.3f; x <= Width * 0.7f; x += 10f)
            {
                var r = NearestRiver(x, z);
                if (r.valid && r.distance < 25f)
                    continue;
                var score = -Mathf.Abs(x - Width * 0.5f); // prefer the middle
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Vector2(x, z);
                }
            }

            StartPosition = new Vector3(best.x, Height(best.x, best.y) + 0.05f, best.y);
        }

        // ------------------------------------------------------------------ helpers

        float BaseHeight(float x, float z)
        {
            var gx = Mathf.Clamp(x / MacroCell - 0.5f, 0f, m_GW - 1.001f);
            var gz = Mathf.Clamp(z / MacroCell - 0.5f, 0f, m_GL - 1.001f);
            var i0 = (int)gx;
            var j0 = (int)gz;
            var tx = gx - i0;
            var tz = gz - j0;
            var h00 = m_Filled[j0 * m_GW + i0];
            var h10 = m_Filled[j0 * m_GW + i0 + 1];
            var h01 = m_Filled[(j0 + 1) * m_GW + i0];
            var h11 = m_Filled[(j0 + 1) * m_GW + i0 + 1];
            var h = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);

            h += (Fbm(x * 0.025f + m_DetailOffset.x, z * 0.025f + m_DetailOffset.y, 3) - 0.5f) * 5f;
            h += (Mathf.PerlinNoise(x * 0.12f + m_DetailOffset.y, z * 0.12f + m_DetailOffset.x) - 0.5f) * 0.6f;
            return h;
        }

        /// <summary>Bilinear sample of a per-cell value at a world position.</summary>
        float SampleCells(float[] values, float x, float z)
        {
            var gx = Mathf.Clamp(x / MacroCell - 0.5f, 0f, m_GW - 1.001f);
            var gz = Mathf.Clamp(z / MacroCell - 0.5f, 0f, m_GL - 1.001f);
            var i0 = (int)gx;
            var j0 = (int)gz;
            var tx = gx - i0;
            var tz = gz - j0;
            var v00 = values[j0 * m_GW + i0];
            var v10 = values[j0 * m_GW + i0 + 1];
            var v01 = values[(j0 + 1) * m_GW + i0];
            var v11 = values[(j0 + 1) * m_GW + i0 + 1];
            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), tz);
        }

        float SegmentDistance(int s, Vector2 p, out float t, out Vector2 q)
        {
            var a = m_SegA[s];
            var ab = m_SegB[s] - a;
            var len2 = ab.sqrMagnitude;
            t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            q = a + ab * t;
            return Vector2.Distance(p, q);
        }

        static float HalfWidth(float flow)
        {
            if (flow < RiverFlow)
                return 0.9f + Mathf.Clamp01((flow - StreamFlow) / (RiverFlow - StreamFlow)) * 0.8f;
            return Mathf.Min(2f + Mathf.Sqrt(flow - RiverFlow) * 0.35f, 10f);
        }

        float Relief(int c)
        {
            var ci = c % m_GW;
            var cj = c / m_GW;
            var sum = 0f;
            var count = 0;
            for (var dj = -1; dj <= 1; dj++)
            for (var di = -1; di <= 1; di++)
            {
                if (di == 0 && dj == 0)
                    continue;
                var i = ci + di;
                var j = cj + dj;
                if (i < 0 || j < 0 || i >= m_GW || j >= m_GL)
                    continue;
                sum += m_Filled[j * m_GW + i];
                count++;
            }
            return count > 0 ? m_Filled[c] - sum / count : 0f;
        }

        int CellIndex(float x, float z)
        {
            var i = Mathf.Clamp((int)(x / MacroCell), 0, m_GW - 1);
            var j = Mathf.Clamp((int)(z / MacroCell), 0, m_GL - 1);
            return j * m_GW + i;
        }

        Vector2 CellCenter(int c) => new Vector2((c % m_GW + 0.5f) * MacroCell, (c / m_GW + 0.5f) * MacroCell);

        float NeighbourDistance(int a, int b) => (a % m_GW != b % m_GW && a / m_GW != b / m_GW) ? 1.414f : 1f;

        bool IsSeaCell(int c) => (c / m_GW + 0.5f) * MacroCell > CoastZ;

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float Fbm(float x, float y, int octaves)
        {
            var sum = 0f;
            var amp = 1f;
            var norm = 0f;
            for (var o = 0; o < octaves; o++)
            {
                sum += Mathf.PerlinNoise(x, y) * amp;
                norm += amp;
                amp *= 0.5f;
                x *= 2.03f;
                y *= 2.03f;
            }
            return sum / norm;
        }

        sealed class MinHeap
        {
            readonly float[] m_Keys;
            readonly int[] m_Values;
            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                m_Keys = new float[capacity];
                m_Values = new int[capacity];
            }

            public void Push(float key, int value)
            {
                var i = Count++;
                m_Keys[i] = key;
                m_Values[i] = value;
                while (i > 0)
                {
                    var p = (i - 1) / 2;
                    if (m_Keys[p] <= m_Keys[i])
                        break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                var top = m_Values[0];
                Count--;
                m_Keys[0] = m_Keys[Count];
                m_Values[0] = m_Values[Count];
                var i = 0;
                while (true)
                {
                    var l = i * 2 + 1;
                    var r = l + 1;
                    var s = i;
                    if (l < Count && m_Keys[l] < m_Keys[s]) s = l;
                    if (r < Count && m_Keys[r] < m_Keys[s]) s = r;
                    if (s == i)
                        break;
                    Swap(i, s);
                    i = s;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                (m_Keys[a], m_Keys[b]) = (m_Keys[b], m_Keys[a]);
                (m_Values[a], m_Values[b]) = (m_Values[b], m_Values[a]);
            }
        }
    }
}
