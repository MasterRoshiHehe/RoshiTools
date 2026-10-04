using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The in-game inspector: a panel on the left of the screen that shows, for one NPC, the Explain trees of the
    /// frameworks that have an editor API. It draws whatever nodes arrive, so it doesn't know what an outfit entry,
    /// detour or roll is. Drawn in UI space; not a menu, so the game keeps running behind it.
    /// </summary>
    internal sealed class InspectorPanel
    {
        private sealed class Row
        {
            public string Text;
            public int Indent;
            public Color Color;
            /// <summary>The node path this row toggles when clicked, or null.</summary>
            public string TogglePath;
        }

        private const float Scale = Ui.Scale;
        private const int Padding = Ui.BoxPadding;
        private const int IndentWidth = 18;
        private const int RefreshTicks = 30;

        private readonly Func<NPC, List<ExplainNode>> GetTrees;
        private readonly Action<NPC> DumpToConsole;

        private NPC Npc;

        /// <summary>The character the panel shows, or null.</summary>
        public NPC SelectedNpc => this.Npc;
        private List<ExplainNode> Trees = new();
        private readonly HashSet<string> Expanded = new();
        private List<Row> Rows = new();
        private bool HideFailed = true;
        private int Scroll;
        private int TicksSinceRefresh;
        private float RowsBuiltForWidth;

        private readonly WindowDrag Drag = new();
        private Rectangle TitleArea;
        private Rectangle Bounds;
        private Rectangle BodyBounds;
        private readonly List<(Rectangle Area, Action OnClick)> HeaderButtons = new();
        private readonly List<(Rectangle Area, string Path)> RowAreas = new();

        public InspectorPanel(Func<NPC, List<ExplainNode>> getTrees, Action<NPC> dumpToConsole)
        {
            this.GetTrees = getTrees;
            this.DumpToConsole = dumpToConsole;
        }

        public bool IsOpen => this.Npc != null;

        public void Open(NPC npc)
        {
            if (npc == null)
                return;

            if (!ReferenceEquals(npc, this.Npc))
            {
                this.Expanded.Clear();
                this.Scroll = 0;
                // open on the parts people look for first
                foreach (string path in new[] { "LAF/appearance", "LAF/detours", "APF/shown", "APF/rolls", "Basics/lines" })
                    this.Expanded.Add(path);
            }

            this.Npc = npc;
            this.Refresh();
        }

        public void Close()
        {
            this.Npc = null;
            this.Trees = new List<ExplainNode>();
            this.Rows = new List<Row>();
        }

        /// <summary>Call every tick: re-asks the frameworks now and then, so the panel follows the game.</summary>
        public void Update(bool leftDown, Point mouse)
        {
            this.Drag.Update(leftDown, mouse);
            if (this.Npc == null)
                return;
            if (++this.TicksSinceRefresh >= RefreshTicks)
                this.Refresh();
        }

        private void Refresh()
        {
            this.TicksSinceRefresh = 0;
            try
            {
                this.Trees = this.GetTrees(this.Npc) ?? new List<ExplainNode>();
            }
            catch (Exception ex)
            {
                this.Trees = new List<ExplainNode> { new() { Id = "error", Title = "Couldn't read the frameworks", Status = "warning", Text = ex.Message } };
            }
            this.RowsBuiltForWidth = 0; // rebuild on the next draw
        }

        /*********
        ** Input
        *********/
        /// <summary>Whether a UI pixel is on the panel.</summary>
        public bool Contains(Point uiPixel)
        {
            return this.IsOpen && this.Bounds.Contains(uiPixel);
        }

        /// <summary>Handle a left click. Returns true if it was on the panel.</summary>
        public bool OnLeftClick(Point uiPixel)
        {
            if (!this.Contains(uiPixel))
                return false;

            foreach ((Rectangle area, Action onClick) in this.HeaderButtons.ToList())
            {
                if (area.Contains(uiPixel))
                {
                    Game1.playSound("smallSelect");
                    onClick();
                    return true;
                }
            }

            foreach ((Rectangle area, string path) in this.RowAreas)
            {
                if (path != null && area.Contains(uiPixel))
                {
                    if (!this.Expanded.Remove(path))
                        this.Expanded.Add(path);
                    this.RowsBuiltForWidth = 0;
                    Game1.playSound("shwip");
                    return true;
                }
            }

            if (this.TitleArea.Contains(uiPixel))
                this.Drag.Begin(uiPixel);
            return true;
        }

        /// <summary>Scroll by a mouse wheel delta (positive = up).</summary>
        public void OnScroll(int delta)
        {
            this.Scroll -= Math.Sign(delta) * 3;
        }

        /*********
        ** Drawing
        *********/
        /// <param name="top">The first free UI pixel row below the top-left button group.</param>
        /// <param name="bottom">The last free UI pixel row above the bottom-left button group.</param>
        public void Draw(SpriteBatch b, int left, int top, int bottom)
        {
            if (this.Npc == null)
                return;

            SpriteFont font = Game1.smallFont;
            int lineHeight = (int)(font.LineSpacing * Scale);
            int width = Math.Clamp((int)(Game1.uiViewport.Width * 0.4f), 520, 820);
            this.Bounds = this.Drag.Apply(new Rectangle(left, top, width, Math.Max(220, bottom - top)));
            this.TitleArea = new Rectangle(this.Bounds.X, this.Bounds.Y, this.Bounds.Width, Padding + lineHeight + 6);
            Ui.DrawBox(b, this.Bounds);

            // header
            this.HeaderButtons.Clear();
            int x = this.Bounds.X + Padding;
            int y = this.Bounds.Y + Padding;
            string where = this.Npc.currentLocation != null ? $"{this.Npc.currentLocation.NameOrUniqueName} {this.Npc.TilePoint.X} {this.Npc.TilePoint.Y}" : "(no location)";
            this.DrawText(b, UiText.Clean($"{this.Npc.Name} | {TimeMachine.Format(Game1.timeOfDay)} | {where}"), x, y, Ui.Title, bold: true);
            y += lineHeight + 6;

            x += this.DrawButton(b, this.HideFailed ? "Failed: hidden" : "Failed: shown", x, y, lineHeight, this.HideFailed, () =>
            {
                this.HideFailed = !this.HideFailed;
                this.RowsBuiltForWidth = 0;
            }) + 6;
            x += this.DrawButton(b, "Collapse all", x, y, lineHeight, false, () =>
            {
                this.Expanded.Clear();
                this.Scroll = 0;
                this.RowsBuiltForWidth = 0;
            }) + 6;
            x += this.DrawButton(b, "Console", x, y, lineHeight, false, () => this.DumpToConsole(this.Npc)) + 6;
            this.DrawButton(b, "Close", x, y, lineHeight, false, this.Close);
            y += Ui.ButtonHeight + 6;

            // body
            this.BodyBounds = new Rectangle(this.Bounds.X + Padding, y, this.Bounds.Width - Padding * 2, this.Bounds.Bottom - y - Padding);
            if (this.Npc == null)
                return; // closed by a header button this frame

            if (this.RowsBuiltForWidth != this.BodyBounds.Width)
            {
                this.Rows = this.BuildRows(font, this.BodyBounds.Width);
                this.RowsBuiltForWidth = this.BodyBounds.Width;
            }

            int visible = Math.Max(1, this.BodyBounds.Height / lineHeight);
            this.Scroll = Math.Clamp(this.Scroll, 0, Math.Max(0, this.Rows.Count - visible));

            this.RowAreas.Clear();
            Point mouse = new(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));
            for (int i = 0; i < visible && this.Scroll + i < this.Rows.Count; i++)
            {
                Row row = this.Rows[this.Scroll + i];
                var area = new Rectangle(this.BodyBounds.X, this.BodyBounds.Y + i * lineHeight, this.BodyBounds.Width, lineHeight);
                this.RowAreas.Add((area, row.TogglePath));
                if (row.TogglePath != null && area.Contains(mouse))
                    b.Draw(Game1.staminaRect, area, Color.Black * 0.08f);
                this.DrawText(b, row.Text, area.X + row.Indent * IndentWidth, area.Y, row.Color);
            }

            // scroll bar
            if (this.Rows.Count > visible)
            {
                int trackHeight = this.BodyBounds.Height;
                int barHeight = Math.Max(24, trackHeight * visible / this.Rows.Count);
                int barY = this.BodyBounds.Y + (trackHeight - barHeight) * this.Scroll / Math.Max(1, this.Rows.Count - visible);
                b.Draw(Game1.staminaRect, new Rectangle(this.Bounds.Right - Padding + 4, barY, 4, barHeight), Ui.Dim * 0.6f);
            }
        }

        private void DrawText(SpriteBatch b, string text, int x, int y, Color color, bool bold = false)
        {
            Ui.DrawText(b, text, x, y, color, Scale, bold);
        }

        private int DrawButton(SpriteBatch b, string label, int x, int y, int lineHeight, bool active, Action onClick)
        {
            Rectangle area = Ui.DrawButton(b, label, x, y, active);
            this.HeaderButtons.Add((area, onClick));
            return area.Width;
        }

        /*********
        ** Rows
        *********/
        private List<Row> BuildRows(SpriteFont font, int width)
        {
            var rows = new List<Row>();
            float Measure(string text) => font.MeasureString(text).X * Scale;

            foreach (ExplainNode tree in this.Trees)
            {
                // a framework header: always open, its sections are the collapsible part
                rows.Add(new Row { Text = tree.Title, Indent = 0, Color = Ui.Title });
                if (!string.IsNullOrWhiteSpace(tree.Text))
                {
                    foreach (string line in UiText.Wrap(tree.Text, width - IndentWidth, Measure))
                        rows.Add(new Row { Text = line, Indent = 1, Color = Ui.Dim });
                }
                this.AddChildren(rows, tree, tree.Id, 1, width, Measure);
                rows.Add(new Row { Text = "", Indent = 0, Color = Ui.TextColor });
            }

            if (this.Trees.Count == 0)
                rows.Add(new Row { Text = "Nothing to show.", Indent = 0, Color = Ui.Dim });
            return rows;
        }

        private void AddChildren(List<Row> rows, ExplainNode parent, string parentPath, int indent, int width, Func<string, float> measure)
        {
            int hidden = 0;
            var seen = new Dictionary<string, int>();
            foreach (ExplainNode node in parent.Children)
            {
                // paths must be unique among siblings even if two share an ID
                string id = node.Id ?? node.Title ?? "";
                seen[id] = seen.TryGetValue(id, out int count) ? count + 1 : 0;
                string path = $"{parentPath}/{id}{(seen[id] > 0 ? "#" + seen[id] : "")}";

                bool expanded = this.Expanded.Contains(path);
                if (this.HideFailed && node.IsHiddenWhenFiltering && !expanded)
                {
                    hidden++;
                    continue;
                }

                bool canExpand = node.Children.Count > 0 || !string.IsNullOrWhiteSpace(node.Text);
                string marker = !canExpand ? "  " : expanded ? "- " : "+ ";
                float room = width - indent * IndentWidth;
                List<string> titleLines = UiText.Wrap(marker + GetStatusLabel(node.Status) + node.Title, room, measure);
                for (int i = 0; i < titleLines.Count; i++)
                    rows.Add(new Row { Text = i == 0 ? titleLines[i] : "  " + titleLines[i], Indent = indent, Color = Ui.StatusColor(node.Status), TogglePath = canExpand ? path : null });

                if (!expanded)
                    continue;

                if (!string.IsNullOrWhiteSpace(node.Text))
                {
                    foreach (string line in UiText.Wrap(node.Text, width - (indent + 1) * IndentWidth, measure))
                        rows.Add(new Row { Text = line, Indent = indent + 1, Color = Ui.Dim });
                }
                this.AddChildren(rows, node, path, indent + 1, width, measure);
            }

            if (hidden > 0)
                rows.Add(new Row { Text = $"  ({hidden} failed or skipped hidden)", Indent = indent, Color = Ui.Faint });
        }

        private static string GetStatusLabel(string status)
        {
            return status switch
            {
                "winner" => "[WIN] ",
                "pass" => "[ok] ",
                "fail" => "[no] ",
                "skipped" => "[skip] ",
                "warning" => "[!] ",
                _ => ""
            };
        }

    }
}
