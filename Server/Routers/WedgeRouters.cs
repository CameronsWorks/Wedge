using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using Wedge.Server.Services;

namespace Wedge.Server.Routers;

// ABPS rebuilds every map's BossLocationSpawn wholesale per raid, and the level-scaled spawn chance
// depends on who's in the raid — so we re-inject Wedge's wave here at 150000 (after ABPS, before the
// core match router clones the location) with the chance computed from the raid's level.
[Injectable(TypePriority = 150000)]
public class WedgeRaidRouter : StaticRouter
{
    static SpawnInjector _injector = null!;
    static ConfigService _config = null!;
    static ProfileHelper _profiles = null!;
    static ProfileActivityService _activity = null!;
    static ISptLogger<WedgeRaidRouter> _logger = null!;

    public WedgeRaidRouter(
        SpawnInjector injector,
        ConfigService config,
        ProfileHelper profiles,
        ProfileActivityService activity,
        ISptLogger<WedgeRaidRouter> logger,
        JsonUtil jsonUtil)
        : base(jsonUtil, GetRoutes())
    {
        _injector = injector;
        _config = config;
        _profiles = profiles;
        _activity = activity;
        _logger = logger;
    }

    // Solo = your level. Co-op = the average level of everyone signed in to the server (each Fika peer
    // registers on login, so they're all present by raid start), which for a party of friends is the
    // squad. Falls back to the host's level when group scaling is off or the group can't be read.
    static void Reinject(string sessionId)
    {
        var cfg = _config.Config;
        int level = cfg.groupScaling ? GroupLevel(sessionId) : HostLevel(sessionId);
        _injector.Inject(cfg.ChanceForLevel(level));
    }

    static int HostLevel(string sessionId) =>
        _profiles.GetPmcProfile(sessionId)?.Info?.Level ?? _config.Config.baseLevel;

    static int GroupLevel(string sessionId)
    {
        var cfg = _config.Config;
        int sum = 0, count = 0;
        foreach (var id in _activity.GetActiveProfileIdsWithinMinutes(cfg.groupWindowMinutes))
        {
            var level = _profiles.GetPmcProfile(id)?.Info?.Level ?? 0;
            if (level > 0)
            {
                sum += level;
                count++;
            }
        }

        // Nobody resolved (or none had a PMC yet) — fall back to the host so we never scale off zero.
        return count > 0 ? sum / count : HostLevel(sessionId);
    }

    static List<RouteAction> GetRoutes() =>
    [
        new RouteAction(
            "/client/match/local/start",
            async (url, info, sessionId, output) =>
            {
                try { Reinject(sessionId); }
                catch (Exception ex) { _logger.Error("[Wedge] raid-start reinject failed: " + ex); }
                return await new ValueTask<object>(output ?? string.Empty);
            }
        ),
        new RouteAction(
            "/client/match/local/end",
            async (url, info, sessionId, output) =>
            {
                try { Reinject(sessionId); }
                catch (Exception ex) { _logger.Error("[Wedge] raid-end reinject failed: " + ex); }
                return await new ValueTask<object>(output ?? string.Empty);
            }
        ),
    ];
}
