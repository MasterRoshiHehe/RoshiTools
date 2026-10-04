using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Finds out which content pack adds a character, by looking for her name (as a quoted JSON string) in the JSON
    /// files of every installed content pack. The game doesn't record who added an NPC, so this is the only way to
    /// name the mod behind a hidden copy. It reads files only, runs once in the background, and never blocks the game.
    /// </summary>
    internal sealed class NpcSources
    {
        private const long MaxFileBytes = 6 * 1024 * 1024;

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly ConcurrentDictionary<string, string> Found = new(StringComparer.Ordinal);
        private readonly HashSet<string> Asked = new(StringComparer.Ordinal);

        public NpcSources(IModHelper helper, IMonitor monitor)
        {
            this.Helper = helper;
            this.Monitor = monitor;
        }

        /// <summary>The name of the mod that adds an NPC, or null if it isn't known (yet).</summary>
        public string GetModName(string npcName)
        {
            return this.Found.TryGetValue(npcName, out string mod) ? mod : null;
        }

        /// <summary>Look for the mods behind these names (the ones not asked about before). Returns at once; results arrive later.</summary>
        public void Find(IEnumerable<string> npcNames)
        {
            List<string> names = npcNames.Where(name => !string.IsNullOrEmpty(name) && this.Asked.Add(name)).ToList();
            if (names.Count == 0)
                return;

            // read on the main thread what needs SMAPI: each content pack's name and folder
            var packs = new List<(string Name, string Folder)>();
            foreach (IModInfo mod in this.Helper.ModRegistry.GetAll())
            {
                if (!mod.IsContentPack)
                    continue;
                string folder = mod.GetType().GetProperty("DirectoryPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(mod) as string;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    packs.Add((mod.Manifest.Name, folder));
            }

            Task.Run(() => this.Scan(names, packs));
        }

        private void Scan(List<string> names, List<(string Name, string Folder)> packs)
        {
            try
            {
                // each name as it can appear in a JSON file: as typed, or with unusual characters escaped
                var needles = names.ToDictionary(name => name, name => new[] { $"\"{name}\"", $"\"{Escape(name, upper: false)}\"", $"\"{Escape(name, upper: true)}\"" }.Distinct().ToArray());
                var open = new HashSet<string>(names, StringComparer.Ordinal);

                foreach ((string packName, string folder) in packs)
                {
                    if (open.Count == 0)
                        break;

                    foreach (string file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
                    {
                        if (open.Count == 0)
                            break;
                        try
                        {
                            if (new FileInfo(file).Length > MaxFileBytes)
                                continue;
                            string text = File.ReadAllText(file);
                            foreach (string name in open.ToList())
                            {
                                if (needles[name].Any(needle => text.Contains(needle, StringComparison.Ordinal)))
                                {
                                    this.Found[name] = packName;
                                    open.Remove(name);
                                }
                            }
                        }
                        catch (IOException)
                        {
                            // locked or gone: skip the file
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Looking up which mods add the hidden characters failed: {ex.Message}", LogLevel.Trace);
            }
        }

        private static string Escape(string name, bool upper)
        {
            var text = new StringBuilder();
            foreach (char c in name)
                text.Append(c < 128 ? c.ToString() : "\\u" + ((int)c).ToString(upper ? "X4" : "x4"));
            return text.ToString();
        }
    }

    /// <summary>What the unmodded game has: read straight from its Content folder, past SMAPI, so no mod's edits are in it.</summary>
    internal static class Vanilla
    {
        private static readonly Dictionary<string, int> ExpressionCounts = new(StringComparer.Ordinal);

        /// <summary>How many expressions the game's own portrait sheet for this character has (0 if she isn't a vanilla character).</summary>
        public static int GetExpressionCount(string portraitName)
        {
            if (string.IsNullOrEmpty(portraitName))
                return 0;
            if (ExpressionCounts.TryGetValue(portraitName, out int count))
                return count;

            try
            {
                var raw = new Microsoft.Xna.Framework.Content.ContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
                Texture2D sheet = raw.Load<Texture2D>(Path.Combine("Portraits", portraitName));
                count = sheet.Width / 64 * (sheet.Height / 64);
                raw.Unload();
            }
            catch
            {
                count = 0; // no such file: a modded character
            }
            ExpressionCounts[portraitName] = count;
            return count;
        }
    }
}
