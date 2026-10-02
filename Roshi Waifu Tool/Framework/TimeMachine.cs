using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Moves the day forward. Verified against Stardew Valley 1.6.15:
    /// - Game1.performTenMinuteClockUpdate() is the real clock step (time +10, every location's performTenMinuteUpdate,
    ///   which calls NPC.checkSchedule for every visible NPC).
    /// - NPC.warpToPathControllerDestination() (vanilla's "debug hurry") pops an NPC's schedule path down to its last two
    ///   tiles, handling map warps, so she arrives and plays her end-of-route behaviour naturally.
    /// - NPC.checkSchedule returns early while she still has a path, but queues the stop first; the queued legs are
    ///   started in the settle phase.
    /// There is no way back: schedules only move forward, so going back in time means restarting the day.
    /// </summary>
    internal sealed class TimeMachine
    {
        /// <summary>The last time a jump may reach. 2600 makes the player pass out and ends the day.</summary>
        public const int LatestTime = 2550;

        /// <summary>How long the NPCs get to finish their queued schedule legs after a jump.</summary>
        private const int SettleTimeoutTicks = 20 * 60;

        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;

        private int Target = -1;
        private int TickCounter;
        private int SettleTicks = -1;
        private Action OnDone;

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
                if (++this.TickCounter < Math.Max(1, this.Config().JumpTicksPerStep))
                    return;
                this.TickCounter = 0;

                if (Game1.timeOfDay < this.Target && this.Step())
                    return;

                this.Target = -1;
                this.SettleTicks = 0;
                return;
            }

            if (this.SettleTicks >= 0)
            {
                this.SettleTicks++;
                if (this.SettleTicks % 5 == 0 && !this.Settle() || this.SettleTicks > SettleTimeoutTicks)
                {
                    if (this.SettleTicks > SettleTimeoutTicks)
                        this.Monitor.Log($"Some NPCs were still on their way {SettleTimeoutTicks / 60} seconds after the jump to {Game1.timeOfDay}: {string.Join(", ", this.GetBusyNpcs().Select(npc => npc.Name))}.", LogLevel.Debug);

                    this.SettleTicks = -1;
                    Action done = this.OnDone;
                    this.OnDone = null;
                    done?.Invoke();
                }
            }
        }

        /// <summary>Move every NPC who is walking a schedule leg to the end of it.</summary>
        private void HurryAll()
        {
            foreach (NPC npc in this.GetScheduleNpcs())
            {
                if (npc.controller?.pathToEndPoint == null || npc.controller.pathToEndPoint.Count <= 2)
                    continue;

                try
                {
                    npc.warpToPathControllerDestination();
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce($"Couldn't hurry {npc.Name} along her path: {ex.Message}", LogLevel.Trace);
                }
            }
        }

        /// <summary>Start queued legs and hurry walkers. Returns whether anyone is still busy.</summary>
        private bool Settle()
        {
            foreach (NPC npc in this.GetScheduleNpcs())
            {
                if (npc.controller == null && npc.temporaryController == null && npc.queuedSchedulePaths.Count > 0)
                {
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

            this.HurryAll();
            return this.GetBusyNpcs().Any();
        }

        private IEnumerable<NPC> GetBusyNpcs()
        {
            return this.GetScheduleNpcs().Where(npc => npc.controller != null || npc.queuedSchedulePaths.Count > 0);
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
