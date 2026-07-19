using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Wedge.Client
{
    [BepInPlugin(PluginId, "Wedge", "2.0.0")]
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
        internal static ConfigEntry<bool> CommandsEnable;
        internal static ConfigEntry<int> CommandPriority;
        internal static ConfigEntry<float> AckChance;
        internal static ConfigEntry<bool> CommandDebug;
        internal static ConfigEntry<string> GasMaskTpls;
        internal static ConfigEntry<string> GasRespiratorTpls;
        internal static ConfigEntry<bool> GasMaskCheckHeadwear;
        internal static ConfigEntry<bool> GasEnable;
        internal static ConfigEntry<string> GasGrenadeTpl;
        internal static ConfigEntry<float> GasDamage;
        internal static ConfigEntry<float> GasBlur;
        internal static ConfigEntry<float> GasTunnel;
        internal static ConfigEntry<float> GasBuildTime;
        internal static ConfigEntry<float> GasLingerTime;
        internal static ConfigEntry<float> GasTickInterval;
        internal static ConfigEntry<bool> GasDebug;

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

            CommandsEnable = Config.Bind("3. Command", "Squad Commands", true,
                "Wedge calls orders and his guards act on them — cover, hold, advance, look.");
            CommandPriority = Config.Bind("3. Command", "Layer Priority", 26,
                new ConfigDescription("BigBrain priority for the guard command layer. Has to sit above SAIN's combat layers (20/22/24 by default) or cover and hold calls can't cut into a firefight. Advanced.",
                    new AcceptableValueRange<int>(1, 60)));
            AckChance = Config.Bind("3. Command", "Acknowledge Chance", 0.6f,
                new ConfigDescription("How often a guard answers an order out loud.",
                    new AcceptableValueRange<float>(0f, 1f)));
            CommandDebug = Config.Bind("3. Command", "Log Orders", false,
                "Write every issued order to the BepInEx log.");

            GasEnable = Config.Bind("4. Gas", "CS Gas", true,
                "His Model 8230 leaves a cloud that burns anyone caught in it without a filter.");
            GasGrenadeTpl = Config.Bind("4. Gas", "Gas Grenade Id", "6a5bea01f2c4d9081b3a7e64",
                "Item id of his gas grenade. Only clouds from this item are harmful — ordinary smoke stays harmless.");
            GasDamage = Config.Bind("4. Gas", "Damage Per Second", 3.5f,
                new ConfigDescription("Health per second drained while you stand in the cloud unprotected.",
                    new AcceptableValueRange<float>(0f, 25f)));
            GasBlur = Config.Bind("4. Gas", "Blur Strength", 1.5f,
                new ConfigDescription("How hard the gas stings your eyes at full exposure. Zero leaves vision alone. Local player only — the engine binds screen effects to your own camera.",
                    new AcceptableValueRange<float>(0f, 2f)));
            GasTunnel = Config.Bind("4. Gas", "Tunnel Vision", 0.7f,
                new ConfigDescription("How far the edges of your vision close in at full exposure. This is most of the 'nearly unplayable' at a full dose.",
                    new AcceptableValueRange<float>(0f, 1f)));
            GasBuildTime = Config.Bind("4. Gas", "Effect Build-up (s)", 3.5f,
                new ConfigDescription("Seconds in the cloud before the sting reaches full strength.",
                    new AcceptableValueRange<float>(0.5f, 15f)));
            GasLingerTime = Config.Bind("4. Gas", "Effect Linger (s)", 30f,
                new ConfigDescription("Seconds a full dose takes to wear off once you're clear of the gas. A lighter dose clears proportionally faster.",
                    new AcceptableValueRange<float>(1f, 120f)));
            GasTickInterval = Config.Bind("4. Gas", "Tick Interval", 0.5f,
                new ConfigDescription("Seconds between gas checks. Lower is smoother and costs more. Advanced.",
                    new AcceptableValueRange<float>(0.1f, 2f)));

            // Nothing on a face cover marks it as filtering gas, so protection is two template lists.
            // His own Avon M53A1 leads the full-face one — leave it out and he gasses himself.
            GasMaskTpls = Config.Bind("4. Gas", "Protective Face Covers",
                "689b880fff8b4adc420f5b56,5b432c305acfc40019478128,60363c0c92ec1c31037959f5",
                "Comma-separated item ids of full-face masks that block his gas completely. Defaults are the Avon M53A1, GP-5 and GP-7.");
            GasRespiratorTpls = Config.Bind("4. Gas", "Respirators",
                "689b404db49f27df1c0873f6,59e7715586f7742ee5789605",
                "Comma-separated item ids of half masks that keep the gas out of your lungs but leave your eyes stinging. Defaults are the Ops-Core SOTR and the 3M respirator.");
            GasMaskCheckHeadwear = Config.Bind("4. Gas", "Also Check Headwear", false,
                "Count a sealed helmet in the headwear slot (a Devtac Ronin, for instance) as full protection too.");
            GasDebug = Config.Bind("4. Gas", "Log Gas", false,
                "Write every cloud he lays down to the BepInEx log.");

            FikaBridge.Init();
            new Patches.BotsControllerInitPatch().Enable();
            new Patches.GrenadeIconPatch().Enable();
            new Gas.GasPrefabFixup().Enable();
            new Gas.GasDetonationPatch().Enable();

            Log.LogInfo("Wedge client loaded");
        }

        void Update()
        {
            // The gas runs on every machine — each client owns the cloud it can see, and a peer's
            // lungs are only that peer's client to burn. The commander is the opposite: one brain,
            // host only, or every peer issues a competing set of orders.
            Gas.GasCloud.Tick();

            if (!FikaBridge.IsHost()) return;
            Brain.WedgeCommander.Tick();
        }
    }
}
