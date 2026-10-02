# Roshi Tool (working title) — design draft

Status: **v0.1.0 coded 2026-10-02 (stages A + B), compiles against the real 1.6.15 DLLs, untested in game.**
Mod name "Roshi Waifu Tool RWT", UniqueID `MasterRoshiHehe.RoshiWaifuTool`. Source folder (Glarthon): `D:\Modding\Stardew\mods\Roshi Waifu Tool`
(first delivered to `Waifu Claude Sources\Roshi Tool\Roshi Waifu Tool` because the folder-access prompt wasn't answered).
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
- To test first: restart day under SMAPI with LAF/APF/CP; a full-day jump (who doesn't settle is logged at Debug);
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
