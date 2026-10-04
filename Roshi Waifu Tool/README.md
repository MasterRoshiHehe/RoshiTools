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
- The clock is held, and the day can't end, so the game never saves. NPCs keep moving and animating while the clock
  is held (it is not a full time pause).
- Everything is drawn with the game's own menu art (so interface recolours apply).
- **Bottom left: the hub.** A column of mode buttons; the open mode's tools appear to the right of it. Click the open
  mode again to fold it away.
  - **Test**: Camera, Inspect, Reload packs, and the world controls (season, day, weather).
  - **LAF**: Scan now, Re-pick outfits, and for the selected character Inspect, Console report and Take off.
  - **APF**: for the selected character Outfits (her window), Expressions (the ring) and Inspect.
  - **NPCs**: the character browser as a drawer (see below).
  - **Settings**: Game controls (off / on), HUD on/off, Labels (shown / on mouse over / off: the text boxes above characters), Alts in column
    and Animals in column (hidden / shown), All doors (unlocked / as in game), and the config menus of this tool,
    LAF, APF and the watched packs.
  - **Exit** closes the workspace.
  The "selected character" is the one you clicked in the column, the browser or the favourites (she is highlighted;
  click her again to unselect), else the one in the character window, else the one in the inspector. Selecting
  doesn't open a window: use Outfits in her ring or in the APF mode. A gem floats above the selected character
  (the game's emerald; "Marker" in Settings switches it off, "Selection marker" in the config menu takes any item ID).
- **Top right**, below the game's clock: the time controls (Play/Pause, clock speed, +10m, +1h, the checkpoint times,
  Restart day).
- The game's HUD and other mods' HUD icons are hidden while the workspace is open ("Hide the game's HUD", on by
  default); the clock stays ("Keep the clock"). The **HUD** button switches it; closing the workspace restores it.
- **Left edge, above the hub**: one column of faces, one for every villager in the current location. Click a face to
  select her; right-click for her options. If more are here than fit, the mouse wheel scrolls the column (`+N` shows
  how many more). Left out by default: hidden copies of characters (extra NPCs some mods park out of reach, whose
  names contain characters that can't be typed) and characters you can't befriend (animals and other background
  NPCs mods add). "Alts in column" and "Animals in column" in Settings show them.
- **All doors: unlocked** (Settings) opens shops and houses at any hour and bedroom doors without friendship, while
  the workspace is open.
  The game's notifications are moved up so they stay above the hub and the column.
- **Right-click** a character or a tile for a ring of options. Character (her head is in the middle, big and without
  a frame): Inspect, Outfits, Expressions, Follow, Warp to me, Warp to (her home map), Favourite / Unfavourite (and Take off
  while she wears a preview). A character with sub-characters first shows a ring of her versions (Haley, Haley (alt 1),
  HaleyLewd, ...); pick one for that one's options, and click her head there to go back to the versions. Tile (the farmer's head in the middle, the map and tile on the plate below): Warp here,
  copy `x y`, copy `Map x y` to the clipboard, and "Put <name> here" while a character window is open. Hold Shift to
  right-click normally.
- The head in the middle looks at the option you point at: it switches to her side view (from the right- and
  left-facing frames of her sheet) and tilts up or down towards it; for an option straight above or below she keeps
  facing you. She also leans towards the cursor a little, and further towards an option while the cursor is on it.
- **Expressions** replaces the ring with one button per portrait index ("More" turns the page). An index the
  game's own sheet has reads `$3 Vanilla`; every other one reads `$40 Custom`. Click one for a test dialogue box with that expression and one of 100 random lines. Click her face in the middle
  to go back to her options. The indexes come from APF's list for her, else from her portrait sheet.
- The faces are cut from frame 0 of the sprite sheet she wears right now: a square from the first row with pixels on
  it, as wide as her head. The ring uses a tighter cut: only the head (11 rows on a normal sprite), no shoulders.
- The world controls (hub, Test) are buttons for the season, the weather of the area you're in, and the day of the month. After a change
  the tool runs Content Patcher's `patch update`, lets every NPC pick her appearance again and makes LAF scan, so
  outfits that depend on season or weather switch right away. Schedules stay as loaded in the morning. Nothing is
  saved; closing the workspace reloads your save (with "Restore on exit").
- The windows (inspector, character window, character browser) can be dragged by their title bar.
- **Forward** jumps step the clock 10 minutes at a time and teleport every walking NPC to the end of her walk
  (schedule legs, walks back to her spot, and LAF detour walks), so she arrives and starts her animation normally.
  The farmer can't move during a jump.
- **Backward** (a checkpoint earlier than now, or Restart day) reloads the save, so the day starts over at 6:00, and
  then jumps forward. Everything you did since the morning is undone.
- **Exit** reloads the save and brings you back to the time and place you opened the workspace at ("Restore on exit";
  turn it off to keep the world as you left it).
- **Game controls: off** (Settings, the default): while the workspace is open, left click, the use-tool key and the
  mouse wheel don't reach the game, so the farmer doesn't swing a tool or switch hotbar items. The interact key
  still works (and Shift + right-click), and menus and dialogue get their clicks as usual.
- **Camera**: the view stops following the farmer. Move it with the arrow keys (Shift = faster) or **drag the map**:
  hold the left mouse button on the world and move the mouse (as in The Sims). Dragging switches Camera on. The
  movement keys keep moving the farmer; switch Camera off (hub, Test) to have the view follow the farmer again.
- **Number keys** (while Game controls is off): `1` = normal speed, and play / pause when already at normal speed;
  `2` = double speed; `3` = four times.
