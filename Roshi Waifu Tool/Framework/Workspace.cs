using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The sandbox: a frozen copy of today you can step, jump and restart. The real save is never written while it's
    /// open (the day can't end), and closing it reloads the save, so nothing done inside is kept.
    /// </summary>
    internal sealed class Workspace
    {
        /// <summary>What to do once the save has been reloaded.</summary>
        private sealed class AfterReload
        {
            public int TargetTime;
            public bool Reenter;
            public bool WarpToEntry;
            public int ReadyTicks;
            public int WaitedTicks;
        }

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;
        private readonly PackWatcher Watcher;

        public readonly TimeMachine Time;
        public readonly FreeCamera Camera;
        public readonly Inspector Inspector;
        private readonly Toolbar Toolbar = new();

        private AfterReload Pending;
        private int Speed = 1;
        private int NormalMinuteMs = 700;
        private int NormalTenMinuteMs = 7000;

        // where and when the workspace was opened (kept across day restarts)
        private int EntryTime;
        private string EntryLocation;
        private Point EntryTile;
        private int EntryFacing;

        public Workspace(IModHelper helper, IMonitor monitor, Func<ModConfig> config, CommandRunner commands, PackWatcher watcher)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Config = config;
            this.Watcher = watcher;
            this.Time = new TimeMachine(monitor, config);
            this.Camera = new FreeCamera(helper, config);
            this.Inspector = new Inspector(helper, monitor, config, commands);
        }

        /// <summary>Whether the workspace is open.</summary>
        public bool Active { get; private set; }

        /// <summary>Whether the clock is held.</summary>
        public bool Paused { get; private set; } = true;

        /// <summary>Whether the day must not end right now (workspace open, or a reload/jump of ours is running).</summary>
        public bool IsSandbox => this.Active || this.Pending != null || this.Time.IsBusy;

        /*********
        ** Open / close
        *********/
        public void Toggle()
        {
            if (this.Active)
                this.Exit(this.Config().RestoreOnExit);
            else
                this.Enter();
        }

        public bool Enter(bool quiet = false)
        {
            if (this.Active)
                return true;

            string blocker =
                !Context.IsWorldReady ? "load a save first"
                : Context.IsMultiplayer ? "it only works in singleplayer"
                : Game1.eventUp || Game1.isFestival() ? "not during an event or festival"
                : Game1.activeClickableMenu != null ? "close the menu first"
                : this.Pending != null || this.Time.IsBusy ? "wait for the current jump to finish"
                : null;
            if (blocker != null)
            {
                this.Say($"Can't open the workspace: {blocker}.", LogLevel.Warn);
                return false;
            }

            if (!quiet)
            {
                this.EntryTime = Game1.timeOfDay;
                this.EntryLocation = Game1.currentLocation.NameOrUniqueName;
                this.EntryTile = Game1.player.TilePoint;
                this.EntryFacing = Game1.player.FacingDirection;
            }

            this.NormalMinuteMs = Game1.realMilliSecondsPerGameMinute;
            this.NormalTenMinuteMs = Game1.realMilliSecondsPerGameTenMinutes;
            this.Speed = 1;
            this.Paused = true;
            this.Active = true;
            Game1.isTimePaused = true;

            if (!quiet)
                this.Say($"Workspace open at {TimeMachine.Format(Game1.timeOfDay)}. Time is paused.", LogLevel.Info);
            return true;
        }

        /// <summary>Close the workspace. With <paramref name="restore"/>, reload the save and return to where it was opened.</summary>
        public void Exit(bool restore)
        {
            if (!this.Active)
                return;

            if (this.Time.IsBusy)
            {
                this.Say("Wait for the jump to finish.", LogLevel.Warn);
                return;
            }

            if (restore)
            {
                this.Say("Closing the workspace: reloading the save.", LogLevel.Info);
                this.RestartDay(this.EntryTime, reenter: false, warpToEntry: true);
                return;
            }

            this.Close();
            this.Say("Workspace closed. The world stays as it is now.", LogLevel.Info);
        }

        /// <summary>Drop every sandbox setting without touching the world.</summary>
        private void Close()
        {
            this.Active = false;
            this.Time.Cancel();
            this.Camera.Set(false);
            this.Inspector.Enabled = false;
            this.SetSpeed(1);
            Game1.isTimePaused = false;
        }

        /// <summary>The player left to the title screen (or we did, for a restart).</summary>
        public void OnReturnedToTitle()
        {
            if (this.Active)
                this.Close();
            else
                this.Time.Cancel();
        }

        /*********
        ** Time
        *********/
        public void TogglePause()
        {
            if (!this.Active)
                return;
            if (this.Paused && Game1.timeOfDay >= TimeMachine.LatestTime)
            {
                this.Say("It's the end of the day; restart the day to go on.", LogLevel.Info);
                return;
            }
            this.Paused = !this.Paused;
        }

        public void SetSpeed(int speed)
        {
            this.Speed = Math.Clamp(speed, 1, 8);
            Game1.realMilliSecondsPerGameMinute = Math.Max(1, this.NormalMinuteMs / this.Speed);
            Game1.realMilliSecondsPerGameTenMinutes = Math.Max(10, this.NormalTenMinuteMs / this.Speed);
        }

        public void Step(int steps)
        {
            if (!this.Require())
                return;
            this.Time.JumpTo(TimeMachine.Normalize(AddMinutes(Game1.timeOfDay, steps * 10)));
        }

        /// <summary>Go to a time today: forward by stepping, backward by restarting the day first.</summary>
        public void GoTo(int time)
        {
            if (!this.Require())
                return;

            time = TimeMachine.Normalize(time);
            if (time > Game1.timeOfDay)
                this.Time.JumpTo(time);
            else if (time < Game1.timeOfDay)
                this.RestartDay(time, reenter: true, warpToEntry: false);
        }

        /// <summary>Reload the save (the day starts over at 6:00), then jump to a time.</summary>
        public void RestartDay(int time, bool reenter, bool warpToEntry)
        {
            string slot = Constants.SaveFolderName;
            if (string.IsNullOrEmpty(slot))
            {
                this.Say("Can't restart the day: no save slot.", LogLevel.Error);
                return;
            }

            this.Pending = new AfterReload { TargetTime = TimeMachine.Normalize(time), Reenter = reenter, WarpToEntry = warpToEntry };
            this.Close();
            this.Monitor.Log($"Reloading save '{slot}', then going to {this.Pending.TargetTime}.", LogLevel.Debug);

            // Verified in 1.6.15: Game1.Update runs CleanupReturningToTitle() and then this callback;
            // SaveGame.Load + exitActiveMenu is what the load menu's save slot does.
            Game1.ExitToTitle(() =>
            {
                SaveGame.Load(slot);
                Game1.exitActiveMenu();
            });
        }

        private bool Require()
        {
            if (!this.Active)
            {
                this.Say("Open the workspace first.", LogLevel.Warn);
                return false;
            }
            if (this.Time.IsBusy)
            {
                this.Say("A jump is already running.", LogLevel.Warn);
                return false;
            }
            return true;
        }

        private static int AddMinutes(int time, int minutes)
        {
            int total = time / 100 * 60 + time % 100 + minutes;
            return total / 60 * 100 + total % 60;
        }

        /*********
        ** Per tick
        *********/
        public void Update()
        {
            if (this.Pending != null)
                this.UpdatePending();

            if (!Context.IsWorldReady)
                return;

            bool wasBusy = this.Time.IsBusy;
            this.Time.Update();

            if (this.Active)
            {
                if (Context.IsMultiplayer)
                {
                    this.Close();
                    this.Say("Workspace closed: another player joined.", LogLevel.Warn);
                    return;
                }

                if (!this.Paused && Game1.timeOfDay >= TimeMachine.LatestTime)
                {
                    this.Paused = true;
                    this.Say("End of the day: time paused.", LogLevel.Info);
                }

                Game1.isTimePaused = this.Paused || this.Time.IsBusy;
                this.Camera.Update();
            }
            else if (wasBusy)
                Game1.isTimePaused = this.Time.IsBusy; // a jump after closing: hold the clock until it's done
        }

        private void UpdatePending()
        {
            AfterReload pending = this.Pending;
            if (++pending.WaitedTicks > 60 * 60)
            {
                this.Pending = null;
                this.Say("The save didn't finish loading as expected; the workspace state was dropped.", LogLevel.Warn);
                return;
            }

            bool ready = Context.IsWorldReady && Game1.currentLocation != null && !Game1.eventUp && Game1.activeClickableMenu == null
                && !Game1.fadeToBlack && !Game1.newDay && Game1.player.CanMove;
            if (!ready)
            {
                pending.ReadyTicks = 0;
                return;
            }
            if (++pending.ReadyTicks < 30)
                return;

            this.Pending = null;
            if (pending.Reenter)
                this.Enter(quiet: true);

            Game1.isTimePaused = true;
            this.Time.JumpTo(pending.TargetTime, () =>
            {
                if (pending.WarpToEntry && this.EntryLocation != null && Game1.getLocationFromName(this.EntryLocation) != null)
                    Game1.warpFarmer(this.EntryLocation, this.EntryTile.X, this.EntryTile.Y, this.EntryFacing);
                if (!this.Active)
                    Game1.isTimePaused = false;
                this.Say(pending.Reenter
                    ? $"Day restarted; it's {TimeMachine.Format(Game1.timeOfDay)}."
                    : $"Back at {TimeMachine.Format(Game1.timeOfDay)}. The day was reloaded from your save.", LogLevel.Info);
            });
        }

        /*********
        ** Other tools
        *********/
        /// <summary>Warp the farmer next to an NPC.</summary>
        public void GoToNpc(string name)
        {
            NPC npc = Utility.fuzzyCharacterSearch(name, must_be_villager: false);
            if (npc?.currentLocation == null)
            {
                this.Say($"No NPC found matching '{name}'.", LogLevel.Warn);
                return;
            }

            this.Camera.Follow = null;
            Game1.warpFarmer(npc.currentLocation.NameOrUniqueName, npc.TilePoint.X, npc.TilePoint.Y + 1, 0);
            this.Say($"{npc.Name} is in {npc.currentLocation.NameOrUniqueName} at {npc.TilePoint.X} {npc.TilePoint.Y}.", LogLevel.Info);
        }

        /// <summary>Keep the camera on an NPC (she must be in your location), or stop following.</summary>
        public void FollowNpc(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                this.Camera.Follow = null;
                return;
            }

            NPC npc = Utility.fuzzyCharacterSearch(name, must_be_villager: false);
            if (npc == null)
            {
                this.Say($"No NPC found matching '{name}'.", LogLevel.Warn);
                return;
            }

            this.Camera.Set(true);
            this.Camera.Follow = npc;
        }

        /*********
        ** Input and drawing
        *********/
        /// <summary>Handle a left click. Returns true if the workspace used it.</summary>
        public bool OnLeftClick()
        {
            if (!this.Active || Game1.activeClickableMenu != null || Game1.eventUp || !Game1.displayHUD || this.Time.IsBusy)
                return false;

            var mouse = new Point(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            Toolbar.Button button = this.Toolbar.GetButtonAt(mouse);
            if (button != null)
            {
                Game1.playSound("smallSelect");
                button.OnClick?.Invoke();
                return true;
            }

            if (this.Inspector.Enabled)
            {
                NPC npc = this.Inspector.GetHovered();
                if (npc != null)
                {
                    this.Inspector.Inspect(npc);
                    return true;
                }
            }

            return false;
        }

        public void DrawWorld(SpriteBatch b)
        {
            if (this.Active)
                this.Inspector.DrawWorld(b);
        }

        public void DrawHud(SpriteBatch b)
        {
            if (this.Time.IsBusy || this.Pending != null)
            {
                string text = this.Pending != null ? "Restarting the day..."
                    : this.Time.IsSettling ? $"{TimeMachine.Format(Game1.timeOfDay)} - NPCs are catching up..."
                    : $"{TimeMachine.Format(Game1.timeOfDay)} -> {TimeMachine.Format(this.Time.TargetTime)}";
                b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.6f);
                Vector2 size = Game1.dialogueFont.MeasureString(text);
                b.DrawString(Game1.dialogueFont, text, new Vector2((Game1.uiViewport.Width - size.X) / 2f, (Game1.uiViewport.Height - size.Y) / 2f), Color.White);
                return;
            }

            if (!this.Active)
                return;

            string status = $"RWT workspace | {TimeMachine.Format(Game1.timeOfDay)} | {(this.Paused ? "paused" : $"running x{this.Speed}")} | {Game1.currentLocation?.NameOrUniqueName}";
            this.Toolbar.Draw(b, status, this.BuildButtons());
        }

        private List<Toolbar.Button> BuildButtons()
        {
            var buttons = new List<Toolbar.Button>
            {
                new() { Label = this.Paused ? "Play" : "Pause", Active = !this.Paused, Tooltip = "Let the clock run, or hold it.", OnClick = this.TogglePause },
                new() { Label = $"x{this.Speed}", Tooltip = "Clock speed while running (NPCs still walk at normal speed).", OnClick = () => this.SetSpeed(this.Speed >= 4 ? 1 : this.Speed * 2) },
                new() { Label = "+10m", Tooltip = "Skip 10 minutes.", OnClick = () => this.Step(1) },
                new() { Label = "+1h", Tooltip = "Skip one hour.", OnClick = () => this.Step(6) }
            };

            foreach (int time in ParseCheckpoints(this.Config().Checkpoints))
            {
                bool back = time < Game1.timeOfDay;
                buttons.Add(new Toolbar.Button
                {
                    Label = (back ? "<" : "") + TimeMachine.Format(time),
                    Tooltip = back ? "Earlier today: restarts the day from your save, then jumps here." : "Jump forward to this time.",
                    OnClick = () => this.GoTo(time)
                });
            }

            buttons.Add(new() { Label = "Restart day", Tooltip = "Reload the save and start the day again at 6:00.", OnClick = () => this.RestartDay(600, reenter: true, warpToEntry: false) });
            buttons.Add(new() { Label = "Camera", Active = this.Camera.Enabled, Tooltip = "Free camera: move with the movement or arrow keys, Shift = faster.", OnClick = () => this.Camera.Set(!this.Camera.Enabled) });
            buttons.Add(new() { Label = "Inspect", Active = this.Inspector.Enabled, Tooltip = "Hover an NPC for details; click her for the full report in the SMAPI console.", OnClick = () => this.Inspector.Enabled = !this.Inspector.Enabled });
            buttons.Add(new() { Label = "Reload packs", Tooltip = "Run 'patch reload' for every watched content pack now.", OnClick = () => this.Watcher.ReloadNow() });
            buttons.Add(new() { Label = "Exit", Tooltip = this.Config().RestoreOnExit ? "Close: reloads the save and returns you to where you opened the workspace." : "Close and keep the world as it is.", OnClick = () => this.Exit(this.Config().RestoreOnExit) });
            return buttons;
        }

        internal static List<int> ParseCheckpoints(string text)
        {
            var times = new List<int>();
            foreach (string part in (text ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out int time))
                    times.Add(TimeMachine.Normalize(time));
            }
            return times.Distinct().OrderBy(time => time).ToList();
        }

        private void Say(string message, LogLevel level)
        {
            this.Monitor.Log(message, level);
            if (Context.IsWorldReady)
                Game1.addHUDMessage(new HUDMessage(message) { noIcon = true });
        }
    }
}
