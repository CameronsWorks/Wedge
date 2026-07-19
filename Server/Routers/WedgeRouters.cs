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
        var group = ReadGroup(sessionId);
        int level = cfg.groupScaling ? group.Level : HostLevel(sessionId);
        _injector.Inject(cfg.ChanceForLevel(level), cfg.GuardsForParty(group.Players));
    }

    // GetProfile throws on an empty or unknown id rather than returning null, and ids reach us from
    // both the request and the active-profile list — so every lookup goes through here.
    static int LevelOf(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return 0;
        try { return _profiles.GetPmcProfile(sessionId)?.Info?.Level ?? 0; }
        catch { return 0; }
    }

    static int HostLevel(string sessionId)
    {
        var level = LevelOf(sessionId);
        return level > 0 ? level : _config.Config.baseLevel;
    }

    // One pass gives both numbers the wave needs: the average level sets the chance, the head count
    // sets the escort. Same caveat for both — this is everyone active on the server, not everyone in
    // this raid, so a second party or someone idling in the menu inflates it.
    static (int Level, int Players) ReadGroup(string sessionId)
    {
        var cfg = _config.Config;
        int sum = 0, count = 0;
        foreach (var id in _activity.GetActiveProfileIdsWithinMinutes(cfg.groupWindowMinutes))
        {
            var level = LevelOf(id);
            if (level > 0)
            {
                sum += level;
                count++;
            }
        }

        // Nobody resolved (or none had a PMC yet) — fall back to the host so we never scale off zero,
        // and treat the raid as solo rather than handing the escort a count of nought.
        return count > 0 ? (sum / count, count) : (HostLevel(sessionId), 1);
    }

    static List<RouteAction> GetRoutes() =>
    [
        // Under Fika the raid doesn't read the live database at /start — the lobby snapshots the
        // location when it's created, and that snapshot is what the raid serves. Running before Fika's
        // own handler on this request puts the wave (and the real host's level-scaled chance) into the
        // snapshot itself. Without Fika the route never fires and /start below covers everything.
        new RouteAction(
            "/fika/raid/create",
            async (url, info, sessionId, output) =>
            {
                try { Reinject(sessionId); }
                catch (Exception ex) { _logger.Error("[Wedge] raid-create reinject failed: " + ex); }
                return await new ValueTask<object>(output ?? string.Empty);
            }
        ),
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
                // Not redundant with raid start, though it looks it: the wave lists are rebuilt around
                // raid end by other mods, and the next raid's location can be snapshotted before the
                // next /start runs — a raid-start-only reassert misses that window and the boss silently
                // sits out every consecutive raid. LevelOf shields the empty session id this request
                // carries (the 0.1.2 crash was that, not the route).
                try { Reinject(sessionId); }
                catch (Exception ex) { _logger.Error("[Wedge] raid-end reinject failed: " + ex); }
                return await new ValueTask<object>(output ?? string.Empty);
            }
        ),
    ];
}
