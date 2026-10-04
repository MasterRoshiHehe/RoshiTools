# Roshi Tool (working title) — design draft

Status: **v0.1.0 coded 2026-10-02 (stages A + B), compiles against the real 1.6.15 DLLs, untested in game.**
Mod name "Roshi Waifu Tool RWT", UniqueID `MasterRoshiHehe.RoshiWaifuTool`.
**Working copy (Glarthon, 2026-10-02): `D:\Modding\Stardew\mods\Roshi Waifu Tool\RoshiTools\Roshi Waifu Tool`** — git repo root is
`...\RoshiTools`, remote https://github.com/MasterRoshiHehe/RoshiTools (don't push unless asked). This doc's copy lives in the repo root.
Leftover duplicates not to edit: `...\Roshi Waifu Tool\Roshi Waifu Tool` and `Waifu Claude Sources\Roshi Tool`.
Targets: SDV 1.6.15, SMAPI 4.5.2, Content Patcher 2.9.1. Singleplayer only.

Source check: the game facts below were first read in the 1.6.9-era decompile and then **re-checked on 2026-10-02
against the 1.6.15 `Stardew Valley.dll` and the installed `StardewModdingAPI.dll`**, decompiled in the container
(.NET SDK 8 from builds.dotnet.microsoft.com + `ilspycmd` 9.0 from NuGet; both reachable now). One fact changed: §3.6.

Container build recipe: SDK in `/home/claude/dotnet`; references = the DLLs in `SDV Modding src\Game Sources`
+ `SMAPI-develop\build\0Harmony.dll` + `SMAPI.Toolkit.CoreInterfaces` built from `SMAPI-develop\src`; a plain csproj
with `<Reference Include="refs/*.dll" Private="false" />` (no ModBuildConfig).

## 0. What v0.1.0 contains
- `ModEntry.cs` (events, console commands `rwt`, `rwt_time`, `rwt_step`, `rwt_restart`, `rwt_pause`, `rwt_speed`,
  `rwt_goto`, `rwt_follow`, `rwt_reload`, `rwt_packs`; GMCM).
- `Framework/Workspace.cs` (sandbox state, enter/exit, restart day via ExitToTitle + SaveGame.Load, pending work after
  the reload, toolbar buttons), `TimeMachine.cs` (step / jump / settle), `FreeCamera.cs`, `Toolbar.cs` (plain drawn
  buttons, no StardewUI), `Inspector.cs` (labels, hover box, click = run `laf_why`/`laf_schedule`/`laf_pose`/
  `laf_detour`/`apf_portraits` for that NPC; uses LAF's existing API when present), `SandboxPatches.cs` (Harmony prefix
  on `Game1.NewDay`), `CommandRunner.cs` (SMAPI internals), `PackWatcher.cs` (polling + `patch reload`).
- Jump details found while coding: `GameLocation.performTenMinuteUpdate` is what calls `NPC.checkSchedule`; a hurried
  NPC keeps the last tile of her path, so the next legs queue up during a jump and are started in a "settle" phase
  afterwards (max 20 s). LAF detours and idle poses need real time and don't play during a jump.
- **2026-10-02 first in-game test (Glarthon): everything worked** (workspace, jump, inspect, camera, restart day,
  back-jump, auto reload under MO2, exit/restore). Found: the farmer could move during a jump; and after a jump to
  12:00 Haley was still in HaleyHouse instead of the Beach, with "catching up" running into the 20 s cap. Cause
  (from her report + code): LAF detours walk with `temporaryController` in real time, the tool only hurried
  `controller`, and vanilla holds schedule legs while `temporaryController` is set.
- **Fix (same day, compiled, untested):** `TimeMachine.FinishWalk` teleports along `controller` *and*
  `temporaryController` to the last tile (Position = tile*64 + (0,12): `moveCharacter` needs her box inside the tile
  with ≥2 px below it), every tick during jump and settle. LAF's `DetourManager` tolerates this: it only compares the
  controller reference, arrival comes through the controller's `endBehaviorFunction`, and the stay is counted in game
  minutes (`NowMinutes`). Settle ends when nobody is busy, or when nothing changed for 90 ticks (a held leg / detour
  stay), or after 20 s; who is left is logged at Info. Config `JumpStepTicks` (was `JumpTicksPerStep` = 2) defaults
  to 10 = LAF's default `ScanInterval`, because LAF only starts detours on its scan. Farmer locked during jumps
  (movement keys suppressed + `Halt`). Later: a LAF editor-API hook to scan on every jump step would allow fast jumps.
