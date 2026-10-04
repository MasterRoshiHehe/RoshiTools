using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The window for one character: shortcuts (go to her, bring her here, go to her home, inspect, follow) and a
    /// browser of her outfits with a portrait and sprite preview. Outfit groups come from APF's Explain tree when APF
    /// knows her (roots with their rolled variants, then the other variants by name), plus the game's Appearance
    /// entries. Drawn in UI space on the right of the screen; not a menu, so the game keeps running behind it.
    /// </summary>
    internal sealed class CharacterWindow
    {
        /// <summary>One thing that can be previewed: a portrait sheet and a sprite sheet.</summary>
        private sealed class Outfit
        {
            public string Label;
            public string PortraitAsset;
            public string SpriteAsset;
            /// <summary>winner (today's roll), pass, fail or info.</summary>
            public string Status = "info";
        }

        private sealed class Group
        {
            public string Label;
            public List<Outfit> Outfits = new();
        }

        private sealed class Row
        {
            public string Text;
            public int Indent;
            public Color Color;
            public Group Group;
            public Outfit Outfit;
        }

        /// <summary>What the window can do in the world; supplied by the workspace.</summary>
        public sealed class Actions
        {
            public Action<NPC> GoTo;
            public Action<NPC> BringHere;
            public Action<NPC> GoHome;
            public Action<NPC> Inspect;
            public Action<NPC> Follow;
            public Action<NPC, string, string> Wear;
            public Action<NPC> StopWearing;
            public Func<NPC, bool> IsWearing;
        }

        private const float Scale = Ui.Scale;
        private const int Padding = Ui.BoxPadding;
        private const int PortraitBox = 256;

        private readonly IMonitor Monitor;
        private readonly Func<NPC, ExplainNode> GetApfTree;
        private readonly Actions Do;

        private NPC Npc;
        private List<Group> Groups = new();
        private readonly HashSet<string> OpenGroups = new(StringComparer.OrdinalIgnoreCase);
        private Outfit Selected;
        private int Expression;
        private int Facing;
        private int Scroll;
        /// <summary>APF's roll today per root (root → winning variant), from its Explain tree.</summary>
        private readonly Dictionary<string, string> Rolls = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Show a rolled root with today's roll (as the game does) instead of the root's own portrait sheet.</summary>
        private bool ShowRolls = true;
        private Outfit Worn;
        private readonly Dictionary<string, Texture2D> Textures = new(StringComparer.OrdinalIgnoreCase);

        private readonly WindowDrag Drag = new();
        private Rectangle TitleArea;
        private Rectangle Bounds;
        private readonly List<(Rectangle Area, Action OnClick)> Buttons = new();
        private readonly List<(Rectangle Area, Row Row)> RowAreas = new();
        private Rectangle ListBounds;

        public CharacterWindow(IMonitor monitor, Func<NPC, ExplainNode> getApfTree, Actions actions)
        {
            this.Monitor = monitor;
            this.GetApfTree = getApfTree;
            this.Do = actions;
        }

        public bool IsOpen => this.Npc != null;

        /// <summary>The character the window shows, or null.</summary>
        public NPC SelectedNpc => this.Npc;

        /// <summary>Open the window for an NPC, or close it if it already shows her.</summary>
        public void Toggle(NPC npc)
        {
            if (npc != null && ReferenceEquals(npc, this.Npc))
                this.Close();
            else
                this.Open(npc);
        }

        public void Update(bool leftDown, Point mouse)
        {
            this.Drag.Update(leftDown, mouse);
        }

        public void Open(NPC npc)
        {
            if (npc == null)
                return;

            this.Npc = npc;
            this.Worn = null;
            this.Scroll = 0;
            this.Expression = 0;
            this.Facing = 0;
            this.Textures.Clear();
            this.OpenGroups.Clear();
            this.Rebuild();

            // start on what she wears now, else on the base sheets
            Outfit current = this.Groups.SelectMany(group => group.Outfits).FirstOrDefault(outfit => outfit.Status == "winner" && SameAsset(outfit.SpriteAsset, npc.Sprite?.Texture?.Name))
                ?? this.Groups.SelectMany(group => group.Outfits).FirstOrDefault();
            this.Selected = current;
            Group home = this.Groups.FirstOrDefault(group => group.Outfits.Contains(current));
            if (home != null)
                this.OpenGroups.Add(home.Label);
        }

        public void Close()
        {
            this.Npc = null;
            this.Groups = new List<Group>();
            this.Selected = null;
            this.Worn = null;
            this.Textures.Clear();
        }

        /*********
        ** Data
        *********/
        /// <summary>Read her outfit groups again (APF's rolls can change during the day).</summary>
        private void Rebuild()
        {
            NPC npc = this.Npc;
            string texture = npc.getTextureName();
            var groups = new List<Group>();

            var baseGroup = new Group { Label = "Base" };
            baseGroup.Outfits.Add(new Outfit { Label = $"{texture} (base)", PortraitAsset = $"Portraits/{texture}", SpriteAsset = $"Characters/{texture}" });
            groups.Add(baseGroup);

            try
            {
                this.AddApfGroups(groups, npc, texture);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"Couldn't read APF's variants for {npc.Name}: {ex.Message}", LogLevel.Trace);
            }

            // the game's own Appearance entries (what Content Patcher and LAF choose between)
            CharacterData data = npc.GetData();
            if (data?.Appearance != null && data.Appearance.Count > 0)
            {
                var group = new Group { Label = $"Game appearances ({data.Appearance.Count})" };
                foreach (CharacterAppearanceData entry in data.Appearance)
                {
                    group.Outfits.Add(new Outfit
                    {
                        Label = UiText.Clean(entry.Id),
                        PortraitAsset = string.IsNullOrWhiteSpace(entry.Portrait) ? $"Portraits/{texture}" : entry.Portrait,
                        SpriteAsset = string.IsNullOrWhiteSpace(entry.Sprite) ? $"Characters/{texture}" : entry.Sprite,
                        Status = entry.Id == npc.LastAppearanceId ? "winner" : "info"
                    });
                }
                groups.Add(group);
            }

            this.Groups = groups;
        }

        /// <summary>
        /// Groups from APF's Explain tree: section "rolls" has one child per root (ID = root) whose children are the
        /// rolled variants (ID = variant, status winner/pass/fail); section "variants" lists every variant (ID = name).
        /// </summary>
        private void AddApfGroups(List<Group> groups, NPC npc, string texture)
        {
            ExplainNode tree = this.GetApfTree?.Invoke(npc);
            if (tree == null)
                return;

            Outfit Make(string variant, string status = "info") => new()
            {
                Label = UiText.Clean(variant),
                PortraitAsset = $"Portraits/{texture}_{variant}",
                SpriteAsset = $"Characters/{texture}_{variant}",
                Status = status
            };

            var grouped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ExplainNode rolls = tree.Children.FirstOrDefault(section => section.Id == "rolls");
            this.Rolls.Clear();
            if (rolls != null)
            {
                foreach (ExplainNode root in rolls.Children)
                {
                    string winner = root.Children.FirstOrDefault(variant => variant.Status == "winner")?.Id;
                    if (!string.IsNullOrEmpty(root.Id) && !string.IsNullOrEmpty(winner))
                        this.Rolls[root.Id] = winner;

                    var group = new Group { Label = $"{UiText.Clean(root.Id)} (rolled)" };
                    foreach (ExplainNode variant in root.Children)
                    {
                        group.Outfits.Add(Make(variant.Id, variant.Status));
                        grouped.Add(variant.Id);
                    }
                    if (group.Outfits.Count > 0)
                        groups.Add(group);
                }
            }

            ExplainNode variants = tree.Children.FirstOrDefault(section => section.Id == "variants");
            if (variants != null)
            {
                // the rest by the part of the name before the first underscore: Fall, Fall_Base, Fall_Rainy -> "Fall"
                var byPrefix = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in variants.Children.Select(node => node.Id).Where(id => !string.IsNullOrEmpty(id) && !grouped.Contains(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                {
                    int cut = name.IndexOf('_');
                    string prefix = cut > 0 ? name.Substring(0, cut) : name;
                    if (!byPrefix.TryGetValue(prefix, out Group group))
                    {
                        byPrefix[prefix] = group = new Group { Label = UiText.Clean(prefix) };
                        groups.Add(group);
                    }
                    group.Outfits.Add(Make(name));
                }
            }
        }

        private Texture2D GetTexture(string asset)
        {
            if (string.IsNullOrWhiteSpace(asset))
                return null;
            if (this.Textures.TryGetValue(asset, out Texture2D cached))
                return cached != null && !cached.IsDisposed ? cached : null;

            Texture2D texture = null;
            try
            {
                if (Game1.content.DoesAssetExist<Texture2D>(asset))
                    texture = Game1.content.Load<Texture2D>(asset);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce($"Couldn't load {asset} for the preview: {ex.Message}", LogLevel.Trace);
            }
            this.Textures[asset] = texture;
            return texture;
        }

        private static bool SameAsset(string a, string b)
        {
            return a != null && b != null && string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        /*********
        ** Input
        *********/
        public bool Contains(Point uiPixel)
        {
            return this.IsOpen && this.Bounds.Contains(uiPixel);
        }

        /// <summary>Handle a left click. Returns true if it was on the window.</summary>
        public bool OnLeftClick(Point uiPixel)
        {
            if (!this.Contains(uiPixel))
                return false;

            foreach ((Rectangle area, Action onClick) in this.Buttons.ToList())
            {
                if (area.Contains(uiPixel))
                {
                    Game1.playSound("smallSelect");
                    onClick();
                    return true;
                }
            }

            foreach ((Rectangle area, Row row) in this.RowAreas.ToList())
            {
                if (!area.Contains(uiPixel))
                    continue;

                if (row.Outfit != null)
                {
                    this.Selected = row.Outfit;
                    this.Expression = 0;
                    Game1.playSound("smallSelect");
                }
                else if (row.Group != null)
                {
                    if (!this.OpenGroups.Remove(row.Group.Label))
                        this.OpenGroups.Add(row.Group.Label);
                    Game1.playSound("shwip");
                }
                return true;
            }

            if (this.TitleArea.Contains(uiPixel))
                this.Drag.Begin(uiPixel);
            return true;
        }

        public void OnScroll(int delta)
        {
            this.Scroll -= Math.Sign(delta) * 3;
        }

        /*********
        ** Drawing
        *********/
        /// <param name="top">The first free UI pixel row below the top-right button group and the clock.</param>
        /// <param name="bottom">The last free UI pixel row above the character bar.</param>
        public void Draw(SpriteBatch b, int top, int bottom)
        {
            if (this.Npc == null)
                return;

            NPC npc = this.Npc;
            SpriteFont font = Game1.smallFont;
            int lineHeight = (int)(font.LineSpacing * Scale);
            int width = Math.Clamp((int)(Game1.uiViewport.Width * 0.42f), 700, 860);
            this.Bounds = this.Drag.Apply(new Rectangle(Game1.uiViewport.Width - width - 8, top, width, Math.Max(440, bottom - top)));
            this.TitleArea = new Rectangle(this.Bounds.X, this.Bounds.Y, this.Bounds.Width, Padding + Ui.ButtonHeight + 4);
            Ui.DrawBox(b, this.Bounds);

            this.Buttons.Clear();
            this.RowAreas.Clear();
            int x = this.Bounds.X + Padding;
            int y = this.Bounds.Y + Padding;

            // header
            string where = npc.currentLocation != null ? $"{npc.currentLocation.NameOrUniqueName} {npc.TilePoint.X} {npc.TilePoint.Y}" : "(no location)";
            this.DrawText(b, UiText.Clean($"{npc.Name} | {where}"), x, y, Ui.Title, bold: true);
            this.DrawButton(b, "Close", this.Bounds.Right - Padding - this.MeasureButton("Close"), y - 6, lineHeight, false, this.Close);
            y += Ui.ButtonHeight;

            // shortcuts
            string home = npc.DefaultMap ?? "home";
            var shortcuts = new (string Label, Action OnClick)[]
            {
                (UiText.Clean($"Warp to {npc.Name}"), () => this.Do.GoTo(npc)),
                ("Warp to me", () => this.Do.BringHere(npc)),
                (UiText.Clean($"Warp to {home}"), () => this.Do.GoHome(npc)),
                ("Inspect", () => this.Do.Inspect(npc)),
                ("Follow", () => this.Do.Follow(npc))
            };
            int rowX = x;
            foreach ((string label, Action onClick) in shortcuts)
            {
                int buttonWidth = this.MeasureButton(label);
                if (rowX > x && rowX + buttonWidth > this.Bounds.Right - Padding)
                {
                    rowX = x;
                    y += Ui.ButtonHeight + 6;
                }
                this.DrawButton(b, label, rowX, y, lineHeight, false, onClick);
                rowX += buttonWidth + 6;
            }
            y += Ui.ButtonHeight + 10;
            if (this.Npc == null)
                return; // closed by a button this frame

            // preview column (right) and outfit list (left)
            int previewX = this.Bounds.Right - Padding - PortraitBox;
            this.DrawPreview(b, previewX, y, lineHeight);

            this.ListBounds = new Rectangle(x, y, previewX - x - Padding, this.Bounds.Bottom - y - Padding);
            this.DrawList(b, font, lineHeight);
        }

        /// <summary>
        /// If an outfit's sheets belong to a root APF rolls today, the rolled variant; else null. APF paints the roll
        /// onto the root's sprite sheet (Characters/X_Root) and shows the rolled variant's portrait in dialogue, while
        /// the Portraits/X_Root asset itself stays the root's own sheet.
        /// </summary>
        private string GetRoll(Outfit outfit)
        {
            if (outfit?.SpriteAsset == null)
                return null;

            string prefix = $"Characters/{this.Npc.getTextureName()}_";
            string asset = outfit.SpriteAsset.Replace('\\', '/');
            if (!asset.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            string root = asset.Substring(prefix.Length);
            return this.Rolls.TryGetValue(root, out string winner) && !string.Equals(winner, root, StringComparison.OrdinalIgnoreCase) ? winner : null;
        }

        private void DrawPreview(SpriteBatch b, int x, int y, int lineHeight)
        {
            Outfit outfit = this.Selected;
            var box = new Rectangle(x, y, PortraitBox, PortraitBox);
            b.Draw(Game1.staminaRect, box, new Color(30, 30, 40));

            string roll = this.GetRoll(outfit);
            string portraitAsset = outfit?.PortraitAsset;
            if (roll != null && this.ShowRolls)
                portraitAsset = $"Portraits/{this.Npc.getTextureName()}_{roll}";
            Texture2D portrait = this.GetTexture(portraitAsset) ?? this.GetTexture(outfit?.PortraitAsset);
            int count = 0;
            if (portrait != null)
            {
                // portrait sheets are two columns of square cells, whatever their resolution
                int cell = Math.Max(1, portrait.Width / 2);
                count = Math.Max(1, portrait.Height / cell * 2);
                this.Expression = ((this.Expression % count) + count) % count;
                var source = new Rectangle(this.Expression % 2 * cell, this.Expression / 2 * cell, cell, cell);
                b.Draw(portrait, box, source, Color.White);
            }
            else
                this.DrawText(b, "no portrait sheet", x + 8, y + 8, Color.LightGray);
            y += PortraitBox + 6;

            // expression arrows
            int left = this.DrawButton(b, "<", x, y, lineHeight, false, () => this.Expression--);
            string label = count > 0 ? $"${this.Expression}  ({this.Expression + 1} of {count})" : "-";
            this.DrawText(b, label, x + left + 10, y + 7, Ui.TextColor);
            int rightWidth = this.MeasureButton(">");
            this.DrawButton(b, ">", x + PortraitBox - rightWidth, y, lineHeight, false, () => this.Expression++);
            y += Ui.ButtonHeight + 6;

            if (outfit != null)
            {
                foreach (string line in UiText.Wrap(UiText.Clean($"{outfit.Label}\n{outfit.PortraitAsset}\n{outfit.SpriteAsset}"), PortraitBox, text => Game1.smallFont.MeasureString(text).X * Scale))
                {
                    this.DrawText(b, line, x, y, Ui.Dim);
                    y += lineHeight;
                }

                if (roll != null)
                {
                    string note = this.ShowRolls
                        ? $"Today's roll for this root is {roll}: the sprite sheet carries it and the portrait shown is {roll}'s."
                        : $"Today's roll for this root is {roll}: the sprite sheet carries it; the portrait shown is the root's own sheet.";
                    foreach (string line in UiText.Wrap(UiText.Clean(note), PortraitBox, text => Game1.smallFont.MeasureString(text).X * Scale))
                    {
                        this.DrawText(b, line, x, y, Ui.StatusColor("winner"));
                        y += lineHeight;
                    }
                    this.DrawButton(b, this.ShowRolls ? "Portrait: today's roll" : "Portrait: root sheet", x, y + 2, lineHeight, this.ShowRolls, () => this.ShowRolls = !this.ShowRolls);
                    y += Ui.ButtonHeight + 4;
                }
            }
            y += 6;

            // overworld sprite, one facing at a time
            Texture2D sprite = this.GetTexture(outfit?.SpriteAsset);
            int spriteWidth = this.Npc.Sprite?.SpriteWidth ?? 16;
            int spriteHeight = this.Npc.Sprite?.SpriteHeight ?? 32;
            var spriteBox = new Rectangle(x, y, spriteWidth * 4 + 16, spriteHeight * 4 + 16);
            b.Draw(Game1.staminaRect, spriteBox, new Color(30, 30, 40));
            if (sprite != null && sprite.Width >= spriteWidth && sprite.Height >= spriteHeight * 4)
            {
                // rows of the walking frames: down, right, up, left
                var source = new Rectangle(0, this.Facing * spriteHeight, spriteWidth, spriteHeight);
                b.Draw(sprite, new Rectangle(spriteBox.X + 8, spriteBox.Y + 8, spriteWidth * 4, spriteHeight * 4), source, Color.White);
            }
            else if (sprite != null)
                b.Draw(sprite, new Rectangle(spriteBox.X + 8, spriteBox.Y + 8, spriteWidth * 4, spriteHeight * 4), new Rectangle(0, 0, Math.Min(sprite.Width, spriteWidth), Math.Min(sprite.Height, spriteHeight)), Color.White);
            else
                this.DrawText(b, "no sprite", spriteBox.X + 4, spriteBox.Y + 4, Color.LightGray);

            this.DrawButton(b, "Turn", spriteBox.Right + 10, spriteBox.Y, lineHeight, false, () => this.Facing = (this.Facing + 1) % 4);
            this.DrawButton(b, "Reload", spriteBox.Right + 10, spriteBox.Y + Ui.ButtonHeight + 6, lineHeight, false, () =>
            {
                this.Textures.Clear();
                this.Rebuild();
            });

            // wear it in the world
            bool wearing = this.Do.IsWearing(this.Npc);
            int wearY = spriteBox.Y + (Ui.ButtonHeight + 6) * 2;
            if (outfit != null)
            {
                bool wornNow = wearing && ReferenceEquals(this.Worn, outfit);
                this.DrawButton(b, wornNow ? "Wearing" : "Wear", spriteBox.Right + 10, wearY, lineHeight, wornNow, () =>
                {
                    this.Do.Wear(this.Npc, outfit.SpriteAsset, outfit.PortraitAsset);
                    this.Worn = this.Do.IsWearing(this.Npc) ? outfit : null;
                });
            }
            if (wearing)
            {
                this.DrawButton(b, "Take off", spriteBox.Right + 10, wearY + Ui.ButtonHeight + 6, lineHeight, false, () =>
                {
                    this.Do.StopWearing(this.Npc);
                    this.Worn = null;
                });
            }
        }

        private void DrawList(SpriteBatch b, SpriteFont font, int lineHeight)
        {
            var rows = new List<Row>();
            foreach (Group group in this.Groups)
            {
                bool open = this.OpenGroups.Contains(group.Label);
                rows.Add(new Row { Text = (open ? "- " : "+ ") + group.Label, Indent = 0, Color = Ui.TextColor, Group = group });
                if (!open)
                    continue;
                foreach (Outfit outfit in group.Outfits)
                {
                    string mark = ReferenceEquals(outfit, this.Worn) ? "[worn] " : outfit.Status switch { "winner" => "[now] ", "fail" => "[off] ", _ => "" };
                    rows.Add(new Row { Text = mark + outfit.Label, Indent = 1, Color = outfit.Status switch { "winner" => Ui.StatusColor("winner"), "fail" => Ui.Faint, _ => Ui.Dim }, Outfit = outfit });
                }
            }

            int visible = Math.Max(1, this.ListBounds.Height / lineHeight);
            this.Scroll = Math.Clamp(this.Scroll, 0, Math.Max(0, rows.Count - visible));
            Point mouse = new(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));

            for (int i = 0; i < visible && this.Scroll + i < rows.Count; i++)
            {
                Row row = rows[this.Scroll + i];
                var area = new Rectangle(this.ListBounds.X, this.ListBounds.Y + i * lineHeight, this.ListBounds.Width, lineHeight);
                this.RowAreas.Add((area, row));
                if (row.Outfit != null && ReferenceEquals(row.Outfit, this.Selected))
                    b.Draw(Game1.staminaRect, area, new Color(120, 190, 110) * 0.55f);
                else if (area.Contains(mouse))
                    b.Draw(Game1.staminaRect, area, Color.Black * 0.08f);

                // cut labels that don't fit the column
                string text = row.Text;
                float room = area.Width - row.Indent * 18 - 4;
                while (text.Length > 4 && font.MeasureString(text).X * Scale > room)
                    text = text.Substring(0, text.Length - 1);
                this.DrawText(b, text, area.X + row.Indent * 18, area.Y, row.Color);
            }

            if (rows.Count > visible)
            {
                int barHeight = Math.Max(24, this.ListBounds.Height * visible / rows.Count);
                int barY = this.ListBounds.Y + (this.ListBounds.Height - barHeight) * this.Scroll / Math.Max(1, rows.Count - visible);
                b.Draw(Game1.staminaRect, new Rectangle(this.ListBounds.Right - 4, barY, 4, barHeight), Ui.Dim * 0.6f);
            }
        }

        private void DrawText(SpriteBatch b, string text, int x, int y, Color color, bool bold = false)
        {
            Ui.DrawText(b, text, x, y, color, Scale, bold);
        }

        private int MeasureButton(string label)
        {
            return Ui.MeasureButton(label);
        }

        private int DrawButton(SpriteBatch b, string label, int x, int y, int lineHeight, bool active, Action onClick)
        {
            Rectangle area = Ui.DrawButton(b, label, x, y, active);
            this.Buttons.Add((area, onClick));
            return area.Width;
        }
    }
}
