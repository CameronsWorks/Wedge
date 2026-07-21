using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using MoreBotsServer;
using MoreBotsServer.Services;

namespace Wedge.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.sipto.wedge";
    public override string Name { get; init; } = "Wedge";
    public override string Author { get; init; } = "Sipto";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new(2, 1, 0);
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

// Registers the two custom roles/faction and all custom items. TypePriority sits in a window:
// after MoreBotsAPI (400005) seeds the built-in sides and WTT/BlackDiv add the tpls his kit
// borrows (400002/480085), but before SaveCallbacks (700000) loads saved profiles — the boot
// validator marks any profile invalid whose inventory holds a tpl it can't resolve, so late
// item registration (2.0.1 used int.MaxValue) bricked every profile that kept his gear.
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = 500000)]
public class WedgeRegistration(
    MoreBotsAPI moreBotsLib,
    MoreBotsCustomBotTypeService customBotTypeService,
    FactionService factionService,
    WTTServerCommonLib.WTTServerCommonLib commonLib,
    Services.KitVerifier kitVerifier,
    ConfigServer configServer,
    SaveServer saveServer,
    DatabaseService databaseService,
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

        // Profiles already in memory means we ran after SaveCallbacks and the window above is
        // broken — every saved profile holding Wedge gear just got flagged invalid.
        if (saveServer.GetProfiles().Count > 0)
            logger.Error("[Wedge] items registered after profiles loaded — TypePriority must stay below 700000");

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

        // Loot caps. The generator resolves both of these tables by role name and falls back to
        // "default" — whose spawn-limit entry is EMPTY, so nothing stopped a bot drawing the
        // cardholder case four times from the generic pools. One cardholder, one euro wad, and
        // the wedge stack table keeps that wad at 5k or under.
        Dictionary<string, double> euroStacks = new() { ["1000"] = 10, ["2500"] = 6, ["5000"] = 2 };
        foreach (var role in new[] { "wedge", "wedgeguard" })
        {
            botConfig.ItemSpawnLimits[role] = new()
            {
                // The cardholder is Wedge's alone; the guards get none at all.
                [new MongoId("619cbf9e0a7c3a1a2731940a")] = role == "wedge" ? 1 : 0, // keycard holder case
                [new MongoId("569668774bdc2da2298b4568")] = 1, // euros, one stack
            };
            botConfig.CurrencyStackSize[role] =
                new Dictionary<string, Dictionary<string, double>>(botConfig.CurrencyStackSize["default"])
                {
                    ["569668774bdc2da2298b4568"] = euroStacks,
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

        var botDb = databaseService.GetBots().Types;
        var us = new[] { (WildSpawnType)WedgeType, (WildSpawnType)WedgeGuardType };
        List<string> roles = ["wedge", "wedgeguard"];

        // Add our two types to one Mind list on a single db entry by name. Returns whether the entry
        // existed. This is the reverse direction the MoreBots API mishandles for CUSTOM members.
        bool SeedEntry(string dbName, bool friendly)
        {
            if (!botDb.TryGetValue(dbName, out var t) || t?.BotDifficulty is null) return false;
            foreach (var difficulty in t.BotDifficulty.Values)
            {
                var mind = difficulty.Mind;
                if (mind is null) continue;
                var list = friendly ? mind.FriendlyBotTypes ??= [] : mind.EnemyBotTypes ??= [];
                foreach (var ours in us)
                    if (!list.Contains(ours)) list.Add(ours);
            }
            return true;
        }

        // Walk a faction's members, resolving each custom int to its db key ourselves and seeding it —
        // the API can't (it doesn't lowercase custom names to match the key). Works only for mods that
        // published their int->name map via AddCustomWildSpawnTypeNames (Black Division, RUAF do).
        int SeedReverse(string factionName, bool friendly)
        {
            if (!factionService.Factions.TryGetValue(factionName, out var f)) return 0;
            var reached = 0;
            foreach (var member in f.GetAllBotTypes())
            {
                var name = (customBotTypeService.GetCustomTypeNameOrEmpty((int)member) ?? "").ToLowerInvariant();
                if (name.Length > 0 && SeedEntry(name, friendly)) reached++;
            }
            return reached;
        }

        // Hostile to everything that isn't Black Division. "savage" already folds in scavs, the scav
        // bosses and their guards, and raiders; usec/bear are the two PMC sides. rogues (exUsec plus
        // the Goons), cultists, the Partisan, and the infected sit outside savage, so name them too.
        // These are all vanilla-membered, so the string reverse overload resolves cleanly.
        foreach (var side in new[] { "savage", "usec", "bear", "rogues", "cultists", "partisan", "infected" })
        {
            factionService.AddEnemyByFaction(roles, side);
            factionService.AddEnemyByFaction(side, "wedge");
        }

        // The BTR is friendly — put its gunner (shooterBTR) on our friendly list, which IsPlayerEnemy
        // checks before the enemy list, so we never open up on it.
        foreach (var role in roles)
            if (botDb.TryGetValue(role, out var rt) && rt?.BotDifficulty is not null)
                foreach (var difficulty in rt.BotDifficulty.Values)
                {
                    var mind = difficulty.Mind;
                    if (mind is null) continue;
                    var fr = mind.FriendlyBotTypes ??= [];
                    if (!fr.Contains((WildSpawnType)46)) fr.Add((WildSpawnType)46);
                }

        // He leads Black Division, so the two mods' factions fight as one side. FriendlyBotTypes both
        // ways — which also keeps a stray grenade from feeding BD's revenge tracking into a war.
        factionService.AddFriendlyByFaction(roles, "blackdiv");
        SeedReverse("blackdiv", friendly: true);

        // RUAF and UNTAR are optional faction mods; wire mutual hostility only when they're present.
        // Forward (their types onto our enemy list) is the API. Reverse is by db name: RUAF publishes
        // its int->name map so SeedReverse walks the faction; UNTAR doesn't, so seed its known boss/
        // follower entries directly (they degrade to a no-op if UNTAR ever renames them).
        var optional = new List<string>();
        if (factionService.Factions.ContainsKey("ruaf"))
        {
            factionService.AddEnemyByFaction(roles, "ruaf");
            optional.Add($"ruaf({SeedReverse("ruaf", friendly: false)})");
        }
        if (factionService.Factions.ContainsKey("untar"))
        {
            factionService.AddEnemyByFaction(roles, "untar");
            var untarReached = new[] { "bossuntarlead", "bossuntarofficer", "followeruntar", "followeruntarmarksman" }
                .Count(name => SeedEntry(name, friendly: false));
            optional.Add($"untar({untarReached})");
        }

        // Read the result back out of the db: every path above fails quietly on a missing faction, db
        // entry or difficulty list, and a silent no-op means they don't fight who they should.
        botDb.TryGetValue("wedge", out var wedgeType);
        var wedgeFriendly = wedgeType?.BotDifficulty?["normal"].Mind?.FriendlyBotTypes;
        var bdTypes = factionService.Factions.TryGetValue("blackdiv", out var bd) ? bd.GetAllBotTypes() : [];
        var bdOnWedge = wedgeFriendly?.Count(t => bdTypes.Contains(t)) ?? 0;
        if (!botDb.TryGetValue("blackdivlead", out var bdLead)) botDb.TryGetValue("blackDivLead", out bdLead);
        var onBdLead = bdLead?.BotDifficulty?["normal"].Mind?.FriendlyBotTypes?
            .Count(t => (int)t is WedgeType or WedgeGuardType) ?? 0;
        if (bdOnWedge > 0 && onBdLead == 2)
            logger.Info($"[Wedge] allied with blackdiv ({bdOnWedge} of their types on him; their lead lists both of ours)");
        else
            logger.Warning($"[Wedge] blackdiv alliance readback failed: {bdOnWedge} bd types on him, {onBdLead}/2 of ours on their lead");

        // Enemy readback: one sentinel per core (always-present) faction on him, plus a plain scav
        // counting him an enemy (the reverse). Trust the sentinel count, not the absence of errors.
        // assault(scavs) bossKilla(scav bosses) pmcBot(raiders) exUsec(rogues) sectantWarrior(cultists)
        // bossPartisan pmcBEAR pmcUSEC infectedAssault — one per core target faction.
        int[] enemySentinels = { 1, 6, 9, 24, 20, 47, 51, 52, 60 };
        var wedgeEnemies = wedgeType?.BotDifficulty?["normal"].Mind?.EnemyBotTypes;
        var sentinelsOn = wedgeEnemies is null ? 0 : enemySentinels.Count(s => wedgeEnemies.Contains((WildSpawnType)s));
        var scavFightsBack = botDb.TryGetValue("assault", out var scav)
            && (scav?.BotDifficulty?["normal"].Mind?.EnemyBotTypes?.Contains((WildSpawnType)WedgeType) ?? false);
        var optionalNote = optional.Count > 0 ? $"; optional: {string.Join(", ", optional)}" : "; no optional faction mods";
        if (sentinelsOn == enemySentinels.Length && scavFightsBack)
            logger.Info($"[Wedge] hostile to {wedgeEnemies!.Count} bot types (all 7 core factions present; scavs fight back{optionalNote})");
        else
            logger.Warning($"[Wedge] enemy wiring incomplete: {sentinelsOn}/{enemySentinels.Length} sentinels on him, scav-reverse={scavFightsBack}{optionalNote}");

        logger.Info($"[Wedge] registered roles {WedgeType}/{WedgeGuardType} under faction 'wedge'");
    }
}
