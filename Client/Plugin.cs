using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Wedge.Client
{
    [BepInPlugin(PluginId, "Wedge", "0.1.0")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("xyz.drakia.bigbrain")]
    public class WedgePlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.sipto.wedge.client";

        // Must match the prepatcher + server dict. Pinned into EFT.WildSpawnType at preload.
        public const int WedgeType = 848430;
        public const int WedgeGuardType = 848431;

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> MasterEnable;
        internal static ConfigEntry<bool> BrainEnable;
        internal static ConfigEntry<bool> GrenadeEnable;
        internal static ConfigEntry<float> RushRadius;
        internal static ConfigEntry<int> BrainPriority;

        void Awake()
        {
            Log = Logger;

            MasterEnable = Config.Bind("1. General", "Enable", true,
                "Master switch for Wedge's client-side behaviour.");
            BrainEnable = Config.Bind("2. Brain", "Aggressive Rush", true,
                "Wedge and his guards push toward the nearest player when not already in a SAIN firefight.");
            GrenadeEnable = Config.Bind("2. Brain", "Grenade Barrage", true,
                "Wedge lobs flash/smoke grenades at the player on approach (throttled by the engine's own throw cooldown).");
            RushRadius = Config.Bind("2. Brain", "Rush Radius (m)", 125f,
                new ConfigDescription("Only hunt players within this range, so Wedge doesn't sprint the whole map from spawn.",
                    new AcceptableValueRange<float>(20f, 300f)));
            BrainPriority = Config.Bind("2. Brain", "Layer Priority", 12,
                new ConfigDescription("BigBrain priority for the rush layer. Must stay below SAIN combat (20+). Advanced.",
                    new AcceptableValueRange<int>(1, 19)));

            FikaBridge.Init();
            new Patches.BotsControllerInitPatch().Enable();

            Log.LogInfo("Wedge client loaded");
        }
    }
}
