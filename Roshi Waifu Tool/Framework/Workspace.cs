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
        private readonly CommandRunner Commands;

        public readonly TimeMachine Time;
        public readonly FreeCamera Camera;
        public readonly Inspector Inspector;
        public readonly InspectorPanel Panel;
        public readonly CharacterWindow Character;
        public readonly NpcBrowser Browser;
        private readonly Random Random = new();
        /// <summary>The character picked in the column, the browser or the favourites; the hub's LAF and APF modes act on her.</summary>
        private NPC Selected;
        /// <summary>The NPCs wearing a previewed outfit, to give them their own outfit back when the workspace closes.</summary>
        private readonly HashSet<NPC> Wearing = new();
        private List<NPC> Villagers = new();
        private int VillagersAge = int.MaxValue - 1;
        private readonly Toolbar Toolbar = new();
        private readonly CharacterBar Bar = new();
        private readonly PieMenu Pie = new();
        /// <summary>Whether the game's HUD (and other mods' HUD drawing) is hidden while the workspace is open.</summary>
        private bool HudHidden;
        private bool HudWasShown = true;

        /// <summary>The config menus the toolbar can open: button label → action. Set by the mod entry once GMCM is known.</summary>
        public List<KeyValuePair<string, Action>> ConfigMenus { get; set; } = new();
        /// <summary>The hub's open mode (Test, LAF, APF, NPCs, Settings), or null when it is folded.</summary>
        private string Mode = "Test";

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
            this.Commands = commands;
            this.Time = new TimeMachine(monitor, config);
            this.Camera = new FreeCamera(helper, config);
            this.Inspector = new Inspector(helper, monitor, config, commands);
            this.Panel = new InspectorPanel(this.Inspector.GetTrees, this.Inspector.DumpToConsole);
            this.Character = new CharacterWindow(monitor, this.Inspector.GetApfTree, new CharacterWindow.Actions
            {
                GoTo = this.GoToNpc,
                BringHere = this.BringHere,
                GoHome = this.GoToHome,
                Inspect = npc => this.Panel.Open(npc),
                Wear = this.Wear,
                StopWearing = this.StopWearing,
                IsWearing = npc => this.Wearing.Contains(npc),
                Follow = this.StartFollow
            });
            this.Browser = new NpcBrowser(helper, monitor, config, this.GetVillagers, () => this.HubNpc);
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
            this.HudWasShown = Game1.displayHUD;
            this.HudHidden = this.Config().HideGameHud;

            if (!quiet)
                this.Say($"Workspace open at {TimeMachine.Format(Game1.timeOfDay)}. The clock is held.", LogLevel.Info);
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
            this.FollowStart = null;
            this.FollowParked = false;
            Game1.displayFarmer = true;
            this.Camera.Set(false);
            this.Inspector.Enabled = false;
            this.Panel.Close();
            this.Character.Close();
            this.Pie.Close();
            this.Selected = null;
            if (this.HudHidden)
                Game1.displayHUD = this.HudWasShown;
            this.HudHidden = false;
            foreach (NPC npc in this.Wearing.ToList())
                this.StopWearing(npc);
            this.Browser.Visible = false;
            this.Browser.StopTyping();
            this.SetSpeed(1);
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
        /// <summary>Call before the game reads input: keeps the farmer still while the camera is free or a jump runs.</summary>
        public void BeforeInput()
        {
            if (!Context.IsWorldReady)
                return;

            bool jumping = this.Time.IsBusy || this.Pending != null;
            if (jumping)
                Game1.player.Halt();
            if (jumping || this.FollowStart != null)
                this.Camera.SuppressMovement(); // a jump runs, or the farmer is hidden and parked while following
            this.HoldClock();
        }

        public void Update()
        {
            if (this.Pending != null)
                this.UpdatePending();

            if (!Context.IsWorldReady)
                return;

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

                this.UpdateFollow();
                this.Camera.Update();
                if (Game1.ticks % 300 == 0)
                    this.NonSocial.Clear();
                if (Game1.ticks % 300 == 0)
                    Faces.Clear(); // a pack reload repaints the same texture, so read the faces again now and then
                bool leftDown = this.Helper.Input.IsDown(SButton.MouseLeft) || this.Helper.Input.IsSuppressed(SButton.MouseLeft);
                Point mouse = Ui.RealMouse;
                this.Panel.Update(leftDown, mouse);
                this.Character.Update(leftDown, mouse);
                if (this.HudHidden && !Game1.eventUp)
                    Game1.displayHUD = false; // warps and events turn it back on
            }

            this.HoldClock();
        }

        /// <summary>
        /// Keep the clock from advancing while it's paused or a jump of ours runs. This zeroes the game's 10-minute
        /// timer instead of using Game1.isTimePaused: GameLocation.updateCharacters only updates NPCs while
        /// Game1.shouldTimePass() is true, so a real time pause would freeze every NPC (no walking, no animations,
        /// and walks moved by a jump would never finish).
        /// </summary>
        private void HoldClock()
        {
            if (this.Time.IsBusy || (this.Active && this.Paused))
                Game1.gameTimeInterval = 0;
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

            this.Time.JumpTo(pending.TargetTime, () =>
            {
                if (pending.WarpToEntry && this.EntryLocation != null && Game1.getLocationFromName(this.EntryLocation) != null)
                    Game1.warpFarmer(this.EntryLocation, this.EntryTile.X, this.EntryTile.Y, this.EntryFacing);
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
            this.GoToNpc(npc);
        }

        /// <summary>
        /// Make an NPC wear an outfit in the world: load the sprite and portrait and mark both as overridden, exactly
        /// like the event commands changeSprite / changePortrait do. The game (NPC.TryLoadSprites / TryLoadPortraits)
        /// and LAF (RefreshEngine.CanHandle) leave an NPC with overridden textures alone, so the outfit stays until
        /// <see cref="StopWearing"/>. APF in ContentPatcher mode reads the variant from the portrait's asset name, so
        /// her dialogue portrait follows too.
        /// </summary>
        public void Wear(NPC npc, string spriteAsset, string portraitAsset)
        {
            if (npc == null)
                return;

            var done = new List<string>();
            try
            {
                if (!string.IsNullOrWhiteSpace(spriteAsset) && Game1.content.DoesAssetExist<Texture2D>(spriteAsset))
                {
                    npc.spriteOverridden = true;
                    npc.Sprite.LoadTexture(spriteAsset);
                    done.Add("sprite");
                }
                if (!string.IsNullOrWhiteSpace(portraitAsset) && Game1.content.DoesAssetExist<Texture2D>(portraitAsset))
                {
                    Texture2D portrait = Game1.content.Load<Texture2D>(portraitAsset);
                    portrait.Name = portraitAsset;
                    npc.portraitOverridden = true;
                    npc.Portrait = portrait;
                    done.Add("portrait");
                }
            }
            catch (Exception ex)
            {
                this.Say($"Couldn't put that outfit on {npc.Name}: {ex.Message}", LogLevel.Warn);
            }

            if (done.Count == 0)
            {
                this.Say($"Neither {spriteAsset} nor {portraitAsset} exists, so {npc.Name} keeps her outfit.", LogLevel.Warn);
                return;
            }

            this.Wearing.Add(npc);
            this.Say($"{npc.Name} wears it ({string.Join(" + ", done)}) until you click Take off or close the workspace.", LogLevel.Info);
        }

        /// <summary>Give an NPC her own outfit back.</summary>
        public void StopWearing(NPC npc)
        {
            if (npc == null || !this.Wearing.Remove(npc))
                return;

            npc.spriteOverridden = false;
            npc.portraitOverridden = false;
            this.Inspector.ChooseAppearance(npc);
        }

        /// <summary>Warp an NPC next to the farmer. Her schedule walk is dropped; restart the day to put her back.</summary>
        public void BringHere(NPC npc)
        {
            if (npc == null || Game1.currentLocation == null)
                return;
            if (this.FollowStart != null)
            {
                this.Say("Stop following first: your farmer is hidden while the camera follows someone.", LogLevel.Warn);
                return;
            }

            Point tile = Game1.player.TilePoint;
            Vector2 target = Game1.player.FacingDirection switch
            {
                0 => new Vector2(tile.X, tile.Y - 1),
                1 => new Vector2(tile.X + 1, tile.Y),
                3 => new Vector2(tile.X - 1, tile.Y),
                _ => new Vector2(tile.X, tile.Y + 1)
            };
            this.BringTo(npc, Game1.currentLocation, new Point((int)target.X, (int)target.Y));
        }

        /// <summary>Warp an NPC onto a tile. Her schedule walk is dropped; restart the day to put her back.</summary>
        public void BringTo(NPC npc, GameLocation location, Point tile)
        {
            if (npc == null || location == null)
                return;

            npc.controller = null;
            npc.temporaryController = null;
            npc.Halt();
            Game1.warpCharacter(npc, location, new Vector2(tile.X, tile.Y));
            this.Say($"{npc.Name} is at {location.NameOrUniqueName} {tile.X} {tile.Y} now. Her schedule walk was dropped; restart the day to put her back.", LogLevel.Info);
        }

        /// <summary>Warp the farmer to an NPC's home map, onto her default tile.</summary>
        public void GoToHome(NPC npc)
        {
            string home = npc?.DefaultMap;
            if (string.IsNullOrEmpty(home) || Game1.getLocationFromName(home) == null)
            {
                this.Say($"{npc?.Name} has no home map.", LogLevel.Warn);
                return;
            }

            this.StopFollow(goBack: false);
            Vector2 tile = npc.DefaultPosition / 64f;
            Game1.warpFarmer(home, (int)tile.X, (int)tile.Y, 2);
        }

        /// <summary>Warp the farmer next to an NPC.</summary>
        public void GoToNpc(NPC npc)
        {
            if (npc?.currentLocation == null)
                return;

            this.StopFollow(goBack: false);
            Game1.warpFarmer(npc.currentLocation.NameOrUniqueName, npc.TilePoint.X, npc.TilePoint.Y + 1, 0);
            this.Say($"{npc.Name} is in {npc.currentLocation.NameOrUniqueName} at {npc.TilePoint.X} {npc.TilePoint.Y}.", LogLevel.Info);
        }

        /// <summary>Keep the camera on an NPC (she must be in your location), or stop following.</summary>
        public void FollowNpc(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                this.StopFollow(goBack: true);
                return;
            }

            NPC npc = Utility.fuzzyCharacterSearch(name, must_be_villager: false);
            if (npc == null)
            {
                this.Say($"No NPC found matching '{name}'.", LogLevel.Warn);
                return;
            }

            this.StartFollow(npc);
        }

        /*********
        ** Following an NPC
        *********/
        /// <summary>Where the farmer was when following began (to go back to), or null while not following.</summary>
        private (string Location, Vector2 Position, int Facing)? FollowStart;
        private bool FollowParked;

        /// <summary>Where the hidden farmer waits while the camera follows someone: off the map, so nobody bumps into or reacts to them.</summary>
        private static readonly Vector2 ParkingSpot = new(-1280f, -1280f);

        /// <summary>
        /// Keep the camera on an NPC, also when she changes map. The game only draws the map the farmer is in, so the
        /// farmer is hidden (Game1.displayFarmer), parked off the map, and warped after her whenever she leaves.
        /// </summary>
        public void StartFollow(NPC npc)
        {
            if (npc == null || !this.Require())
                return;

            if (this.FollowStart == null && Game1.currentLocation != null)
                this.FollowStart = (Game1.currentLocation.NameOrUniqueName, Game1.player.Position, Game1.player.FacingDirection);
            this.FollowParked = false;
            this.Camera.Set(true);
            this.Camera.Follow = npc;
        }

        /// <summary>Stop following and show the farmer again.</summary>
        /// <param name="goBack">Return the farmer to where they were when following began.</param>
        /// <param name="stayWith">Instead: put the farmer next to this NPC.</param>
        public void StopFollow(bool goBack, NPC stayWith = null)
        {
            this.Camera.Follow = null;
            if (this.FollowStart == null)
                return;

            var start = this.FollowStart.Value;
            this.FollowStart = null;
            this.FollowParked = false;
            Game1.displayFarmer = true;

            if (stayWith?.currentLocation != null)
            {
                this.PutFarmer(stayWith.currentLocation.NameOrUniqueName, new Vector2(stayWith.TilePoint.X, stayWith.TilePoint.Y + 1) * 64f, 0);
                this.Camera.Set(false);
            }
            else if (goBack)
                this.PutFarmer(start.Location, start.Position, start.Facing);
            else if (this.IsFarmerOffMap())
                this.PutFarmer(start.Location, start.Position, start.Facing); // never leave the farmer in the parking spot
        }

        private bool IsFarmerOffMap()
        {
            return Game1.player.Position.X < 0 || Game1.player.Position.Y < 0;
        }

        private void PutFarmer(string location, Vector2 position, int facing)
        {
            if (Game1.currentLocation?.NameOrUniqueName == location)
            {
                Game1.player.Position = position;
                Game1.player.faceDirection(facing);
            }
            else if (Game1.getLocationFromName(location) != null)
                Game1.warpFarmer(location, (int)(position.X / 64f), (int)(position.Y / 64f), facing);
        }

        /// <summary>Call every tick: go after the followed NPC, keep the farmer hidden and parked.</summary>
        private void UpdateFollow()
        {
            if (this.FollowStart == null)
                return;

            NPC npc = this.Camera.Follow;
            if (npc == null || !this.Camera.Enabled)
            {
                this.StopFollow(goBack: true); // the camera was switched off, or following ended some other way
                return;
            }

            Game1.displayFarmer = false; // warps turn it back on
            bool busy = Game1.isWarping || Game1.locationRequest != null || Game1.fadeToBlack || Game1.globalFade;
            if (busy || npc.currentLocation == null)
                return;

            if (npc.currentLocation != Game1.currentLocation)
            {
                this.FollowParked = false;
                Game1.warpFarmer(npc.currentLocation.NameOrUniqueName, npc.TilePoint.X, npc.TilePoint.Y, 2);
            }
            else if (!this.FollowParked || Game1.player.Position != ParkingSpot)
            {
                Game1.player.Halt();
                Game1.player.Position = ParkingSpot;
                this.FollowParked = true;
            }
        }

        /*********
        ** World (season, day, weather)
        *********/
        /// <summary>Change the season the way the game's own "season" debug command does, then let everything re-check its conditions.</summary>
        public void SetSeason(Season season)
        {
            if (!this.Require())
                return;

            Game1.season = season;
            Game1.setGraphicsForSeason();
            this.AfterWorldChange($"It's {season} now.");
        }

        /// <summary>Change the day of the month the way the game's "day" debug command does. Schedules stay as they were loaded this morning.</summary>
        public void SetDay(int day)
        {
            if (!this.Require())
                return;

            day = Math.Clamp(day, 1, 28);
            Game1.stats.DaysPlayed = (uint)(Game1.seasonIndex * 28 + day + (Game1.year - 1) * 4 * 28);
            Game1.dayOfMonth = day;
            this.AfterWorldChange($"It's day {day} now. Schedules stay as they were loaded this morning.");
        }

        /// <summary>
        /// Set the weather of the area you're in (its location context), like the game's rain / debrisWeather debug
        /// commands: the context's weather flags, and the game-wide flags when it's the valley.
        /// </summary>
        public void SetWeather(string weather)
        {
            if (!this.Require() || Game1.currentLocation == null)
                return;

            string context = Game1.currentLocation.GetLocationContextId();
            var state = Game1.netWorldState.Value.GetWeatherForLocation(context);
            state.Weather = weather;
            state.IsRaining = weather is "Rain" or "Storm" or "GreenRain";
            state.IsLightning = weather == "Storm";
            state.IsSnowing = weather == "Snow";
            state.IsDebrisWeather = weather == "Wind";
            state.IsGreenRain = weather == "GreenRain";
            if (context == "Default")
            {
                Game1.isRaining = state.IsRaining;
                Game1.isLightning = state.IsLightning;
                Game1.isSnowing = state.IsSnowing;
                Game1.isDebrisWeather = state.IsDebrisWeather;
                Game1.isGreenRain = state.IsGreenRain;
            }
            Game1.debrisWeather.Clear();
            if (state.IsDebrisWeather)
                Game1.populateDebrisWeatherArray();

            this.AfterWorldChange($"Weather here: {weather}. Music and ambient sound follow when you enter a map.");
        }

        private string CurrentWeather => Game1.currentLocation != null ? Game1.netWorldState.Value.GetWeatherForLocation(Game1.currentLocation.GetLocationContextId()).Weather : null;

        /// <summary>
        /// After the date or weather changed: Content Patcher only re-reads most tokens at the start of a day, so run its
        /// "patch update" command; then let every NPC pick her appearance again and LAF scan now.
        /// </summary>
        private void AfterWorldChange(string message)
        {
            if (this.Commands.Exists("patch"))
                this.Commands.Run("patch", "update");

            Utility.ForEachVillager(npc =>
            {
                if (!this.Wearing.Contains(npc))
                    this.Inspector.ChooseAppearance(npc);
                return true;
            });
            this.Inspector.ScanNow?.Invoke();
            this.Say(message, LogLevel.Info);
        }

        /*********
        ** Input and drawing
        *********/
        /// <summary>Handle a left click. Returns true if the workspace used it.</summary>
        /// <summary>Whether other mods' HUD drawing should be skipped right now (the HUD toggle is off).</summary>
        public bool HidesHud => this.Active && this.HudHidden;

        /// <summary>
        /// Whether the game shouldn't get left clicks, the use-tool key or the mouse wheel right now: the workspace is
        /// open with "Game controls" set to the tool, and no menu or event has the screen.
        /// </summary>
        public bool BlocksGameControls => this.Active && this.Config().TakeOverControls && Game1.activeClickableMenu == null && !Game1.eventUp;

        /// <summary>
        /// How far the game's notifications (bottom left) must move up to stay clear of the hub and the character column. The game
        /// puts the first one 64 px above the bottom of the screen.
        /// </summary>
        public int ReservedBottomLeft
        {
            get
            {
                if (!this.Active || Game1.activeClickableMenu != null || Game1.eventUp)
                    return 0;
                int top = this.Bar.Bounds.IsEmpty ? this.HubTop : Math.Min(this.Bar.Bounds.Y, this.HubTop);
                return Math.Max(0, Game1.uiViewport.Height - top + 8 - 64);
            }
        }

        private bool CanUseMouse => this.Active && Game1.activeClickableMenu == null && !Game1.eventUp && !this.Time.IsBusy && this.Pending == null;

        /// <summary>Handle a left click. Returns true if the workspace used it.</summary>
        public bool OnLeftClick()
        {
            if (!this.CanUseMouse)
                return false;

            Point mouse = Ui.RealMouse;
            this.Browser.StopTyping(); // a click anywhere but the search box takes the keyboard back
            if (this.Pie.OnLeftClick(mouse))
                return true;

            // the button groups are drawn on top of the windows, so they get the click first
            Toolbar.Button button = this.Toolbar.GetButtonAt(mouse);
            if (button != null)
            {
                Game1.playSound("smallSelect");
                button.OnClick?.Invoke();
                return true;
            }
            if (this.Toolbar.Contains(mouse))
                return true;

            NPC inBar = this.Bar.GetNpcAt(mouse);
            if (inBar != null)
            {
                Game1.playSound("smallSelect");
                this.Select(inBar);
                return true;
            }
            if (this.Bar.Contains(mouse))
                return true;

            // windows: the one drawn last is on top
            // the hub's NPCs drawer
            NPC picked = this.Browser.OnLeftClick(mouse, out bool usedByBrowser);
            if (picked != null)
                this.Select(picked);
            if (usedByBrowser)
                return true;

            if (this.Character.OnLeftClick(mouse) || this.Panel.OnLeftClick(mouse))
                return true;

            if (this.Inspector.Enabled)
            {
                NPC npc = this.Inspector.GetHovered();
                if (npc != null)
                {
                    this.Panel.Open(npc);
                    return true;
                }
            }

            // a click on the world itself: holding and moving the mouse drags the map
            this.Camera.BeginGrab();
            return false;
        }

        /// <summary>
        /// Handle a right click: open the ring of options for the character or tile under the cursor. Holding Shift
        /// leaves the click to the game (talk, use, interact). Returns true if the workspace used it.
        /// </summary>
        public bool OnRightClick()
        {
            if (!this.CanUseMouse || !this.Config().PieMenu)
                return false;
            if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                return false;

            Point mouse = Ui.RealMouse;
            if (this.Pie.IsOpen)
            {
                this.Pie.Close();
                return true;
            }

            // a face in the corner column or in the browser: her options, or first the ring of her versions if she has sub-characters
            NPC face = this.Bar.GetNpcAt(mouse) ?? this.Browser.GetNpcAt(mouse);
            if (face != null)
            {
                if (this.Browser.GetFamily(face).Count > 0)
                    this.OpenFamilyPie(mouse, face);
                else
                    this.OpenNpcPie(mouse, face);
                return true;
            }

            if (this.Toolbar.Contains(mouse) || this.Bar.Contains(mouse) || this.Browser.Contains(mouse) || this.Panel.Contains(mouse) || this.Character.Contains(mouse))
                return true; // on the tool's own windows: nothing to open

            NPC npc = this.Inspector.GetHovered();
            if (npc != null)
            {
                this.OpenNpcPie(mouse, npc);
                return true;
            }

            if (Game1.currentLocation == null)
                return false;
            Vector2 tile = this.Helper.Input.GetCursorPosition().Tile;
            this.Pie.Open(mouse, $"{Game1.currentLocation.NameOrUniqueName} {(int)tile.X} {(int)tile.Y}", this.GetTileOptions(Game1.currentLocation, new Point((int)tile.X, (int)tile.Y)), farmerHead: true);
            return true;
        }

        /// <summary>The ring of a character's versions (herself and her sub-characters); each opens that one's options.</summary>
        private void OpenFamilyPie(Point at, NPC npc)
        {
            List<NpcGroups.Entry> family = this.Browser.GetFamily(npc);
            if (family.Count == 0)
            {
                this.OpenNpcPie(at, npc);
                return;
            }

            var items = new List<KeyValuePair<string, Action>>();
            foreach (NpcGroups.Entry entry in family)
            {
                NPC version = entry.Npc;
                items.Add(new(UiText.Clean(entry.Label), () => this.OpenNpcPie(at, version, () => this.OpenFamilyPie(at, npc))));
            }
            this.Pie.Open(at, UiText.Clean(family[0].Label), items, face: family[0].Npc);
        }

        /// <param name="back">What a click on her head does (reopen the menu this one came from), or null.</param>
        /// <summary>Select a character (highlighted in the column and the browser), or unselect her if she already is.</summary>
        private void Select(NPC npc)
        {
            this.Selected = ReferenceEquals(this.Selected, npc) ? null : npc;
        }

        private void OpenNpcPie(Point at, NPC npc, Action back = null)
        {
            this.Pie.Open(at, this.GetLabel(npc), this.GetNpcOptions(npc, at, back), face: npc, onCenter: back);
        }

        /// <summary>The name to show for an NPC: her own, or "Haley (alt 1)" for a sub-character with an unprintable name.</summary>
        private string GetLabel(NPC npc)
        {
            foreach (NpcGroups.Entry entry in this.Browser.GetFamily(npc))
            {
                if (ReferenceEquals(entry.Npc, npc))
                    return UiText.Clean(entry.Label);
            }
            return UiText.Clean(npc.Name);
        }

        private List<KeyValuePair<string, Action>> GetNpcOptions(NPC npc, Point at, Action back)
        {
            var options = new List<KeyValuePair<string, Action>>
            {
                new("Inspect", () => this.Panel.Open(npc)),
                new("Outfits", () => this.Character.Toggle(npc)),
                new("Expressions", () => this.OpenExpressionPie(at, npc, 0, back)),
                ReferenceEquals(this.Camera.Follow, npc)
                    ? new("Stop following", () => this.StopFollow(goBack: true))
                    : new("Follow", () => this.StartFollow(npc)),
                new("Warp to me", () => this.BringHere(npc)),
                new(UiText.Clean($"Warp to {npc.DefaultMap ?? "home"}"), () => this.GoToHome(npc))
            };
            if (ReferenceEquals(this.Camera.Follow, npc))
                options.Insert(4, new("Stay here", () => this.StopFollow(goBack: false, stayWith: npc)));
            else if (npc.currentLocation != Game1.currentLocation)
                options.Insert(3, new($"Warp to {this.GetLabel(npc)}", () => this.GoToNpc(npc)));
            if (this.Wearing.Contains(npc))
                options.Add(new("Take off", () => this.StopWearing(npc)));
            options.Add(new(this.Browser.IsFavourite(npc) ? "Unfavourite" : "Favourite", () => this.Browser.ToggleFavourite(npc)));
            return options;
        }

        /*********
        ** Expressions
        *********/
        private const int ExpressionsPerPage = 12;
        /// <summary>The ring of expressions (portrait indexes) for an NPC; the middle goes back to her options.</summary>
        private void OpenExpressionPie(Point at, NPC npc, int page, Action back)
        {
            List<int> all = this.GetExpressions(npc);
            int pages = Math.Max(1, (all.Count + ExpressionsPerPage - 1) / ExpressionsPerPage);
            page = ((page % pages) + pages) % pages;

            // an expression the game's own sheet has is "Vanilla", the rest are "Custom". (APF can't say yet which
            // mod a single expression is made for; when it reports that, the mod's name goes here.)
            int vanillaCount = Vanilla.GetExpressionCount(npc.Name);

            var items = new List<KeyValuePair<string, Action>>();
            foreach (int index in all.Skip(page * ExpressionsPerPage).Take(ExpressionsPerPage))
            {
                int expression = index;
                string label = $"${expression} {(expression < vanillaCount ? "Vanilla" : "Custom")}";
                items.Add(new(label, () => this.ShowExpression(npc, expression)));
            }
            if (pages > 1)
                items.Add(new($"More ({page + 1}/{pages})", () => this.OpenExpressionPie(at, npc, page + 1, back)));

            this.Pie.Open(at, this.GetLabel(npc), items, face: npc, onCenter: () => this.OpenNpcPie(at, npc, back));
        }

        /// <summary>
        /// Which portrait indexes an NPC has: APF's list if it has one for her (section "variants", text
        /// "Base expressions [0, 1, ...]"), else one per cell of her portrait sheet (two cells per row).
        /// </summary>
        private List<int> GetExpressions(NPC npc)
        {
            var found = new List<int>();
            try
            {
                string text = this.Inspector.GetApfTree(npc)?.Children.FirstOrDefault(section => section.Id == "variants")?.Text;
                int start = text?.IndexOf('[') ?? -1;
                int end = start >= 0 ? text.IndexOf(']', start) : -1;
                if (end > start)
                {
                    foreach (string part in text.Substring(start + 1, end - start - 1).Split(new[] { ',', ' ', '$' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] range = part.Split('-');
                        if (range.Length == 2 && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to))
                        {
                            for (int i = from; i <= to && i - from < 200; i++)
                                found.Add(i);
                        }
                        else if (int.TryParse(part, out int index))
                            found.Add(index);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read APF's expression list for {npc.Name}: {ex.Message}", LogLevel.Trace);
            }

            if (found.Count == 0)
            {
                Texture2D portrait = npc.Portrait;
                int cell = portrait != null ? Math.Max(1, portrait.Width / 2) : 0;
                int count = cell > 0 ? Math.Clamp(portrait.Height / cell * 2, 1, 200) : 6;
                for (int i = 0; i < count; i++)
                    found.Add(i);
            }
            return found.Distinct().OrderBy(index => index).ToList();
        }

        /// <summary>Open a dialogue box with a random test line and the chosen expression.</summary>
        private void ShowExpression(NPC npc, int expression)
        {
            try
            {
                string line = TestLines.Pick(this.Random);
                Game1.DrawDialogue(new Dialogue(npc, null, $"{line}${expression}"));
            }
            catch (Exception ex)
            {
                this.Say($"Couldn't show {npc.Name}'s expression ${expression}: {ex.Message}", LogLevel.Warn);
            }
        }

        private List<KeyValuePair<string, Action>> GetTileOptions(GameLocation location, Point tile)
        {
            string name = location.NameOrUniqueName;
            var options = new List<KeyValuePair<string, Action>>
            {
                new("Warp here", () =>
                {
                    this.StopFollow(goBack: false);
                    Game1.warpFarmer(name, tile.X, tile.Y, Game1.player.FacingDirection);
                }),
                new($"Copy \"{tile.X} {tile.Y}\"", () => this.Copy($"{tile.X} {tile.Y}")),
                new("Copy map + tile", () => this.Copy($"{name} {tile.X} {tile.Y}"))
            };

            // with a character window open: put her on this tile
            NPC selected = this.HubNpc;
            if (selected != null)
                options.Add(new(UiText.Clean($"Put {selected.Name} here"), () => this.BringTo(selected, location, tile)));
            return options;
        }

        private void Copy(string text)
        {
            bool copied = false;
            try
            {
                copied = DesktopClipboard.SetText(text);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't copy to the clipboard: {ex.Message}", LogLevel.Trace);
            }
            this.Say(copied ? $"Copied: {text}" : $"Couldn't copy; it's in the SMAPI console: {text}", LogLevel.Info);
        }

        /// <summary>Handle the mouse wheel. Returns true if the panel used it.</summary>
        public bool OnScroll(int delta)
        {
            if (!this.Active)
                return false;

            var mouse = new Point(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            if (this.Bar.Contains(mouse))
            {
                this.Bar.OnScroll(delta);
                return true;
            }
            if (this.Browser.Contains(mouse))
            {
                this.Browser.OnScroll(delta);
                return true;
            }
            if (this.Panel.Contains(mouse))
            {
                this.Panel.OnScroll(delta);
                return true;
            }
            if (this.Character.Contains(mouse))
            {
                this.Character.OnScroll(delta);
                return true;
            }
            return false;
        }

        /// <summary>Open the inspector panel for an NPC by name.</summary>
        public void InspectNpc(string name)
        {
            if (!this.Require())
                return;
            NPC npc = Utility.fuzzyCharacterSearch(name, must_be_villager: false);
            if (npc == null)
                this.Say($"No NPC found matching '{name}'.", LogLevel.Warn);
            else
                this.Panel.Open(npc);
        }

        public void DrawWorld(SpriteBatch b)
        {
            if (!this.Active)
                return;

            this.Inspector.HoverBlocked = this.Pie.IsOpen;
            this.Inspector.DrawWorld(b);
            this.DrawSelectionMarker(b);
        }

        /// <summary>
        /// A gem floating above the selected character (the one the hub's LAF and APF modes act on), so it is clear who
        /// you are working on. It is one of the game's own items (an emerald unless config says otherwise), so it
        /// follows recolours of the item sheet.
        /// </summary>
        private void DrawSelectionMarker(SpriteBatch b)
        {
            NPC npc = this.HubNpc;
            string itemId = this.Config().SelectionMarker;
            if (npc == null || npc.IsInvisible || npc.currentLocation != Game1.currentLocation || Game1.eventUp || string.IsNullOrWhiteSpace(itemId))
                return;

            try
            {
                var data = ItemRegistry.GetDataOrErrorItem(itemId);
                Texture2D texture = data.GetTexture();
                Rectangle source = data.GetSourceRect();

                const float scale = 3f;
                int spriteHeight = (npc.Sprite?.SpriteHeight ?? 32) * 4;
                float bob = (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 350.0) * 6f;
                var world = new Vector2(npc.StandingPixel.X, npc.Position.Y + 64 - spriteHeight - 72 + bob);
                Vector2 screen = Game1.GlobalToLocal(Game1.viewport, world);
                var origin = new Vector2(source.Width / 2f, source.Height / 2f);

                b.Draw(texture, screen + new Vector2(2f, 2f), source, Color.Black * 0.35f, 0f, origin, scale, SpriteEffects.None, 1f);
                b.Draw(texture, screen, source, Color.White, 0f, origin, scale, SpriteEffects.None, 1f);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"Couldn't draw the selection marker '{itemId}': {ex.Message}", LogLevel.Trace);
            }
        }

        public void DrawHud(SpriteBatch b)
        {
            if (this.Time.IsBusy || this.Pending != null)
            {
                string text = this.Pending != null ? "Restarting the day..."
                    : this.Time.IsSettling ? $"{TimeMachine.Format(Game1.timeOfDay)} - NPCs are catching up..."
                    : $"{TimeMachine.Format(Game1.timeOfDay)}  to  {TimeMachine.Format(this.Time.TargetTime)}";
                b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.6f);
                Vector2 size = Game1.dialogueFont.MeasureString(text);
                b.DrawString(Game1.dialogueFont, text, new Vector2((Game1.uiViewport.Width - size.X) / 2f, (Game1.uiViewport.Height - size.Y) / 2f), Color.White);
                return;
            }

            if (!this.Active || Game1.activeClickableMenu != null || Game1.eventUp)
                return; // drawn above the HUD, so stay out of the way of menus (e.g. a config menu opened from Settings)

            ModConfig config = this.Config();
            bool clockShown = !this.HudHidden || config.KeepClock;
            if (this.HudHidden && config.KeepClock)
                Game1.dayTimeMoneyBox?.draw(b); // the game's own clock box, at its usual place

            Ui.HoverBlocked = this.Pie.IsOpen; // nothing under an open ring reacts to the cursor

            // the windows fit around the hub and the character column (their areas are from the last frame)
            Rectangle time = this.Toolbar.GetBounds("time");
            Rectangle modes = this.Toolbar.GetBounds("modes");
            Rectangle column = this.Bar.Bounds;
            int windowLeft = (column.IsEmpty ? 4 : column.Right) + 4;
            int rightTop = (time.IsEmpty ? 8 : time.Bottom) + 4;
            this.Panel.Draw(b, windowLeft, 12, this.HubTop - 4);
            this.Character.Draw(b, rightTop, Game1.uiViewport.Height - 12);

            // characters in this location: a column on the left edge, above the hub's mode buttons
            this.Bar.Draw(b, 12, (modes.IsEmpty ? Game1.uiViewport.Height - 4 : modes.Y) - 4, this.HubNpc, npc => $"{npc.Name}: {npc.LastAppearanceId ?? "(default)"}", npc => this.IsLeftOutOfColumn(npc, config));

            // the NPCs drawer: the hub's open mode, drawn by the browser itself
            this.Browser.Visible = this.Mode == "NPCs";
            this.Browser.Draw(b, (modes.IsEmpty ? 8 : modes.Right) + 4, Game1.uiViewport.Height - 8);

            this.Toolbar.Draw(b, this.BuildGroups(clockShown));
            Ui.FlushTooltip(b); // after every window, so nothing covers it
            Ui.HoverBlocked = false;
            this.Pie.Draw(b);

            if (!Game1.displayHUD)
                Ui.DrawCursor(b); // the game only draws its cursor with the HUD
        }

        private List<Toolbar.Group> BuildGroups(bool clockShown)
        {
            ModConfig config = this.Config();

            // top right: time, below the game's clock
            var time = new Toolbar.Group
            {
                Id = "time",
                Corner = Toolbar.Corner.TopRight,
                Top = clockShown ? (this.HudHidden ? 300 : 352) : 8,
                Title = UiText.Clean($"{TimeMachine.Format(Game1.timeOfDay)} | {(this.Paused ? "clock held" : $"running x{this.Speed}")} | {Game1.currentLocation?.NameOrUniqueName}"),
                MaxWidth = Math.Max(420, (int)(Game1.uiViewport.Width * 0.3f)),
                EdgeInset = 0
            };
            time.Buttons.Add(new() { Label = this.Paused ? "Play" : "Pause", Active = !this.Paused, Tooltip = "Let the clock run, or hold it. NPCs keep moving while it is held.", OnClick = this.TogglePause });
            time.Buttons.Add(new() { Label = $"x{this.Speed}", Tooltip = "Clock speed while running (NPCs still walk at normal speed).", OnClick = () => this.SetSpeed(this.Speed >= 4 ? 1 : this.Speed * 2) });
            time.Buttons.Add(new() { Label = "+10m", Tooltip = "Skip 10 minutes.", OnClick = () => this.Step(1) });
            time.Buttons.Add(new() { Label = "+1h", Tooltip = "Skip one hour.", OnClick = () => this.Step(6) });
            bool firstCheckpoint = true;
            foreach (int checkpoint in ParseCheckpoints(config.Checkpoints))
            {
                int target = checkpoint;
                bool back = target < Game1.timeOfDay;
                time.Buttons.Add(new Toolbar.Button
                {
                    Label = (back ? "<" : "") + TimeMachine.Format(target),
                    NewRow = firstCheckpoint,
                    Tooltip = back ? "Earlier today: restarts the day from your save, then jumps here." : "Jump forward to this time.",
                    OnClick = () => this.GoTo(target)
                });
                firstCheckpoint = false;
            }
            time.Buttons.Add(new() { Label = "Restart day", Tooltip = "Reload the save and start the day again at 6:00.", OnClick = () => this.RestartDay(600, reenter: true, warpToEntry: false) });

            // bottom left: the hub. A column of mode buttons, and the open mode's tools to the right of it.
            string[] names = { "Test", "LAF", "APF", "NPCs", "Settings" };
            string[] tips =
            {
                "Camera, inspecting, reloading packs, and the season, weather and day.",
                "Live Appearance Framework: scan, outfits, and the selected character's report.",
                "Animated Portrait Framework: the selected character's outfits and expressions.",
                "The character browser and your favourites.",
                "The config menus of this tool, LAF, APF and the watched packs."
            };
            int modeWidth = Ui.MeasureButton("Settings");
            var modes = new Toolbar.Group { Id = "modes", Corner = Toolbar.Corner.BottomLeft, MaxWidth = modeWidth + Ui.BoxPadding * 2 };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                modes.Buttons.Add(new() { Label = name, NewRow = true, MinWidth = modeWidth, Active = this.Mode == name, Tooltip = tips[i] + (this.Mode == name ? " Click to fold." : ""), OnClick = () => this.Mode = this.Mode == name ? null : name });
            }
            modes.Buttons.Add(new() { Label = "Exit", NewRow = true, MinWidth = modeWidth, Tooltip = config.RestoreOnExit ? "Close: reloads the save and returns you to where you opened the workspace." : "Close and keep the world as it is.", OnClick = () => this.Exit(config.RestoreOnExit) });

            var groups = new List<Toolbar.Group> { time, modes };
            if (this.Mode != null && this.Mode != "NPCs")
            {
                Rectangle modesArea = this.Toolbar.GetBounds("modes");
                var hub = new Toolbar.Group
                {
                    Id = "hub",
                    Corner = Toolbar.Corner.BottomLeft,
                    Left = (modesArea.IsEmpty ? modeWidth + Ui.BoxPadding * 2 + 8 : modesArea.Right) + 4,
                    MaxWidth = Math.Max(480, (int)(Game1.uiViewport.Width * 0.42f))
                };
                this.FillHub(hub);
                groups.Add(hub);
            }
            return groups;
        }

        /// <summary>The character the LAF and APF modes act on: the selected one, else the one in the character window, else the one in the inspector.</summary>
        private NPC HubNpc => this.Selected ?? this.Character.SelectedNpc ?? this.Panel.SelectedNpc;

        /// <summary>Where the hub starts (UI pixels from the top): the top of its mode buttons or its open mode, whichever is higher.</summary>
        private int HubTop
        {
            get
            {
                int top = Game1.uiViewport.Height - 8;
                foreach (string id in new[] { "modes", "hub" })
                {
                    Rectangle area = this.Toolbar.GetBounds(id);
                    if (!area.IsEmpty)
                        top = Math.Min(top, area.Y);
                }
                if (!this.Browser.Area.IsEmpty)
                    top = Math.Min(top, this.Browser.Area.Y);
                return top;
            }
        }

        private void FillHub(Toolbar.Group hub)
        {
            NPC npc = this.HubNpc;
            string who = npc != null ? this.GetLabel(npc) : null;
            const string pickOne = "No character selected: click a face, or pick one under NPCs.";

            switch (this.Mode)
            {
                case "Test":
                {
                    hub.Title = UiText.Clean($"Test | {Game1.season} {Game1.dayOfMonth} | {this.CurrentWeather ?? "?"} | {Game1.currentLocation?.NameOrUniqueName}");
                    hub.Buttons.Add(new() { Label = "Camera", Active = this.Camera.Enabled, Tooltip = "Free camera: move with the movement or arrow keys, Shift = faster.", OnClick = () => this.Camera.Set(!this.Camera.Enabled) });
                    hub.Buttons.Add(new() { Label = "Inspect", Active = this.Inspector.Enabled, Tooltip = "Hover an NPC for details; click her to open the inspector.", OnClick = () => this.Inspector.Enabled = !this.Inspector.Enabled });
                    hub.Buttons.Add(new() { Label = "Reload packs", Tooltip = "Run 'patch reload' for every watched content pack now.", OnClick = () => this.Watcher.ReloadNow() });

                    bool first = true;
                    foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter })
                    {
                        Season target = season;
                        hub.Buttons.Add(new() { Label = season.ToString(), NewRow = first, Active = Game1.season == season, Tooltip = "Switch to this season (maps, crops and trees change like on a real season change). Nothing is saved.", OnClick = () => this.SetSeason(target) });
                        first = false;
                    }
                    hub.Buttons.Add(new() { Label = "Day -", Tooltip = "One day back in the month. Schedules stay as they were loaded this morning.", OnClick = () => this.SetDay(Game1.dayOfMonth - 1) });
                    hub.Buttons.Add(new() { Label = "Day +", Tooltip = "One day forward in the month. Schedules stay as they were loaded this morning.", OnClick = () => this.SetDay(Game1.dayOfMonth + 1) });

                    string now = this.CurrentWeather;
                    first = true;
                    foreach (string weather in new[] { "Sun", "Rain", "Storm", "Snow", "Wind", "GreenRain" })
                    {
                        string target = weather;
                        hub.Buttons.Add(new() { Label = weather == "GreenRain" ? "Green rain" : weather, NewRow = first, Active = now == weather, Tooltip = "Set the weather of the area you're in.", OnClick = () => this.SetWeather(target) });
                        first = false;
                    }
                    break;
                }

                case "LAF":
                {
                    hub.Title = !this.Inspector.HasLaf ? "LAF | Live Appearance Framework isn't installed." : UiText.Clean(who != null ? $"LAF | {who}: {npc.LastAppearanceId ?? "(default)"}" : $"LAF | {pickOne}");
                    if (this.Inspector.ScanNow != null)
                        hub.Buttons.Add(new() { Label = "Scan now", Tooltip = "Make LAF do its periodic scan right now (poses, detours, reactions).", OnClick = () => this.Inspector.ScanNow?.Invoke() });
                    hub.Buttons.Add(new() { Label = "Re-pick outfits", Tooltip = "Let every character choose her appearance again.", OnClick = this.RepickAppearances });
                    if (npc != null)
                    {
                        hub.Buttons.Add(new() { Label = "Inspect", NewRow = true, Active = ReferenceEquals(this.Panel.SelectedNpc, npc), Tooltip = UiText.Clean($"Open the inspector for {who}: why she wears this, her schedule, poses and detours."), OnClick = () => this.TogglePanel(npc) });
                        hub.Buttons.Add(new() { Label = "Console report", Tooltip = UiText.Clean($"Print LAF's classic reports about {who} to the SMAPI console."), OnClick = () => this.Inspector.DumpToConsole(npc) });
                        if (this.Wearing.Contains(npc))
                            hub.Buttons.Add(new() { Label = "Take off", Tooltip = "Give her own outfit back.", OnClick = () => this.StopWearing(npc) });
                    }
                    break;
                }

                case "APF":
                {
                    hub.Title = !this.Inspector.HasApf ? "APF | Animated Portrait Framework isn't installed (or has no editor API)." : UiText.Clean(who != null ? $"APF | {who}" : $"APF | {pickOne}");
                    if (npc != null)
                    {
                        hub.Buttons.Add(new() { Label = "Outfits", Active = ReferenceEquals(this.Character.SelectedNpc, npc), Tooltip = UiText.Clean($"{who}'s window: outfits, variants and today's rolls, with previews."), OnClick = () => this.Character.Toggle(npc) });
                        hub.Buttons.Add(new() { Label = "Expressions", Tooltip = "The ring of her expressions; click one for a test dialogue.", OnClick = () => this.OpenExpressionPie(new Point(Game1.uiViewport.Width / 2, Game1.uiViewport.Height / 2), npc, 0, null) });
                        hub.Buttons.Add(new() { Label = "Inspect", Active = ReferenceEquals(this.Panel.SelectedNpc, npc), Tooltip = "Open the inspector: the portrait shown and today's rolls.", OnClick = () => this.TogglePanel(npc) });
                    }
                    break;
                }

                case "Settings":
                {
                    ModConfig config = this.Config();
                    string labels = (config.Labels ?? "Show").ToLowerInvariant() switch { "off" => "Off", "hover" => "Hover", _ => "Show" };
                    hub.Title = "Settings";
                    hub.Buttons.Add(new() { Label = this.HudHidden ? "HUD: off" : "HUD: on", Active = !this.HudHidden, Tooltip = "Show or hide the game's HUD and other mods' HUD icons.", OnClick = this.ToggleHud });
                    hub.Buttons.Add(new()
                    {
                        Label = labels switch { "Off" => "Labels: off", "Hover" => "Labels: on mouse over", _ => "Labels: shown" },
                        Active = labels != "Off",
                        Tooltip = "The text boxes above characters (appearance ID and pose). Click to switch: shown, on mouse over, off.",
                        OnClick = () =>
                        {
                            config.Labels = labels switch { "Show" => "Hover", "Hover" => "Off", _ => "Show" };
                            this.SaveConfig();
                        }
                    });
                    hub.Buttons.Add(new()
                    {
                        Label = config.TakeOverControls ? "Game controls: off" : "Game controls: on",
                        Active = config.TakeOverControls,
                        Tooltip = "Off: left click, the use-tool key and the mouse wheel belong to the tool, so the farmer doesn't swing a tool or switch items. The interact key still works. On: the game gets them too.",
                        OnClick = () =>
                        {
                            config.TakeOverControls = !config.TakeOverControls;
                            this.SaveConfig();
                        }
                    });
                    hub.Buttons.Add(new()
                    {
                        Label = string.IsNullOrWhiteSpace(config.SelectionMarker) ? "Marker: off" : "Marker: on",
                        Active = !string.IsNullOrWhiteSpace(config.SelectionMarker),
                        Tooltip = "The gem floating above the selected character. Which item it is can be changed in the config menu (Selection marker).",
                        OnClick = () =>
                        {
                            config.SelectionMarker = string.IsNullOrWhiteSpace(config.SelectionMarker) ? "(O)60" : "";
                            this.SaveConfig();
                        }
                    });
                    hub.Buttons.Add(new()
                    {
                        NewRow = true,
                        Label = config.HideAltsInColumn ? "Alts in column: hidden" : "Alts in column: shown",
                        Active = config.HideAltsInColumn,
                        Tooltip = "Hidden copies of characters (extra NPCs some mods park out of reach) in the column of faces on the left.",
                        OnClick = () =>
                        {
                            config.HideAltsInColumn = !config.HideAltsInColumn;
                            this.SaveConfig();
                        }
                    });
                    hub.Buttons.Add(new()
                    {
                        Label = config.HideNonSocialInColumn ? "Animals in column: hidden" : "Animals in column: shown",
                        Active = config.HideNonSocialInColumn,
                        Tooltip = "Characters you can't befriend (animals and other background NPCs mods add) in the column of faces on the left.",
                        OnClick = () =>
                        {
                            config.HideNonSocialInColumn = !config.HideNonSocialInColumn;
                            this.NonSocial.Clear();
                            this.SaveConfig();
                        }
                    });
                    hub.Buttons.Add(new()
                    {
                        Label = config.UnlockDoors ? "All doors: unlocked" : "All doors: as in game",
                        NewRow = true,
                        Active = config.UnlockDoors,
                        Tooltip = "Shops and houses open at any hour, and bedroom doors open without friendship. Only while the workspace is open.",
                        OnClick = () =>
                        {
                            config.UnlockDoors = !config.UnlockDoors;
                            this.SaveConfig();
                        }
                    });
                    bool firstMenu = true;
                    foreach (var menu in this.ConfigMenus)
                    {
                        hub.Buttons.Add(new() { Label = menu.Key, NewRow = firstMenu, Tooltip = "Open this config menu.", OnClick = menu.Value });
                        firstMenu = false;
                    }
                    break;
                }
            }
        }

        /// <summary>Whether each NPC seen so far can be befriended (asked once: the game runs a query for it).</summary>
        private readonly Dictionary<NPC, bool> NonSocial = new();

        private bool IsLeftOutOfColumn(NPC npc, ModConfig config)
        {
            if (config.HideAltsInColumn && this.Browser.IsHiddenCopy(npc))
                return true;
            if (!config.HideNonSocialInColumn)
                return false;

            if (!this.NonSocial.TryGetValue(npc, out bool nonSocial))
            {
                try
                {
                    nonSocial = !npc.CanSocialize;
                }
                catch
                {
                    nonSocial = false;
                }
                this.NonSocial[npc] = nonSocial;
            }
            return nonSocial;
        }

        /// <summary>A key was pressed. Returns true if the tool used it (typing in the search box).</summary>
        public bool OnKey(SButton button)
        {
            if (!this.Active)
                return false;
            if (!this.Browser.Typing || !this.Browser.Visible)
                return this.OnTimeKey(button);
            if (button.TryGetKeyboard(out _) == false)
                return false;

            this.Browser.OnKey(button, this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift));
            return true;
        }

        /// <summary>
        /// The number keys as time controls (as in The Sims), while the game's controls are taken over: 1 = normal
        /// speed, and play / pause when already at normal speed; 2 = double speed; 3 = four times.
        /// </summary>
        private bool OnTimeKey(SButton button)
        {
            if (button is not (SButton.D1 or SButton.D2 or SButton.D3) || !this.BlocksGameControls)
                return false;
            if (this.Time.IsBusy || this.Pending != null)
                return true;

            if (button == SButton.D1)
            {
                if (!this.Paused && this.Speed > 1)
                    this.SetSpeed(1);
                else
                {
                    this.SetSpeed(1);
                    this.TogglePause();
                }
            }
            else
            {
                this.SetSpeed(button == SButton.D2 ? 2 : 4);
                if (this.Paused)
                    this.TogglePause();
            }
            Game1.playSound("smallSelect");
            return true;
        }

        private void SaveConfig()
        {
            try
            {
                this.Helper.WriteConfig(this.Config());
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't save the settings: {ex.Message}", LogLevel.Trace);
            }
        }

        private void TogglePanel(NPC npc)
        {
            if (ReferenceEquals(this.Panel.SelectedNpc, npc))
                this.Panel.Close();
            else
                this.Panel.Open(npc);
        }

        private void RepickAppearances()
        {
            Utility.ForEachVillager(npc =>
            {
                if (!this.Wearing.Contains(npc))
                    this.Inspector.ChooseAppearance(npc);
                return true;
            });
            this.Inspector.ScanNow?.Invoke();
            this.Say("Every character picked her outfit again.", LogLevel.Info);
        }

        private void ToggleHud()
        {
            this.HudHidden = !this.HudHidden;
            Game1.displayHUD = !this.HudHidden;
        }

        /// <summary>Every villager in the save (vanilla and modded), by name. Cached for a few seconds.</summary>
        private List<NPC> GetVillagers()
        {
            if (++this.VillagersAge > 300)
            {
                this.VillagersAge = 0;
                var found = new Dictionary<string, NPC>(StringComparer.OrdinalIgnoreCase);
                Utility.ForEachVillager(npc =>
                {
                    if (!string.IsNullOrEmpty(npc.Name))
                        found.TryAdd(npc.Name, npc);
                    return true;
                });
                this.Villagers = found.Values.OrderBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            return this.Villagers;
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
