using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using MoreBotsServer;
using MoreBotsServer.Services;

namespace Wedge.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.sipto.wedge";
    public override string Name { get; init; } = "Wedge";
    public override string Author { get; init; } = "Sipto";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(0, 1, 1);
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

// Registers the two custom roles and their faction. Runs last (int.MaxValue) so the
// built-in sides (savage/usec/bear) MoreBots seeds are already present when we wire the
// reciprocal hostility below — the reverse AddEnemyByFaction("side","wedge") looks "wedge"
// up in FactionService.Factions, so the faction is created before that call.
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = int.MaxValue)]
public class WedgeRegistration(
    MoreBotsAPI moreBotsLib,
    MoreBotsCustomBotTypeService customBotTypeService,
    FactionService factionService,
    ISptLogger<WedgeRegistration> logger
) : IOnLoad
{
    public const int WedgeType = 848430;
    public const int WedgeGuardType = 848431;

    public async Task OnLoad()
    {
        var asm = Assembly.GetExecutingAssembly();

        // Boss and guard are distinct templates, so load each shared file on its own name.
        await moreBotsLib.LoadBotsShared(asm, "wedge", ["wedge"]);
        await moreBotsLib.LoadBotsShared(asm, "wedgeguard", ["wedgeguard"]);

        customBotTypeService.AddCustomWildSpawnTypeNames(new Dictionary<int, string>
        {
            { WedgeType, "wedge" },
            { WedgeGuardType, "wedgeguard" },
        });

        var faction = new Faction { Name = "wedge" };
        faction.BotTypes.Add((WildSpawnType)WedgeType);
        faction.BotTypes.Add((WildSpawnType)WedgeGuardType);
        faction.RevengeAfterRaids = false;
        factionService.Factions["wedge"] = faction;

        List<string> roles = ["wedge", "wedgeguard"];
        foreach (var side in new[] { "savage", "usec", "bear" })
        {
            factionService.AddEnemyByFaction(roles, side);
            factionService.AddEnemyByFaction(side, "wedge");
        }

        logger.Info($"[Wedge] registered roles {WedgeType}/{WedgeGuardType} under faction 'wedge'");
    }
}
