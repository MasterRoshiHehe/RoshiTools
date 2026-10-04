using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The workspace's button groups, each anchored to a corner of the screen (the hub bottom left, the time controls
    /// top right). Drawn in UI space with the game's interface art.
    /// </summary>
    internal sealed class Toolbar
    {
        public enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft
        }

        public sealed class Button
        {
            public string Label;
            public string Tooltip;
            public Action OnClick;
            public bool Active;
            /// <summary>Start a new row with this button.</summary>
            public bool NewRow;
            /// <summary>The least width of the button (to line up a column of them), or 0.</summary>
            public int MinWidth;
            internal Rectangle Bounds;
        }

        public sealed class Group
        {
            public Corner Corner;
            /// <summary>A name to find the group's area by afterwards.</summary>
            public string Id;
            /// <summary>A line of text above the buttons, or null.</summary>
            public string Title;
            public List<Button> Buttons = new();
            /// <summary>The widest the group may get, in UI pixels.</summary>
            public int MaxWidth = 600;
            /// <summary>Extra space to leave at the screen edge the group is anchored to (e.g. for the game's clock).</summary>
            public int EdgeInset;
            /// <summary>Where the group's top goes (UI pixels), or -1 for the screen edge of its corner.</summary>
            public int Top = -1;
            /// <summary>Where the group's left edge goes (UI pixels), or -1 for the screen edge of its corner.</summary>
            public int Left = -1;
            internal Rectangle Bounds;
        }

        private const int Gap = 4;
        private const int Margin = 8;
        private List<Group> Groups = new();

        /// <summary>The area a group covered in the last draw (empty if it wasn't drawn).</summary>
        public Rectangle GetBounds(string id)
        {
            foreach (Group group in this.Groups)
            {
                if (group.Id == id)
                    return group.Bounds;
            }
            return Rectangle.Empty;
        }

        public void Draw(SpriteBatch b, List<Group> groups)
        {
            this.Groups = groups;
            Point mouse = Ui.Mouse;
            Button hovered = null;

            foreach (Group group in groups)
            {
                // rows
                int inner = Math.Max(120, group.MaxWidth - Ui.BoxPadding * 2);
                var rows = new List<List<Button>> { new() };
                var rowWidths = new List<int> { 0 };
                foreach (Button button in group.Buttons)
                {
                    int width = Math.Max(button.MinWidth, Ui.MeasureButton(button.Label));
                    int current = rowWidths[^1];
                    if (current > 0 && (button.NewRow || current + Gap + width > inner))
                    {
                        rows.Add(new List<Button>());
                        rowWidths.Add(0);
                        current = 0;
                    }
                    button.Bounds = new Rectangle(0, 0, width, Ui.ButtonHeight);
                    rows[^1].Add(button);
                    rowWidths[^1] = current + (current > 0 ? Gap : 0) + width;
                }

                int contentWidth = 0;
                foreach (int width in rowWidths)
                    contentWidth = Math.Max(contentWidth, width);
                if (group.Title != null)
                    contentWidth = Math.Max(contentWidth, (int)Ui.Measure(group.Title, bold: true));

                int titleHeight = group.Title != null ? Ui.LineHeight + 4 : 0;
                int boxWidth = contentWidth + Ui.BoxPadding * 2;
                int boxHeight = titleHeight + rows.Count * (Ui.ButtonHeight + Gap) - Gap + Ui.BoxPadding * 2;

                int x = group.Corner == Corner.TopRight ? Game1.uiViewport.Width - Margin - group.EdgeInset - boxWidth : Margin;
                int y = group.Corner == Corner.BottomLeft ? Game1.uiViewport.Height - Margin - group.EdgeInset - boxHeight : Margin;
                if (group.Top >= 0)
                    y = group.Top;
                if (group.Left >= 0)
                    x = group.Left;
                group.Bounds = new Rectangle(x, y, boxWidth, boxHeight);
                Ui.DrawBox(b, group.Bounds);

                int rowY = y + Ui.BoxPadding;
                if (group.Title != null)
                {
                    Ui.DrawText(b, group.Title, x + Ui.BoxPadding, rowY, Ui.Title, bold: true);
                    rowY += titleHeight;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    // the tools group reads from the right edge inwards
                    int rowX = group.Corner == Corner.TopRight ? x + boxWidth - Ui.BoxPadding - rowWidths[i] : x + Ui.BoxPadding;
                    foreach (Button button in rows[i])
                    {
                        button.Bounds = Ui.DrawButton(b, button.Label, rowX, rowY, button.Active, button.Bounds.Width);
                        if (button.Bounds.Contains(mouse))
                            hovered = button;
                        rowX += button.Bounds.Width + Gap;
                    }
                    rowY += Ui.ButtonHeight + Gap;
                }
            }

            Ui.DrawTooltip(b, hovered?.Tooltip);
        }

        /// <summary>The button under a UI pixel, if any.</summary>
        public Button GetButtonAt(Point uiPixel)
        {
            foreach (Group group in this.Groups)
            {
                foreach (Button button in group.Buttons)
                {
                    if (button.Bounds.Contains(uiPixel))
                        return button;
                }
            }
            return null;
        }

        /// <summary>Whether a UI pixel is on one of the groups (so a click there isn't for the world).</summary>
        public bool Contains(Point uiPixel)
        {
            foreach (Group group in this.Groups)
            {
                if (group.Bounds.Contains(uiPixel))
                    return true;
            }
            return false;
        }
    }
}
