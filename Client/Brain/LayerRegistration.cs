using System.Collections.Generic;
using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace Wedge.Client.Brain
{
    internal static class LayerRegistration
    {
        static bool _done;

        public static void EnsureRegistered()
        {
            if (_done) return;
            _done = true;

            // Wedge and his guards inherit the pmcBot base brain (named "PMC" in BigBrain), so the
            // rush layer attaches to PMC-brained bots but is restricted to our two custom types.
            var types = new List<WildSpawnType>
            {
                (WildSpawnType)WedgePlugin.WedgeType,
                (WildSpawnType)WedgePlugin.WedgeGuardType,
            };

            BrainManager.AddCustomLayer(typeof(WedgeRushLayer),
                new List<string> { "PMC" }, WedgePlugin.BrainPriority.Value, types);

            WedgePlugin.Log.LogInfo($"[Wedge] rush layer registered at priority {WedgePlugin.BrainPriority.Value}");
        }
    }
}
