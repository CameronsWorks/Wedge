using System.Collections.Generic;
using BepInEx.Logging;
using Mono.Cecil;
using MoreBotsAPI;

namespace Wedge.Prepatch
{
    // BepInEx preloader patcher, discovered by its TargetDLLs + Patch members. It pins two
    // custom EFT.WildSpawnType members — wedge (848430) and wedgeguard (848431) — into
    // Assembly-CSharp so the client accepts them; the server mod supplies the matching bot
    // data and faction. RegisterWildSpawnType injects each value on the spot, so this is
    // self-contained and order-independent among the MoreBots patchers. Mirrors Black Division.
    public static class WildSpawnTypePatch
    {
        public const int WedgeType = 848430;
        public const int WedgeGuardType = 848431;

        // 9 == EFT.WildSpawnType.pmcBot: the base brain the roles inherit. SAIN's PMC layers
        // (below) drive the actual combat; this is just the vanilla fallback.
        const int PmcBrain = 9;

        static readonly List<string> BrainsToApply = new List<string> { "PMC", "ExUsec" };
        static readonly List<string> LayersToRemove = new List<string>
        {
            "Request", "KnightFight", "PmcBear", "PmcUsec", "ExURequest", "StationaryWS"
        };

        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Patch(ref AssemblyDefinition assembly)
        {
            var log = Logger.CreateLogSource("Wedge Prepatch");

            var wedge = new CustomWildSpawnType(
                WedgeType, "wedge", "Wedge", PmcBrain,
                isBoss: true, isFollower: false, isHostileToEverybody: false);
            wedge.SetCountAsBossForStatistics(true);
            wedge.SetSAINSettings(new SAINSettings(wedge.WildSpawnTypeValue)
            {
                Name = "Wedge",
                Description = "Wedge, leader of the Black Division.",
                Section = "Wedge",
                BaseBrain = "PMC",
                BrainsToApply = BrainsToApply,
                LayersToRemove = LayersToRemove,
            });
            CustomWildSpawnTypeManager.RegisterWildSpawnType(wedge, assembly);

            // isBoss AND isFollower both true = the raider/rogue self-grouping pattern BlackDiv
            // uses on every unit. isFollower alone leaves the guard without the MoreBots brain
            // swap (it never gets "Changing brain for custom bot wedgeguard") and it stands inert;
            // isBoss is what routes it through the brain assignment.
            var guard = new CustomWildSpawnType(
                WedgeGuardType, "wedgeguard", "WedgeGuard", PmcBrain,
                isBoss: true, isFollower: true, isHostileToEverybody: false);
            guard.SetCountAsBossForStatistics(false);
            guard.SetSAINSettings(new SAINSettings(guard.WildSpawnTypeValue)
            {
                Name = "Wedge Guard",
                Description = "Black Division soldier escorting Wedge.",
                Section = "Wedge",
                BaseBrain = "PMC",
                BrainsToApply = BrainsToApply,
                LayersToRemove = LayersToRemove,
            });
            CustomWildSpawnTypeManager.RegisterWildSpawnType(guard, assembly);

            CustomWildSpawnTypeManager.AddSuitableGroup(new List<int> { WedgeType, WedgeGuardType });

            log.LogInfo($"pinned wedge={WedgeType}, wedgeguard={WedgeGuardType} into EFT.WildSpawnType");
        }
    }
}
