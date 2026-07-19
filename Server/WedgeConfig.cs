namespace Wedge.Server;

public class WedgeConfig
{
    public bool debugLogs { get; set; }

    // Spawn chance scales with the raid's level: baseChance at baseLevel, +chancePerStep every
    // stepLevels levels, clamped to [baseChance, chanceCap]. Defaults: 10% at level 15, +5% per 5
    // levels, cap 25% (lvl 20 = 15%, lvl 30+ = 25%).
    public bool levelScaling { get; set; } = true;

    // Co-op: scale off the average level of everyone signed in to the server rather than the host
    // alone, so the chance tracks the squad. Each peer registers on login, so they're all counted by
    // raid start. Off = host's level only. Solo is the same either way (you're the only one online).
    public bool groupScaling { get; set; } = true;

    // How recently a profile must have pinged the server to count toward the group average, in minutes.
    public int groupWindowMinutes { get; set; } = 15;

    public int baseLevel { get; set; } = 15;
    public double baseChance { get; set; } = 10;
    public double chancePerStep { get; set; } = 5;
    public int stepLevels { get; set; } = 5;
    public double chanceCap { get; set; } = 25;

    // Used when levelScaling is off — a flat chance on every enabled map.
    public double flatChance { get; set; } = 15;

    public string escortAmount { get; set; } = "3";

    // Scale the escort with the number of players in the raid, read from the same active-profile list
    // as the level average. Off = always escortAmount.
    public bool partyScaling { get; set; } = true;

    // Guards by party size: entry 0 is solo, entry 1 is a duo, and the last entry covers anything
    // larger. Shorter or longer lists work — the ends just clamp.
    public List<int> guardsByPartySize { get; set; } = [3, 4, 6];

    public string GuardsForParty(int players)
    {
        if (!partyScaling || guardsByPartySize.Count == 0)
        {
            return escortAmount;
        }

        var index = Math.Clamp(players - 1, 0, guardsByPartySize.Count - 1);
        return guardsByPartySize[index].ToString();
    }

    // Pin the boss + his escort to one zone so they land together.
    public bool singleZone { get; set; } = true;

    public List<string> enabledMaps { get; set; } =
    [
        "rezervbase", "tarkovstreets", "bigmap", "factory4_day", "factory4_night",
        "interchange", "laboratory", "lighthouse", "sandbox", "sandbox_high",
        "shoreline", "woods", "labyrinth",
    ];

    // Compute the spawn chance for a given raid level.
    public double ChanceForLevel(int level)
    {
        if (!levelScaling)
        {
            return flatChance;
        }

        var steps = Math.Floor((level - baseLevel) / (double)stepLevels);
        var chance = baseChance + (chancePerStep * steps);
        return Math.Clamp(chance, baseChance, chanceCap);
    }
}
