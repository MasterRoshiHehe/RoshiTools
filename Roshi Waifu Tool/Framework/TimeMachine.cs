using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Moves the day forward. Verified against Stardew Valley 1.6.15:
    /// - Game1.performTenMinuteClockUpdate() is the real clock step (time +10, every location's performTenMinuteUpdate,
    ///   which calls NPC.checkSchedule for every visible NPC).
    /// - A walking NPC is teleported along her path the way NPC.warpToPathControllerDestination() (vanilla's "debug
    ///   hurry") does it, map warps included, but all the way to the last tile. PathFindController.moveCharacter then
    ///   sees her standing on it at her next update and finishes the walk normally (facing, end-of-route behaviour,
    ///   end callback), so nothing is skipped except the walking itself.
    /// - The same is done for NPC.temporaryController, which the game uses to walk an NPC back to her spot after a
    ///   schedule animation and which LAF uses for detours. LAF keeps the controller object (it only checks the
    ///   reference) and counts a detour's stay in game minutes, so detours play out during a jump.
    /// - NPC.checkSchedule returns early while she still has a path, but queues the stop first; queued legs are started
    ///   in the settle phase.
    /// There is no way back: schedules only move forward, so going back in time means restarting the day.
    /// </summary>
    internal sealed class TimeMachine
    {
        /// <summary>The last time a jump may reach. 2600 makes the player pass out and ends the day.</summary>
        public const int LatestTime = 2550;

        /// <summary>How long the NPCs get to finish their queued schedule legs after a jump.</summary>
        private const int SettleTimeoutTicks = 20 * 60;

        /// <summary>The settle phase ends when nobody's walk state changed for this long (an NPC held on purpose, e.g. by a detour stay).</summary>
        private const int StallTicks = 90;

        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;

        private int Target = -1;
        private int TickCounter;
        private int SettleTicks = -1;
        private string LastBusy;
        private int UnchangedTicks;
        private Action OnDone;

        /// <summary>
        /// LAF's "scan now" hook, if available: called after every step so LAF checks detours, poses and rules for that
        /// step right away instead of on its scan interval. Lets jumps run at <see cref="ModConfig.FastJumpStepTicks"/>.
        /// </summary>
        public Func<Action> GetScanNow { get; set; }

        public TimeMachine(IMonitor monitor, Func<ModConfig> config)
        {
            this.Monitor = monitor;
            this.Config = config;
        }

        /// <summary>Whether a jump is running (including its settle phase).</summary>
        public bool IsBusy => this.Target >= 0 || this.SettleTicks >= 0;

        /// <summary>The time the running jump is heading for, or -1.</summary>
        public int TargetTime => this.Target;

        /// <summary>Whether the NPCs are being settled after the clock reached the target.</summary>
        public bool IsSettling => this.Target < 0 && this.SettleTicks >= 0;

        /// <summary>Round a time to a valid 10-minute game time within the day.</summary>
        public static int Normalize(int time)
        {
            int hours = Math.Max(0, time) / 100;
            int minutes = Math.Max(0, time) % 100;
            if (minutes >= 60)
            {
                hours++;
                minutes = 0;
            }
            return Math.Clamp(hours * 100 + minutes / 10 * 10, 600, LatestTime);
        }

        /// <summary>Format a game time as 14:30.</summary>
        public static string Format(int time)
        {
            return $"{time / 100 % 24:00}:{time % 100:00}";
        }

        /// <summary>Advance one 10-minute step now. Returns false at the end of the day.</summary>
        public bool Step()
        {
            if (Game1.timeOfDay >= LatestTime)
                return false;

            Game1.performTenMinuteClockUpdate();
            this.HurryAll();
            try
            {
                this.GetScanNow?.Invoke()?.Invoke();
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"LAF's scan hook failed: {ex.Message}", LogLevel.Trace);
            }
            return true;
        }

        /// <summary>Start jumping forward to a time; <paramref name="onDone"/> runs when the NPCs have settled.</summary>
        public bool JumpTo(int time, Action onDone = null)
        {
            time = Normalize(time);
            if (this.IsBusy)
                return false;

            if (time <= Game1.timeOfDay)
            {
                onDone?.Invoke();
                return true;
            }

            this.Target = time;
            this.TickCounter = 0;
            this.OnDone = onDone;
            this.Monitor.Log($"Jumping from {Game1.timeOfDay} to {time}.", LogLevel.Trace);
            return true;
        }

        /// <summary>Stop a running jump where it is.</summary>
        public void Cancel()
        {
            this.Target = -1;
            this.SettleTicks = -1;
            this.OnDone = null;
        }

        /// <summary>Call every tick.</summary>
        public void Update()
        {
            if (this.Target >= 0)
            {
                // every tick: walks can start between two steps (a queued leg, another mod's detour)
                this.HurryAll();

                // with the scan hook: LAF scans on the tick after a step and handles a clock change one tick later
                int stepTicks = this.GetScanNow?.Invoke() != null ? Math.Max(3, this.Config().FastJumpStepTicks) : Math.Max(1, this.Config().JumpStepTicks);
                if (++this.TickCounter < stepTicks)
                    return;
                this.TickCounter = 0;

                if (Game1.timeOfDay < this.Target && this.Step())
                    return;

                this.Target = -1;
                this.SettleTicks = 0;
                this.LastBusy = null;
                this.UnchangedTicks = 0;
                return;
            }

            if (this.SettleTicks >= 0)
            {
                this.SettleTicks++;
                this.HurryAll();
                if (this.SettleTicks % 5 == 0)
                    this.StartQueuedLegs();

                string busy = string.Join("; ", this.GetBusyNpcs().Select(Describe));
                this.UnchangedTicks = busy == this.LastBusy ? this.UnchangedTicks + 1 : 0;
                this.LastBusy = busy;

                bool done = busy.Length == 0 && this.SettleTicks >= 5;
                bool stalled = this.UnchangedTicks >= StallTicks;
                bool timedOut = this.SettleTicks > SettleTimeoutTicks;
                if (!done && !stalled && !timedOut)
                    return;

                if (!done)
                    this.Monitor.Log($"Jump to {Game1.timeOfDay}: not everyone finished walking ({(stalled ? "nothing changed for a moment, e.g. a detour stay or a held leg" : $"still moving after {SettleTimeoutTicks / 60} s")}): {busy}", LogLevel.Info);

                this.SettleTicks = -1;
                Action onDone = this.OnDone;
                this.OnDone = null;
                onDone?.Invoke();
            }
        }

        /// <summary>Teleport every walking NPC to the last tile of her path.</summary>
        private void HurryAll()
        {
            foreach (NPC npc in this.GetScheduleNpcs())
            {
                try
                {
                    if (npc.controller != null)
                        FinishWalk(npc, npc.controller);
                    if (npc.temporaryController != null)
                        FinishWalk(npc, npc.temporaryController);
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce($"Couldn't hurry {npc.Name} along her path: {ex.Message}", LogLevel.Trace);
                }
            }
        }

        /// <summary>
        /// Move an NPC along a path until only its last tile is left, and stand her on it. Same steps as
        /// NPC.warpToPathControllerDestination (pop, handleWarps, place, Halt), which stops two tiles early.
        /// </summary>
        private static void FinishWalk(NPC npc, PathFindController walk)
        {
            Stack<Point> path = walk.pathToEndPoint;
            if (path == null || path.Count <= 1)
                return;

            // Still playing an animation (usually the outro of her schedule animation): wait for it. Her motion stays
            // frozen until its last frame calls routeEndAnimationFinished, and Halt() below would cancel the animation
            // (Sprite.StopAnimation clears it), leaving her frozen for the rest of the day.
            if (npc.Sprite?.CurrentAnimation != null)
                return;

            int guard = 0;
            while (path.Count > 1 && guard++ < 10000)
            {
                path.Pop();
                Point tile = path.Peek();
                walk.handleWarps(new Rectangle(tile.X * 64, tile.Y * 64, 64, 64)); // may warp her and pop more tiles
                if (path.Count == 0)
                    return;

                tile = path.Peek();
                npc.Position = new Vector2(tile.X * 64, tile.Y * 64 + 16);
                npc.Halt();
            }

            if (path.Count == 1)
            {
                // 4 px above the tile's bottom edge: moveCharacter only counts her as arrived with a gap of 2+
                Point last = path.Peek();
                npc.Position = new Vector2(last.X * 64, last.Y * 64 + 12);
            }
        }

        /// <summary>Start the schedule legs that queued up while NPCs were busy.</summary>
        private void StartQueuedLegs()
        {
            foreach (NPC npc in this.GetScheduleNpcs())
            {
                if (IsWalking(npc.controller) || IsWalking(npc.temporaryController) || npc.queuedSchedulePaths.Count == 0)
                    continue;

                try
                {
                    npc.checkSchedule(Game1.timeOfDay);
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce($"Couldn't start {npc.Name}'s queued schedule leg: {ex.Message}", LogLevel.Trace);
                }
            }
        }

        private IEnumerable<NPC> GetBusyNpcs()
        {
            // A controller with an empty path is normal for an NPC who arrived and plays her schedule animation: the
            // game only clears it once her motion is unfrozen, and NPC.checkSchedule treats it as idle too.
            return this.GetScheduleNpcs().Where(npc => IsWalking(npc.controller) || IsWalking(npc.temporaryController) || npc.queuedSchedulePaths.Count > 0);
        }

        private static bool IsWalking(PathFindController walk)
        {
            return walk?.pathToEndPoint != null && walk.pathToEndPoint.Count > 0;
        }

        private static string Describe(NPC npc)
        {
            var parts = new List<string>();
            if (IsWalking(npc.controller))
                parts.Add($"schedule walk, {npc.controller.pathToEndPoint.Count} tile(s) left");
            if (IsWalking(npc.temporaryController))
                parts.Add($"other walk, {npc.temporaryController.pathToEndPoint.Count} tile(s) left");
            if (npc.queuedSchedulePaths.Count > 0)
                parts.Add($"{npc.queuedSchedulePaths.Count} leg(s) waiting");
            if (npc.Sprite?.CurrentAnimation != null)
                parts.Add("animating");
            return $"{npc.Name} in {npc.currentLocation?.NameOrUniqueName} {npc.TilePoint.X} {npc.TilePoint.Y} ({string.Join(", ", parts)})";
        }

        /// <summary>Every NPC with a schedule today, as a copy (hurrying can move NPCs between locations).</summary>
        private List<NPC> GetScheduleNpcs()
        {
            var npcs = new List<NPC>();
            Utility.ForEachCharacter(npc =>
            {
                if (npc.Schedule != null && !npc.IsInvisible)
                    npcs.Add(npc);
                return true;
            });
            return npcs;
        }
    }
}
