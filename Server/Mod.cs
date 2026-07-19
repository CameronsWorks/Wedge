using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using MoreBotsServer;
using MoreBotsServer.Services;

namespace Wedge.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.sipto.wedge";
    public override string Name { get; init; } = "Wedge";
    public override string Author { get; init; } = "Sipto";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(2, 0, 0);
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.morebotsapi.tacticaltoaster"] = new(">=2.0.0"),
        ["com.wtt.commonlib"] = new(">=2.0.0"),
        // His guards wear Black Division kit, and the tpls for it (LV-119, Avon M53A1, PVS-31A, the
        // BD clothing suites) are runtime-injected by these two rather than sitting in the static DB.
        ["com.wtt.contentbackport"] = new(">=1.0.7"),
        ["com.blackdiv.tacticaltoaster"] = new(">=1.1.3"),
    };
    public override string? Url { get; init; }
    // Gates LoadBundlesAsync — left unset, the server skips bundles.json without logging anything.
    public override bool? IsBundleMod { get; init; } = true;
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
    WTTServerCommonLib.WTTServerCommonLib commonLib,
    Services.KitVerifier kitVerifier,
    ConfigServer configServer,
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

        // db\CustomLocales holds the ScavRole/* names the raid-end screen prints for the two roles.
        // MoreBots has no locale loader of its own — the folder is a WTT convention and stays inert
        // without this call, leaving the screen to render the raw key ("SCAVROLE/WEDGE").
        await commonLib.CustomLocaleService.CreateCustomLocales(asm);

        // His 1.0.5 art. Customization covers head/body/feet, the voice service registers the phrase
        // bank and its bundle, and the item service adds his kit items. Each reads db\<its folder>
        // from this assembly's directory.
        await commonLib.CustomCustomizationService.CreateCustomCustomizations(asm);
        await commonLib.CustomVoiceService.CreateCustomVoices(asm);
        await commonLib.CustomItemServiceExtended.CreateCustomItems(asm);

        // db\CustomHeads carries a second, PMC-side copy of his face so players can pick it in
        // character creation; his own entry above stays Savage-only and untouched.
        await commonLib.CustomHeadService.CreateCustomHeads(asm);

        // His helmet went missing in raid with a clean boot log, so confirm the end state rather than
        // trusting registration: the item exists, kept the EXFIL's slots, and is equippable.
        kitVerifier.Verify("6a5be99e3a129fec056b645b", "Headwear", "mod_nvg", "mod_equipment_000");

        // The NVG only exists if every link generates: helmet accepts the Wilcox mount (inherited EXFIL
        // filter says no — widen it), and the mount accepts the PVS-31A (WTT's item — verify only).
        kitVerifier.EnsureSlotAccepts("6a5be99e3a129fec056b645b", "mod_nvg", "689dbded6c7e684817080c29");
        kitVerifier.VerifySlotAccepts("689dbded6c7e684817080c29", "mod_nvg", "689b889473ebd6871805edd6");

        // Roles the equipment config doesn't know fall back to a 90% night / 15% day roll for whether
        // NVGs spawn flipped down. The leader of a night-fighting faction doesn't dice for his goggles.
        // ConfigServer is the 4.0 way in; direct config injection is 4.1 and resolves to nothing here.
#pragma warning disable CS0618
        var botConfig = configServer.GetConfig<BotConfig>();
#pragma warning restore CS0618
        foreach (var role in new[] { "wedge", "wedgeguard" })
        {
            if (botConfig.Equipment.ContainsKey(role)) continue;
            botConfig.Equipment[role] = new EquipmentFilters
            {
                NvgIsActiveChanceNightPercent = 100,
                NvgIsActiveChanceDayPercent = 0,
            };
        }

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
