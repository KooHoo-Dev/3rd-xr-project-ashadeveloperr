using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Untrodden.EditorTools
{
    /// <summary>
    /// Hooks up the free "Low Poly Winter Log Cabin Pack" (Small Hearth Studios) if it is in the project:
    /// trees, rocks and props become instanced models, the assembled cabin from its demo scene becomes
    /// a prefab, and its camp fire and chopped log replace the placeholders.
    /// The pack's prefabs use a built-in (Standard) material, which renders pink in URP, so everything is
    /// re-pointed at a copy of the pack's own URP atlas material.
    /// Without the pack, all of this is left empty and the game falls back to primitives.
    /// </summary>
    public static class WinterPack
    {
        static readonly string[] k_Trees = { "Tree_pine_A_snow", "Tree_pine_B_snow", "Tree_pine_C_snow" };
        static readonly string[] k_Rocks = { "Rock_A_Snow", "Rock_B_Snow", "Rock_C_Snow", "Flat_Rock_A_Snow", "Flat_Rock_B_Snow", "Flat_Rock_C_Snow" };
        static readonly string[] k_Props = { "Dead_Bush_A", "Dead_Bush_B", "Dead_Bush_C", "Snow_mound_A", "snow_mound_B", "snow_mound_C", "TreeStump_Snow" };

        public static void Apply(GameAssets assets, string ourRoot)
        {
            var root = FindPackRoot();
            if (root == null)
            {
                Debug.Log("[Untrodden] Winter Cabin pack not found; using placeholder models.");
                Clear(assets);
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(root + "/Materials/Winter_Pack_Mat_URP.mat");
            if (source == null)
            {
                Debug.LogWarning("[Untrodden] Winter Cabin pack has no URP material; using placeholder models.");
                Clear(assets);
                return;
            }

            assets.packMaterial = AtlasMaterial(source, ourRoot + "/Materials/WinterPackAtlas.mat");
            assets.trees = k_Trees.Select(n => Model(root, n)).Where(m => m != null).ToArray();
            assets.rocks = k_Rocks.Select(n => Model(root, n)).Where(m => m != null).ToArray();
            assets.props = k_Props.Select(n => Model(root, n)).Where(m => m != null).ToArray();
            assets.firewood = Model(root, "Chopped_Log");

            var prefabDir = ourRoot + "/Prefabs";
            if (!AssetDatabase.IsValidFolder(prefabDir))
                AssetDatabase.CreateFolder(ourRoot, "Prefabs");

            assets.cabinPrefab = ExtractFromScene(root + "/Scenes/Woodland_Cabin.unity", "house",
                prefabDir + "/WinterCabin.prefab", assets.packMaterial, out var cabinSize);
            assets.cabinSize = cabinSize;
            assets.campfirePrefab = Repoint(root + "/Prefabs/CampFire.prefab", prefabDir + "/PackCampFire.prefab", assets.packMaterial, out _);

            Debug.Log($"[Untrodden] Winter Cabin pack: {assets.trees.Length} trees, {assets.rocks.Length} rocks, {assets.props.Length} props, " +
                      $"firewood {(assets.firewood != null)}, cabin {(assets.cabinPrefab != null)} {cabinSize}, campfire {(assets.campfirePrefab != null)}.");
        }

        static void Clear(GameAssets assets)
        {
            assets.packMaterial = null;
            assets.trees = null;
            assets.rocks = null;
            assets.props = null;
            assets.firewood = null;
            assets.cabinPrefab = null;
            assets.campfirePrefab = null;
        }

        static string FindPackRoot()
        {
            var guid = AssetDatabase.FindAssets("Tree_pine_A_snow t:Prefab").FirstOrDefault();
            if (guid == null)
                return null;
            var prefabPath = AssetDatabase.GUIDToAssetPath(guid);             // .../Low_Poly_Winter_Cabin/Prefabs/x.prefab
            return Path.GetDirectoryName(Path.GetDirectoryName(prefabPath))?.Replace('\\', '/');
        }

        static Material AtlasMaterial(Material source, string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Flattens a prefab's meshes into parts relative to its root (keeping the root's own rotation and scale).</summary>
        static PackModel Model(string root, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{root}/Prefabs/{name}.prefab");
            if (prefab == null)
            {
                Debug.LogWarning($"[Untrodden] Winter Cabin pack: prefab {name} not found.");
                return null;
            }

            var rootT = prefab.transform;
            var rootRS = Matrix4x4.TRS(Vector3.zero, rootT.localRotation, rootT.localScale);
            var meshes = new List<Mesh>();
            var parts = new List<Matrix4x4>();
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var m = rootRS * (rootT.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                meshes.Add(filter.sharedMesh);
                parts.Add(m);

                var b = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }

            if (meshes.Count == 0)
            {
                Debug.LogWarning($"[Untrodden] Winter Cabin pack: {name} has no meshes.");
                return null;
            }

            return new PackModel { name = name, meshes = meshes.ToArray(), parts = parts.ToArray(), boundsMin = min, boundsMax = max };
        }

        /// <summary>Saves a copy of a pack prefab with every renderer using our atlas material, pivot at its bottom centre.</summary>
        static GameObject Repoint(string sourcePath, string targetPath, Material mat, out Vector3 size)
        {
            size = Vector3.zero;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("[Untrodden] Winter Cabin pack: missing " + sourcePath);
                return null;
            }
            var copy = (GameObject)Object.Instantiate(prefab);
            return SaveCentered(copy, targetPath, mat, out size);
        }

        /// <summary>Pulls an assembled object (e.g. the cabin) out of one of the pack's demo scenes.</summary>
        static GameObject ExtractFromScene(string scenePath, string objectName, string targetPath, Material mat, out Vector3 size)
        {
            size = Vector3.zero;
            if (!File.Exists(scenePath))
            {
                Debug.LogWarning("[Untrodden] Winter Cabin pack: missing scene " + scenePath);
                return null;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            GameObject result = null;
            try
            {
                var source = scene.GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t => t.name == objectName);
                if (source == null)
                {
                    Debug.LogWarning($"[Untrodden] Winter Cabin pack: '{objectName}' not found in {scenePath}");
                }
                else
                {
                    var copy = Object.Instantiate(source.gameObject);
                    copy.transform.rotation = source.rotation;
                    copy.transform.localScale = source.lossyScale;
                    result = SaveCentered(copy, targetPath, mat, out size);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            return result;
        }

        static GameObject SaveCentered(GameObject copy, string targetPath, Material mat, out Vector3 size)
        {
            copy.transform.position = Vector3.zero;

            // Drop anything that is not geometry (cameras, lights, volumes, audio) that a demo object might carry.
            foreach (var c in copy.GetComponentsInChildren<Component>(true))
            {
                if (c is Camera || c is AudioListener || c is AudioSource || c is ParticleSystem || c is ParticleSystemRenderer)
                    Object.DestroyImmediate(c);
            }

            var renderers = copy.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(copy.transform.position, Vector3.zero);
            var first = true;
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }

            var pivot = new GameObject(Path.GetFileNameWithoutExtension(targetPath));
            copy.transform.SetParent(pivot.transform, true);
            copy.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            size = bounds.size;

            // Unpack nested prefab links so the saved prefab stands on its own.
            if (PrefabUtility.IsPartOfPrefabInstance(copy))
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            var saved = PrefabUtility.SaveAsPrefabAsset(pivot, targetPath);
            Object.DestroyImmediate(pivot);
            return saved;
        }
    }
}
