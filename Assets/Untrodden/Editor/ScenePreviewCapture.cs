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

            var startT = GameObject.Find("StartPoint").transform;
            var start = startT.position;
            var fire0 = GameObject.Find("CampfireSite_0").transform.position;
            var goal = GameObject.Find("GoalPoint").transform.position;
            var gate3 = GameObject.Find("Gate_03").transform;

            var go = new GameObject("PreviewCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.02f;

            Shot(cam, "01_start_view", start + Vector3.up * 1.6f, start + Vector3.up * 1.2f + startT.forward * 10f);
            Shot(cam, "02_slope_down", start + Vector3.up * 1.6f + startT.forward * 8f, start + startT.forward * 40f + Vector3.down * 8f);
            var gEye = gate3.position - gate3.forward * 14f;
            gEye.y = Terrain.activeTerrain.SampleHeight(gEye) + Terrain.activeTerrain.transform.position.y + 1.6f;
            Shot(cam, "03_gate", gEye, gate3.position + Vector3.up * 0.8f);
            Shot(cam, "04_campfire", fire0 + new Vector3(0f, 2f, -6f), fire0);
            var fEye = goal - Vector3.forward * 18f;
            fEye.y = Terrain.activeTerrain.SampleHeight(fEye) + Terrain.activeTerrain.transform.position.y + 1.6f;
            Shot(cam, "05_finish", fEye, goal + Vector3.up * 2f + Vector3.forward * 10f);
            Shot(cam, "06_overview_side", new Vector3(-80f, 150f, 200f), new Vector3(120f, 30f, 200f), fogOff: true, far: 700f);
            Shot(cam, "07_overview_top", new Vector3(120f, 260f, 200f), new Vector3(120f, 0f, 201f), fogOff: true, far: 700f);

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
