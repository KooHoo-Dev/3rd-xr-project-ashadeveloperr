using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Untrodden.Items
{
    /// <summary>Builds grabbable placeholder items from primitives (no prefabs needed).</summary>
    public class ItemFactory
    {
        readonly GameAssets m_Assets;

        public ItemFactory(GameAssets assets) => m_Assets = assets;

        public WorldItem Create(ItemKind kind, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetPositionAndRotation(position, rotation);
            var rb = go.AddComponent<Rigidbody>();
            rb.linearDamping = 0.5f;
            rb.angularDamping = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            WorldItem item;
            switch (kind)
            {
                case ItemKind.Firewood: item = BuildFirewood(go, rb); break;
                case ItemKind.Fish: item = BuildFish(go, rb); break;
                case ItemKind.Can: item = BuildCan(go, rb); break;
                case ItemKind.AmmoBox: item = BuildAmmoBox(go, rb); break;
                case ItemKind.Bullet: item = BuildBullet(go, rb); break;
                case ItemKind.Rifle: item = BuildRifle(go, rb); break;
                case ItemKind.FireSteel: item = BuildFireSteel(go, rb); break;
                case ItemKind.Compass: item = BuildCompass(go, rb); break;
                default: item = Finish<WorldItem>(go); break;
            }
            item.kind = kind;
            return item;
        }

        // Colliders first, then the grab interactable (it collects colliders on Awake), then the item script.
        static T Finish<T>(GameObject go, bool dynamicAttach = true, Transform attach = null) where T : WorldItem
        {
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = dynamicAttach;
            if (attach != null)
                grab.attachTransform = attach;
            grab.throwOnDetach = true;
            return go.AddComponent<T>();
        }

        WorldItem BuildFirewood(GameObject go, Rigidbody rb)
        {
            rb.mass = 1.5f;
            var log = m_Assets.firewood;
            if (log != null && log.Valid && m_Assets.packMaterial != null)
            {
                // The pack's chopped log, scaled so its longest side is 0.6 m and centred on the collider (long axis = X).
                var visual = log.Spawn(go.transform, m_Assets.packMaterial).transform;
                var size = log.Size;
                var longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                var s = 0.6f / Mathf.Max(0.01f, longest);
                if (size.z >= size.x && size.z >= size.y)
                    visual.localRotation = Quaternion.Euler(0f, 90f, 0f);
                else if (size.y >= size.x && size.y >= size.z)
                    visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.localScale = Vector3.one * s;
                visual.localPosition = -(visual.localRotation * ((log.boundsMin + log.boundsMax) * 0.5f * s));
            }
            else
            {
                Meshes.Part(go.transform, PrimitiveType.Cylinder, m_Assets.wood, Vector3.zero, new Vector3(0.12f, 0.3f, 0.12f), new Vector3(0f, 0f, 90f));
            }
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 0;
            col.radius = 0.06f;
            col.height = 0.6f;
            return Finish<Firewood>(go);
        }

        WorldItem BuildFish(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.6f;
            Meshes.Part(go.transform, PrimitiveType.Capsule, m_Assets.fish, Vector3.zero, new Vector3(0.07f, 0.13f, 0.04f), new Vector3(0f, 0f, 90f));
            Meshes.Part(go.transform, PrimitiveType.Cube, m_Assets.fish, new Vector3(-0.15f, 0f, 0f), new Vector3(0.05f, 0.07f, 0.01f), new Vector3(0f, 0f, 45f));
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(0.32f, 0.08f, 0.05f);
            return Finish<Edible>(go);
        }

        WorldItem BuildCan(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.4f;
            Meshes.Part(go.transform, PrimitiveType.Cylinder, m_Assets.can, Vector3.zero, new Vector3(0.08f, 0.05f, 0.08f));
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.04f;
            col.height = 0.1f;
            return Finish<Edible>(go);
        }

        WorldItem BuildAmmoBox(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.5f;
            Meshes.Part(go.transform, PrimitiveType.Cube, m_Assets.ammoBox, Vector3.zero, new Vector3(0.14f, 0.07f, 0.09f));
            go.AddComponent<BoxCollider>().size = new Vector3(0.14f, 0.07f, 0.09f);
            return Finish<AmmoBox>(go);
        }

        WorldItem BuildBullet(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.05f;
            Meshes.Part(go.transform, PrimitiveType.Cylinder, m_Assets.brass, Vector3.zero, new Vector3(0.02f, 0.03f, 0.02f), new Vector3(90f, 0f, 0f));
            Meshes.Part(go.transform, PrimitiveType.Sphere, m_Assets.darkMetal, new Vector3(0f, 0f, 0.03f), new Vector3(0.018f, 0.018f, 0.03f));
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = 0.015f;
            col.height = 0.08f;
            return Finish<Bullet>(go);
        }

        WorldItem BuildRifle(GameObject go, Rigidbody rb)
        {
            rb.mass = 3.5f;
            var t = go.transform;
            Meshes.Part(t, PrimitiveType.Cube, m_Assets.wood, new Vector3(0f, -0.03f, -0.3f), new Vector3(0.045f, 0.12f, 0.36f));
            Meshes.Part(t, PrimitiveType.Cube, m_Assets.darkMetal, new Vector3(0f, 0f, 0.05f), new Vector3(0.05f, 0.07f, 0.36f));
            Meshes.Part(t, PrimitiveType.Cube, m_Assets.wood, new Vector3(0f, -0.045f, 0.22f), new Vector3(0.05f, 0.04f, 0.3f));
            Meshes.Part(t, PrimitiveType.Cylinder, m_Assets.metal, new Vector3(0f, 0.015f, 0.5f), new Vector3(0.022f, 0.28f, 0.022f), new Vector3(90f, 0f, 0f));
            Meshes.Part(t, PrimitiveType.Cube, m_Assets.darkMetal, new Vector3(0.035f, 0.02f, 0.02f), new Vector3(0.02f, 0.02f, 0.06f)); // bolt handle

            var body = go.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, -0.02f, -0.05f);
            body.size = new Vector3(0.06f, 0.13f, 0.75f);
            var barrel = go.AddComponent<CapsuleCollider>();
            barrel.direction = 2;
            barrel.center = new Vector3(0f, 0.015f, 0.5f);
            barrel.radius = 0.015f;
            barrel.height = 0.56f;

            var grip = new GameObject("Grip").transform;
            grip.SetParent(t, false);
            grip.localPosition = new Vector3(0f, -0.04f, -0.08f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(t, false);
            muzzle.localPosition = new Vector3(0f, 0.015f, 0.82f);

            var flashGo = new GameObject("Muzzle Flash");
            flashGo.transform.SetParent(muzzle, false);
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.8f, 0.5f);
            flash.range = 8f;
            flash.intensity = 4f;
            flash.shadows = LightShadows.None;

            var rifle = Finish<Rifle>(go, false, grip);

            // Chamber socket on the right side of the action. Added after the grab interactable
            // so its trigger is not treated as part of the rifle's grab volume.
            var chamberGo = new GameObject("Chamber");
            chamberGo.transform.SetParent(t, false);
            chamberGo.transform.localPosition = new Vector3(0.06f, 0.03f, 0.08f);
            var chamberCol = chamberGo.AddComponent<SphereCollider>();
            chamberCol.isTrigger = true;
            chamberCol.radius = 0.07f;
            var chamber = chamberGo.AddComponent<TypedSocket>();
            chamber.showInteractableHoverMeshes = false;

            rifle.muzzle = muzzle;
            rifle.chamber = chamber;
            rifle.flash = flash;
            return rifle;
        }

        WorldItem BuildFireSteel(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.2f;
            Meshes.Part(go.transform, PrimitiveType.Cube, m_Assets.wood, Vector3.zero, new Vector3(0.03f, 0.08f, 0.03f));
            Meshes.Part(go.transform, PrimitiveType.Cylinder, m_Assets.darkMetal, new Vector3(0f, 0.1f, 0f), new Vector3(0.015f, 0.06f, 0.015f));
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.04f, 0f);
            col.radius = 0.02f;
            col.height = 0.2f;
            var tip = new GameObject("Tip").transform;
            tip.SetParent(go.transform, false);
            tip.localPosition = new Vector3(0f, 0.16f, 0f);
            var steel = Finish<FireSteel>(go);
            steel.tip = tip;
            return steel;
        }

        WorldItem BuildCompass(GameObject go, Rigidbody rb)
        {
            rb.mass = 0.2f;
            Meshes.Part(go.transform, PrimitiveType.Cylinder, m_Assets.compassFace, Vector3.zero, new Vector3(0.09f, 0.008f, 0.09f));
            var needleRoot = new GameObject("Needle").transform;
            needleRoot.SetParent(go.transform, false);
            needleRoot.localPosition = new Vector3(0f, 0.01f, 0f);
            Meshes.Part(needleRoot, PrimitiveType.Cube, m_Assets.needle, new Vector3(0f, 0f, 0.018f), new Vector3(0.008f, 0.004f, 0.036f));
            Meshes.Part(needleRoot, PrimitiveType.Cube, m_Assets.metal, new Vector3(0f, 0f, -0.018f), new Vector3(0.008f, 0.004f, 0.036f));
            go.AddComponent<BoxCollider>().size = new Vector3(0.09f, 0.02f, 0.09f);
            var compass = Finish<Compass>(go);
            compass.needle = needleRoot;
            return compass;
        }
    }
}
