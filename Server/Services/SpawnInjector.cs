using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace Wedge.Server.Services;

// Adds a forced Wedge boss wave to every enabled map at the given chance. Idempotent: it strips any
// wave it previously added before re-adding, so OnLoad + the raid routers can all call it safely. The
// chance is passed in (the raid-start router computes it from the raid's level).
[Injectable(InjectionType.Singleton)]
public class SpawnInjector(
    DatabaseService databaseService,
    ConfigService configService,
    ISptLogger<SpawnInjector> logger)
{
    const string Marker = "wedge_test_";

    public void Inject(double chance)
    {
        var cfg = configService.Config;
        var maps = new HashSet<string>(cfg.enabledMaps, StringComparer.OrdinalIgnoreCase);
        var injected = 0;

        foreach (var location in databaseService.GetLocations().GetDictionary().Values)
        {
            var mapBase = location?.Base;
            var waves = mapBase?.BossLocationSpawn;
            if (waves == null)
            {
                continue;
            }

            var id = mapBase!.Id?.ToLowerInvariant();
            if (id == null || !maps.Contains(id))
            {
                continue;
            }

            waves.RemoveAll(w => w.TriggerId?.StartsWith(Marker) == true);

            if (chance <= 0)
            {
                continue;
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
                BossEscortAmount = cfg.escortAmount,
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

            injected++;
        }

        logger.Info($"[Wedge] injected boss wave into {injected} map(s) (chance {chance:0}%, escort {cfg.escortAmount})");
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
        injector.Inject(configService.Config.ChanceForLevel(configService.Config.baseLevel));
        logger.Info("[Wedge] spawn injector ready");
        return Task.CompletedTask;
    }
}
