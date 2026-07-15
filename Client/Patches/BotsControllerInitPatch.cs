using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace Wedge.Client.Patches
{
    // Register the brain layer once SAIN and the bot system are up. Host only — clients replicate
    // the host's bots and must not run a second brain.
    internal class BotsControllerInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(BotsController).GetMethod(nameof(BotsController.Init), BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPostfix]
        static void Postfix()
        {
            if (!FikaBridge.IsHost()) return;
            if (!WedgePlugin.MasterEnable.Value) return;
            Brain.LayerRegistration.EnsureRegistered();
        }
    }
}
