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
            Directory.CreateDirectory("Builds/Preview");
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                EditorSceneManager.OpenScene(scenes[i].path);
                string tag = $"S{i + 1}";

                var startT = GameObject.Find("StartPoint").transform;
                var start = startT.position;

                var go = new GameObject("PreviewCam");
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
                cam.fieldOfView = 90f;
                cam.nearClipPlane = 0.02f;

                Shot(cam, $"{tag}_01_start_view", start + Vector3.up * 1.6f, start + Vector3.up * 1.2f + startT.forward * 10f);

                // 스테이지 특징: 점프대 > 자갈 > 코스 위 나무 순으로 찾아 그 앞에서 찍는다
                Transform feature = null;
                foreach (var name in new[] { "Ramp_1", "GravelPatch_02", "CourseTrees", "Gate_03" })
                {
                    var f = GameObject.Find(name);
                    if (f != null) { feature = f.transform; break; }
                }
                if (feature != null)
                {
                    var fp = feature.name == "CourseTrees" && feature.childCount > 10 ? feature.GetChild(10).position : FeaturePoint(feature);
                    var eye = fp - Vector3.forward * 16f;
                    eye.y = GroundY(eye) + 1.6f;
                    Shot(cam, $"{tag}_02_feature", eye, fp + Vector3.up * 0.5f);
                    var side = fp + new Vector3(14f, 0f, 2f);
                    side.y = GroundY(side) + 3f;
                    Shot(cam, $"{tag}_03_feature_side", side, fp, fogOff: true, far: 120f);
                }
                Shot(cam, $"{tag}_04_overview_top", new Vector3(120f, 260f, 200f), new Vector3(120f, 0f, 201f), fogOff: true, far: 700f);
                Object.DestroyImmediate(go);
            }
            Debug.Log("[Preview] done");
        }

        static Vector3 FeaturePoint(Transform t)
        {
            var r = t.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }

        static float GroundY(Vector3 p) => Terrain.activeTerrain.SampleHeight(p) + Terrain.activeTerrain.transform.position.y;

        /// <summary>배치 모드용: 씬을 만들고 미리보기를 찍는다.</summary>
        public static void BuildAndCapture()
        {
            UntroddenSceneBuilder.Build();
            Capture();
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
