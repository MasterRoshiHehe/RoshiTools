using System;
using System.Reflection;
using StardewModdingAPI;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Runs another mod's console command. SMAPI has no public API for this (ICommandHelper only has Add), so this reads
    /// SMAPI's internal command list: SCore.Instance → CommandManager → Get(name) → Callback. Verified against
    /// StardewModdingAPI.dll 4.x; every step is null-checked so a SMAPI change only disables the feature.
    /// </summary>
    internal sealed class CommandRunner
    {
        private readonly IMonitor Monitor;
        private object Manager;
        private MethodInfo GetMethod;
        private bool Broken;

        public CommandRunner(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>Whether a console command with this name exists.</summary>
        public bool Exists(string name)
        {
            return this.Find(name) != null;
        }

        /// <summary>Run a console command now. Returns false if it doesn't exist or SMAPI's internals couldn't be read.</summary>
        public bool Run(string name, params string[] args)
        {
            object command = this.Find(name);
            if (command == null)
                return false;

            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var callback =
                    command.GetType().GetProperty("Callback", flags)?.GetValue(command) as Action<string, string[]>
                    ?? command.GetType().GetField("Callback", flags)?.GetValue(command) as Action<string, string[]>;
                if (callback == null)
                {
                    this.Fail("the command has no readable callback");
                    return false;
                }

                callback(name, args ?? Array.Empty<string>());
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Running '{name} {string.Join(" ", args ?? Array.Empty<string>())}' failed:\n{ex}", LogLevel.Error);
                return false;
            }
        }

        private object Find(string name)
        {
            if (this.Broken || string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                if (this.Manager == null)
                {
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    Type coreType = typeof(IModHelper).Assembly.GetType("StardewModdingAPI.Framework.SCore");
                    object core = coreType?.GetProperty("Instance", flags)?.GetValue(null);
                    this.Manager = coreType?.GetField("CommandManager", flags)?.GetValue(core);
                    this.GetMethod = this.Manager?.GetType().GetMethod("Get", flags, null, new[] { typeof(string) }, null);
                    if (this.Manager == null || this.GetMethod == null)
                    {
                        this.Fail("SMAPI's command manager wasn't found");
                        return null;
                    }
                }

                return this.GetMethod.Invoke(this.Manager, new object[] { name });
            }
            catch (Exception ex)
            {
                this.Fail(ex.Message);
                return null;
            }
        }

        private void Fail(string reason)
        {
            this.Broken = true;
            this.Monitor.Log($"Can't run console commands for you ({reason}); this SMAPI version probably changed its internals. Type the commands in the SMAPI console instead.", LogLevel.Warn);
        }
    }
}
