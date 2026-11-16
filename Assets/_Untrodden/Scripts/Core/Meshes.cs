using System.Collections.Generic;
using UnityEngine;

namespace Untrodden
{
    /// <summary>Shared primitive meshes and small helpers for building placeholder models from code.</summary>
    public static class Meshes
    {
        static readonly Dictionary<PrimitiveType, Mesh> s_Primitives = new Dictionary<PrimitiveType, Mesh>();
        static Mesh s_Cone;

        public static Mesh Primitive(PrimitiveType type)
        {
            if (s_Primitives.TryGetValue(type, out var mesh) && mesh != null)
                return mesh;

            var go = GameObject.CreatePrimitive(type);
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            s_Primitives[type] = mesh;
            return mesh;
        }

        /// <summary>Cone with radius 1, height 1, pivot at the base centre.</summary>
        public static Mesh Cone
        {
            get
            {
                if (s_Cone != null)
                    return s_Cone;

                const int sides = 8;
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var tris = new List<int>();
                var slope = 1f / Mathf.Sqrt(2f);
                for (var i = 0; i < sides; i++)
                {
                    var a0 = i * Mathf.PI * 2f / sides;
                    var a1 = (i + 1) * Mathf.PI * 2f / sides;
                    var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var mid = (p0 + p1).normalized;
                    var n = new Vector3(mid.x * slope, slope, mid.z * slope);
                    var start = verts.Count;
                    verts.Add(p0); verts.Add(Vector3.up); verts.Add(p1);
                    normals.Add(new Vector3(p0.x * slope, slope, p0.z * slope));
                    normals.Add(n);
                    normals.Add(new Vector3(p1.x * slope, slope, p1.z * slope));
                    tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);

                    // base
                    var b = verts.Count;
                    verts.Add(Vector3.zero); verts.Add(p0); verts.Add(p1);
                    normals.Add(Vector3.down); normals.Add(Vector3.down); normals.Add(Vector3.down);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                }

                s_Cone = new Mesh { name = "Cone" };
                s_Cone.SetVertices(verts);
                s_Cone.SetNormals(normals);
                s_Cone.SetTriangles(tris, 0);
                s_Cone.RecalculateBounds();
                return s_Cone;
            }
        }

        /// <summary>Adds a visual-only primitive child (collider removed).</summary>
        public static GameObject Part(Transform parent, PrimitiveType type, Material mat,
            Vector3 localPos, Vector3 localScale, Vector3 localEuler = default, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!keepCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>()); // immediate: grab interactables collect colliders on Awake
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localEulerAngles = localEuler;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }

    /// <summary>Survives scene reloads: which seed to use next and whether to skip the title.</summary>
    public static class GameSession
    {
        public static int? NextSeed;
        public static bool SkipTitle;
    }
}
