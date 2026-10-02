# Roshi Waifu Tool (RWT)

A workspace for testing Live Appearance Framework (LAF) and Animated Portrait Framework (APF) content in Stardew
Valley 1.6.15 / SMAPI 4.x. Neither framework is required; without them the tool still works, with fewer details.
Singleplayer only.

Version 0.1.0 contains the workspace core and auto patch reload. Design and roadmap: `claude/RoshiTool-design.md`
in the SDV project (copy in `Waifu Claude Sources\Roshi Tool`).

## Build
`dotnet build -c Release` → `bin\Release\net6.0\`. Nothing is deployed to the game's Mods folder
(`EnableModDeploy` is false); install the output through Mod Organizer 2.

## Workspace
Open or close it with **F6** (configurable) or `rwt` in the SMAPI console.

While it's open:
- The clock is held, and the day can't end, so the game never saves.
- A toolbar at the top of the screen: Play/Pause, clock speed, +10m, +1h, the checkpoint times, Restart day, Camera,
  Inspect, Reload packs, Exit.
- **Forward** jumps step the clock 10 minutes at a time and move every NPC to the end of each schedule leg.
- **Backward** (a checkpoint earlier than now, or Restart day) reloads the save, so the day starts over at 6:00, and
  then jumps forward. Everything you did since the morning is undone.
- **Exit** reloads the save and brings you back to the time and place you opened the workspace at ("Restore on exit";
  turn it off to keep the world as you left it).
- **Camera**: free camera with the movement or arrow keys (Shift = faster). `rwt_follow <npc>` keeps it on an NPC.
- **Inspect**: hover an NPC for sprite, portrait, appearance ID, pose and next schedule stop; click her to print the
  full report (`laf_why`, `laf_schedule`, `laf_pose`, `laf_detour`, `apf_portraits`) to the SMAPI console.
- NPC labels show each villager's appearance ID above her head.

Limits:
- A jump skips time; it doesn't play it. Things that need real walking time (LAF detours, idle poses with intros)
  don't happen during a jump. Jump to shortly before, then press Play.
- The clock speed only changes the clock; NPCs walk at normal speed.
- Not available during events, festivals or in multiplayer.

## Auto reload
Every second the tool checks the JSON files of the watched Content Patcher packs and runs `patch reload <pack>` when
one changed. Watched by default: packs whose manifest lists LAF or APF as a dependency. Add others with `WatchedPacks`
in config.json, or turn on "Watch every pack".

`patch reload` does not re-read a pack's ConfigSchema, DynamicTokens, manifest.json or config.json, and under Mod
Organizer 2 a file added while the game runs may not be visible until the next launch.

## Console commands
| Command | What it does |
|---|---|
| `rwt [exit\|stay]` | Toggle the workspace; `exit` closes with a save reload, `stay` closes and keeps the world. |
| `rwt_time <time>` | Go to a time today, e.g. `rwt_time 1430`. Earlier times restart the day first. |
| `rwt_step [count]` | Skip 10-minute steps. |
| `rwt_restart [time]` | Reload the save, then jump to a time (default 6:00). |
| `rwt_pause` | Pause or resume the clock. |
| `rwt_speed <1-8>` | Clock speed while running. |
| `rwt_goto <npc>` | Warp next to an NPC. |
| `rwt_follow [npc]` | Camera follows an NPC in your location; no argument stops. |
| `rwt_reload [pack ID]` | Run `patch reload` for one watched pack, or all. |
| `rwt_packs` | List the watched packs. |

## How it works (verified against the 1.6.15 game DLL and SMAPI)
- Restart: `Game1.ExitToTitle(callback)` then `SaveGame.Load(slot)` in the callback, as the load menu does.
- Jump: `Game1.performTenMinuteClockUpdate()` per step, then `NPC.warpToPathControllerDestination()` for walking NPCs
  (vanilla's `debug hurry`); queued schedule legs are started afterwards until everyone has arrived.
- Pause: `Game1.isTimePaused`. Camera: `Game1.viewportFreeze` + `Game1.viewport`.
- Day end is blocked with a Harmony prefix on `Game1.NewDay`.
- Other mods' console commands are run through SMAPI's internal command list (there is no public API for it).
