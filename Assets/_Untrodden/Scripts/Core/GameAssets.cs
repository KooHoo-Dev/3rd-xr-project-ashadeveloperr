using UnityEngine;

namespace Untrodden
{
    /// <summary>
    /// Materials created by the editor builder. Runtime code never calls Shader.Find,
    /// so every shader the game needs is guaranteed to be in the Quest build.
    /// </summary>
    [CreateAssetMenu(menuName = "Untrodden/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        [Header("World")]
        public Material snowGround;
        public Material water;
        public Material sea;
        public Material trunk;
        public Material pine;
        public Material rock;

        [Header("Buildings")]
        public Material wall;
        public Material roof;
        public Material windowGlow;

        [Header("Items")]
        public Material wood;
        public Material fish;
        public Material can;
        public Material metal;
        public Material darkMetal;
        public Material brass;
        public Material ammoBox;
        public Material compassFace;
        public Material needle;
        public Material ash;

        [Header("Bear")]
        public Material bearFur;
        public Material bearDark;

        [Header("Effects")]
        public Material snowflake;
        public Material fireParticle;
        public Material overlay;
        public Material handful;
        public Texture2D softDot;

        [Header("Winter Cabin pack (optional, filled by the builder when the pack is imported)")]
        [Tooltip("Copy of the pack's URP atlas material with GPU instancing on.")]
        public Material packMaterial;
        public PackModel[] trees;
        public PackModel[] rocks;
        public PackModel[] props;
        public PackModel firewood;
        public GameObject cabinPrefab;
        public Vector3 cabinSize;
        public GameObject campfirePrefab;

        public bool HasPackTrees => packMaterial != null && trees != null && trees.Length > 0;

        [Header("Text")]
        [Tooltip("Korean-capable TextMesh Pro font (built from Fonts/NotoSansKR-Regular.otf).")]
        public TMPro.TMP_FontAsset font;

        [Header("Credits")]
        [Tooltip("Shown on the title screen.")]
        public string creatorName = "ashadeveloperr";
    }

    /// <summary>
    /// A model flattened to meshes + transforms relative to its root, so it can be drawn with
    /// Graphics.RenderMeshInstanced (no GameObjects). Pivot: the root; bounds measured in that space.
    /// </summary>
    [System.Serializable]
    public class PackModel
    {
        public string name;
        public Mesh[] meshes;
        public Matrix4x4[] parts;
        public Vector3 boundsMin;
        public Vector3 boundsMax;

        public bool Valid => meshes != null && meshes.Length > 0 && parts != null && parts.Length == meshes.Length;
        public Vector3 Size => boundsMax - boundsMin;
        public float Height => Mathf.Max(0.001f, boundsMax.y - boundsMin.y);

        /// <summary>Visual-only GameObject copy of the model (for items).</summary>
        public GameObject Spawn(Transform parent, Material material)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            for (var i = 0; i < meshes.Length; i++)
            {
                var part = new GameObject(meshes[i].name);
                part.transform.SetParent(root.transform, false);
                var m = parts[i];
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var r = part.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return root;
        }
    }
}
