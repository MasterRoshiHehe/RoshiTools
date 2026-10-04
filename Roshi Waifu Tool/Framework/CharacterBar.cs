using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The villagers in the current location as one column of faces on the left edge, above the hub. Click a face to
    /// select her, right-click for her options. If there are more than fit, the mouse wheel scrolls the column.
    /// </summary>
    internal sealed class CharacterBar
    {
        private const int Slot = 64;
        private const int Face = 56;
        private readonly List<(Rectangle Area, NPC Npc)> Slots = new();
        private int Scroll;

        /// <summary>The area the column covered in the last draw (empty if it wasn't drawn).</summary>
        public Rectangle Bounds { get; private set; }

        public bool Contains(Point uiPixel)
        {
            return this.Bounds.Contains(uiPixel);
        }

        public void OnScroll(int delta)
        {
            this.Scroll -= Math.Sign(delta);
        }

        /// <summary>The NPC whose slot is under a UI pixel, if any.</summary>
        public NPC GetNpcAt(Point uiPixel)
        {
            foreach ((Rectangle area, NPC npc) in this.Slots)
            {
                if (area.Contains(uiPixel))
                    return npc;
            }
            return null;
        }

        /// <param name="top">The highest the column may reach (UI pixels).</param>
        /// <param name="bottom">Where the column ends (the top of the hub).</param>
        /// <param name="selected">The NPC to highlight, if any.</param>
        /// <param name="isHidden">Which NPCs to leave out, or null.</param>
        public void Draw(SpriteBatch b, int top, int bottom, NPC selected, Func<NPC, string> describe, Func<NPC, bool> isHidden)
        {
            this.Slots.Clear();
            this.Bounds = Rectangle.Empty;
            if (Game1.currentLocation == null)
                return;

            List<NPC> npcs = Game1.currentLocation.characters
                .Where(npc => npc.IsVillager && !npc.IsInvisible && (isHidden == null || !isHidden(npc)))
                .OrderBy(npc => npc.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (npcs.Count == 0)
                return;

            int padding = Ui.BoxPadding - 4;
            int fits = Math.Max(1, (bottom - top - padding * 2) / Slot);
            int rows = Math.Min(fits, npcs.Count);
            this.Scroll = Math.Clamp(this.Scroll, 0, npcs.Count - rows);

            int width = Slot + padding * 2;
            int height = rows * Slot + padding * 2;
            this.Bounds = new Rectangle(8, bottom - height, width, height);
            Ui.DrawBox(b, this.Bounds);

            Point mouse = Ui.Mouse;
            NPC hovered = null;
            for (int row = 0; row < rows; row++)
            {
                NPC npc = npcs[this.Scroll + row];
                var area = new Rectangle(this.Bounds.X + padding, this.Bounds.Y + padding + row * Slot, Slot, Slot);
                this.Slots.Add((area, npc));

                bool hover = area.Contains(mouse);
                if (hover)
                    hovered = npc;
                if (ReferenceEquals(npc, selected))
                    b.Draw(Game1.staminaRect, area, new Color(120, 190, 110) * 0.6f);
                else if (hover)
                    b.Draw(Game1.staminaRect, area, Color.Black * 0.1f);

                Faces.Draw(b, npc, new Rectangle(area.X + (Slot - Face) / 2, area.Y + (Slot - Face) / 2, Face, Face));
            }

            // how many more there are above and below (scroll with the mouse wheel)
            int above = this.Scroll;
            int below = npcs.Count - rows - this.Scroll;
            if (above > 0)
                Ui.DrawLabel(b, $"+{above}", this.Bounds.Center.X, this.Bounds.Y - 6);
            if (below > 0)
                Ui.DrawLabel(b, $"+{below}", this.Bounds.Center.X, this.Bounds.Bottom - Ui.ButtonHeight + 6);

            if (hovered != null)
                Ui.DrawTooltip(b, UiText.Clean(describe?.Invoke(hovered) ?? hovered.Name));
        }
    }
}
