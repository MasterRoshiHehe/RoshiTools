using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoshiWaifuTool.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace RoshiWaifuTool
{
    /// <summary>The mod entry point.</summary>
    internal sealed class ModEntry : Mod
    {
        private ModConfig Config;
        private CommandRunner Commands;
        private PackWatcher Watcher;
        private Workspace Workspace;
        private IGmcmApi Gmcm;

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.Commands = new CommandRunner(this.Monitor);
            this.Watcher = new PackWatcher(helper, this.Monitor, () => this.Config, this.Commands);
            this.Workspace = new Workspace(helper, this.Monitor, () => this.Config, this.Commands, this.Watcher);

            SandboxPatches.Apply(new Harmony(this.ModManifest.UniqueID), this.Monitor, () => this.Workspace.IsSandbox, () => this.Config.IgnoreEvents && this.Workspace.IsSandbox, () => this.Workspace.HidesHud, () => this.Workspace.ReservedBottomLeft, () => this.Config.UnlockDoors && this.Workspace.Active, () => this.Workspace.BlocksGameControls);

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Workspace.OnReturnedToTitle();
            helper.Events.GameLoop.UpdateTicking += (_, _) =>
            {
                this.Workspace.BeforeInput();
            };
            helper.Events.GameLoop.UpdateTicked += (_, _) =>
            {
                this.Watcher.Update();
                this.Workspace.Update();
            };
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += (_, e) => this.Workspace.OnScroll(e.Delta);
            helper.Events.Display.RenderedWorld += (_, e) => this.Workspace.DrawWorld(e.SpriteBatch);
            // Drawn at the start of the game's overlay step, not with the HUD: the overlay step comes after the HUD and
            // after other mods' HUD drawing (e.g. UI Info Suite's icons), and before the mouse cursor, so the tool's
            // windows are on top of everything but the cursor. The sprite batch is open in UI space here.
            helper.Events.Display.RenderingStep += (_, e) =>
            {
                if (e.Step == StardewValley.Mods.RenderSteps.Overlays)
                    this.Workspace.DrawHud(e.SpriteBatch);
            };

            helper.ConsoleCommands.Add("rwt", "Opens or closes the workspace.\n\nUsage: rwt [exit|stay]\n- no argument: toggle.\n- exit: close and reload the save.\n- stay: close and keep the world as it is.", this.CommandToggle);
            helper.ConsoleCommands.Add("rwt_time", "Goes to a time today (workspace only). An earlier time restarts the day from your save first.\n\nUsage: rwt_time <time>, e.g. rwt_time 1430", this.CommandTime);
            helper.ConsoleCommands.Add("rwt_step", "Skips 10-minute steps (workspace only).\n\nUsage: rwt_step [count]", this.CommandStep);
            helper.ConsoleCommands.Add("rwt_restart", "Reloads the save and starts the day again, then jumps to a time (workspace only).\n\nUsage: rwt_restart [time]", this.CommandRestart);
            helper.ConsoleCommands.Add("rwt_pause", "Pauses or resumes the clock (workspace only).", (_, _) => this.Workspace.TogglePause());
            helper.ConsoleCommands.Add("rwt_speed", "Sets the clock speed while running (workspace only).\n\nUsage: rwt_speed <1-8>", this.CommandSpeed);
            helper.ConsoleCommands.Add("rwt_goto", "Warps you next to an NPC.\n\nUsage: rwt_goto <npc>", this.CommandGoto);
            helper.ConsoleCommands.Add("rwt_inspect", "Opens the inspector panel for an NPC (workspace only).\n\nUsage: rwt_inspect <npc>", (_, args) => this.Workspace.InspectNpc(string.Join(" ", args)));
            helper.ConsoleCommands.Add("rwt_follow", "Keeps the camera on an NPC in your location; no argument stops following.\n\nUsage: rwt_follow [npc]", (_, args) => this.Workspace.FollowNpc(string.Join(" ", args)));
            helper.ConsoleCommands.Add("rwt_reload", "Runs 'patch reload' for a watched content pack, or for all of them.\n\nUsage: rwt_reload [pack ID]", (_, args) => this.Watcher.ReloadNow(args.Length > 0 ? args[0] : null));
            helper.ConsoleCommands.Add("rwt_npcs", "Lists every character the browser knows, with unusual characters in her name written as codes, and her sub-characters.", (_, _) =>
            {
                if (!Context.IsWorldReady)
                    this.Monitor.Log("Load a save first.", LogLevel.Warn);
                else
                    this.Monitor.Log("Characters (sub-characters indented):\n" + string.Join("\n", this.Workspace.Browser.Describe()), LogLevel.Info);
            });
            helper.ConsoleCommands.Add("rwt_packs", "Lists the content packs watched for auto reload.", this.CommandPacks);
        }

        /*********
        ** Events
        *********/
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this.Watcher.BuildWatchList();
            this.Workspace.Inspector.HookFrameworks();
            this.Workspace.Time.GetScanNow = () => this.Workspace.Inspector.ScanNow;
            this.RegisterConfigMenu();
        }

        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            if (this.Config.ToggleWorkspace.JustPressed())
            {
                this.Helper.Input.SuppressActiveKeybinds(this.Config.ToggleWorkspace);
                this.Workspace.Toggle();
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (e.Button == SButton.MouseLeft && (this.Workspace.OnLeftClick() || this.Workspace.BlocksGameControls))
                this.Helper.Input.Suppress(e.Button);
            else if (e.Button == SButton.MouseRight && this.Workspace.OnRightClick())
                this.Helper.Input.Suppress(e.Button);
            else if (this.Workspace.OnKey(e.Button))
                this.Helper.Input.Suppress(e.Button); // typed into the tool's search box
            else if (this.Workspace.BlocksGameControls && Game1.options.useToolButton.Any(button => button.ToSButton() == e.Button))
                this.Helper.Input.Suppress(e.Button); // the use-tool key
        }

        /*********
        ** Console commands
        *********/
        private void CommandToggle(string name, string[] args)
        {
            switch (args.FirstOrDefault()?.ToLowerInvariant())
            {
                case "exit":
                    this.Workspace.Exit(restore: true);
                    break;
                case "stay":
                    this.Workspace.Exit(restore: false);
                    break;
                default:
                    this.Workspace.Toggle();
                    break;
            }
        }

        private void CommandTime(string name, string[] args)
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int time))
            {
                this.Monitor.Log("Usage: rwt_time <time>, e.g. rwt_time 1430", LogLevel.Info);
                return;
            }
            this.Workspace.GoTo(time);
        }

        private void CommandStep(string name, string[] args)
        {
            int count = args.Length > 0 && int.TryParse(args[0], out int parsed) ? Math.Max(1, parsed) : 1;
            this.Workspace.Step(count);
        }

        private void CommandRestart(string name, string[] args)
        {
            if (!this.Workspace.Active)
            {
                this.Monitor.Log("Open the workspace first (rwt).", LogLevel.Info);
                return;
            }
            int time = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 600;
            this.Workspace.RestartDay(time, reenter: true, warpToEntry: false);
        }

        private void CommandSpeed(string name, string[] args)
        {
            if (!this.Workspace.Active || args.Length < 1 || !int.TryParse(args[0], out int speed))
            {
                this.Monitor.Log("Usage (workspace only): rwt_speed <1-8>", LogLevel.Info);
                return;
            }
            this.Workspace.SetSpeed(speed);
        }

        private void CommandGoto(string name, string[] args)
        {
            if (!Context.IsWorldReady || args.Length < 1)
            {
                this.Monitor.Log("Usage (in a loaded save): rwt_goto <npc>", LogLevel.Info);
                return;
            }
            this.Workspace.GoToNpc(string.Join(" ", args));
        }

        private void CommandPacks(string name, string[] args)
        {
            string[] lines = this.Watcher.Describe().ToArray();
            this.Monitor.Log(lines.Length == 0
                ? "No content packs are watched. By default, Content Patcher packs that depend on LAF or APF are; add others to WatchedPacks in config.json."
                : $"Auto reload is {(this.Config.AutoReload ? "on" : "off")}. Watched packs:\n- {string.Join("\n- ", lines)}", LogLevel.Info);
        }

        /*********
        ** Config menu
        *********/
        /// <summary>The toolbar's "Settings" buttons: one per mod whose Generic Mod Config Menu page can be opened.</summary>
        private void BuildConfigMenuButtons()
        {
            var menus = new List<KeyValuePair<string, Action>>();
            if (this.Gmcm != null)
            {
                var ids = new List<string> { this.ModManifest.UniqueID, "MasterRoshiHehe.LiveAppearanceFramework", "tyr4ntx.AnimatedPortraitFramework" };
                ids.AddRange(this.Watcher.GetPackIds());
                ids.AddRange(this.Config.ConfigMenuMods ?? new List<string>());

                foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    IManifest manifest = id == this.ModManifest.UniqueID ? this.ModManifest : this.Helper.ModRegistry.Get(id)?.Manifest;
                    if (manifest == null)
                        continue;

                    menus.Add(new KeyValuePair<string, Action>(ToLabel(manifest.Name), () =>
                    {
                        try
                        {
                            this.Gmcm.OpenModMenu(manifest);
                        }
                        catch (Exception ex)
                        {
                            this.Monitor.Log($"Couldn't open the config menu of {manifest.Name} (it may not have one): {ex.Message}", LogLevel.Warn);
                            Game1.addHUDMessage(new HUDMessage($"{manifest.Name} has no config menu") { noIcon = true });
                        }
                    }));
                }
            }
            this.Workspace.ConfigMenus = menus;
        }

        /// <summary>A mod name as a short button label the game font can draw.</summary>
        private static string ToLabel(string name)
        {
            string text = new string((name ?? "?").Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray()).Trim();
            return text.Length > 30 ? text.Substring(0, 29) + "." : text;
        }

        private void RegisterConfigMenu()
        {
            IGmcmApi gmcm;
            try
            {
                gmcm = this.Helper.ModRegistry.GetApi<IGmcmApi>("spacechase0.GenericModConfigMenu");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read Generic Mod Config Menu's API: {ex.Message}", LogLevel.Trace);
                return;
            }
            if (gmcm == null)
                return;
            this.Gmcm = gmcm;

            gmcm.Register(
                this.ModManifest,
                reset: () =>
                {
                    this.Config = new ModConfig();
                    this.Watcher.BuildWatchList();
                    this.BuildConfigMenuButtons();
                },
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    this.Watcher.BuildWatchList();
                    this.BuildConfigMenuButtons();
                }
            );
            this.BuildConfigMenuButtons();

            gmcm.AddSectionTitle(this.ModManifest, () => "Workspace");
            gmcm.AddKeybindList(this.ModManifest, () => this.Config.ToggleWorkspace, value => this.Config.ToggleWorkspace = value, () => "Open / close", () => "Opens or closes the workspace.");
            gmcm.AddTextOption(this.ModManifest, () => this.Config.Checkpoints, value => this.Config.Checkpoints = value, () => "Checkpoints", () => "The time buttons on the toolbar: 24-hour game times, comma separated (e.g. 600, 1200, 1800, 2200).");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.RestoreOnExit, value => this.Config.RestoreOnExit = value, () => "Restore on exit", () => "Closing the workspace reloads your save and returns you to the time and place you opened it at. Off: the world stays as you left it.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.HideGameHud, value => this.Config.HideGameHud = value, () => "Hide the game's HUD", () => "Hide the game's HUD (hotbar, energy, buffs) and other mods' HUD icons when the workspace opens. The toolbar's HUD button switches it; closing the workspace restores it.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.KeepClock, value => this.Config.KeepClock = value, () => "Keep the clock", () => "Keep the game's clock in the top right corner while the HUD is hidden.");
