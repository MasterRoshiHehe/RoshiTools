using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>Keeps the day from ending (and so the game from saving) while the workspace is open.</summary>
    internal static class SandboxPatches
    {
        private static IMonitor Monitor;
        private static Func<bool> IsSandbox;

        public static void Apply(Harmony harmony, IMonitor monitor, Func<bool> isSandbox)
        {
            Monitor = monitor;
            IsSandbox = isSandbox;

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
