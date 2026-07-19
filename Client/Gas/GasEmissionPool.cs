using System.Collections.Generic;
using Comfort.Common;
using HarmonyLib;
using Systems.Effects;
using UnityEngine;

namespace Wedge.Client.Gas
{
    // The engine picks a grenade's cloud by name: EmmisionEffect is computed from the settings object's
    // GameObject and looked up in Effects.EmissionEffects, which knows two keys in this build. 1.0.5
    // has a third — m8230_emission_gas_grenade — and it turns out to be the stock white-smoke rig with
    // one child removed and four material values changed. That we can rebuild: clone RDG-2's template,
    // apply his numbers to the clone's own material instances (nothing shared is touched, so vanilla
    // smokes stay vanilla), and register it under his grenade's real name.
    internal static class GasEmissionPool
    {
        const string SourceKey = "weapon_rdg2_world";
        const string Key = "weapon_m8230_world";
        const string CloudShader = "Particles/Smoke Distorted Custom Billboard";

        // The Effects singleton dies with the raid, and the registration with it.
        static Effects _registeredOn;

        public static bool EnsureRegistered()
        {
            var fx = Singleton<Effects>.Instance;
            if (fx == null) return false;
            if (ReferenceEquals(_registeredOn, fx)) return true;

            var pool = fx.EmissionEffects;
            if (pool == null) return false;

            Effects.EmissionEffect source = null;
            foreach (var entry in pool)
            {
                if (entry == null) continue;
                if (entry.Key == Key)
                {
                    // Someone (a future SPT?) already ships his cloud — use theirs.
                    _registeredOn = fx;
                    return true;
                }
                if (entry.Key == SourceKey) source = entry;
            }

            if (source?.Instance == null)
            {
                WedgePlugin.Log.LogWarning("[Wedge] no RDG-2 cloud template to build his gas from");
                return false;
            }

            var template = Object.Instantiate(source.Instance, fx.transform);
            template.gameObject.SetActive(false);
            template.gameObject.name = "m8230_emission_gas_grenade";

            // His rig has no volume system — the cloud is all billboard flow. 1.0.5 pruned the emission
            // arrays along with the child, and that pruning is load-bearing: the volume is the ONLY
            // entry in _crucialSystems, StartEmission and Precaution walk these arrays with no null
            // checks, and Instantiate turns references to destroyed objects into nulls — so an unpruned
            // template NREs the first time a clone detonates.
            var volume = FindDeep(template.transform, "Effect Smoke Volume");
            if (volume != null)
            {
                var crucial = AccessTools.Field(typeof(GrenadeEmission), "_crucialSystems");
                var renderers = AccessTools.Field(typeof(GrenadeEmission), "_particleSystemRenderers");
                if (crucial == null || renderers == null)
                {
                    // Field names drifted — a template with dead array entries is worse than the stock
                    // look, so keep the volume and ship RDG-2's rig with his colours.
                    WedgePlugin.Log.LogWarning("[Wedge] emission fields moved - keeping the volume system");
                }
                else
                {
                    crucial.SetValue(template, new ParticleSystem[0]);

                    var kept = new List<ParticleSystemRenderer>();
                    foreach (var renderer in (ParticleSystemRenderer[])renderers.GetValue(template))
                    {
                        if (renderer != null && !renderer.transform.IsChildOf(volume)) kept.Add(renderer);
                    }
                    renderers.SetValue(template, kept.ToArray());

                    Object.Destroy(volume.gameObject);
                }
            }

            Retint(template);

            fx.EmissionEffects = Append(pool, new Effects.EmissionEffect
            {
                Key = Key,
                Instance = template,
                // GetEffect reads Cache.Count before the null check in InstantiateNewEffect can save it,
                // so an entry without a list NREs on first use.
                Cache = new List<GrenadeEmission>(),
            });

            _registeredOn = fx;
            WedgePlugin.Log.LogInfo("[Wedge] gas cloud effect registered");
            return true;
        }

        // His material vs the stock one differs in exactly these values (read out of 1.0.5's effects
        // bundle): a pale blue-white at 0.82 alpha, brighter ambient, a touch less distortion. Applied
        // to the clone's own material instances; the shader-name gate keeps the ignition sparks (same
        // renderer family, different shader) untouched.
        static void Retint(GrenadeEmission template)
        {
            var tint = new Color(0.9176f, 0.997f, 1f, 0.8196f);

            foreach (var renderer in template.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var touches = false;
                foreach (var mat in shared)
                {
                    if (mat != null && mat.shader != null && mat.shader.name == CloudShader) touches = true;
                }
                if (!touches) continue;

                foreach (var mat in renderer.materials)
                {
                    if (mat == null || mat.shader == null || mat.shader.name != CloudShader) continue;

                    mat.SetColor("_TintColor", tint);
                    if (mat.HasProperty("_AddAmbient")) mat.SetFloat("_AddAmbient", 0.8f);
                    if (mat.HasProperty("_Ambient")) mat.SetFloat("_Ambient", 0.1f);
                    if (mat.HasProperty("_Distortion")) mat.SetFloat("_Distortion", 0.1f);
                }
            }
        }

        static Effects.EmissionEffect[] Append(Effects.EmissionEffect[] pool, Effects.EmissionEffect entry)
        {
            var grown = new Effects.EmissionEffect[pool.Length + 1];
            pool.CopyTo(grown, 0);
            grown[pool.Length] = entry;
            return grown;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
