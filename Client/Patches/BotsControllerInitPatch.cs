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
            // Ahead of the host gate: the gas burns on every machine, so last raid's clouds have to be
            // dropped on peers too. Tick() also clears itself once the game world goes away, which
            // covers any client where this never runs.
            Gas.GasCloud.Reset();

            if (!FikaBridge.IsHost()) return;
            if (!WedgePlugin.MasterEnable.Value) return;

            // Runs per raid, so clear the commander before re-registering or last raid's dead
            // BotOwners and group ids leak into this one.
            Brain.WedgeCommander.Reset();
            Brain.LayerRegistration.EnsureRegistered();
        }
    }
}