- **Follow** (her ring, her window, or `rwt_follow <npc>`) keeps the camera on an NPC, also when she walks into
  another map. The view stops at the map's edges like the game's own camera (no black outside the map). The game
  only draws the map the farmer is in, so while following the farmer is hidden, parked off the
  map (nobody bumps into or reacts to them) and warped after her whenever she changes map. **Stop following** (or
  switching the camera off) puts the farmer back where they were when following began; **Stay here** puts them next
  to her instead. "Warp to me" is unavailable while following.
- **Inspect**: hover an NPC for sprite, portrait, appearance ID, pose and next schedule stop; click her to open the
  **inspector panel** on the left. It shows LAF's and APF's own explanation of her as a tree: outfit entries with the
  winner and each condition part marked ok or no, schedule, idle poses, detours, reactions, named conditions, the
  portrait shown and today's rolls. Click a line to open or close it, scroll with the mouse wheel. "Failed: hidden"
  hides failed and skipped lines; "Console" prints the classic reports to the SMAPI console. The panel refreshes
  twice a second. `rwt_inspect <npc>` opens it by name. Needs LAF/APF versions with the editor API.
- **NPCs drawer** (hub, NPCs): every character in the save as face cards, two rows at a time; `<` `>` or the mouse
  wheel turn the page. Filters are stacked on the left (All, Favourites, In this map, Vanilla, Modded, and one per
  mod whose characters carry its ID in their name, up to three columns). Click the **search box** and type to filter
  by name (Esc or Enter gives the keyboard back, `x` clears it). A gold star on a card marks a favourite (Favourite in her ring; click the star to
  unfavourite; saved outside the Mods folder, for every save). Click a card to select her, right-click for her options.
  Sub-characters are folded into the character they belong to: an NPC whose name is another NPC's name plus
  characters that can't be typed (Xtardew's extra Haleys) or plus a suffix from "Sub-character suffixes" in
  config.json (default `Lewd`). A card with `+N` has N of them: right-click it for the ring of her versions. Hidden copies are
  named after the content pack that adds them (`Abigail (Xtardew Valley 1)`): the tool looks for the NPC's name in
  the JSON files of the installed content packs, once, in the background; until it knows it says `alt`.
  `rwt_npcs` in the console lists every character with her sub-characters, spells out unusual characters in names
  (`Haley[U+00B7]`) and says which ones count as not social.
- Outfits in her ring (or in the APF mode) opens her **character window** on the right:
  - shortcuts: go to her, bring her to you, go to her home map, open the inspector, follow her with the camera;
  - her outfits in groups: the base sheets, APF's rolled roots with their variants (today's roll marked `[now]`),
    APF's other variants grouped by name, and the game's Appearance entries;
  - a preview of the selected outfit: the portrait sheet one expression at a time (`<` `>` step through `$0`, `$1`, ...)
    and the overworld sprite (Turn = next facing). Reload re-reads the outfits and images.
  - **Wear** puts the selected outfit on her in the world (sprite and portrait) until **Take off** or until the
    workspace closes. It works like the event commands `changeSprite` / `changePortrait`, so the game and LAF leave
    her alone while she wears it.
  - If the outfit's sheets belong to a root APF rolls today, a note says which variant won: APF paints the roll onto
    the root's sprite sheet, so the sprite shows the roll. "Portrait: today's roll / root sheet" switches the portrait
    preview between the rolled variant and the root's own sheet.
  The preview reads `Portraits/<NPC>_<Variant>` and `Characters/<NPC>_<Variant>` through the game's content pipeline.
- While a ring is open, nothing under it reacts to the cursor. Tooltips are drawn last, on top of every window.
- Everything the tool draws is in the game's overlay step, above the HUD and other mods' HUD icons.
- Events (cutscenes) don't start while the workspace is open ("Ignore events", on by default). They aren't marked
  as seen.
- NPC labels show each villager's appearance ID above her head (Settings: shown, on mouse over, or off).
- **Settings** shows a row of buttons that open the Generic Mod Config Menu page of this tool, LAF, APF and each
  watched content pack (add more mods with `ConfigMenuMods` in config.json).

Limits:
- A jump skips the walking, not the logic: LAF detours start, stay and end during a jump because LAF counts their
  stay in game minutes. Animations with real-time parts (idle pose intros) only show once the jump is over.
- With a LAF that has the editor API, the tool asks LAF to scan after every step, so a jump takes one step every 3
  ticks ("Jump: ticks per step (fast)"): about 2 seconds for 6 hours. With an older LAF it uses one step every 10
  ticks so LAF's own scan sees every step.
- If someone hasn't finished walking when the jump ends, the SMAPI console lists who and why.
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
| `rwt_inspect <npc>` | Open the inspector panel for an NPC. |
| `rwt_follow [npc]` | Camera follows an NPC in your location; no argument stops. |
| `rwt_reload [pack ID]` | Run `patch reload` for one watched pack, or all. |
| `rwt_packs` | List the watched packs. |

## How it works (verified against the 1.6.15 game DLL and SMAPI)
- Restart: `Game1.ExitToTitle(callback)` then `SaveGame.Load(slot)` in the callback, as the load menu does.
- Jump: `Game1.performTenMinuteClockUpdate()` per step; walking NPCs are moved along `controller` /
  `temporaryController` paths like vanilla's `debug hurry` (pop, `handleWarps`, place), but to the last tile, where
  `PathFindController.moveCharacter` finishes the walk on her next update. Queued legs are started afterwards.
- Clock hold: `Game1.gameTimeInterval = 0` every tick. Not `Game1.isTimePaused`: `GameLocation.updateCharacters` only
  updates NPCs while `Game1.shouldTimePass()` is true, so a real pause freezes them and walks never finish. Camera: `Game1.viewportFreeze` + `Game1.viewport`.
- Day end is blocked with a Harmony prefix on `Game1.NewDay`.
- Other mods' console commands are run through SMAPI's internal command list (there is no public API for it).