gmcm.AddBoolOption(this.ModManifest, () => this.Config.TakeOverControls, value => this.Config.TakeOverControls = value, () => "Take over game controls", () => "While the workspace is open, left click, the use-tool key and the mouse wheel belong to the tool: the farmer doesn't swing a tool or switch items. The interact key still works.");
                        gmcm.AddBoolOption(this.ModManifest, () => this.Config.PieMenu, value => this.Config.PieMenu = value, () => "Right-click options", () => "Right-click a character or a tile in the workspace to open a ring of options. Hold Shift to right-click normally.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.IgnoreEvents, value => this.Config.IgnoreEvents = value, () => "Ignore events", () => "Don't start events (cutscenes) when you enter a location while the workspace is open. They aren't marked as seen, so they play normally later.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.JumpStepTicks, value => this.Config.JumpStepTicks = value, () => "Jump: ticks per step (no hook)", () => "Used only with an older LAF that has no scan hook. 10 lets LAF see every step, so detours start as in real play. Lower is faster but less accurate.", min: 1, max: 30);
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.FastJumpStepTicks, value => this.Config.FastJumpStepTicks = value, () => "Jump: ticks per step (fast)", () => "Used when LAF has its scan hook (LAF with the editor API): the tool makes LAF scan after every step, so jumps can be fast and still accurate. Minimum 3.", min: 3, max: 30);
            gmcm.AddTextOption(this.ModManifest, () => this.Config.Labels, value => this.Config.Labels = value, () => "NPC labels", () => "The text boxes above characters (appearance ID and pose) while the workspace is open: off, always shown, or only for the one under the cursor.", allowedValues: new[] { "Off", "Show", "Hover" });
