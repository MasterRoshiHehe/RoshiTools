using System.Collections.Generic;
using StardewModdingAPI.Utilities;

namespace RoshiWaifuTool.Framework
{
    /// <summary>The mod settings (config.json, and GMCM if installed).</summary>
    public sealed class ModConfig
    {
        /// <summary>Opens or closes the workspace.</summary>
        public KeybindList ToggleWorkspace { get; set; } = KeybindList.Parse("F6");

        /// <summary>The time buttons on the toolbar (24-hour game times, comma separated).</summary>
        public string Checkpoints { get; set; } = "600, 1200, 1800, 2200";

        /// <summary>
        /// When the workspace closes, reload the save and bring you back to the time and place you opened it at, so
        /// nothing you did inside is kept. If false, the world stays as you left it.
        /// </summary>
        public bool RestoreOnExit { get; set; } = true;

        /// <summary>Game ticks between two 10-minute steps of a time jump (higher = slower, gives other mods more time per step).</summary>
        public int JumpTicksPerStep { get; set; } = 2;

        /// <summary>Show each NPC's appearance ID above her head while the workspace is open.</summary>
        public bool ShowLabels { get; set; } = true;

        /// <summary>How fast the free camera moves (pixels per tick; hold Shift for 3x).</summary>
        public int CameraSpeed { get; set; } = 16;

        /// <summary>Run 'patch reload' for a watched content pack when one of its JSON files changes on disk.</summary>
        public bool AutoReload { get; set; } = true;

        /// <summary>How often the watched packs are checked for changed files, in seconds.</summary>
        public int PollSeconds { get; set; } = 1;

        /// <summary>
        /// Reload the whole pack on any change (reliable). If false, a changed file other than content.json is reloaded as
        /// an Include file only, which is faster but fails if that file isn't loaded by an enabled Include patch.
        /// </summary>
        public bool ReloadWholePack { get; set; } = true;

        /// <summary>Watch every Content Patcher pack instead of only those that depend on LAF or APF.</summary>
        public bool WatchAllContentPacks { get; set; } = false;

        /// <summary>Extra Content Patcher packs to watch, by unique ID (config.json only).</summary>
        public List<string> WatchedPacks { get; set; } = new();

        /// <summary>
        /// Console commands run when you click an NPC with the inspector on; {0} is her name. Commands that aren't
        /// installed are skipped (config.json only).
        /// </summary>
        public List<string> InspectCommands { get; set; } = new()
        {
            "laf_why {0}",
            "laf_schedule {0}",
            "laf_pose {0}",
            "laf_detour {0}",
            "apf_portraits {0}"
        };
    }
}
