using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The character browser, shown as the hub's NPCs drawer: filters stacked on the left (All, Favourites, In this map,
    /// Vanilla, Modded, and one per mod whose characters carry its ID in their name), two rows of face cards with
    /// arrows to page through them, and a search box. Sub-characters (see <see cref="NpcGroups"/>) are folded into the
    /// character they belong to.
    /// </summary>
    internal sealed class NpcBrowser
    {
        private sealed class Filter
        {
            public string Label;
            public Func<NpcGroups.Entry, bool> Matches;
        }

        private const int CardWidth = 104;
        private const int FaceSize = 64;
        private const string DataKey = "tool-data";

        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;
        private readonly Func<List<NPC>> GetVillagers;
        private readonly Func<NPC> GetSelected;
        private readonly NpcSources Sources;

        private HashSet<string> Favourites;
        private HashSet<string> VanillaNames;
        private List<NpcGroups.Entry> Entries = new();
        private List<Filter> Filters = new();
        private string ActiveFilter = "All";
        private int BuiltAt = -100000;
        private readonly HashSet<NPC> HiddenCopies = new();
        private int Page;
        private int Pages = 1;
        private string Search = "";

        private Rectangle Bounds;
        private Rectangle SearchArea;
        private readonly List<(Rectangle Area, Action OnClick)> Buttons = new();
        private readonly List<(Rectangle Area, NpcGroups.Entry Entry)> Cards = new();
        private readonly List<(Rectangle Area, NpcGroups.Entry Entry)> Stars = new();

        public NpcBrowser(IModHelper helper, IMonitor monitor, Func<ModConfig> config, Func<List<NPC>> getVillagers, Func<NPC> getSelected)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Config = config;
            this.GetVillagers = getVillagers;
            this.GetSelected = getSelected;
            this.Sources = new NpcSources(helper, monitor);
        }

        /// <summary>Whether the drawer is shown (set by the workspace every frame).</summary>
        public bool Visible { get; set; }

        /// <summary>The area the drawer covered in the last draw (empty if it isn't shown).</summary>
        public Rectangle Area => this.Visible ? this.Bounds : Rectangle.Empty;

        /// <summary>Whether the search box has the keyboard.</summary>
        public bool Typing { get; private set; }

        public void StopTyping()
        {
            this.Typing = false;
        }

        /// <summary>The sub-characters of an NPC (or of the character she is a sub-character of), herself included; empty if she has none.</summary>
        public List<NpcGroups.Entry> GetFamily(NPC npc)
        {
            this.Rebuild(force: false);
            foreach (NpcGroups.Entry entry in this.Entries)
            {
                if (entry.Subs.Count == 0)
                    continue;
                if (ReferenceEquals(entry.Npc, npc) || entry.Subs.Any(sub => ReferenceEquals(sub.Npc, npc)))
                    return new List<NpcGroups.Entry> { entry }.Concat(entry.Subs).ToList();
            }
            return new List<NpcGroups.Entry>();
        }

        /*********
        ** Data
        *********/
        private void Rebuild(bool force)
        {
            if (!force && Game1.ticks - this.BuiltAt <= 300 && Game1.ticks >= this.BuiltAt)
                return;
            this.BuiltAt = Game1.ticks;

            this.Entries = NpcGroups.Build(this.GetVillagers(), this.Config().SubCharacterSuffixes);
            this.Relabel();
            this.HiddenCopies.Clear();
            foreach (NpcGroups.Entry entry in this.Entries)
            {
                if (entry.IsHiddenCopy)
                    this.HiddenCopies.Add(entry.Npc);
                foreach (NpcGroups.Entry sub in entry.Subs)
                {
                    if (sub.IsHiddenCopy)
                        this.HiddenCopies.Add(sub.Npc);
                }
            }
            HashSet<string> vanilla = this.GetVanillaNames();
            HashSet<string> favourites = this.GetFavourites();

            var filters = new List<Filter>
            {
                new() { Label = "All", Matches = _ => true },
                new() { Label = "Favourites", Matches = entry => favourites.Contains(entry.Npc.Name) || entry.Subs.Any(sub => favourites.Contains(sub.Npc.Name)) },
                new() { Label = "In this map", Matches = entry => entry.Npc.currentLocation == Game1.currentLocation || entry.Subs.Any(sub => sub.Npc.currentLocation == Game1.currentLocation) },
                new() { Label = "Vanilla", Matches = entry => vanilla.Contains(entry.Npc.Name) },
                new() { Label = "Modded", Matches = entry => !vanilla.Contains(entry.Npc.Name) }
            };

            // one filter per mod whose characters carry its ID in their name (Author.Mod_Name)
            foreach (var group in this.Entries.Select(entry => NpcGroups.GetModPrefix(entry.Npc.Name)).Where(prefix => prefix != null).GroupBy(prefix => prefix, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                string prefix = group.Key;
                string name = this.Helper.ModRegistry.Get(prefix)?.Manifest.Name ?? prefix.Substring(prefix.IndexOf('.') + 1);
                if (name.Length > 18)
                    name = name.Substring(0, 17) + ".";
                filters.Add(new Filter { Label = UiText.Clean(name), Matches = entry => string.Equals(NpcGroups.GetModPrefix(entry.Npc.Name), prefix, StringComparison.OrdinalIgnoreCase) });
            }

            this.Filters = filters;
            if (!filters.Any(filter => filter.Label == this.ActiveFilter))
                this.ActiveFilter = "All";
        }

        /// <summary>
        /// Name the hidden copies after the mod that adds them: "Abigail (Xtardew Valley 1)". Until the mod is known
        /// (the lookup runs in the background), or if no content pack names her, it says "alt".
        /// </summary>
        private void Relabel()
        {
            var hidden = new List<string>();
            foreach (NpcGroups.Entry root in this.Entries)
            {
                List<NpcGroups.Entry> copies = (root.IsHiddenCopy ? new[] { root } : Array.Empty<NpcGroups.Entry>()).Concat(root.Subs.Where(sub => sub.IsHiddenCopy)).ToList();
                if (copies.Count == 0)
                    continue;

                string baseName = UiText.Clean(root.IsHiddenCopy ? NpcGroups.GetPlain(root.Npc.Name) : root.Npc.Name);
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (NpcGroups.Entry copy in copies)
                {
                    hidden.Add(copy.Npc.Name);
                    string mod = UiText.Clean(this.Sources.GetModName(copy.Npc.Name) ?? "alt");
                    counts[mod] = counts.TryGetValue(mod, out int count) ? count + 1 : 1;
                    copy.Label = $"{baseName} ({mod} {counts[mod]})";
                }
            }
            this.Sources.Find(hidden);
        }

        private HashSet<string> GetFavourites()
        {
            if (this.Favourites == null)
            {
                try
                {
                    this.Favourites = new HashSet<string>(this.Helper.Data.ReadGlobalData<ToolData>(DataKey)?.Favourites ?? new List<string>(), StringComparer.Ordinal);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't read the favourites: {ex.Message}", LogLevel.Trace);
                    this.Favourites = new HashSet<string>(StringComparer.Ordinal);
                }
            }
            return this.Favourites;
        }

        /// <summary>Whether an NPC is a hidden copy of another character (see <see cref="NpcGroups.Entry.IsHiddenCopy"/>).</summary>
        public bool IsHiddenCopy(NPC npc)
        {
            this.Rebuild(force: false);
            return this.HiddenCopies.Contains(npc);
        }

        /// <summary>The favourite characters that are in this save, by name.</summary>
        public List<NPC> GetFavouriteNpcs()
        {
            HashSet<string> favourites = this.GetFavourites();
            return favourites.Count == 0 ? new List<NPC>() : this.GetVillagers().Where(npc => favourites.Contains(npc.Name)).ToList();
        }

        public bool IsFavourite(NPC npc)
        {
            return npc != null && this.GetFavourites().Contains(npc.Name);
        }

        /// <summary>What the browser knows, for the log: every character, how her name is spelled, and her sub-characters.</summary>
        public IEnumerable<string> Describe()
        {
            this.Rebuild(force: true);
            foreach (NpcGroups.Entry entry in this.Entries)
            {
                yield return $"{NpcGroups.Spell(entry.Npc.Name)}  ({entry.Npc.currentLocation?.NameOrUniqueName ?? "nowhere"}{(entry.Npc.CanSocialize ? "" : ", not social")}{(entry.IsHiddenCopy ? ", hidden copy" : "")}, sprite {entry.Npc.Sprite?.SpriteWidth}x{entry.Npc.Sprite?.SpriteHeight})";
                foreach (NpcGroups.Entry sub in entry.Subs)
                    yield return $"    + {NpcGroups.Spell(sub.Npc.Name)}  shown as \"{sub.Label}\"  ({sub.Npc.currentLocation?.NameOrUniqueName ?? "nowhere"})";
            }
        }

        public void ToggleFavourite(NPC npc)
        {
            HashSet<string> favourites = this.GetFavourites();
            if (!favourites.Remove(npc.Name))
                favourites.Add(npc.Name);

            try
            {
                // SMAPI's global data folder is outside the Mods folder, so this is safe under Mod Organizer 2
                this.Helper.Data.WriteGlobalData(DataKey, new ToolData { Favourites = favourites.OrderBy(name => name).ToList() });
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't save the favourites: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>
        /// The characters of the unmodded game: the keys of the game's own Data/Characters file, read straight from the
        /// Content folder (past SMAPI, so no mod's edits are in it). Falls back to a built-in list.
        /// </summary>
        private HashSet<string> GetVanillaNames()
        {
            if (this.VanillaNames != null)
                return this.VanillaNames;

            try
            {
                var raw = new Microsoft.Xna.Framework.Content.ContentManager(Game1.content.ServiceProvider, Game1.content.RootDirectory);
                var data = raw.Load<Dictionary<string, CharacterData>>(Path.Combine("Data", "Characters"));
                this.VanillaNames = new HashSet<string>(data.Keys, StringComparer.Ordinal);
                raw.Unload();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read the unmodded character list ({ex.Message}); using the built-in list.", LogLevel.Trace);
                this.VanillaNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Abigail", "Alex", "Birdie", "Bouncer", "Caroline", "Clint", "Demetrius", "Dwarf", "Elliott", "Emily", "Evelyn", "Fizz",
                    "George", "Gil", "Governor", "Grandpa", "Gunther", "Gus", "Haley", "Harvey", "Henchman", "Jas", "Jodi", "Kent", "Krobus",
                    "Leah", "Leo", "Lewis", "Linus", "Marcello", "Marlon", "Marnie", "Maru", "Mister Qi", "Morris", "Pam", "Penny", "Pierre",
                    "Robin", "Sam", "Sandy", "Sebastian", "Shane", "Vincent", "Willy", "Wizard"
                };
            }
            return this.VanillaNames;
        }

        /*********
        ** Input
        *********/
        public bool Contains(Point uiPixel)
        {
            return this.Visible && this.Bounds.Contains(uiPixel);
        }

        /// <summary>Handle a left click. Returns the character whose card was clicked (to select her), or null; <paramref name="used"/> says whether the click was on the drawer at all.</summary>
        public NPC OnLeftClick(Point uiPixel, out bool used)
        {
            used = this.Contains(uiPixel);
            this.Typing = used && this.SearchArea.Contains(uiPixel);
            if (!used)
                return null;
            if (this.Typing)
            {
                Game1.playSound("smallSelect");
                return null;
            }

            foreach ((Rectangle area, Action onClick) in this.Buttons.ToList())
            {
                if (area.Contains(uiPixel))
                {
                    Game1.playSound("smallSelect");
                    onClick();
                    return null;
                }
            }
            foreach ((Rectangle area, NpcGroups.Entry entry) in this.Stars)
            {
                if (area.Contains(uiPixel))
                {
                    Game1.playSound("smallSelect");
                    this.ToggleFavourite(entry.Npc);
                    return null;
                }
            }
            foreach ((Rectangle area, NpcGroups.Entry entry) in this.Cards)
            {
                if (area.Contains(uiPixel))
                {
                    Game1.playSound("smallSelect");
                    return entry.Npc;
                }
            }
            return null;
        }

        /// <summary>The character whose card is under a UI pixel, or null.</summary>
        public NPC GetNpcAt(Point uiPixel)
        {
            if (!this.Contains(uiPixel))
                return null;
            foreach ((Rectangle area, NpcGroups.Entry entry) in this.Cards)
            {
                if (area.Contains(uiPixel))
                    return entry.Npc;
            }
            return null;
        }

        public void OnScroll(int delta)
        {
            this.Page -= Math.Sign(delta);
        }

        /// <summary>
        /// A key was pressed while the search box has the keyboard. The game clears its own text input target whenever
        /// no menu is open (Game1.Update), so the keys are read here instead. Always uses the key.
        /// </summary>
        public void OnKey(SButton button, bool shift)
        {
            switch (button)
            {
                case SButton.Escape:
                case SButton.Enter:
                case SButton.Tab:
                    this.Typing = false;
                    return;
                case SButton.Back:
                    if (this.Search.Length > 0)
                        this.Search = this.Search.Substring(0, this.Search.Length - 1);
                    this.Page = 0;
                    return;
                case SButton.Delete:
                    this.Search = "";
                    this.Page = 0;
                    return;
            }

            char? typed = button switch
            {
                >= SButton.A and <= SButton.Z => (char)((shift ? 'A' : 'a') + (button - SButton.A)),
                >= SButton.D0 and <= SButton.D9 => (char)('0' + (button - SButton.D0)),
                >= SButton.NumPad0 and <= SButton.NumPad9 => (char)('0' + (button - SButton.NumPad0)),
                SButton.Space => ' ',
                SButton.OemPeriod => '.',
                SButton.OemMinus => shift ? '_' : '-',
                _ => null
            };
            if (typed != null && this.Search.Length < 24)
            {
                this.Search += typed.Value;
                this.Page = 0;
            }
        }

        private bool MatchesSearch(NpcGroups.Entry entry)
        {
            if (this.Search.Length == 0)
                return true;
            string search = this.Search.Trim();
            bool Has(NPC npc) => npc.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || (npc.displayName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase);
            return Has(entry.Npc) || entry.Label.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Subs.Any(sub => Has(sub.Npc));
        }

        /*********
        ** Drawing
        *********/
        /// <param name="left">Where the drawer starts (right of the hub's mode buttons).</param>
        /// <param name="bottom">Where the drawer ends (the bottom of the hub).</param>
        public void Draw(SpriteBatch b, int left, int bottom)
        {
            this.Buttons.Clear();
            this.Cards.Clear();
            this.Stars.Clear();
            if (!this.Visible)
            {
                this.Typing = false;
                return;
            }

            this.Rebuild(force: false);
            Filter active = this.Filters.FirstOrDefault(filter => filter.Label == this.ActiveFilter) ?? this.Filters[0];
            List<NpcGroups.Entry> shown = this.Entries.Where(entry => active.Matches(entry) && this.MatchesSearch(entry)).ToList();

            // sizes: a header line, then two rows of cards with the filters to their left
            const int rows = 2;
            int cardHeight = FaceSize + Ui.LineHeight + 12;
            int bodyHeight = rows * cardHeight;
            int perColumn = Math.Max(1, (bodyHeight + 4) / (Ui.ButtonHeight + 4));
            int filterColumns = Math.Clamp((this.Filters.Count + perColumn - 1) / perColumn, 1, 3);
            int filterCount = Math.Min(this.Filters.Count, filterColumns * perColumn);
            int filterWidth = this.Filters.Take(filterCount).Max(filter => Ui.MeasureButton(filter.Label));
            int filtersWidth = filterColumns * (filterWidth + 4) + 8;
            int arrowWidth = Ui.MeasureButton("<");

            int maxWidth = Math.Min(Game1.uiViewport.Width - left - 8, Math.Max(640, (int)(Game1.uiViewport.Width * 0.62f)));
            int perRow = Math.Clamp((maxWidth - Ui.BoxPadding * 2 - filtersWidth - (arrowWidth + 6) * 2) / CardWidth, 2, 10);
            int perPage = perRow * rows;
            this.Pages = Math.Max(1, (shown.Count + perPage - 1) / perPage);
            this.Page = Math.Clamp(this.Page, 0, this.Pages - 1);

            int width = Ui.BoxPadding * 2 + filtersWidth + (arrowWidth + 6) * 2 + perRow * CardWidth;
            int height = Ui.BoxPadding * 2 + Ui.ButtonHeight + 8 + bodyHeight;
            this.Bounds = new Rectangle(left, bottom - height, width, height);
            Ui.DrawBox(b, this.Bounds);

            int x = this.Bounds.X + Ui.BoxPadding;
            int y = this.Bounds.Y + Ui.BoxPadding;

            // header: what is shown, and the search box on the right
            Ui.DrawText(b, UiText.Clean($"NPCs | {active.Label} ({shown.Count}){(this.Pages > 1 ? $" | page {this.Page + 1}/{this.Pages}" : "")}"), x, y + 5, Ui.Title, bold: true);
            const int searchWidth = 230;
            this.SearchArea = new Rectangle(this.Bounds.Right - Ui.BoxPadding - searchWidth, y, searchWidth, Ui.ButtonHeight);
            Ui.DrawField(b, this.SearchArea, this.Search, "Search...", this.Typing);
            if (this.Search.Length > 0)
            {
                int clearWidth = Ui.MeasureButton("x");
                this.Buttons.Add((Ui.DrawButton(b, "x", this.SearchArea.X - clearWidth - 4, y), () =>
                {
                    this.Search = "";
                    this.Page = 0;
                }));
            }
            y += Ui.ButtonHeight + 8;

            // filters: stacked on the left, in up to three columns
            for (int i = 0; i < filterCount; i++)
            {
                Filter filter = this.Filters[i];
                var area = Ui.DrawButton(b, filter.Label, x + i / perColumn * (filterWidth + 4), y + i % perColumn * (Ui.ButtonHeight + 4), filter.Label == this.ActiveFilter, filterWidth);
                this.Buttons.Add((area, () =>
                {
                    this.ActiveFilter = filter.Label;
                    this.Page = 0;
                }));
            }

            // arrows left and right of the cards
            int gridX = x + filtersWidth + arrowWidth + 6;
            int arrowY = y + (bodyHeight - Ui.ButtonHeight) / 2;
            if (this.Pages > 1)
            {
                this.Buttons.Add((Ui.DrawButton(b, "<", gridX - arrowWidth - 6, arrowY), () => this.Page = (this.Page + this.Pages - 1) % this.Pages));
                this.Buttons.Add((Ui.DrawButton(b, ">", gridX + perRow * CardWidth + 2, arrowY), () => this.Page = (this.Page + 1) % this.Pages));
            }

            // cards
            Point mouse = Ui.Mouse;
            NPC selected = this.GetSelected();
            HashSet<string> favourites = this.GetFavourites();
            NpcGroups.Entry hovered = null;
            for (int slot = 0; slot < perPage; slot++)
            {
                int i = this.Page * perPage + slot;
                if (i >= shown.Count)
                    break;

                NpcGroups.Entry entry = shown[i];
                var area = new Rectangle(gridX + slot % perRow * CardWidth, y + slot / perRow * cardHeight, CardWidth - 4, cardHeight - 4);
                this.Cards.Add((area, entry));

                bool isSelected = selected != null && (ReferenceEquals(entry.Npc, selected) || entry.Subs.Any(sub => ReferenceEquals(sub.Npc, selected)));
                if (isSelected)
                    b.Draw(Game1.staminaRect, area, new Color(120, 190, 110) * 0.6f);
                else if (area.Contains(mouse))
                    b.Draw(Game1.staminaRect, area, Color.Black * 0.1f);
                if (area.Contains(mouse))
                    hovered = entry;

                Faces.Draw(b, entry.Npc, new Rectangle(area.X + (area.Width - FaceSize) / 2, area.Y + 4, FaceSize, FaceSize));

                // a gold star (top right) on a favourite; click it to unfavourite. How many sub-characters: top left.
                if (favourites.Contains(entry.Npc.Name))
                {
                    var star = new Rectangle(area.Right - 26, area.Y + 2, 24, 24);
                    this.Stars.Add((star, entry));
                    Ui.DrawStar(b, star.X, star.Y);
                }
                if (entry.Subs.Count > 0)
                    Ui.DrawText(b, $"+{entry.Subs.Count}", area.X + 2, area.Y, Ui.Dim, 0.75f);

                // the name, cut to the card
                string name = entry.Label;
                while (name.Length > 3 && Ui.Measure(name) > area.Width - 4)
                    name = name.Substring(0, name.Length - 1);
                Ui.DrawText(b, name, area.X + (int)((area.Width - Ui.Measure(name)) / 2), area.Y + FaceSize + 6, Ui.TextColor);
            }

            if (shown.Count == 0)
                Ui.DrawText(b, this.Search.Length > 0 ? "Nobody matches the search." : active.Label == "Favourites" ? "No favourites yet: right-click a character and choose Favourite." : "Nobody here.", gridX, y + 4, Ui.Dim);

            if (hovered != null)
                Ui.DrawTooltip(b, UiText.Clean($"{hovered.Npc.displayName} - {hovered.Npc.currentLocation?.NameOrUniqueName}{(hovered.Subs.Count > 0 ? $" (+{hovered.Subs.Count}: right-click)" : "")}"));
        }
    }
}
