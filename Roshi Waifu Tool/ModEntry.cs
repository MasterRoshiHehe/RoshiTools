using System;
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

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.Commands = new CommandRunner(this.Monitor);
            this.Watcher = new PackWatcher(helper, this.Monitor, () => this.Config, this.Commands);
            this.Workspace = new Workspace(helper, this.Monitor, () => this.Config, this.Commands, this.Watcher);

            SandboxPatches.Apply(new Harmony(this.ModManifest.UniqueID), this.Monitor, () => this.Workspace.IsSandbox);

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.Workspace.OnReturnedToTitle();
            helper.Events.GameLoop.UpdateTicking += (_, _) =>
            {
                if (this.Workspace.Active)
                    this.Workspace.Camera.SuppressMovement();
            };
            helper.Events.GameLoop.UpdateTicked += (_, _) =>
            {
                this.Watcher.Update();
                this.Workspace.Update();
            };
            helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.RenderedWorld += (_, e) => this.Workspace.DrawWorld(e.SpriteBatch);
            helper.Events.Display.RenderedHud += (_, e) => this.Workspace.DrawHud(e.SpriteBatch);

            helper.ConsoleCommands.Add("rwt", "Opens or closes the workspace.\n\nUsage: rwt [exit|stay]\n- no argument: toggle.\n- exit: close and reload the save.\n- stay: close and keep the world as it is.", this.CommandToggle);
            helper.ConsoleCommands.Add("rwt_time", "Goes to a time today (workspace only). An earlier time restarts the day from your save first.\n\nUsage: rwt_time <time>, e.g. rwt_time 1430", this.CommandTime);
            helper.ConsoleCommands.Add("rwt_step", "Skips 10-minute steps (workspace only).\n\nUsage: rwt_step [count]", this.CommandStep);
            helper.ConsoleCommands.Add("rwt_restart", "Reloads the save and starts the day again, then jumps to a time (workspace only).\n\nUsage: rwt_restart [time]", this.CommandRestart);
            helper.ConsoleCommands.Add("rwt_pause", "Pauses or resumes the clock (workspace only).", (_, _) => this.Workspace.TogglePause());
            helper.ConsoleCommands.Add("rwt_speed", "Sets the clock speed while running (workspace only).\n\nUsage: rwt_speed <1-8>", this.CommandSpeed);
            helper.ConsoleCommands.Add("rwt_goto", "Warps you next to an NPC.\n\nUsage: rwt_goto <npc>", this.CommandGoto);
            helper.ConsoleCommands.Add("rwt_follow", "Keeps the camera on an NPC in your location; no argument stops following.\n\nUsage: rwt_follow [npc]", (_, args) => this.Workspace.FollowNpc(string.Join(" ", args)));
            helper.ConsoleCommands.Add("rwt_reload", "Runs 'patch reload' for a watched content pack, or for all of them.\n\nUsage: rwt_reload [pack ID]", (_, args) => this.Watcher.ReloadNow(args.Length > 0 ? args[0] : null));
            helper.ConsoleCommands.Add("rwt_packs", "Lists the content packs watched for auto reload.", this.CommandPacks);
        }

        /*********
        ** Events
        *********/
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            this.Watcher.BuildWatchList();
            this.Workspace.Inspector.HookLiveAppearance();
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
            if (e.Button == SButton.MouseLeft && this.Workspace.OnLeftClick())
                this.Helper.Input.Suppress(e.Button);
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

            gmcm.Register(
                this.ModManifest,
                reset: () =>
                {
                    this.Config = new ModConfig();
                    this.Watcher.BuildWatchList();
                },
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    this.Watcher.BuildWatchList();
                }
            );

            gmcm.AddSectionTitle(this.ModManifest, () => "Workspace");
            gmcm.AddKeybindList(this.ModManifest, () => this.Config.ToggleWorkspace, value => this.Config.ToggleWorkspace = value, () => "Open / close", () => "Opens or closes the workspace.");
            gmcm.AddTextOption(this.ModManifest, () => this.Config.Checkpoints, value => this.Config.Checkpoints = value, () => "Checkpoints", () => "The time buttons on the toolbar: 24-hour game times, comma separated (e.g. 600, 1200, 1800, 2200).");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.RestoreOnExit, value => this.Config.RestoreOnExit = value, () => "Restore on exit", () => "Closing the workspace reloads your save and returns you to the time and place you opened it at. Off: the world stays as you left it.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.JumpTicksPerStep, value => this.Config.JumpTicksPerStep = value, () => "Jump: ticks per step", () => "Game ticks between two 10-minute steps of a time jump. Higher is slower but gives other mods more time per step.", min: 1, max: 30);
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.ShowLabels, value => this.Config.ShowLabels = value, () => "NPC labels", () => "Show each NPC's appearance ID above her head while the workspace is open.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.CameraSpeed, value => this.Config.CameraSpeed = value, () => "Camera speed", () => "Free camera speed in pixels per tick (hold Shift for 3x).", min: 4, max: 64, interval: 4);

            gmcm.AddSectionTitle(this.ModManifest, () => "Auto reload");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.AutoReload, value => this.Config.AutoReload = value, () => "Reload changed packs", () => "Run 'patch reload' for a watched content pack when one of its JSON files changes on disk.");
            gmcm.AddNumberOption(this.ModManifest, () => this.Config.PollSeconds, value => this.Config.PollSeconds = value, () => "Check every (seconds)", () => "How often the watched packs are checked for changed files.", min: 1, max: 30);
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.ReloadWholePack, value => this.Config.ReloadWholePack = value, () => "Reload the whole pack", () => "On: any change reloads the whole pack (reliable). Off: a changed file other than content.json is reloaded as an Include file only, which is faster but fails if no enabled Include patch loads that file.");
            gmcm.AddBoolOption(this.ModManifest, () => this.Config.WatchAllContentPacks, value => this.Config.WatchAllContentPacks = value, () => "Watch every pack", () => "Watch every Content Patcher pack instead of only those that depend on LAF or APF. More packs can be listed in config.json (WatchedPacks).");
        }
    }
}
