#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using Unity.XR.CoreUtils;
using Untrodden.Bears;
using Untrodden.Items;
using Untrodden.Player;
using Untrodden.Weather;
using Untrodden.World;
using UnityEngine;

namespace Untrodden.DebugTools
{
    /// <summary>
    /// Editor-only: walks through the game in play mode and saves screenshots from a plain camera,
    /// so the build can be reviewed without a headset. Started by the editor CaptureRunner.
    /// </summary>
    public class CaptureDriver : MonoBehaviour
    {
        public static string OutputDir = "Captures";
        const int k_W = 1280, k_H = 720;

        Camera m_Cam;
        RenderTexture m_Rt;
        GameManager m_Game;
        XROrigin m_Origin;
        ChunkStreamer m_Streamer;
        WeatherController m_Weather;

        public const string ActiveFlag = "Untrodden.Capture.Active";
        public const int Seed = 20261116;

        // Statics are reset by the domain reload on entering play mode, so set the seed here, before GameManager.Awake.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad()
        {
            if (UnityEditor.SessionState.GetBool(ActiveFlag, false))
            {
                GameSession.NextSeed = Seed;
                GameSession.SkipTitle = false;
            }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(OutputDir);
            yield return Frames(10);

            m_Game = GameManager.Instance;
            m_Origin = Field<XROrigin>("m_Origin");
            m_Streamer = Field<ChunkStreamer>("m_Streamer");
            m_Weather = Field<WeatherController>("m_Weather");

            m_Rt = new RenderTexture(k_W, k_H, 24);
            m_Cam = new GameObject("Capture Camera").AddComponent<Camera>();
            m_Cam.fieldOfView = 75f;
            m_Cam.nearClipPlane = 0.02f;
            m_Cam.farClipPlane = 120f;
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.targetTexture = m_Rt;
            m_Cam.enabled = false;

            var world = m_Game.World;
            Log($"seed {world.Seed}, start {world.StartPosition}, village {world.VillageXZ}");
            yield return Seconds(1f);

            // 1. title screen, seen from a standing player's eyes facing north
            var eye = world.StartPosition + Vector3.up * 1.6f;
            var north = Quaternion.Euler(5f, 0f, 0f);
            yield return Look(eye, north);
            Call("ShowTitle");
            yield return Shot("01_title", eye, north);

            // start the run
            Call("BeginRun");
            yield return Seconds(1f);

            // 2. first look north from the start
            yield return Shot("02_start_forest", eye, north);

            // 3. on a river bank, looking downstream
            var riverView = FindRiverView(world, out var downstream);
            yield return MovePlayer(riverView);
            var riverEye = riverView + Vector3.up * 1.6f;
            var riverLook = Quaternion.LookRotation(downstream + Vector3.down * 0.15f);
            yield return Shot("03_river", riverEye, riverLook);

            // 4. camp: lit fire, items on the ground, a bear in the fog
            var fwd = Vector3.ProjectOnPlane(downstream, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            var campCenter = Ground(world, riverView - right * 7f);
            yield return MovePlayer(campCenter - fwd * 2.5f);
            var pit = FirePit.Create(Ground(world, campCenter), m_Game.Assets);
            typeof(FirePit).GetMethod("Ignite", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(pit, null);
            Spawn(ItemKind.Firewood, campCenter + right * 1.2f);
            Spawn(ItemKind.Fish, campCenter + right * 1.0f - fwd * 0.7f);
            Spawn(ItemKind.Can, campCenter - right * 1.0f - fwd * 0.5f);
            Spawn(ItemKind.AmmoBox, campCenter - right * 1.2f);
            Spawn(ItemKind.Rifle, campCenter - right * 0.6f - fwd * 1.0f);
            Spawn(ItemKind.Compass, campCenter + right * 0.5f - fwd * 1.1f);
            var bearPos = Ground(world, campCenter + fwd * 10f - right * 2f);
            var bear = Bear.Create(bearPos, Object.FindFirstObjectByType<BearSpawner>(), m_Game.Assets, 1);
            yield return Seconds(2f); // flames and snow need real time to build up
            var campEye = campCenter - fwd * 2.5f + Vector3.up * 1.6f;
            yield return Shot("04_camp_and_bear", campEye, Quaternion.LookRotation((campCenter + fwd * 3f) - campEye));
            var closeEye = campCenter - fwd * 1.4f + Vector3.up * 0.9f;
            yield return Shot("05_items_closeup", closeEye, Quaternion.LookRotation(campCenter - closeEye));
            if (bear != null) bear.Despawn();

            // 6. wrist display (controllers are inactive without a headset, so lift it out to look at it)
            var wrist = Object.FindFirstObjectByType<UI.WristDisplay>(FindObjectsInactive.Include);
            if (wrist != null)
            {
                var camPos = campEye;
                var camRot = Quaternion.LookRotation(fwd + Vector3.down * 0.4f);
                wrist.transform.SetParent(null, true);
                wrist.transform.SetPositionAndRotation(camPos + camRot * Vector3.forward * 0.16f, camRot);
                wrist.gameObject.SetActive(true);
                yield return Seconds(0.5f);
                yield return Shot("06_wrist", camPos, camRot);
                wrist.gameObject.SetActive(false);
            }
            else
            {
                Log("wrist display not found");
            }

            // 7. blizzard at the river
            yield return MovePlayer(riverView);
            m_Weather.SetTarget(1f, 0f);
            yield return Seconds(2f);
            yield return Shot("07_blizzard", riverEye, riverLook);
            m_Weather.SetTarget(0f, 0f);

            // 8. death screen (menu appears in front of the head)
            yield return Look(campEye, Quaternion.LookRotation(fwd));
            m_Game.Survival.Damage(999f, Cause.Cold);
            yield return Seconds(1.5f);
            yield return Shot("08_death", campEye, Quaternion.LookRotation(fwd));
            Call("Respawn");
            yield return Seconds(2.5f); // fade back in

            // 9. the village from the south
            var v = new Vector3(world.VillageXZ.x, world.VillageHeight, world.VillageXZ.y);
            var approach = Ground(world, v + new Vector3(0f, 0f, -40f));
            yield return MovePlayer(approach);
            var approachEye = approach + Vector3.up * 1.6f;
            yield return Shot("09_village", approachEye, Quaternion.LookRotation((v + Vector3.up * 3f) - approachEye));

            // 10. arrival / result screen
            var inside = v + Vector3.up * 0.1f + new Vector3(0f, 0f, -6f);
            var insideEye = inside + Vector3.up * 1.5f;
            yield return Look(insideEye, Quaternion.identity);
            yield return MovePlayer(inside);
            yield return Seconds(1f);
            yield return Shot("10_result", insideEye, Quaternion.identity);

            Log("done");
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // ------------------------------------------------------------------ helpers

        Vector3 FindRiverView(WorldMap world, out Vector3 downstream)
        {
            // Walk north from the start until a real river (not a stream) is found.
            var best = world.StartPosition;
            downstream = Vector3.forward;
            for (var z = world.StartPosition.z; z < WorldMap.CoastZ - 120f; z += 8f)
            for (var x = 40f; x < WorldMap.Width - 40f; x += 8f)
            {
                var r = world.NearestRiver(x, z);
                if (!r.valid || r.flow < WorldMap.RiverFlow || r.distance > 2f)
                    continue;
                var probe = world.NearestRiver(x, z + 12f);
                var ahead = new Vector3(probe.point.x - r.point.x, 0f, probe.point.y - r.point.y);
                downstream = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, downstream);
                var bank = new Vector3(r.point.x, 0f, r.point.y) + side * (r.halfWidth + 3f);
                return Ground(world, bank);
            }
            return best;
        }

        static Vector3 Ground(WorldMap world, Vector3 p) => new Vector3(p.x, world.Height(p.x, p.z) + 0.05f, p.z);

        IEnumerator MovePlayer(Vector3 feet)
        {
            m_Streamer.LoadImmediate(feet, 2);
            var cc = m_Origin.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m_Origin.transform.position = feet;
            if (cc != null) cc.enabled = true;
            yield return Frames(30);
        }

        void Spawn(ItemKind kind, Vector3 p)
        {
            var item = m_Game.Items.Create(kind, Ground(m_Game.World, p) + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            item.MarkOwned();
        }

        IEnumerator Look(Vector3 pos, Quaternion rot)
        {
            m_Game.Head.SetPositionAndRotation(pos, rot);
            yield return null; // belt and overlays follow the head in LateUpdate
            m_Game.Head.SetPositionAndRotation(pos, rot);
        }

        IEnumerator Shot(string name, Vector3 pos, Quaternion rot)
        {
            yield return Look(pos, rot);
            m_Cam.transform.SetPositionAndRotation(pos, rot);
            m_Cam.backgroundColor = RenderSettings.fogColor;
            yield return null; // let this frame's instanced draws be queued
            m_Cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = m_Rt;
            var tex = new Texture2D(k_W, k_H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, k_W, k_H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutputDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Log("saved " + name);
        }

        static IEnumerator Seconds(float s)
        {
            var end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }

        static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++)
                yield return null;
        }

        T Field<T>(string name) where T : class =>
            typeof(GameManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(m_Game) as T;

        void Call(string method) =>
            typeof(GameManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(m_Game, null);

        static void Log(string msg) => Debug.Log("[Capture] " + msg);
    }
}
#endif
