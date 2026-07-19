using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace Wedge.Client.Patches
{
    // The grid icon and the inspect preview render the same pooled container, but only the icon
    // pipeline reads PreviewPivot.Icon.overrideIcon — off the pooled root, after its camera pass. A
    // sprite planted during OnEnable is therefore guaranteed to be picked up by every grid render and
    // ignored by inspect, which is exactly the split we want: the engine's icon render of this
    // container has come out wrong in raid while inspect stays correct.
    //
    // Same seam as the gas repair: the icon path SetActives the pooled object for its single frame,
    // and OnEnable runs inside that call, before the sprite is read.
    internal class GrenadeIconPatch : ModulePatch
    {
        static Sprite _icon;
        static bool _loaded;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeaponPrefab), nameof(WeaponPrefab.OnEnable));
        }

        [PatchPostfix]
        static void Postfix(WeaponPrefab __instance)
        {
            var prefab = __instance as GrenadePrefab;

            // ThrowingParts is serialized in the bundle, so it identifies his grenade no matter what
            // name the pool hangs on the root.
            if (prefab == null || prefab.ThrowingParts == null || prefab.ThrowingParts.Length == 0 ||
                prefab.ThrowingParts[0] != "weapon_grenade_m8230") return;

            var pivot = prefab.GetComponent<PreviewPivot>();
            if (pivot == null || pivot.Icon == null || pivot.Icon.overrideIcon != null) return;

            pivot.Icon.overrideIcon = Icon();
        }

        static Sprite Icon()
        {
            if (_loaded) return _icon;
            _loaded = true;

            using (var stream = typeof(GrenadeIconPatch).Assembly.GetManifestResourceStream("Wedge.Client.m8230.png"))
            {
                if (stream == null)
                {
                    WedgePlugin.Log.LogWarning("[Wedge] m8230 icon resource missing - grid icon stays engine-rendered");
                    return null;
                }

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    WedgePlugin.Log.LogWarning("[Wedge] m8230 icon failed to decode - grid icon stays engine-rendered");
                    return null;
                }

                tex.name = "m8230_icon";
                _icon = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return _icon;
            }
        }
    }
}
