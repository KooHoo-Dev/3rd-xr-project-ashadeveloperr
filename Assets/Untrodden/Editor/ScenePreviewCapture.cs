using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Untrodden.EditorTools
{
    /// <summary>헤드셋 없이 씬 모습을 확인하기 위해 몇 개 시점을 PNG로 렌더링한다. 결과: Builds/Preview/*.png</summary>
    public static class ScenePreviewCapture
    {
        [MenuItem("Untrodden/Capture Preview Shots")]
        public static void Capture()
        {
            EditorSceneManager.OpenScene("Assets/Untrodden/Scenes/Untrodden.unity");
            Directory.CreateDirectory("Builds/Preview");

            var start = GameObject.Find("StartPoint").transform.position;
            var fire0 = GameObject.Find("CampfireSite_0").transform.position;
            var fire1 = GameObject.Find("CampfireSite_1").transform.position;
            var goal = GameObject.Find("GoalPoint").transform.position;

            var go = new GameObject("PreviewCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 90f;
            cam.farClipPlane = 80f;
            cam.nearClipPlane = 0.02f;

            Shot(cam, "01_start_view", start + Vector3.up * 1.6f, start + new Vector3(0f, 1.6f, 5f));
            Shot(cam, "02_start_campfire", fire0 + new Vector3(1.5f, 1.4f, -2.5f), fire0);
            Shot(cam, "03_forest_north", fire1 + new Vector3(0f, 1.7f, -4f), fire1 + new Vector3(0f, 1.2f, 20f));
            var villageEye = goal + new Vector3(0f, 0f, -26f);
            villageEye.y = Terrain.activeTerrain.SampleHeight(villageEye) + Terrain.activeTerrain.transform.position.y + 1.7f;
            Shot(cam, "04_village", villageEye, goal + Vector3.up * 2f);
            var bridge = GameObject.Find("Bridge_0").transform;
            var bEye = bridge.position - bridge.forward * 16f;
            bEye.y = Terrain.activeTerrain.SampleHeight(bEye) + Terrain.activeTerrain.transform.position.y + 1.7f;
            Shot(cam, "06_trail_bridge", bEye, bridge.position + Vector3.up * 0.8f);
            var sh = GameObject.Find("ShelterZone_2").transform.position;
            Shot(cam, "07_shelter_branch", sh + new Vector3(-6f, 1.7f, -8f), sh + Vector3.up * 1.5f);
            Shot(cam, "08_overview_high", new Vector3(120f, 160f, 150f), new Vector3(120f, 0f, 151f), fogOff: true, far: 600f);
            Shot(cam, "09_trail_from_above", start + new Vector3(0f, 45f, 30f), start + new Vector3(0f, 0f, 31f), fogOff: true, far: 300f);
            var tz = new Vector3(start.x - 2f, 0f, start.z + 14f);
            tz.y = Terrain.activeTerrain.SampleHeight(tz) + Terrain.activeTerrain.transform.position.y + 1.7f;
            Shot(cam, "10_trail_walk", tz, tz + new Vector3(4f, -0.6f, 20f));
            Shot(cam, "05_overview", new Vector3(120f, 90f, -20f), new Vector3(120f, 0f, 160f), fogOff: true, far: 600f);

            Object.DestroyImmediate(go);
            Debug.Log("[Preview] done");
        }

        static void Shot(Camera cam, string name, Vector3 pos, Vector3 lookAt, bool fogOff = false, float far = 80f)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(lookAt);
            cam.farClipPlane = far;
            bool fog = RenderSettings.fog;
            if (fogOff) RenderSettings.fog = false;

            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"Builds/Preview/{name}.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            RenderSettings.fog = fog;
        }
    }
}
