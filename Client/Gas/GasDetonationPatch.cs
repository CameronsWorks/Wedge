using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Wedge.Client.Gas
{
    // Deliberately not host-gated. ObservedSmokeGrenade declares no override of OnExplosion and doesn't
    // wait to be told when to blow, so this fires on every machine and each one registers its own copy
    // of the cloud. Gating it to the host, the way the squad-command patches are gated, would leave
    // co-op peers wandering through gas that can't touch them.
    internal class GasDetonationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SmokeGrenade), nameof(SmokeGrenade.OnExplosion));
        }

        [PatchPostfix]
        static void Postfix(SmokeGrenade __instance)
        {
            if (!WedgePlugin.MasterEnable.Value || !WedgePlugin.GasEnable.Value) return;

            // Ordinary RDG-2s reach this too, and they should stay ordinary smoke.
            var source = __instance.WeaponSource;
            if (source == null) return;
            if (!string.Equals(source.StringTemplateId, WedgePlugin.GasGrenadeTpl.Value,
                    StringComparison.OrdinalIgnoreCase)) return;

            GasCloud.Register(__instance);
        }
    }
}
