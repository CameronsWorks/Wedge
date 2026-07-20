# Wedge

Adds The Wedge — the Black Division "Icebreaker" boss from EFT 1.0.5 — and his guard squad to most maps.
He turns up in his own kit and comes looking for you.

## What it adds

His own spawn type and brain rather than a reskinned existing boss — wearing his real 1.0.5 face, coat and
trousers, speaking with his own voice. Spiritus LV-119 plate carrier, Team Wendy EXFIL with L3Harris
PVS-31A night vision on a Wilcox mount, an Avon M53A1 gas mask, and his weapons: SCAR-H, SA58, MDR or MP7,
a USP .45, a SOG tomahawk, and his Model 8230 CS gas grenades. Gear spawns at near-perfect durability and
he has 880 health, roughly double a PMC.

The brain is the part worth caring about. He pushes toward the nearest player instead of holding a corner,
opens with gas and flash on the approach, and hands the actual gunfight off to SAIN so he still shoots
like something you know how to read. He calls orders his guards actually follow — cover, hold, push — and
the Black Division Operator escort spawns in the same zone and rushes with him, so you get the squad
arriving together rather than trickling in.

The gas is real. His 8230 leaves a cloud that drains anyone standing in it without a filter, stings your
eyes and sets you coughing. A full-face mask shuts it out completely — his own M53A1, a GP-5 or GP-7; a
respirator like the Ops-Core SOTR or a 3M keeps it out of your lungs but leaves your eyes stinging. The list
is editable in F12. His face and voice are also selectable for your own PMC in character creation.

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

All four of these. None are optional — he won't run without them.

- **MoreBotsAPI** (`com.morebotsapi.tacticaltoaster`) — supplies the custom boss role he spawns as. The
  server mod references it directly, so it won't load without it. Needs DrakiaXYZ-BigBrain itself.
- **WTT-ContentBackport** (`com.wtt.contentbackport`) — every piece of his guards' Black Division gear and
  clothing. Its server half needs **WTT-ServerCommonLib** (`com.wtt.commonlib`), which this also uses
  directly to register his own head, body, voice and kit items, plus the names the end-of-raid screen
  shows. Installing the WTT set normally brings WTT-ClientCommonLib along too.
- **Black Division** (`com.blackdiv.tacticaltoaster`) — the faction he leads. His guards are Black Division
  soldiers and share its content.
- **DrakiaXYZ-BigBrain** (`xyz.drakia.bigbrain`) — the rush layer registers against it. BepInEx skips the
  plugin without it.

MoreBotsAPI, WTT-ContentBackport and Black Division each ship as several parts — a plugin, a patcher and a
server mod. Install them whole rather than picking out files.

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
| `escortAmount` | 3 | Guards, when `partyScaling` is off |
| `partyScaling` | true | Bigger parties draw more guards |
| `guardsByPartySize` | [3, 4, 6] | Guards for solo, duo, and three or more |
| `singleZone` | true | Keeps him and the escort arriving together |
| `enabledMaps` | 13 maps | Reserve, Streets, Customs, both Factories, Interchange, Labs, Lighthouse, both Ground Zero tiers, Shoreline, Woods, Labyrinth. Delete an entry to switch a map off. |

F12, section *Wedge*: Enable, Aggressive Rush, Grenade Barrage, Rush Radius (caps how far he'll chase so he
doesn't sprint the map from spawn), Layer Priority, the *Command* group (squad orders on/off, acknowledge
chance), and the *Gas* group (damage per second, blur, the protective-mask list, logging). Leave the two
Layer Priority values alone unless you know what you're doing — the rush has to stay below SAIN's combat
layers at 20+ or he'll rush while he should be shooting.

## Removing the mod

Uninstalling has one catch. Once Wedge or a guard turns up in your kill history, your profile stores their
role by name, and the game only knows those names because of Wedge's *prepatcher*. Delete the prepatcher
while the profile still references them and the client throws on load.

So to remove Wedge safely, **leave `BepInEx\patchers\Wedge\` in place** and delete only:

- `SPT\user\mods\Wedge\`
- `BepInEx\plugins\Wedge\`

The patcher on its own does nothing but teach the game two extra names, so it's harmless to keep. He stops
spawning and your profile still loads.

The other catch is his gear. Any Wedge item still in your stash, kit or mail when the server mod goes away
— the helmet, the goggles, the 8230s — is an item the server no longer recognises, and SPT flags the whole
profile invalid at the next boot. Sell or drop his gear before uninstalling, or set
`removeModItemsFromProfile` to `true` in `SPT\SPT_Data\configs\core.json` for one restart and let the
server strip what's left (it deletes attachments with their parent, so a helmet goes goggles and all).

## Build

`dotnet build -c Release` with the .NET SDK 9+. Three projects: `Client` and `Prepatch` on net472, `Server`
on net9.0. They resolve the install through two different anchors — `SptRoot` (the game folder) in Client
and Prepatch, `SptServer` (the `SPT` folder inside it) in Server. Override both if your checkout isn't
three folders down.

Built against SPT 4.0.13 / EFT 0.16.9.
