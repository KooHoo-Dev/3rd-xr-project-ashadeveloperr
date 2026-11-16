using System.IO;
using Untrodden.DebugTools;
using Untrodden.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// Headless review: builds the game scene, draws a top-down map of a generated world,
    /// then plays the game and saves screenshots (see CaptureDriver).
    /// Command line:
    ///   Unity.exe -batchmode -projectPath . -executeMethod Untrodden.EditorTools.CaptureRunner.Run -logFile capture.log
    /// Also available from the menu: Untrodden > Capture Screenshots.
    /// </summary>
    [InitializeOnLoad]
    public static class CaptureRunner
    {
        const string k_Flag = CaptureDriver.ActiveFlag;
        const string k_StartTime = "Untrodden.Capture.Start";
        const string k_Scene = "Assets/_Untrodden/Scenes/Untrodden.unity";
        const int k_MapSeed = CaptureDriver.Seed;

        static CaptureRunner()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Watchdog;
        }

        [MenuItem("Untrodden/Capture Screenshots")]
        public static void Run()
        {
            Debug.Log("[Capture] building scene");
            GameSceneBuilder.Build();

            Directory.CreateDirectory(CaptureDriver.OutputDir);
            DrawWorldMap(k_MapSeed, Path.Combine(CaptureDriver.OutputDir, "00_world_map.png"));

            EditorSceneManager.OpenScene(k_Scene);
            GameSession.NextSeed = k_MapSeed; // same world as the map
            SessionState.SetBool(k_Flag, true);
            SessionState.SetFloat(k_StartTime, (float)EditorApplication.timeSinceStartup);
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(k_Flag, false))
                return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                new GameObject("Capture Driver").AddComponent<CaptureDriver>();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(k_Flag, false);
                Debug.Log("[Capture] finished");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
        }

        static void Watchdog()
        {
            if (!SessionState.GetBool(k_Flag, false))
                return;
            var elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(k_StartTime, 0f);
            if (elapsed > 600)
            {
                Debug.LogError("[Capture] timed out");
                SessionState.SetBool(k_Flag, false);
                if (Application.isBatchMode)
                    EditorApplication.Exit(2);
                else
                    EditorApplication.isPlaying = false;
            }
        }

        /// <summary>Top-down map, 2 m per pixel: terrain shading, biomes, rivers, start (green) and village (red).</summary>
        public static void DrawWorldMap(int seed, string path)
        {
            var world = new WorldMap(seed);
            const float scale = 2f;
            var w = Mathf.CeilToInt(WorldMap.Width / scale);
            var h = Mathf.CeilToInt(WorldMap.Length / scale);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var heights = new float[w, h];

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                heights[x, y] = world.Height(x * scale, y * scale);

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var wx = x * scale;
                var wz = y * scale;
                Color c;
                if (world.TryGetWater(wx, wz, out var surface))
                {
                    var depth = Mathf.Clamp01((surface - heights[x, y]) / 3f);
                    c = Color.Lerp(new Color(0.35f, 0.55f, 0.75f), new Color(0.1f, 0.22f, 0.4f), depth);
                }
                else
                {
                    switch (world.GetBiome(wx, wz))
                    {
                        case Biome.Forest: c = new Color(0.55f, 0.66f, 0.58f); break;
                        case Biome.Valley: c = new Color(0.62f, 0.7f, 0.62f); break;
                        case Biome.Ridge: c = new Color(0.9f, 0.9f, 0.93f); break;
                        case Biome.Stream: c = new Color(0.75f, 0.8f, 0.82f); break;
                        case Biome.River: c = new Color(0.8f, 0.8f, 0.78f); break;
                        case Biome.Downstream: c = new Color(0.82f, 0.78f, 0.7f); break;
                        case Biome.Coast: c = new Color(0.86f, 0.84f, 0.78f); break;
                        case Biome.Village: c = new Color(0.85f, 0.45f, 0.35f); break;
                        default: c = new Color(0.1f, 0.2f, 0.32f); break;
                    }

                    // hill shading from the north-west
                    var hx = heights[Mathf.Min(x + 1, w - 1), y] - heights[Mathf.Max(x - 1, 0), y];
                    var hy = heights[x, Mathf.Min(y + 1, h - 1)] - heights[x, Mathf.Max(y - 1, 0)];
                    var shade = Mathf.Clamp(1f + (-hx + hy) * 0.08f, 0.6f, 1.25f);
                    c *= shade;
                    // contour every 10 m
                    if (Mathf.Repeat(heights[x, y], 10f) < 0.35f)
                        c *= 0.85f;
                }
                tex.SetPixel(x, y, c);
            }

            void Dot(Vector2 p, Color c, int r)
            {
                var cx = Mathf.RoundToInt(p.x / scale);
                var cy = Mathf.RoundToInt(p.y / scale);
                for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r && cx + dx >= 0 && cy + dy >= 0 && cx + dx < w && cy + dy < h)
                        tex.SetPixel(cx + dx, cy + dy, c);
            }

            Dot(new Vector2(world.StartPosition.x, world.StartPosition.z), new Color(0.1f, 0.8f, 0.2f), 6);
            Dot(world.VillageXZ, new Color(0.9f, 0.1f, 0.1f), 7);
            Dot(world.MouthXZ, new Color(1f, 0.85f, 0.1f), 4);

            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[Capture] world map saved ({w}x{h}, seed {seed}, start {world.StartPosition}, village {world.VillageXZ})");
        }
    }
}
