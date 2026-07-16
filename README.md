# Wedge

Adds The Wedge — the Black Division "Icebreaker" boss from EFT 1.0.5 — and his guard squad to most maps.
He turns up in his own kit and comes looking for you.

## What it adds

His own spawn type and brain rather than a reskinned existing boss. Spiritus LV-119 plate carrier, Team
Wendy EXFIL with L3Harris PVS-31A night vision on a Wilcox mount, an Avon M53A1 gas mask, and his weapons:
SCAR-H, SA58, MDR or MP7, a USP .45, and a SOG tomahawk. Gear spawns at near-perfect durability and he has
880 health, roughly double a PMC.

His face and clothing are assembled from Black Division parts, not his real 1.0.5 art — nothing ships that
art yet, so the appearance is a close stand-in rather than the genuine article.

The brain is the part worth caring about. He pushes toward the nearest player instead of holding a corner,
opens with flash and smoke on the approach, and hands the actual gunfight off to SAIN so he still shoots
like something you know how to read. His Black Division Operator escort spawns in the same zone and rushes
with him, so you get the squad arriving together rather than trickling in.

## Spawn chance

Scales with level: 10% at 15, another 5% every 5 levels, capped at 25%. Level 20 is 15%, level 30 and up is
25%. Solo that's your level; in a Fika session it averages everyone who has pinged the server in the last
15 minutes, so one high-level player doesn't drag the lobby's odds up alone.

## Install

Extract into your SPT folder — the one holding `EscapeFromTarkov.exe` and `BepInEx`. Merge `SPT` and
`BepInEx` when Windows asks, then restart the server.

Upgrading from 0.1.0: that build put the server mod in `user\mods\Wedge` at the install root by mistake,
where the server never read it. Delete that stray `user` folder — the real one is `SPT\user\mods\Wedge`.

## Requires

All three of these. None are optional — he won't run without them.

- **MoreBotsAPI** (`com.morebotsapi.tacticaltoaster`) — supplies the custom boss role he spawns as. The
  server mod references it directly, so it won't load without it. Needs DrakiaXYZ-BigBrain itself.
- **WTT-ContentBackport** (`com.wtt.contentbackport`) — every piece of his Black Division gear and clothing.
  Its server half needs WTT-ServerCommonLib; installing the set normally brings WTT-ClientCommonLib too.
- **DrakiaXYZ-BigBrain** (`xyz.drakia.bigbrain`) — the rush layer registers against it. BepInEx skips the
  plugin without it.

Both MoreBotsAPI and WTT-ContentBackport ship as several parts — a plugin, a patcher and a server mod.
Install them whole rather than picking out files.

Optional: **SAIN** (found by reflection, no assembly reference — without it he still spawns, rushes and
throws grenades, he just fights on the vanilla brain; a failed interop logs a warning and carries on) and
**Fika** for co-op, which is also what the group-average spawn scaling needs.

## Config

`SPT/user/mods/Wedge/config.jsonc`, server restart to apply:

| Key | Default | What it does |
|---|---|---|
| `levelScaling` | true | Off falls back to `flatChance` |
| `baseLevel` / `baseChance` | 15 / 10 | Curve start |
| `chancePerStep` / `stepLevels` | 5 / 5 | +5% per 5 levels |
| `chanceCap` | 25 | Ceiling |
| `flatChance` | 15 | Only used with scaling off |
| `groupScaling` | true | Co-op: average the session instead of the host alone |
| `groupWindowMinutes` | 15 | How recent a ping counts toward that average |
| `escortAmount` | 3 | Guards |
| `singleZone` | true | Keeps him and the escort arriving together |
| `enabledMaps` | 13 maps | Reserve, Streets, Customs, both Factories, Interchange, Labs, Lighthouse, both Ground Zero tiers, Shoreline, Woods, Labyrinth. Delete an entry to switch a map off. |

F12, section *Wedge*: Enable, Aggressive Rush, Grenade Barrage, Rush Radius (caps how far he'll chase so he
doesn't sprint the map from spawn), and Layer Priority. Leave Layer Priority alone unless you know what
you're doing — it has to stay below SAIN's combat layers at 20+ or he'll rush while he should be shooting.

## Build

`dotnet build -c Release` with the .NET SDK 9+. Three projects: `Client` and `Prepatch` on net472, `Server`
on net9.0. They resolve the install through two different anchors — `SptRoot` (the game folder) in Client
and Prepatch, `SptServer` (the `SPT` folder inside it) in Server. Override both if your checkout isn't
three folders down.

Built against SPT 4.0.13 / EFT 0.16.9.
