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

        /// <summary>Don't start events (cutscenes) when you enter a location while the workspace is open. They aren't marked as seen.</summary>
        public bool IgnoreEvents { get; set; } = true;

        /// <summary>
        /// Game ticks between two 10-minute steps of a time jump. 10 matches LAF's default scan interval, so LAF sees every
        /// step and detours start as in real play (a 6-hour jump takes about 6 seconds). Lower is faster, but things that
        /// are checked only every few ticks can miss their moment.
        /// </summary>
        public int JumpStepTicks { get; set; } = 10;

        /// <summary>
        /// Game ticks between two steps of a time jump when LAF has its "scan now" hook (LAF with the editor API). The
        /// tool then asks LAF to scan after every step, so this can be small. Minimum 3.
        /// </summary>
        public int FastJumpStepTicks { get; set; } = 3;

        /// <summary>
        /// Hide the game's HUD (hotbar, energy, buffs) and other mods' HUD icons when the workspace opens. It can be
        /// switched back on from the toolbar; closing the workspace always restores it.
        /// </summary>
        public bool HideGameHud { get; set; } = true;

        /// <summary>Keep the game's clock in the top right corner while the HUD is hidden.</summary>
        public bool KeepClock { get; set; } = true;

        /// <summary>
        /// Right-click a character or a tile in the workspace to open a ring of options. Hold Shift to right-click
        /// normally (talk, use, interact).
        /// </summary>
        public bool PieMenu { get; set; } = true;

        /// <summary>
        /// Name endings that mark an NPC as another version of an existing character: "Haley" + "Lewd" is listed under
        /// Haley. Names that are an existing name plus only symbols are always grouped (config.json only).
        /// </summary>
        public List<string> SubCharacterSuffixes { get; set; } = new() { "Lewd" };

        /// <summary>The text boxes above characters (appearance ID and pose) in the workspace: "Off", "Show" (always) or "Hover" (only for the one under the cursor).</summary>
        public string Labels { get; set; } = "Show";

        /// <summary>Leave hidden copies of characters (extra NPCs some mods park out of reach, named like the original plus characters that can't be typed) out of the "in this map" column.</summary>
        public bool HideAltsInColumn { get; set; } = true;

        /// <summary>The item floating above the selected character, as a qualified item ID. "(O)60" is the emerald; "(O)72" diamond, "(O)74" prismatic shard. Empty = no marker.</summary>
        public string SelectionMarker { get; set; } = "(O)60";

        /// <summary>While the workspace is open, keep left clicks, the use-tool key and the mouse wheel from the game (no tool swings, no hotbar switching), so the tool can use them. The interact key still works.</summary>
        public bool TakeOverControls { get; set; } = true;

        /// <summary>Leave characters you can't befriend (animals and other background NPCs mods add) out of the "in this map" column.</summary>
        public bool HideNonSocialInColumn { get; set; } = true;

        /// <summary>Open every door in the workspace: shops and houses outside their hours, and bedroom doors that need friendship.</summary>
        public bool UnlockDoors { get; set; } = false;

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
        /// Extra mods whose config menu gets a button under "Settings" on the toolbar, by unique ID. This tool, LAF, APF
        /// and the watched content packs are always listed (config.json only).
        /// </summary>
        public List<string> ConfigMenuMods { get; set; } = new();

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