- **Second test (Glarthon): Haley still at home, every NPC "schedule walk, 1 tile left".** Real cause, verified in
  the 1.6.15 DLL: `GameLocation.updateCharacters` only calls `NPC.update` while `Game1.shouldTimePass()` is true (or
  for horses / `forceUpdateTimer > 0`), and the workspace set `Game1.isTimePaused`. So NPCs were frozen for the whole
  jump and never finished a walk; the first test's 20 s "catching up" was the same thing. **Fix:** the tool no longer
  touches `isTimePaused`; it holds the clock with `Game1.gameTimeInterval = 0` every tick (UpdateTicking + UpdateTicked)
  while paused or jumping. Side effect, wanted: NPCs stay alive (animations, poses, detour walks) while the clock is
  held. §3.4's "isTimePaused, no Harmony needed" is therefore wrong for this purpose.
- **Settings buttons (Glarthon's request):** toolbar "Settings" shows one button per config menu — RWT, LAF, APF, the
  watched packs, plus `ConfigMenuMods` — each calling GMCM's `OpenModMenu(manifest)`. He'd also accept expandable
  inline lists later; GMCM has no API to read another mod's options, so inline lists would only be possible for RWT's
  own settings.
- **Third test (Glarthon): jump works** — NPCs visibly zap between stops, Haley is at the Beach at 12:00, Settings
  buttons open the config pages. Left over: ~23 NPCs listed as not finished. Two causes, both fixed (compiled,
  untested): (1) "0 tile(s) left" is not stuck — a controller with an empty path stays set while her motion is frozen
  by the schedule animation, and `checkSchedule` treats it as idle; the tool now does too. (2) "1 tile left" forever:
  `FinishWalk` called `Halt()` while the outro of her previous schedule animation was playing; `Sprite.StopAnimation`
  clears `CurrentAnimation`, so `routeEndAnimationFinished` never ran and `freezeMotion` stayed true. Now an NPC isn't
  teleported while `Sprite.CurrentAnimation != null`.
- **Ignore events** (his request, default on): Harmony prefix on `GameLocation.checkForEvents` returns false in the
  sandbox (not when `Game1.killScreen`). Config/GMCM `IgnoreEvents`.
- Open wish: he uses "Modern Config Menu" and would like the Settings buttons to open that instead of GMCM (nice to
  have; its API not looked at yet).
- **Fourth test (Glarthon): all good** — no stragglers, events ignored, Haley's 18:00 kitchen pacing is her real
  schedule (`1700 HaleyHouse square_5_1`).
- **Stage C, 2026-10-02 (compiled, untested):** LAF and APF now have the editor API from
  `claude/RWT-EditorApi-HANDOFF.md` (LAF: `Framework/EditorApi.cs`, members on `ILiveAppearanceApi`, `ScanNow` →
  Engine/Detours/Poses/Interactions `.ScanNow()`, `laf_explain`; APF: `IAnimatedPortraitApi.cs`,
  `Framework/EditorApi.cs` + `EditorNodes.cs`, `apf_explain`; both write JSON with their own small writer).
  RWT side: `Framework/IEditorApis.cs` (`ILiveAppearanceEditorApi` requested separately so an older LAF still gives
  the basic API; `IAnimatedPortraitApi`), `MiniJson.cs` (own reader, no JSON library reference), `ExplainNode.cs`
  (+ `UiText.Clean/Wrap`), `InspectorPanel.cs` (left-side HUD panel, not a menu: header buttons Failed hidden/shown,
  Collapse all, Console, Close; rows built from the node trees; expanded state keyed by node path; refresh every 30
  ticks; wheel scroll via `MouseWheelScrolled`, which can't be suppressed, so the game's toolbar slot also moves).
  Clicking an NPC with Inspect on opens the panel; `rwt_inspect <npc>`. Jump: `TimeMachine.GetScanNow` calls LAF's
  `ScanNow()` after every step and steps every `FastJumpStepTicks` (3) instead of `JumpStepTicks` (10).
- To test next (stage C): panel content for Haley vs `laf_why`; per-part ok/no marks; fast jump with detours
  (06:00 -> 12:00: GetDressed Done, Haley at the Beach); panel at different UI scales.
- **Stage C tested OK (Glarthon, screenshots):** panel matches `laf_why` (winner, 10 failed, skipped). Arrows drew
  as "-▷" (the font's '>' is a triangle) → `UiText.Clean` now writes "to".
- **Character window (Glarthon's addition to stage D, 2026-10-02, first version compiled, untested):** toolbar
  "Characters" lists every villager (`Utility.ForEachVillager`, cached 5 s) as buttons; clicking one opens
  `Framework/CharacterWindow.cs` on the right (HUD panel like the inspector). Shortcuts: Go to her, Bring her to me
  (`Game1.warpCharacter`, drops her controllers), Go to her home (`DefaultMap` + `DefaultPosition`), Inspect, Follow.
  Outfit groups: Base; APF roots from Explain section `rolls` (child ID = root, grandchild ID = variant, status
  winner/pass/fail); remaining names from section `variants` grouped by the prefix before the first `_`; and the
  game's `CharacterData.Appearance` entries. Preview: `Portraits/{textureName}_{Variant}` as 2 columns of square cells
  (cell = width / 2), expression arrows; `Characters/{textureName}_{Variant}` one facing at a time.
  **His vision:** this window becomes the place for the editing features — pick a root (Haley, Haley_Beach,
  Haley_Winter), see its variants, toggle/preview them, then per-variant configs. Any planned feature that fits a
  character goes here. **Not done yet:** toggling variants on/off
  (APF's per-outfit GMCM toggles → needs an APF setter), editing.
- **Character window tested (Glarthon, screenshot 2026-10-02):** works. Three follow-ups done the same day
  (compiled, untested):
  1. *HUD icons (UI Info Suite 2, clock, energy bar) drew over the windows.* The tool drew in `RenderedHud`. It now
     draws in `Display.RenderingStep` with `RenderSteps.Overlays` (Game1.DrawOverlays: sprite batch open in UI mode,
     after the HUD step and before `drawMouseCursor`). Not drawn while a menu is open.
  2. *Sprite didn't match the portrait for some entries (e.g. Roshi_Haley_Beach).* Not his setup: APF paints today's
     roll onto the root sprite sheet (`Characters/{NPC}_{Root}`), while `Portraits/{NPC}_{Root}` stays the root's own
     sheet (APF picks the rolled portrait at dialogue time). The window now reads root → winner from APF's `rolls`
     section, previews the rolled variant's portrait by default, and says so (toggle to see the root sheet).
  3. *Wear in the world ("that's how I want it").* No LAF/APF change needed: `Workspace.Wear` sets
     `npc.spriteOverridden` / `npc.portraitOverridden` and loads the textures like the event commands
     `changeSprite` / `changePortrait` (verified in Event.cs 1.6.15); `NPC.TryLoadSprites/TryLoadPortraits` return
     early for overridden textures and LAF's `RefreshEngine.CanHandle` skips such NPCs. `StopWearing` clears the flags
     and calls LAF's `SafeChooseAppearance` (or `NPC.ChooseAppearance`). All worn outfits are released when the
     workspace closes. APF (ContentPatcher mode) reads the variant from the portrait's asset name, so dialogue follows.
- **Look and layout round (2026-10-02 late, his go-ahead "whatever makes sense next"; compiled, untested):**
  - `Framework/Ui.cs`: frames with `IClickableMenu.drawTextureBox` (menuTexture 0,256,60,60), buttons with the
    Cursors frame (432,439,9,9) at scale 4 (same as `OptionsButton`), dark text colours for parchment, tooltip, own
    cursor. Inspector panel and character window restyled with it.
  - `Toolbar.cs` rewritten as corner groups: bottom left = time (title line with time/state/map), top right = tools
    (left of the clock, `EdgeInset` 304), top left = Characters (expands into the name buttons). Windows are placed
    between the groups each frame (`GetBounds`).
  - HUD toggle: `Game1.displayHUD = false` while the workspace is open (config `HideGameHud`, default true; button
    "HUD: off/on"; re-applied every tick, restored on close). Other mods' HUD: SMAPI raises `RenderingHud` /
    `RenderedHud` from `SCore.OnRenderingStep` / `OnRenderedStep` even when the game's HUD is hidden, so
    `SandboxPatches` prefixes both and skips the HUD step while hidden. Clock: the tool calls
    `Game1.dayTimeMoneyBox.draw` itself (`KeepClock`). The game only draws its cursor with the HUD
    (`DrawOverlays`), so the tool draws the arrow cursor itself while hidden.
  - `CharacterBar.cs`: head icons (`NPC.getMugShotSourceRect`) of the villagers in the current location along the
    bottom, right of the time group; click = window, right-click = options.
  - `PieMenu.cs`: right-click ring (config `PieMenu`, Shift = normal right-click). NPC and tile options as in the
    README; "copy x y" uses `DesktopClipboard.SetText`. Tile options are the stopgap for authoring until stage D.
  - Decisions taken for him (he can change them): separate windows kept (no single docked panel yet), text labels
    (no icons), farmer walks as before.
- **Sims-style round (2026-10-03, his notes on the look round; compiled, untested):** parchment style all the way
  (his decision; anything the game's art lacks gets made).
  - Layout: time group moved to the top right, below the game's clock (`Toolbar.Group.Top`); top left is one
    "Characters" button; bottom left is the "in this map" column of faces (`CharacterBar`, 6 per column).
  - Text: small font at scale 1, buttons and titles faux bold (drawn twice 1 px apart).
  - Windows drag by their title bar (`WindowDrag` in `Ui.cs`); clicking the same face / pie "Outfits" toggles the
    character window.
  - `Faces.cs`: face = square crop of frame 0 of the sheet the NPC wears now, from the first non-transparent row,
    head width from the next 12 rows; cached per texture, cleared every 300 ticks (a pack reload repaints the same
    texture object).
  - Pie: face + name in the middle; "Expressions" opens a second ring (12 per page + More) whose middle goes back.
    An expression opens `Game1.DrawDialogue(new Dialogue(npc, null, "<random line>$<n>"))`. Indexes: APF section
    `variants` text `[..]`, else portrait sheet cells (cell = width / 2).
  - `NpcBrowser.cs`: face cards, filters stacked left (max 3 columns): All, Favourites, In this map, Vanilla, Modded,
    per mod (name starts with a `Author.Mod_` prefix only; a real per-mod scan is undecided). Vanilla names = keys of
    the unmodded `Data/Characters` read with a plain XNA ContentManager. Favourites in SMAPI global data
    (`tool-data`), so nothing is written into Mods (MO2).
  - `NpcGroups.cs`: sub-characters (name = base name + only symbols, or + a suffix from config `SubCharacterSuffixes`,
    default "Lewd") fold into the base character; right-click a face in the browser or column lists them.
  - Notifications: Harmony prefix on `HUDMessage.draw` starts the game's `heightUsed` at the height of the bottom
    left column, so messages stack above it.
- **Follow-up round (2026-10-03, his screenshots; compiled, untested):**
  - Pie middle is now only her head, no frame, up to 160 px at a whole pixel scale with a dark edge
    (`Faces.TryGetHead` / `DrawHead`: 11 rows from the top of the hair on a 16 px sprite, cut to its pixels), name on a
    plate below. The corner column and the browser keep the square crop (he likes that one).
  - 100 test lines (`TestLines.cs`), never the same twice in a row. Cost is nothing: one array of strings.
  - Characters with sub-characters: right-click opens a ring of her versions; each opens that one's normal ring, whose
    head goes back. Pie has Favourite / Unfavourite.
  - Sub-characters weren't found in his game. `NpcGroups` now compares names with everything that isn't a plain ASCII
    letter/digit removed (so dots, invisible characters and look-alikes anywhere in the name count), no nesting.
    `rwt_npcs` prints every name with code points to check. Not confirmed fixed.
  - **World** (tools group): season (`Game1.season` + `setGraphicsForSeason`, as debug `season`), day (as debug `day`),
    weather of the current location context (flags as debug `rain` / `debrisWeather`). Afterwards: CP `patch update`,
    `ChooseAppearance` for every villager, LAF `ScanNow`.
  - Pie head looks at the hovered option (his idea): side view from sheet rows 1 (right) and 3 (left, or row 1
    mirrored if missing), tilt up to 0.4 rad towards it, eased; within 70 px left/right of centre it stays frontal and
    leans up to 14 px up/down (`Faces.TryGetSideHead`, `PieMenu.Tilt` / `Lean`).
  - Ideas not built yet live in `RoshiTool-ideas.md` (next to this file; project doc `claude/RoshiTool-ideas.md`).
- **Hub (2026-10-03, his "next logical step"; compiled, untested):** bottom left, Sims-style. `Toolbar` groups
  "modes" (column: Test, LAF, APF, NPCs, Settings, Exit; `Button.MinWidth`) and "hub" (the open mode, `Group.Left` =
  right of the column; `Workspace.FillHub`). Clicking the open mode folds it. The old top-right tools group and the
  top-left Characters button are gone; time stays top right under the clock (his wish). The in-map column sits above
  the mode column; inspector and browser open to the right of it; notifications clear `HubTop`. LAF and APF modes act
  on `HubNpc` (character window's NPC, else the inspector's) and only hold what exists today; stage D editing goes
  there. Labels: "Warp to me / Warp to <map> / Warp to <name> / Warp here" (his wording).
- **Hub follow-up (2026-10-03, his notes; compiled, untested):** selecting (column, browser, favourites) only
  highlights (`Workspace.Selected`, first choice for `HubNpc`), no window. Column is always one column, scrolls with
  the wheel, and leaves out hidden copies (`NpcGroups.Entry.IsHiddenCopy`: sub-characters whose name differs only by
  untypeable characters, i.e. Xtardew's parked NPCs; config `HideAltsInColumn`, default on). Settings mode holds HUD,
  Labels (config `Labels`: Off / Show / Hover, replaces `ShowLabels`) and the alts toggle, saved with `WriteConfig`.
  Tile ring shows the farmer's head (`FarmerRenderer.drawMiniPortrat`, front view only: it leans but can't turn) with
  map and tile on the plate.
- **NPCs drawer and settings (2026-10-03, his notes; compiled, untested):** `NpcBrowser` is no longer a window: it
  draws the hub's NPCs mode itself (filters left, two rows of cards, `<` `>` / wheel paging, search). The game resets
  `keyboardDispatcher.Subscriber` every update while no menu is open (Game1.Update), so the search box reads keys
  from SMAPI's ButtonPressed and suppresses them (`Workspace.OnKey`). Hidden copies now also cover names with
  untypeable characters whose plain name isn't an NPC (his Witch / Scarlett copies). New config: `HideNonSocialInColumn`
  (default on; `!npc.CanSocialize` as the stand-in for "animals", since the mod adding them is unknown) and
  `UnlockDoors` (prefixes on `GameLocation.lockedDoorWarp`, `performAction` and `performTouchAction` for "Door").
- **Controls and cross-map follow (2026-10-04, his requests; compiled, untested):**
  - Config `TakeOverControls` (default on; Settings "Game controls: off"): every left click and the use-tool buttons
    are suppressed while `Workspace.BlocksGameControls` (active, no menu, no event); Harmony prefix on
    `Game1.pressSwitchToolButton` stops the wheel / triggers from switching the hotbar (the wheel event itself can't
    be suppressed in SMAPI).
  - Follow across maps (`StartFollow` / `StopFollow` / `UpdateFollow`): only the farmer's map is drawn, so the farmer
    is hidden (`Game1.displayFarmer = false`, re-set every tick because warps reset it), parked at (-1280, -1280) and
    warped to the NPC's map when `npc.currentLocation != Game1.currentLocation`. `FollowStart` (map, position,
    facing) is restored on Stop following / camera off; "Stay here" puts the farmer next to her. He chose hidden
    over walking behind her (walking can block her and trigger proximity reactions). Not yet checked in game: whether
    anything minds a farmer standing off the map.
- **Drag the map (2026-10-04, his request; compiled, untested):** a left click nothing else used calls
  `FreeCamera.BeginGrab`; after 8 px of movement with the button held the free camera switches on and the viewport
  moves with the mouse (`UpdateGrab`). Ignored while following.
- **Selection marker (2026-10-04, his idea):** a bobbing game item above `HubNpc` (`Workspace.DrawSelectionMarker`,
  config `SelectionMarker`, default `(O)60` emerald; empty = off). A game item instead of the Sims plumbob: that
  shape is EA's, and an item follows recolours.
- **Polish round (2026-10-04 evening, his list; compiled, untested):**
  - Follow camera clamps to the map like the game's (`FreeCamera.FitToMap`).
  - Camera on no longer takes the movement keys: arrows / drag move the view, WASD moves the farmer. Movement is only
    suppressed during a jump or while the farmer is parked for following.
  - Number keys 1 / 2 / 3 = time controls (`OnTimeKey`), only while `BlocksGameControls`.
  - `Ui.HoverBlocked` (set during drawing while the ring is open) makes `Ui.Mouse` report off-screen, so nothing under
    the ring hovers; clicks use `Ui.RealMouse`. Tooltips are queued and drawn by `Ui.FlushTooltip` after all windows.
  - Favourite = the game's gold quality star (`Game1.mouseCursors` 346,400,8,8), nothing when not a favourite.
  - Expression labels: `$n Vanilla` when the unmodded sheet has that cell (`Vanilla.GetExpressionCount`, raw content
    load), else the pack owning her APF portrait (parsed from Explain `portrait`: "content pack <ID>"), else Modded.
    APF has no per-expression source, so `$40` shows the owning pack, not "Harem Valley"; that needs an APF change.
  - Hidden copies are labelled `Abigail (<mod> N)`: `NpcSources` searches content packs' JSON for the quoted NPC name
    (raw and \u-escaped) on a background task.
  - **Sound while following, not solved:** the game's own positional sounds use distance from the screen
    (`SoundsHelper.GetVolumeForDistance` -> `Utility.distanceFromScreen`), so they still play. Silent are mods that
    measure from the player (his Harem Valley positional audio, the footsteps mod). Riding the hidden farmer on the
    NPC would fix it but risks blocking her and triggers proximity reactions. Proposed: those mods measure from the
    screen centre when the view is detached, via a handoff.
  - TO-TEST.md on disk had no Y/N marks (file unchanged since 2026-10-02), so no failures were read from it.
- **Expression labels, his decision 2026-10-04:** non-vanilla expressions read `$40 Custom` for now. The APF chat
  found the extra expressions live in PWR's own APF pack, so the owning pack is always PWR; naming the mod they are
  made for needs the pack to declare it (APF proposal: `ExpressionSources` ranges). Revisit when APF reports it.
- Still open from the plan: stage D editing (forms, drafts, export; tile/frame pickers would plug into the pie menu),
  world controls (weather/season/day), Modern Config Menu, flight recorder, browser panel.
- Earlier test list: restart day under SMAPI with LAF/APF/CP; a full-day jump (who doesn't settle is logged at Debug);
  auto reload under MO2; F6 conflicts.

---

## 1. What it is

One separate, optional mod. LAF and APF never depend on it. It detects which of the two is installed and shows
a tab for each. Three parts:

1. **Workspace ("braindance mode")** — a sandbox of the current day: freeze, jump, restart the day, free camera,
   layers, click an NPC to see why she looks the way she does.
2. **Self-describing editor** — forms generated from LAF/APF's own data models, so new framework fields appear
   without touching the tool.
3. **Auto patch reload** — watches the JSON of the content packs and reloads the packs that changed.

Later: browser panel (hybrid), what-if strip, flight recorder.

## 2. Agreed decisions (Glarthon)

- United tool, separate mod, self-describing.
- Enter by hotkey, console command or GUI button. Entering/leaving may show a fade or loading screen.
- "Exact point" restore is a nice-to-have. Accepted baseline: **reload the save, restart the day, then jump to a
  checkpoint** (start of day, 12:00, 18:00, 22:00, or any time).
- If the mid-day snapshot gets too advanced, drop it and keep the reload.
- Patch reload: only JSON matters for users (LAF/APF are configured through JSON); C# changes are done by Claude
  and need a relaunch anyway.
- MO2: never write into the game's `Mods` folder. Drafts stay in memory; export goes to clipboard or to an
  absolute folder outside `Mods`.

## 3. Verified facts and what they mean

### 3.1 Reloading the save without restarting the game — verified (game)
- `Game1.ExitToTitle(Action postExitCallback = null)` sets `exitToTitle` and stores the callback.
- `Game1.Update`: `if (exitToTitle) { CleanupReturningToTitle(); Utility.CollectGarbage(); postExitToTitleCallback?.Invoke(); }`.
  `CleanupReturningToTitle` does not clear the callback.
- `SaveGame.Load(string filename)` = `gameMode = 6` + `currentLoader = getLoadEnumerator(filename)`. This is exactly
  what the load menu's slot does (`LoadGameMenu.SaveFileSlot.Activate`) and what `TitleMenu` does for its debug save.

```csharp
string slot = Constants.SaveFolderName;                 // capture before leaving
Game1.ExitToTitle(() => SaveGame.Load(slot));           // "restart the day"
```
Needs an in-game test (SMAPI should see ReturnedToTitle, then SaveLoaded + DayStarted as for a normal load).

### 3.2 Time of day is not in the save — verified (game)
`SaveGame.cs` has no `timeOfDay`. A loaded day always starts at 6:00, so "return to 14:00" always means
load + jump forward.

### 3.3 Jumping forward with NPCs following their schedule — verified (game); my earlier memory was wrong
- There is **no** vanilla "catch up after festival" routine that warps NPCs. `warpToPathControllerDestination()` is
  only called by the debug command `Hurry`.
- `debug time` only sets `Game1.timeOfDay` (NPCs don't move).
- `Game1.performTenMinuteClockUpdate()` is the real clock step (public static; adds 10 min on the host, runs
  location updates, lightning, etc.).
- `NPC.checkSchedule(time)`: queues the stop for `time` if `lastAttemptedSchedule < time`, **returns early while
  the NPC still has a path**, otherwise starts the next queued leg.
- `NPC.warpToPathControllerDestination()`: pops the path down to its last 2 tiles, handling map warps on the way,
  so the NPC arrives naturally and plays her end-of-route behaviour.

Plan: one 10-minute step per game tick behind the fade, so SMAPI raises `TimeChanged` for every step and LAF/APF
react as in real play:
```csharp
void StepTenMinutes()
{
    Game1.performTenMinuteClockUpdate();
    foreach (GameLocation loc in Game1.locations)
        foreach (NPC npc in loc.characters.ToList())
            npc.warpToPathControllerDestination();       // "Hurry"
}
```
06:00 → 22:00 is 96 steps ≈ 1.6 s at 60 ticks/s. Hard cap at 25:50: never step into 26:00 (pass-out / day end).

### 3.4 Freezing time and free camera — verified (game)
- `Game1.isTimePaused` (public static) is checked in `shouldTimePass()` in singleplayer. No Harmony needed.
- `Game1.panMode` + `Game1.viewportFreeze` is vanilla's debug free camera (`debug panmode`); `UpdateGameClock`
  also holds the clock while `panMode` is on. Either reuse it or move `Game1.viewport` ourselves with
  `viewportFreeze = true`.
- `Game1.realMilliSecondsPerGameMinute` / `...TenMinutes` are plain static fields (700 / 7000), so the clock speed
  can be changed. NPC walking speed does not scale with it → real fast-forward needs more work (see §8).

### 3.5 No rewind — verified (game)
`checkSchedule` only moves forward (`lastAttemptedSchedule`, `queuedSchedulePaths`). `NPC.dayUpdate` resets
`lastAttemptedSchedule = -1`, but calling it mid-day has many side effects. So **rewind = restart the day
(3.1) + jump forward (3.3)**. This is the agreed baseline.

### 3.6 Mid-day snapshot (optional, stage E) — changed in 1.6.15
The 1.6.9 decompile has `Game1.savePathOverride` (save to / load from any folder). **1.6.15 removed it**:
`getSaveEnumerator` always writes to `Program.GetSavesFolder()/<name>_<id>/`, and `getLoadEnumerator` reads
`TryReadSaveFileWithFallback(file)` from the Saves folder. A snapshot would have to be a second slot inside the Saves
folder (temporarily changing the save name), or a copy of the save files made by the tool. Not designed yet.
- Still true: SMAPI's `Context.IsSaving()` only looks at `SaveGameMenu` / `ShippingMenu`, so a save started by the
  tool raises no `Saving`/`Saved` events in other mods.
- `cleanupBeforeSave()` mid-day is not checked yet.

### 3.7 Running `patch reload` from another mod — verified (SMAPI, CP)
- `ICommandHelper` only has `Add`. There is no `Trigger`.
- SMAPI: `internal static SCore Instance`; `private readonly CommandManager CommandManager`;
  `CommandManager.Get(string name)` returns the `Command`, whose `Callback` is `Action<string, string[]>`.
  SMAPI itself runs commands with `command.Callback.Invoke(name, args)`. Alternative: `RawCommandQueue.Add("patch reload <id>")`.
- CP `ReloadCommand`: `patch reload "<pack id>"` reloads `content.json` and every patch;
  `patch reload "<pack id>" "<include file>"` reloads only the patches of one Include file (only if that Include
  is currently enabled). "Non-patch content (like config schema and dynamic tokens) aren't reloaded."

```csharp
// reflection, wrapped in try/catch; falls back to telling the user to type the command
object core    = AccessTools.Property(scoreType, "Instance").GetValue(null);
object manager = AccessTools.Field(scoreType, "CommandManager").GetValue(core);
object command = AccessTools.Method(manager.GetType(), "Get").Invoke(manager, new object[] { "patch" });
var callback   = (Action<string, string[]>)AccessTools.Property(command.GetType(), "Callback").GetValue(command);
callback("patch", new[] { "reload", packId });           // or + includePath
```
Limits to show in the UI: ConfigSchema and DynamicTokens changes, new files under MO2, `manifest.json` and
C# all need a relaunch.

### 3.8 What LAF and APF expose today — verified (their sources)
- LAF `ILiveAppearanceApi`: `AppearanceChanged`, `Refresh`, `GetAppearanceId`, `GetExpectedAppearanceId`,
  `SafeChooseAppearance`, `IsInPose`, `GetPoseId`, `GetPoseStageId`. The "why" data exists only as console text
  (`laf_why`, `laf_schedule`, `laf_pose`, `laf_detour`, `laf_rules`, `laf_conditions`, `laf_watch`).
- APF: **no `GetApi()` at all**; diagnostics only through `apf_portraits`.
- So both need an editor API (§5). That is the only change inside the frameworks.

### 3.9 UI toolkit
StardewUI's source is in `Modding Resources\StardewUI-dev-unofficial` (and `StardewUIOld-master`). Not read yet;
decide between StardewUI and plain `IClickableMenu` before the UI stage.

## 4. Workspace flow

**Enter** (hotkey / `roshi_workspace` / GUI button)
1. Refuse if multiplayer, festival, event or menu is open.
2. Remember: save slot, time, player map/tile/facing. (Stage B: write the snapshot.)
3. Set the sandbox flag: `isTimePaused = true`, block sleeping/saving/day end, show the toolbar.

**Inside**
- Time: pause/play, step +10 min, jump to time (forward only), checkpoints 06:00 / 12:00 / 18:00 / 22:00.
  A checkpoint earlier than "now" = restart the day, then jump.
- World: weather, season, day, mail flags, hearts — applied to the live sandbox (reset by the next restart).
- Camera: free camera, jump to map or NPC.
- Layers: Outfit, Schedule, Pose/Detour, Portrait (each framework supplies its own).
- Inspect: click an NPC → her "why" panel.
- Edits: JSON on disk → auto patch reload; drafts → in memory.

**Leave**
- Default: restart the day and jump to the time you entered at, warp the player back. Fade/loading screen.
- Stage B option: return to the snapshot instead.
- Run `patch reload` for packs changed since the last reload (if any are still pending).

Safety: the real save file is never written by the tool. Worst case = the day restarts from the morning.

## 5. Self-describing editor API (in LAF and APF)

Small, read-mostly, versioned. Sketch (names not final):
```csharp
public interface IRoshiEditorApi
{
    int Version { get; }
    IEnumerable<IEditorAsset> GetAssets();          // asset name + model type (IdlePoseData, DetourData, PortraitData…)
    IEnumerable<IEditorQuery> GetQueries();         // GSQ name, alias, argument list, description
    IEnumerable<IEditorLayer> GetLayers();          // id, label; per-NPC labels/markers to draw
    IExplainResult Explain(NPC npc);                // structured "why": entries, each condition true/false, winner
}
```
- Forms are built by reflection over the model types (string → text box, bool → checkbox, list → add/remove).
- Field hints by attribute, e.g. `[EditorHint("Frame")]`, `[EditorHint("Tile")]`, `[EditorHint("Condition")]`,
  pick the special widgets (frame picker, tile capture, condition helper).
- Unknown/newer things show as plain fields, never break the tool.
- Pintail (SMAPI's API proxy) maps interfaces by shape, so the tool keeps its own copy of the interface.

## 6. Drafts and export (MO2-safe)
- Draft layer: the tool edits the LAF/APF data assets in `AssetRequested` at `AssetEditPriority.Late + 1` and
  invalidates them; discard = remove + invalidate.
- Export: clipboard (default) or a file in the configured absolute export folder
  (e.g. `D:\Modding\Stardew\mods\Roshi Exports`). Never the `Mods` folder, never the tool's own folder.
- The tool never rewrites hand-written pack JSON.

## 7. Auto patch reload
- Watch list: the folders of CP packs chosen in the config (default: packs that edit LAF/APF assets).
- Poll file timestamps about once a second (works through MO2's virtual folder; no `FileSystemWatcher`).
- A changed Include file → `patch reload <id> <file>`; `content.json` → full pack reload. Debounce ~500 ms.
- Runs in and outside the workspace. Toggle + manual "Reload now" button.

## 8. Stages
- **A — workspace core:** sandbox flag, pause, step, jump forward, restart day + checkpoints, free camera, toolbar,
  console commands. Uses only §3.1–3.5.
- **B — auto patch reload** (§3.7, §7).
- **C — editor API in LAF and APF + inspector** (click NPC → why; layers).
- **D — generated forms, pickers (tile, facing, frame), drafts, export.**
- **E — optional:** mid-day snapshot (§3.6), flight recorder (log every decision, scrub afterwards),
  what-if strip, browser panel (localhost bridge), real fast-forward.

## 9. Open points before coding
1. Re-check §3.1–3.6 against the 1.6.15 DLL (the decompile is 1.6.9-era).
2. In-game test of `ExitToTitle(() => SaveGame.Load(slot))` under SMAPI, with LAF/APF/CP loaded.
3. Jump forward: NPCs with route-end animations, sleeping NPCs, Ginger Island, festival days; whether one step
   per tick is enough for the legs to chain.
4. `cleanupBeforeSave` mid-day (only for stage E snapshot).
5. StardewUI vs plain menus.
6. Real fast-forward (running several game updates per frame) — not investigated.
7. Final name, UniqueID, display name with abbreviation, source folder.
