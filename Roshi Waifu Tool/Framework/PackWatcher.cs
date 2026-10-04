using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Watches the JSON files of Content Patcher packs and runs 'patch reload' when one changes. Files are polled (no
    /// FileSystemWatcher), which also works through Mod Organizer 2's virtual folder. Only reads; never writes.
    /// </summary>
    internal sealed class PackWatcher
    {
        private const string ContentPatcherId = "Pathoschild.ContentPatcher";
        private static readonly string[] FrameworkIds = { "MasterRoshiHehe.LiveAppearanceFramework", "tyr4ntx.AnimatedPortraitFramework" };

        private sealed class Pack
        {
            public string Id;
            public string Name;
            public string Folder;
            /// <summary>Relative path → "ticks|length" from the last scan; null until the first scan.</summary>
            public Dictionary<string, string> Files;
        }

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;
        private readonly CommandRunner Commands;

        private readonly List<Pack> Packs = new();
        private readonly object Lock = new();
        /// <summary>Pack ID → changed relative paths, waiting to be reloaded (filled by the scan thread).</summary>
        private readonly Dictionary<string, HashSet<string>> Pending = new(StringComparer.OrdinalIgnoreCase);
        private DateTime LastChange = DateTime.MinValue;
        private DateTime LastScan = DateTime.MinValue;
        private Task Scan;

        public PackWatcher(IModHelper helper, IMonitor monitor, Func<ModConfig> config, CommandRunner commands)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Config = config;
            this.Commands = commands;
        }

        /// <summary>The watched packs, for the console.</summary>
        public IEnumerable<string> Describe()
        {
            return this.Packs.Select(pack => $"{pack.Id} ({pack.Name}) — {pack.Folder}, {(pack.Files != null ? $"{pack.Files.Count} JSON file(s)" : "not scanned yet")}");
        }

        /// <summary>The unique IDs of the watched packs.</summary>
        public IEnumerable<string> GetPackIds()
        {
            return this.Packs.Select(pack => pack.Id);
        }

        /// <summary>Find the packs to watch. Call once all mods are loaded, and again when the config changes.</summary>
        public void BuildWatchList()
        {
            ModConfig config = this.Config();
            var extra = new HashSet<string>(config.WatchedPacks ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var old = this.Packs.ToDictionary(pack => pack.Id, StringComparer.OrdinalIgnoreCase);
            this.Packs.Clear();

            foreach (IModInfo mod in this.Helper.ModRegistry.GetAll())
            {
                IManifest manifest = mod.Manifest;
                if (!mod.IsContentPack || !string.Equals(manifest.ContentPackFor?.UniqueID, ContentPatcherId, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool usesFramework = manifest.Dependencies != null && manifest.Dependencies.Any(dep => FrameworkIds.Contains(dep.UniqueID, StringComparer.OrdinalIgnoreCase));
                if (!config.WatchAllContentPacks && !usesFramework && !extra.Contains(manifest.UniqueID))
                    continue;

                // IModInfo is implemented by SMAPI's internal IModMetadata, which has DirectoryPath.
                string folder = mod.GetType().GetProperty("DirectoryPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mod) as string;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    this.Monitor.Log($"Can't watch {manifest.UniqueID}: its folder wasn't found.", LogLevel.Warn);
                    continue;
                }

                this.Packs.Add(old.TryGetValue(manifest.UniqueID, out Pack known) && known.Folder == folder
                    ? known
                    : new Pack { Id = manifest.UniqueID, Name = manifest.Name, Folder = folder });
            }

            this.Monitor.Log(this.Packs.Count == 0
                ? "Auto reload: no Content Patcher packs to watch."
                : $"Auto reload watches {this.Packs.Count} pack(s): {string.Join(", ", this.Packs.Select(pack => pack.Id))}.", LogLevel.Trace);
        }

        /// <summary>Call every tick.</summary>
        public void Update()
        {
            ModConfig config = this.Config();
            if (!config.AutoReload || this.Packs.Count == 0)
                return;

            // start the next scan off the game thread
            if ((this.Scan == null || this.Scan.IsCompleted) && (DateTime.UtcNow - this.LastScan).TotalSeconds >= Math.Max(1, config.PollSeconds))
            {
                if (this.Scan?.IsFaulted == true)
                    this.Monitor.Log($"Scanning the watched packs failed: {this.Scan.Exception?.GetBaseException().Message}", LogLevel.Trace);

                this.LastScan = DateTime.UtcNow;
                Pack[] packs = this.Packs.ToArray();
                this.Scan = Task.Run(() => this.ScanPacks(packs));
            }

            // reload once the files have been quiet for a moment (editors write in several steps)
            if (!CanReloadNow())
                return;

            KeyValuePair<string, HashSet<string>>[] due;
            lock (this.Lock)
            {
                if (this.Pending.Count == 0 || (DateTime.UtcNow - this.LastChange).TotalMilliseconds < 500)
                    return;
                due = this.Pending.ToArray();
                this.Pending.Clear();
            }

            foreach (var pair in due)
                this.Reload(pair.Key, pair.Value);
        }

        /// <summary>Reload a pack now (or every watched pack if no ID is given).</summary>
        public void ReloadNow(string packId = null)
        {
            if (packId != null)
            {
                this.Reload(packId, null);
                return;
            }

            foreach (Pack pack in this.Packs)
                this.Reload(pack.Id, null);
        }

        private static bool CanReloadNow()
        {
            if (!Context.IsWorldReady)
                return Game1.gameMode == 0; // title screen is fine; never while a save loads
            return !Game1.eventUp && Game1.activeClickableMenu == null && !Game1.dialogueUp;
        }

        private void Reload(string packId, HashSet<string> changed)
        {
            if (!this.Commands.Exists("patch"))
            {
                this.Monitor.LogOnce("Can't reload content packs: Content Patcher's 'patch' command wasn't found.", LogLevel.Warn);
                return;
            }

            ModConfig config = this.Config();
            bool includesOnly = !config.ReloadWholePack && changed != null && changed.Count > 0 && !changed.Any(path => path.Equals("content.json", StringComparison.OrdinalIgnoreCase));
            string what = changed == null ? "" : $" (changed: {string.Join(", ", changed.OrderBy(path => path).Take(5))}{(changed.Count > 5 ? ", …" : "")})";

            this.Monitor.Log($"Reloading {packId}{what}.", LogLevel.Info);
            if (includesOnly)
            {
                foreach (string path in changed)
                    this.Commands.Run("patch", "reload", packId, path);
            }
            else
                this.Commands.Run("patch", "reload", packId);

            if (Context.IsWorldReady)
                Game1.addHUDMessage(new HUDMessage($"Reloaded {this.Packs.FirstOrDefault(pack => pack.Id.Equals(packId, StringComparison.OrdinalIgnoreCase))?.Name ?? packId}") { noIcon = true });
        }

        /// <summary>Runs on a background thread.</summary>
        private void ScanPacks(Pack[] packs)
        {
            foreach (Pack pack in packs)
            {
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string path in Directory.EnumerateFiles(pack.Folder, "*.json", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(path);
                    files[Path.GetRelativePath(pack.Folder, path)] = $"{info.LastWriteTimeUtc.Ticks}|{info.Length}";
                }

                Dictionary<string, string> before = pack.Files;
                pack.Files = files;
                if (before == null)
                    continue; // first scan is the baseline

                var changed = new List<string>();
                foreach (var pair in files)
                {
                    // manifest.json and config.json aren't re-read by 'patch reload'
                    if (pair.Key.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!before.TryGetValue(pair.Key, out string old) || old != pair.Value)
                        changed.Add(pair.Key);
                }

                changed.AddRange(before.Keys.Where(path => !files.ContainsKey(path))); // deleted files

                if (changed.Count == 0)
                    continue;

                lock (this.Lock)
                {
                    if (!this.Pending.TryGetValue(pack.Id, out HashSet<string> set))
                        this.Pending[pack.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string path in changed)
                        set.Add(path);
                    this.LastChange = DateTime.UtcNow;
                }
            }
        }
    }
}
