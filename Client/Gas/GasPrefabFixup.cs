using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Wedge.Client.Gas
{
    // His M8230 came out of 1.0.5 carrying a TearGasGrenadeSettings component, and that script has no
    // counterpart in 0.16.9. The reference survives in the bundle but resolves to nothing, so
    // GrenadePrefab.GrenadeItself reads null and the grenade throws an NRE the moment a pin leaves it.
    // Building the settings the client does understand — SmokeGrenadeSettings — hands the factory
    // something it can dispatch on, since GrenadeFactoryClass.Create branches purely on the runtime type.
    //
    // WeaponPrefab.OnEnable is the seam. The obvious targets are both wrong: vmethod_0 only covers the
    // cook, leaving the actual throw to NRE, and smethod_8 is an open generic with no closed
    // instantiation anywhere on its caller chain.
    internal class GasPrefabFixup : ModulePatch
    {
        const string WorldObject = "weapon_m8230_world";

        // GrenadeSettings.EmmisionEffect is computed from the GameObject's name. With his own cloud in
        // the pool (GasEmissionPool) the real name resolves; if that registration ever fails, renaming
        // to RDG-2's key keeps the grenade working with the stock white cloud instead of an NRE.
        const string EmissionKey = "weapon_rdg2_world";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeaponPrefab), nameof(WeaponPrefab.OnEnable));
        }

        [PatchPostfix]
        static void Postfix(WeaponPrefab __instance)
        {
            var prefab = __instance as GrenadePrefab;
            if (prefab == null || prefab.GrenadeItself != null) return;

            // The settings do NOT live under the container. Both are separate roots in the bundle,
            // joined only by the GrenadeItself reference — which is the one thing that didn't survive
            // the port, so there is no live reference to follow. Vanilla grenades are laid out the same
            // way, so searching the container's own hierarchy could never have worked.
            var world = ResolveWorld();
            if (world == null)
            {
                // Should be unreachable — the world object sits in the container prefab's preload
                // table, so it loads whenever the container does. If it happens anyway, borrow the
                // vanilla RDG-2's settings so the grenade still works (the thrown object clones the
                // GameObject the settings sit on, so it flies as an RDG-2 — wrong model, no crash),
                // and log the bundle's residency so the miss is diagnosable.
                ProbeBundle();
                var fallback = ResolveRdg2Settings();
                if (fallback == null)
                {
                    WedgePlugin.Log.LogError($"[Wedge] can't find {WorldObject} or a fallback - his gas grenade will throw on use");
                    return;
                }

                prefab.GrenadeItself = fallback;
                WedgePlugin.Log.LogError($"[Wedge] can't find {WorldObject} - falling back to the RDG-2 model");
                return;
            }

            var area = world.Find("Area");
            var sphere = area != null ? area.GetComponent<SphereCollider>() : null;
            if (sphere == null)
            {
                WedgePlugin.Log.LogError("[Wedge] gas prefab has no Area collider - his grenade will throw");
                return;
            }

            // WeaponPrefab is pooled, so OnEnable runs on every acquisition. Against a shared bundle
            // asset an unguarded AddComponent would stack a fresh copy each time.
            var settings = world.GetComponent<SmokeGrenadeSettings>();
            if (settings != null)
            {
                prefab.GrenadeItself = settings;
                SetEmissionName(world);
                return;
            }

            settings = world.gameObject.AddComponent<SmokeGrenadeSettings>();

            // AttachTo applies this as position + rotation * Offset in the thrown body's frame, so it
            // has to be the vent rather than the origin. RDG-2's serialized value — the two grenades'
            // vents sit within a couple of millimetres of each other, and the emission values around it
            // are all RDG-2's too.
            settings.Offset = new Vector3(-0.0039151f, -0.00015125f, 0.0707f);

            // The M8230's own values, read out of its dead TearGas component with UnityPy — the
            // typetree is gone at runtime, so they can't be read here.
            settings.VelocityTreshold = 0.05f;
            settings.CollisionSound = GrenadeSettings.CollisionSounds.smoke;
            settings.Skoba = null;
            settings._torque = new Vector3(-220f, -220f, 0f);
            settings._torqueDelta = 0.8f;

            settings._emissionArea = sphere;
            settings._sizeOverTime = Rdg2SizeCurve();

            // OnValidate derives these two in the editor and never runs at runtime, so do it by hand.
            settings._initialRadius = sphere.radius;
            settings._pivot = area.localPosition;

            // The constructor seeds 1f here where RDG-2 ships 6.25 — inheriting it would give a cloud
            // roughly a sixth of the intended radius. (_areaStartPosNorm's seed of 0.5 already matches.)
            settings._radiusMultiplier = 6.25f;

            prefab.GrenadeItself = settings;
            SetEmissionName(world);

            WedgePlugin.Log.LogInfo("[Wedge] gas grenade prefab repaired");
        }

        // Re-evaluated every acquisition, both directions: the pool registration dies with each raid,
        // and the asset's name persists across raids — so a fallback rename in one raid must not stick
        // once his own cloud registers again in the next.
        static void SetEmissionName(Transform world)
        {
            var name = GasEmissionPool.EnsureRegistered() ? WorldObject : EmissionKey;
            if (world.gameObject.name != name) world.gameObject.name = name;
        }

        // RDG-2's own curve, lifted keyframe for keyframe. The cloud's radius and lifetime both read
        // from it, so an approximation would visibly change how the gas behaves.
        static AnimationCurve Rdg2SizeCurve()
        {
            const float inf = float.PositiveInfinity;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 1f, inf),
                new Keyframe(0.02681331f, 0.0017523868f, 1.0000169f, 6.4630527f),
                new Keyframe(0.1812328f, 0.99977362f, 6.4630527f, 0.0015882769f),
                new Keyframe(1.0024176f, 1.0010779f, 0.0015882769f, -6.5448208f),
                new Keyframe(1.1545157f, 0.0056225657f, -6.746592f, 0f),
                new Keyframe(1.2386328f, 0.0056225657f, 0f, inf))
            {
                preWrapMode = WrapMode.Loop,
                postWrapMode = WrapMode.Loop,
            };
            return curve;
        }

        static Transform _world;

        // Sweeps loaded assets rather than the scene: the object we want is a prefab root from the same
        // bundle, never instantiated on its own. Expensive, so it runs once and caches.
        //
        // A candidate under RDG-2's name is only accepted if it carries the M8230 body — a raid that
        // fell back to the rename leaves the asset under that name, and without the child check the
        // sweep would either miss our own object next raid or hand back the real RDG-2's.
        static Transform ResolveWorld()
        {
            if (_world != null) return _world;

            foreach (var candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate.parent != null) continue;
                if (candidate.gameObject.scene.IsValid()) continue;
                if (candidate.name != WorldObject &&
                    !(candidate.name == EmissionKey && candidate.Find("weapon_grenade_m8230") != null)) continue;

                _world = candidate;
                return _world;
            }

            return null;
        }

        // The vanilla RDG-2's own settings, straight off its loaded prefab asset. Read-only — assigning
        // an existing component to GrenadeItself mutates nothing on the vanilla grenade. The
        // _emissionArea check keeps a half-repaired object of ours from masquerading as it.
        static SmokeGrenadeSettings ResolveRdg2Settings()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate.name != EmissionKey) continue;
                if (candidate.parent != null) continue;
                if (candidate.gameObject.scene.IsValid()) continue;

                if (candidate.Find("weapon_grenade_m8230") != null) continue;

                var settings = candidate.GetComponent<SmokeGrenadeSettings>();
                if (settings != null && settings._emissionArea != null) return settings;
            }

            return null;
        }

        // Diagnosis only: says whether the container's bundle is even resident, so a failed sweep is
        // distinguishable from the bundle never having loaded.
        static void ProbeBundle()
        {
            const string containerAsset = "Assets/Content/Weapons/m8230/weapon_grenade_m8230_container.prefab";

            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null || !bundle.Contains(containerAsset)) continue;
                WedgePlugin.Log.LogWarning("[Wedge] m8230 bundle is resident but its world object wasn't found");
                return;
            }

            WedgePlugin.Log.LogWarning("[Wedge] m8230 bundle is not resident");
        }
    }
}
