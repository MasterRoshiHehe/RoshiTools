using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Groups "sub-characters" under the character they belong to. Some mods add extra NPCs that are another version
    /// of an existing one: the same name plus symbols (e.g. two hidden copies of Haley) or plus a known suffix (e.g.
    /// "HaleyLewd"). Those are listed under "Haley" instead of as characters of their own.
    /// </summary>
    internal static class NpcGroups
    {
        public sealed class Entry
        {
            public NPC Npc;
            /// <summary>The name to show (sub-characters with unprintable names become "Haley (alt 1)").</summary>
            public string Label;
            public List<Entry> Subs = new();
            /// <summary>A copy of another character whose name differs only by characters that can't be typed (hidden helper NPCs some mods add).</summary>
            public bool IsHiddenCopy;
        }

        public static List<Entry> Build(IEnumerable<NPC> villagers, IEnumerable<string> suffixes)
        {
            List<NPC> all = villagers.Where(npc => !string.IsNullOrEmpty(npc.Name)).ToList();
            var byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (NPC npc in all)
                byName.TryAdd(npc.Name, new Entry { Npc = npc, Label = UiText.Clean(npc.Name) });

            // a name in plain letters and digits -> the entry with exactly that name
            var byPlain = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in byName.Values)
            {
                if (IsPlain(entry.Npc.Name))
                    byPlain.TryAdd(entry.Npc.Name, entry);
            }

            var suffixSet = new HashSet<string>((suffixes ?? Enumerable.Empty<string>()).Where(suffix => !string.IsNullOrWhiteSpace(suffix)), StringComparer.OrdinalIgnoreCase);
            var roots = new List<Entry>();
            foreach (Entry entry in byName.Values.OrderBy(e => e.Npc.Name, StringComparer.OrdinalIgnoreCase))
            {
                Entry parent = FindParent(entry, byPlain, suffixSet);
                if (parent == null)
                    roots.Add(entry);
                else
                    parent.Subs.Add(entry);
            }

            // copies of a character that isn't in the game under her plain name (only "Witch[dot]" and "Witch[dot][dot]"
            // exist): the first one stands for the group, the others go under her
            var orphans = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in roots.ToList())
            {
                string name = entry.Npc.Name;
                string plain = GetPlain(name);
                if (IsPlain(name) || plain.Length == 0)
                    continue;

                entry.IsHiddenCopy = true;
                if (orphans.TryGetValue(plain, out Entry first))
                {
                    roots.Remove(entry);
                    first.Subs.Add(entry);
                    entry.Label = $"{UiText.Clean(plain)} (alt {first.Subs.Count + 1})";
                }
                else
                {
                    orphans[plain] = entry;
                    entry.Label = $"{UiText.Clean(plain)} (alt 1)";
                }
            }

            // names that can't be shown get a number instead: "Haley (alt 1)"
            foreach (Entry root in roots)
            {
                if (root.IsHiddenCopy)
                    continue;
                int alt = 0;
                foreach (Entry sub in root.Subs)
                {
                    if (GetPlain(sub.Npc.Name).Equals(root.Npc.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        sub.Label = $"{root.Label} (alt {++alt})";
                        sub.IsHiddenCopy = true;
                    }
                }
            }
            return roots;
        }

        /// <summary>
        /// The entry a character is a sub-character of, or null. Her name without anything that isn't a plain letter
        /// or digit (mods hide copies behind dots, invisible characters or look-alike symbols anywhere in the name)
        /// must be another character's name, or that name plus a known suffix.
        /// </summary>
        private static Entry FindParent(Entry entry, Dictionary<string, Entry> byPlain, HashSet<string> suffixes)
        {
            string name = entry.Npc.Name;
            string plain = GetPlain(name);
            if (plain.Length == 0)
                return null;

            if (plain.Length != name.Length && byPlain.TryGetValue(plain, out Entry same) && !ReferenceEquals(same, entry))
                return same;

            foreach (string suffix in suffixes)
            {
                if (plain.Length > suffix.Length && plain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && byPlain.TryGetValue(plain.Substring(0, plain.Length - suffix.Length), out Entry parent) && !ReferenceEquals(parent, entry))
                    return parent;
            }
            return null;
        }

        private static bool IsPlainChar(char c)
        {
            return c < 128 && (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '.');
        }

        private static bool IsPlain(string name)
        {
            return name.All(IsPlainChar);
        }

        /// <summary>A name with only its plain characters left.</summary>
        public static string GetPlain(string name)
        {
            return new string((name ?? "").Where(IsPlainChar).ToArray()).Trim();
        }

        /// <summary>A name with every unusual character written as its code, for the log: "Haley[U+00B7]".</summary>
        public static string Spell(string name)
        {
            return string.Concat((name ?? "").Select(c => c >= 32 && c < 127 ? c.ToString() : $"[U+{(int)c:X4}]"));
        }

        /// <summary>The mod an NPC name points to, or null: "Author.Mod_Name" → "Author.Mod", "A.B.C.Name" → "A.B.C".</summary>
        public static string GetModPrefix(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.Contains('.'))
                return null;

            int underscore = name.LastIndexOf('_');
            int cut = underscore > name.IndexOf('.') ? underscore : name.LastIndexOf('.');
            return cut > 0 ? name.Substring(0, cut) : null;
        }
    }

    /// <summary>What the tool remembers between sessions (stored in SMAPI's global data folder, outside the Mods folder).</summary>
    public sealed class ToolData
    {
        public List<string> Favourites { get; set; } = new();
    }
}
