using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>Keeps the day from ending (and so the game from saving) while the workspace is open, and optionally keeps events from starting.</summary>
    internal static class SandboxPatches
    {
        private static IMonitor Monitor;
        private static Func<bool> IsSandbox;
        private static Func<bool> IgnoreEvents;
        private static Func<bool> HideHud;
        private static Func<int> ReservedBottomLeft;
        private static Func<bool> UnlockDoors;
        private static Func<bool> BlockControls;

        public static void Apply(Harmony harmony, IMonitor monitor, Func<bool> isSandbox, Func<bool> ignoreEvents, Func<bool> hideHud, Func<int> reservedBottomLeft, Func<bool> unlockDoors, Func<bool> blockControls)
        {
            Monitor = monitor;
            IsSandbox = isSandbox;
            IgnoreEvents = ignoreEvents;
            HideHud = hideHud;
            ReservedBottomLeft = reservedBottomLeft;
            UnlockDoors = unlockDoors;
            BlockControls = blockControls;

            // The mouse wheel can't be suppressed through SMAPI. Game1.pressSwitchToolButton is what turns a wheel step
            // (or a gamepad trigger) into a hotbar switch (verified against 1.6.15), so skip it instead.
            try
            {
                harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.pressSwitchToolButton)), prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_PressSwitchToolButton)));
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't patch the hotbar switch; the mouse wheel still changes the held item in the workspace. ({ex.Message})", LogLevel.Debug);
            }

            // "All doors unlocked" (verified against 1.6.15): GameLocation.lockedDoorWarp is the shop/house door with
            // opening hours and friendship; the "Door" tile action (performAction) and touch action (performTouchAction)
            // are the bedroom doors that need two hearts.
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.lockedDoorWarp)), prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_LockedDoorWarp)));
                harmony.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performAction), new[] { typeof(string[]), typeof(Farmer), typeof(xTile.Dimensions.Location) }), prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_PerformAction)));
                harmony.Patch(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.performTouchAction), new[] { typeof(string[]), typeof(Microsoft.Xna.Framework.Vector2) }), prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_PerformTouchAction)));
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't patch the door checks; 'All doors unlocked' won't work. ({ex.Message})", LogLevel.Debug);
            }

            // The game stacks its notifications upwards from the bottom left corner (Game1._draw: heightUsed starts at 0
            // and the newest message, the last in Game1.hudMessages, is drawn first). Starting that stack higher keeps
            // them clear of the tool's character column.
            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(HUDMessage), nameof(HUDMessage.draw)),
                    prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_DrawHudMessage))
                );
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't patch HUDMessage.draw; notifications can overlap the tool's bottom left corner. ({ex.Message})", LogLevel.Debug);
            }

            // Other mods draw their HUD icons in SMAPI's RenderingHud / RenderedHud events, which SMAPI raises from
            // SCore.OnRenderingStep / OnRenderedStep even when the game's own HUD is hidden. Skipping those two calls for
            // the HUD step hides every mod's HUD drawing at once (verified against StardewModdingAPI.dll 4.x).
            try
            {
                Type core = typeof(IModHelper).Assembly.GetType("StardewModdingAPI.Framework.SCore");
                var prefix = new HarmonyMethod(typeof(SandboxPatches), nameof(Before_RenderStep));
                harmony.Patch(AccessTools.Method(core, "OnRenderingStep"), prefix: prefix);
                harmony.Patch(AccessTools.Method(core, "OnRenderedStep"), prefix: prefix);
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't hook SMAPI's HUD events; other mods' HUD icons stay visible when the HUD is hidden. ({ex.Message})", LogLevel.Debug);
            }

            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkForEvents)),
                    prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_CheckForEvents))
                );
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't patch GameLocation.checkForEvents; events can still start in the workspace.\n{ex}", LogLevel.Warn);
            }

            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(Game1), nameof(Game1.NewDay), new[] { typeof(float) }),
                    prefix: new HarmonyMethod(typeof(SandboxPatches), nameof(Before_NewDay))
                );
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't patch Game1.NewDay; don't go to bed while the workspace is open.\n{ex}", LogLevel.Warn);
            }
        }

        private static bool Before_RenderStep(StardewValley.Mods.RenderSteps step)
        {
            try
            {
                return step != StardewValley.Mods.RenderSteps.HUD || HideHud == null || !HideHud();
            }
            catch
            {
                return true;
            }
        }

        private static void Before_DrawHudMessage(ref int heightUsed)
        {
            try
            {
                if (heightUsed == 0 && ReservedBottomLeft != null)
                    heightUsed = ReservedBottomLeft();
            }
            catch
            {
                // keep the game's own placement
            }
        }

        private static bool Before_PressSwitchToolButton()
        {
            try
            {
                return BlockControls == null || !BlockControls();
            }
            catch
            {
                return true;
            }
        }

        private static bool DoorsOpen()
        {
            try
            {
                return UnlockDoors != null && UnlockDoors();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>A door with opening hours or a friendship need: go straight in, as the game does when it is open.</summary>
        private static bool Before_LockedDoorWarp(GameLocation __instance, Microsoft.Xna.Framework.Point tile, string locationName)
        {
            if (!DoorsOpen())
                return true;
            try
            {
                Game1.player.completelyStopAnimatingOrDoingAction();
                __instance.playSound("doorClose", Game1.player.Tile);
                Game1.warpFarmer(locationName, tile.X, tile.Y, flip: false);
                return false;
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Opening a locked door failed: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }

        /// <summary>A bedroom door clicked: open it without the two hearts.</summary>
        private static bool Before_PerformAction(GameLocation __instance, string[] action, xTile.Dimensions.Location tileLocation, ref bool __result)
        {
            if (action == null || action.Length < 2 || action[0] != "Door" || !DoorsOpen())
                return true;
            try
            {
                __instance.openDoor(tileLocation, playSound: true);
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Opening a bedroom door failed: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }

        /// <summary>Walking through a bedroom doorway: skip the check that pushes the farmer back without two hearts.</summary>
        private static bool Before_PerformTouchAction(string[] action)
        {
            return action == null || action.Length < 1 || action[0] != "Door" || !DoorsOpen();
        }

        private static bool Before_CheckForEvents()
        {
            try
            {
                // (Game1.killScreen: the same method handles the farmer's death; never block that.)
                return IgnoreEvents == null || !IgnoreEvents() || Game1.killScreen;
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Event check failed: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }

        private static bool Before_NewDay()
        {
            try
            {
                if (IsSandbox == null || !IsSandbox())
                    return true;

                Game1.addHUDMessage(new HUDMessage("The day can't end while the workspace is open.") { noIcon = true });
                return false;
            }
            catch (Exception ex)
            {
                Monitor?.LogOnce($"Sandbox check failed: {ex.Message}", LogLevel.Trace);
                return true;
            }
        }
    }
}