gmcm.AddTextOption(this.ModManifest, () => this.Config.SelectionMarker, value => this.Config.SelectionMarker = value?.Trim() ?? "", () => "Selection marker", () => "The item floating above the selected character, as an item ID: (O)60 emerald, (O)72 diamond, (O)74 prismatic shard, or any other. Empty = no marker.");
                        gmcm.AddBoolOption(this.ModManifest, () => this.Config.HideAltsInColumn, value => this.Config.HideAltsInColumn = value, () => "Hide alts in the map column", () => "Leave hidden copies of characters (extra NPCs some mods park out of reach) out of the column of faces on the left.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.HideNonSocialInColumn, value => this.Config.HideNonSocialInColumn = value, () => "Hide animals in the map column", () => "Leave characters you can't befriend (animals and other background NPCs mods add) out of the column of faces on the left.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.UnlockDoors, value => this.Config.UnlockDoors = value, () => "All doors unlocked", () => "While the workspace is open: shops and houses open at any hour, and bedroom doors open without friendship.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.CameraSpeed, value => this.Config.CameraSpeed = value, () => "Camera speed", () => "Free camera speed in pixels per tick (hold Shift for 3x).", min: 4, max: 64, interval: 4);

            gmcm.AddSectionTitle(this.ModManifest, () => "Auto reload");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.AutoReload, value => this.Config.AutoReload = value, () => "Reload changed packs", () => "Run 'patch reload' for a watched content pack when one of its JSON files changes on disk.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.PollSeconds, value => this.Config.PollSeconds = value, () => "Check every (seconds)", () => "How often the watched packs are checked for changed files.", min: 1, max: 30);
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.ReloadWholePack, value => this.Config.ReloadWholePack = value, () => "Reload the whole pack", () => "On: any change reloads the whole pack (reliable). Off: a changed file other than content.json is reloaded as an Include file only, which is faster but fails if no enabled Include patch loads that file.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.WatchAllContentPacks, value => this.Config.WatchAllContentPacks = value, () => "Watch every pack", () => "Watch every Content Patcher pack instead of only those that depend on LAF or APF. More packs can be listed in config.json (WatchedPacks).");
        }
    }
}
