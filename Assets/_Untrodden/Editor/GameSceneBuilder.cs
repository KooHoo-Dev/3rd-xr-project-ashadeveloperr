using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using Untrodden.Weather;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// Menu: Untrodden > Build Game Scene.
    /// Creates every material the game uses (so the Quest build includes the shaders),
    /// a GameAssets asset that points at them, and the game scene with the XR rig.
    /// Safe to run again: materials are reused, the scene is rebuilt.
    /// </summary>
    public static class GameSceneBuilder
    {
        const string k_Root = "Assets/_Untrodden";
        const string k_ScenePath = k_Root + "/Scenes/Untrodden.unity";
        const string k_AssetsPath = k_Root + "/GameAssets.asset";
        const string k_MaterialDir = k_Root + "/Materials";
        const string k_TextureDir = k_Root + "/Textures";
        const string k_FontAsset = "Assets/99. External Assets/Font/Noto_Sans_KR/NotoSansKR-Regular SDF.asset";
        const string k_ProductName = "Untrodden";

        static readonly Color k_Fog = new Color(0.78f, 0.82f, 0.87f);

        [MenuItem("Untrodden/Build Game Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(k_Root + "/Scenes");
            EnsureFolder(k_MaterialDir);
            EnsureFolder(k_TextureDir);

            var assets = BuildAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Opening a scene in Single mode unloads assets nothing references yet, which turns our
            // freshly created GameAssets into a dead object. Load it again from disk.
            assets = AssetDatabase.LoadAssetAtPath<GameAssets>(k_AssetsPath);
            if (assets == null)
            {
                Debug.LogError("[Untrodden] GameAssets could not be loaded after creating the scene.");
                return;
            }

            var sun = CreateSun();
            var rig = CreateRig();
            if (rig == null)
                return;
            var camera = rig.GetComponentInChildren<Camera>(true);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = k_Fog;
            camera.farClipPlane = 75f;
            AddVignette(rig, camera);

            var snow = CreateSnow(assets.snowflake);
            var weather = new GameObject("Weather").AddComponent<WeatherController>();
            var wso = new SerializedObject(weather);
            wso.FindProperty("m_Camera").objectReferenceValue = camera;
            wso.FindProperty("m_Snow").objectReferenceValue = snow;
            wso.FindProperty("m_Sun").objectReferenceValue = sun;
            wso.FindProperty("m_AutoCycle").boolValue = false;
            wso.ApplyModifiedPropertiesWithoutUndo();

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<XRUIInputModule>();

            var game = new GameObject("Game").AddComponent<GameManager>();
            var gso = new SerializedObject(game);
            gso.FindProperty("m_Assets").objectReferenceValue = assets;
            gso.FindProperty("m_Weather").objectReferenceValue = weather;
            gso.FindProperty("m_Origin").objectReferenceValue = rig.GetComponent<XROrigin>();
            gso.ApplyModifiedPropertiesWithoutUndo();

            ConfigureRenderSettings();

            EditorSceneManager.SaveScene(scene, k_ScenePath);
            SetBuildScenes(k_ScenePath);

            if (PlayerSettings.productName != k_ProductName)
                PlayerSettings.productName = k_ProductName;

            Debug.Log("[Untrodden] Game scene built: " + k_ScenePath +
                      ". Next: Project Validation (Fix All), then Build And Run on Quest.");
        }

        // ---------------------------------------------------------------- assets

        static GameAssets BuildAssets()
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(k_AssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<GameAssets>();
                AssetDatabase.CreateAsset(assets, k_AssetsPath);
            }

            assets.softDot = SoftDot();

            assets.snowGround = Lit("Snow", new Color(0.9f, 0.92f, 0.96f), 0.15f);
            assets.water = Transparent(Lit("Water", new Color(0.16f, 0.28f, 0.36f, 0.85f), 0.85f));
            assets.sea = Transparent(Lit("Sea", new Color(0.1f, 0.18f, 0.26f, 0.95f), 0.8f));
            assets.trunk = Lit("Trunk", new Color(0.25f, 0.18f, 0.13f), 0.05f, true);
            assets.pine = Lit("Pine", new Color(0.12f, 0.24f, 0.18f), 0.05f, true);
            assets.rock = Lit("Rock", new Color(0.45f, 0.46f, 0.48f), 0.1f, true);

            assets.wall = Lit("Wall", new Color(0.36f, 0.25f, 0.17f), 0.1f);
            assets.roof = Lit("Roof", new Color(0.22f, 0.2f, 0.19f), 0.1f);
            assets.windowGlow = Unlit("WindowGlow", new Color(1f, 0.78f, 0.45f));

            assets.wood = Lit("Wood", new Color(0.45f, 0.3f, 0.18f), 0.1f);
            assets.fish = Lit("Fish", new Color(0.55f, 0.6f, 0.65f), 0.6f);
            assets.can = Lit("Can", new Color(0.7f, 0.7f, 0.72f), 0.6f);
            assets.metal = Lit("Metal", new Color(0.6f, 0.62f, 0.65f), 0.7f);
            assets.darkMetal = Lit("DarkMetal", new Color(0.14f, 0.14f, 0.16f), 0.5f);
            assets.brass = Lit("Brass", new Color(0.8f, 0.6f, 0.25f), 0.7f);
            assets.ammoBox = Lit("AmmoBox", new Color(0.25f, 0.35f, 0.2f), 0.2f);
            assets.compassFace = Lit("CompassFace", new Color(0.92f, 0.9f, 0.82f), 0.3f);
            assets.needle = Unlit("Needle", new Color(0.9f, 0.15f, 0.1f));
            assets.ash = Lit("Ash", new Color(0.15f, 0.14f, 0.13f), 0f);

            assets.bearFur = Lit("BearFur", new Color(0.25f, 0.17f, 0.11f), 0.05f);
            assets.bearDark = Lit("BearDark", new Color(0.08f, 0.06f, 0.05f), 0.2f);

            assets.snowflake = Particle("Snowflake", false);
            assets.fireParticle = Particle("Fire", true);
            assets.overlay = Transparent(Unlit("Overlay", Color.white));
            assets.handful = Lit("Handful", Color.white, 0.3f);

            assets.font = KoreanFont();
            WinterPack.Apply(assets, k_Root);

            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            return assets;
        }

        static Material Lit(string name, Color color, float smoothness, bool instancing = false)
        {
            var mat = LoadOrCreate(name, "Universal Render Pipeline/Simple Lit");
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = instancing || mat.enableInstancing;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Unlit(string name, Color color)
        {
            var mat = LoadOrCreate(name, "Universal Render Pipeline/Unlit");
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Particle(string name, bool additive)
        {
            var mat = LoadOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", SoftDot());
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)(additive ? BlendMode.SrcAlpha : BlendMode.SrcAlpha));
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Transparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            var path = $"{k_MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(shaderName);
            if (shader == null)
                Debug.LogError($"[Untrodden] Shader '{shaderName}' not found. Is URP the active render pipeline?");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        /// <summary>
        /// The project's existing Noto Sans KR TMP font (dynamic atlas: glyphs are added on demand at runtime).
        /// Reused as is so the repository carries the font only once.
        /// </summary>
        static TMP_FontAsset KoreanFont()
        {
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_FontAsset);
            if (fontAsset == null)
                Debug.LogError($"[Untrodden] Korean font not found at {k_FontAsset}. UI text will fall back to the default font (no Korean).");
            else if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                Debug.LogWarning("[Untrodden] The Korean font atlas is not dynamic; characters outside it will show as boxes.");
            return fontAsset;
        }

        static Texture2D SoftDot()
        {
            var path = $"{k_TextureDir}/SoftDot.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                var a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------------------------------------------------------------- scene

        static GameObject CreateRig()
        {
            var guid = AssetDatabase.FindAssets("\"XR Origin (XR Rig)\" t:Prefab")
                .FirstOrDefault(g => AssetDatabase.GUIDToAssetPath(g).Contains("Starter Assets"));
            if (guid == null)
            {
                EditorUtility.DisplayDialog("Untrodden",
                    "XR Origin (XR Rig) prefab not found.\nImport the XR Interaction Toolkit 'Starter Assets' sample first.", "OK");
                return null;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        }

        static void AddVignette(GameObject rig, Camera camera)
        {
            var guid = AssetDatabase.FindAssets("TunnelingVignette t:Prefab").FirstOrDefault();
            if (guid == null)
            {
                Debug.LogWarning("[Untrodden] TunnelingVignette prefab not found; skipping comfort vignette.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var vignette = (GameObject)PrefabUtility.InstantiatePrefab(prefab, camera.transform);
            vignette.transform.localPosition = Vector3.zero;
            vignette.transform.localRotation = Quaternion.identity;

            var controller = vignette.GetComponentInChildren<TunnelingVignetteController>(true);
            if (controller == null)
                controller = vignette.AddComponent<TunnelingVignetteController>();

            var providers = new List<LocomotionVignetteProvider>();
            foreach (var move in rig.GetComponentsInChildren<ContinuousMoveProvider>(true))
                providers.Add(new LocomotionVignetteProvider { locomotionProvider = move, enabled = true });
            if (providers.Count == 0)
                Debug.LogWarning("[Untrodden] No ContinuousMoveProvider on the rig; the vignette has no source.");

            controller.locomotionVignetteProviders = providers;
            EditorUtility.SetDirty(controller);
        }

        static Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.86f, 0.9f, 1f);
            light.intensity = 0.7f;
            light.shadows = LightShadows.None;
            return light;
        }

        static ParticleSystem CreateSnow(Material mat)
        {
            var go = new GameObject("Snow");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 6000;
            main.prewarm = true;

            var emission = ps.emission;
            emission.rateOverTime = 250f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 40f, 1f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.3f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void ConfigureRenderSettings()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = k_Fog;
            RenderSettings.fogStartDistance = 4f;
            RenderSettings.fogEndDistance = 60f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.7f, 0.74f, 0.8f);
        }

        // ---------------------------------------------------------------- project

        static void SetBuildScenes(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            foreach (var s in scenes)
                s.enabled = false;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
