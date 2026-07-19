using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace Wedge.Server.Services;

// Adds a forced Wedge boss wave to every enabled map at the given chance. Idempotent: it strips any
// wave it previously added before re-adding, so OnLoad + the raid routers can all call it safely. The
// chance and escort size are passed in (the raid-start router computes both from who's in the raid).
[Injectable(InjectionType.Singleton)]
public class SpawnInjector(
    DatabaseService databaseService,
    ConfigService configService,
    ISptLogger<SpawnInjector> logger)
{
    const string Marker = "wedge_test_";

    public void Inject(double chance, string escort)
    {
        var cfg = configService.Config;
        var maps = new HashSet<string>(cfg.enabledMaps, StringComparer.OrdinalIgnoreCase);
        var injected = 0;

        foreach (var location in databaseService.GetLocations().GetDictionary().Values)
        {
            var mapBase = location?.Base;
            if (mapBase?.BossLocationSpawn == null)
            {
                continue;
            }

            var id = mapBase.Id?.ToLowerInvariant();
            if (id == null || !maps.Contains(id))
            {
                continue;
            }

            // Two raids starting close together — routine in co-op — would otherwise both rewrite the
            // same wave list at once and can tear it. The lock token is the map itself so any mod that
            // adopts the same convention serializes with us for free (RvR will); a private lock only
            // guards Wedge against Wedge. Not covered: ABPS replaces the list wholesale at raid end,
            // so a wave injected into the outgoing list is dropped for that one raid — rare, and it
            // self-heals at the next raid start.
            lock (mapBase)
            {
                if (Apply(mapBase.BossLocationSpawn, id, chance, escort))
                {
                    injected++;
                }
            }
        }

        logger.Info($"[Wedge] injected boss wave into {injected} map(s) (chance {chance:0}%, escort {escort})");
    }

    bool Apply(List<BossLocationSpawn> waves, string id, double chance, string escort)
    {
        var cfg = configService.Config;

        waves.RemoveAll(w => w.TriggerId?.StartsWith(Marker) == true);

        if (chance <= 0)
        {
            return false;
        }

        var zone = waves.FirstOrDefault(w => !string.IsNullOrEmpty(w.BossZone))?.BossZone ?? "";
        if (cfg.singleZone && zone.Contains(','))
        {
            zone = zone.Split(',')[0].Trim();
        }

        waves.Add(new BossLocationSpawn
        {
            BossName = "wedge",
            BossEscortType = "wedgeguard",
            BossEscortAmount = escort,
            BossChance = chance,
            BossDifficulty = "normal",
            BossEscortDifficulty = "normal",
            BossZone = zone,
            IsBossPlayer = false,
            Time = -1,
            Delay = 0,
            TriggerId = Marker + id,
            TriggerName = "",
            IsRandomTimeSpawn = false,
            IgnoreMaxBots = true,
            ForceSpawn = true,
            DependKarma = false,
            DependKarmaPVE = false,
            ShowOnTarkovMap = false,
            ShowOnTarkovMapPvE = false,
            Supports = null!,
            SpawnMode = ["regular", "pve"],
        });

        return true;
    }
}

// Boot injection lands after the mods that rebuild wave lists at load — ABPS replaces every map's
// BossLocationSpawn, so we run well after it (PostDBModLoader + 90000, mirroring RvR). Uses the base
// chance as a placeholder; the raid-start router re-injects with the level-scaled chance.
[Injectable(InjectionType = InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 90000)]
public class WedgeSpawnLoader(
    SpawnInjector injector,
    ConfigService configService,
    ISptLogger<WedgeSpawnLoader> logger) : IOnLoad
{
    public Task OnLoad()
    {
        var cfg = configService.Config;
        injector.Inject(cfg.ChanceForLevel(cfg.baseLevel), cfg.escortAmount);
        logger.Info("[Wedge] spawn injector ready");
        return Task.CompletedTask;
    }
}
