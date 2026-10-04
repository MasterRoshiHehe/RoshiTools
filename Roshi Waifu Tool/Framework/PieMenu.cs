using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Options around the point that was right-clicked: a ring (with the character's face or the tile in the middle),
    /// or a vertical list. A left click on an option runs it; a click on the middle goes back (if there is a previous
    /// menu) and a click anywhere else closes it.
    /// </summary>
    internal sealed class PieMenu
    {
        private string Title;
        private List<KeyValuePair<string, Action>> Items = new();
        private Point Center;
        private NPC Face;
        private bool FarmerHead;
        private Action OnCenter;
        private bool Vertical;
        private readonly List<Rectangle> Areas = new();
        private Rectangle CenterArea;
        /// <summary>Where the head is looking, eased from frame to frame: tilt in radians and how far it leans (pixels).</summary>
        private const float LeanMax = 14f;
        private const float LeanOnOption = 28f;
        private float Tilt;
        private Vector2 Lean;

        public bool IsOpen => this.Items.Count > 0;

        /// <param name="uiCenter">Where to open it (UI pixels).</param>
        /// <param name="title">What was clicked, shown in the middle.</param>
        /// <param name="face">The character whose face goes in the middle, or null.</param>
        /// <param name="onCenter">What a click on the middle does (usually: reopen the previous menu), or null.</param>
        /// <param name="farmerHead">Show the farmer's head in the middle (for a tile's options), with the title on the plate below.</param>
        /// <param name="vertical">A list below the point instead of a ring.</param>
        public void Open(Point uiCenter, string title, List<KeyValuePair<string, Action>> items, NPC face = null, Action onCenter = null, bool vertical = false, bool farmerHead = false)
        {
            this.Title = UiText.Clean(title);
            this.Items = items ?? new List<KeyValuePair<string, Action>>();
            this.Center = uiCenter;
            this.Face = face;
            this.FarmerHead = farmerHead && face == null;
            this.OnCenter = onCenter;
            this.Vertical = vertical;
            this.Areas.Clear();
            this.CenterArea = Rectangle.Empty;
            this.Tilt = 0f;
            this.Lean = Vector2.Zero;
            if (this.Items.Count > 0)
                Game1.playSound("shwip");
        }

        public void Close()
        {
            this.Items = new List<KeyValuePair<string, Action>>();
            this.Areas.Clear();
            this.OnCenter = null;
            this.Face = null;
        }

        /// <summary>Handle a left click while the menu is open. Always uses the click.</summary>
        public bool OnLeftClick(Point uiPixel)
        {
            if (!this.IsOpen)
                return false;

            for (int i = 0; i < this.Areas.Count && i < this.Items.Count; i++)
            {
                if (this.Areas[i].Contains(uiPixel))
                {
                    Action action = this.Items[i].Value;
                    this.Close();
                    Game1.playSound("smallSelect");
                    action?.Invoke();
                    return true;
                }
            }

            Action back = this.CenterArea.Contains(uiPixel) ? this.OnCenter : null;
            this.Close();
            if (back != null)
            {
                Game1.playSound("shwip");
                back();
            }
            return true;
        }

        public void Draw(SpriteBatch b)
        {
            if (!this.IsOpen)
                return;

            if (this.Vertical)
                this.DrawList(b);
            else
                this.DrawRing(b);
        }

        private void DrawList(SpriteBatch b)
        {
            int width = (int)Ui.Measure(this.Title, bold: true);
            foreach (var item in this.Items)
                width = Math.Max(width, Ui.MeasureButton(item.Key));
            int height = Ui.LineHeight + 6 + this.Items.Count * (Ui.ButtonHeight + 4) - 4 + Ui.BoxPadding * 2;
            int boxWidth = width + Ui.BoxPadding * 2;

            int x = Math.Clamp(this.Center.X, 4, Math.Max(4, Game1.uiViewport.Width - boxWidth - 4));
            int y = Math.Clamp(this.Center.Y - height, 4, Math.Max(4, Game1.uiViewport.Height - height - 4));
            this.CenterArea = Rectangle.Empty;
            Ui.DrawBox(b, new Rectangle(x, y, boxWidth, height));
            Ui.DrawText(b, this.Title, x + Ui.BoxPadding, y + Ui.BoxPadding, Ui.Title, bold: true);

            this.Areas.Clear();
            int rowY = y + Ui.BoxPadding + Ui.LineHeight + 6;
            foreach (var item in this.Items)
            {
                this.Areas.Add(Ui.DrawButton(b, item.Key, x + Ui.BoxPadding, rowY, width: width));
                rowY += Ui.ButtonHeight + 4;
            }
        }

        private void DrawRing(SpriteBatch b)
        {
            // an ellipse wide enough that neighbouring labels don't overlap
            int count = this.Items.Count;
            int widest = 0;
            foreach (var item in this.Items)
                widest = Math.Max(widest, Ui.MeasureButton(item.Key));
            bool hasFace = this.Face != null || this.FarmerHead;
            int radiusX = Math.Max(hasFace ? 230 : 170, widest / 2 + (hasFace ? 120 : 90) + count * 6);
            int radiusY = Math.Max(hasFace ? 160 : 120, count * 20);

            // keep the whole ring on screen
            Point center = this.Center;
            center.X = Math.Clamp(center.X, radiusX + widest / 2 + 8, Math.Max(radiusX + widest / 2 + 8, Game1.uiViewport.Width - radiusX - widest / 2 - 8));
            center.Y = Math.Clamp(center.Y, radiusY + Ui.ButtonHeight + 8, Math.Max(radiusY + Ui.ButtonHeight + 8, Game1.uiViewport.Height - radiusY - Ui.ButtonHeight - 8));

            // the middle: only her head, big and without a frame (as in The Sims 2), her name on a plate below it
            const int headSize = 160;
            int titleWidth = (int)Ui.Measure(this.Title, bold: true);
            if (this.Face != null || this.FarmerHead)
            {
                bool hover = this.OnCenter != null && this.CenterArea.Contains(Ui.Mouse);
                // she looks at the option under the cursor (or at the cursor): the side view when it is to her left or
                // right, tilted up or down towards it; straight above or below she leans towards it instead
                var headCentre = new Point(center.X, center.Y - Ui.ButtonHeight / 2);
                Point target = Ui.Mouse;
                bool onOption = false;
                foreach (Rectangle option in this.Areas)
                {
                    if (option.Contains(target))
                    {
                        target = option.Center;
                        onOption = true;
                        break;
                    }
                }
                float dx = target.X - headCentre.X;
                float dy = target.Y - headCentre.Y;
                int side = hover || Math.Abs(dx) < 70 ? 0 : Math.Sign(dx);
                float tilt = side == 0 ? 0f : Math.Clamp((float)Math.Atan2(dy, Math.Abs(dx)) * 0.6f, -0.4f, 0.4f) * side;

                // she leans towards what she looks at: a little for the cursor, further for an option under it
                // (the eased value slides out to the option and back again when the cursor leaves it)
                var lean = Vector2.Zero;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (!hover && distance > 1f)
                    lean = new Vector2(dx, dy) / distance * (onOption ? LeanOnOption : Math.Min(LeanMax, distance / 8f));
                this.Tilt += (tilt - this.Tilt) * 0.25f;
                this.Lean += (lean - this.Lean) * 0.25f;
                if (side == 0)
                    this.Tilt = 0f; // the front view never tilts

                Rectangle head;
                if (this.Face != null)
                    head = Faces.DrawHead(b, this.Face, new Point(headCentre.X + (int)this.Lean.X, headCentre.Y + (int)this.Lean.Y), headSize, hover || this.OnCenter == null, side, this.Tilt);
                else
                {
                    // the farmer: the game's own head drawing (base head and hair, always facing the front), so it leans but doesn't turn
                    const float farmerScale = 9f;
                    int size = (int)(16 * farmerScale);
                    head = new Rectangle(headCentre.X + (int)this.Lean.X - size / 2, headCentre.Y + (int)this.Lean.Y - size / 2, size, size);
                    try
                    {
                        Game1.player.FarmerRenderer.drawMiniPortrat(b, new Vector2(head.X, head.Y), 0.9f, farmerScale, 2, Game1.player);
                    }
                    catch
                    {
                        head = Rectangle.Empty;
                    }
                }
                if (!head.IsEmpty)
                    head.Offset(-(int)this.Lean.X, -(int)this.Lean.Y); // the name plate and the clickable area stay put
                if (head.IsEmpty)
                    head = new Rectangle(center.X, center.Y, 0, 0);
                Rectangle plate = Ui.DrawLabel(b, this.Title, center.X, head.Bottom + 6);
                this.CenterArea = Rectangle.Union(head, plate);
            }
            else
            {
                int boxWidth = titleWidth + Ui.BoxPadding * 2;
                int boxHeight = Ui.LineHeight + Ui.BoxPadding * 2 - 6;
                this.CenterArea = new Rectangle(center.X - boxWidth / 2, center.Y - boxHeight / 2, boxWidth, boxHeight);
                Ui.DrawBox(b, this.CenterArea);
                Ui.DrawText(b, this.Title, this.CenterArea.X + Ui.BoxPadding, this.CenterArea.Y + Ui.BoxPadding - 3, Ui.Title, bold: true);
            }

            this.Areas.Clear();
            for (int i = 0; i < count; i++)
            {
                double angle = -Math.PI / 2 + i * (Math.PI * 2 / count); // first option at the top, then clockwise
                string label = this.Items[i].Key;
                int width = Ui.MeasureButton(label);
                int x = center.X + (int)(Math.Cos(angle) * radiusX) - width / 2;
                int y = center.Y + (int)(Math.Sin(angle) * radiusY) - Ui.ButtonHeight / 2;
                this.Areas.Add(Ui.DrawButton(b, label, x, y));
            }
        }
    }
}
