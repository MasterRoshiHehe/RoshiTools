using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Drawing helpers that use the game's own interface art (the menu frame from Game1.menuTexture and the small
    /// button frame from Game1.mouseCursors), so the tool looks like the rest of the game and follows interface
    /// recolour mods. Everything is drawn in UI space.
    /// </summary>
    /// <summary>Lets a window be dragged by its title bar: remembers how far it was moved from its default place.</summary>
    internal sealed class WindowDrag
    {
        private bool Dragging;
        private Point Grab;

        /// <summary>How far the window was moved from its default position.</summary>
        public Point Offset;

        public void Begin(Point mouse)
        {
            this.Dragging = true;
            this.Grab = new Point(mouse.X - this.Offset.X, mouse.Y - this.Offset.Y);
        }

        /// <summary>Call every tick with whether the left button is still held.</summary>
        public void Update(bool leftDown, Point mouse)
        {
            if (!this.Dragging)
                return;
            if (!leftDown)
            {
                this.Dragging = false;
                return;
            }
            this.Offset = new Point(mouse.X - this.Grab.X, mouse.Y - this.Grab.Y);
        }

        /// <summary>The window's area after moving, kept reachable on screen.</summary>
        public Rectangle Apply(Rectangle area)
        {
            area.Offset(this.Offset);
            area.X = Math.Clamp(area.X, 80 - area.Width, Math.Max(0, Game1.uiViewport.Width - 80));
            area.Y = Math.Clamp(area.Y, 0, Math.Max(0, Game1.uiViewport.Height - 60));
            return area;
        }
    }

    internal static class Ui
    {
        /// <summary>Text scale for the small font. 1 keeps the pixel font crisp; a fractional scale makes it thin.</summary>
        public const float Scale = 1f;

        /// <summary>Space between a window frame and its content (the frame's border is about 16 px).</summary>
        public const int BoxPadding = 22;

        private const int ButtonPadX = 12;
        private const int ButtonPadY = 5;

        /// <summary>The standard small button frame in LooseSprites/Cursors.</summary>
        private static readonly Rectangle ButtonSource = new(432, 439, 9, 9);

        public static Color TextColor => Game1.textColor;
        public static readonly Color Dim = new(95, 70, 45);
        public static readonly Color Faint = new(140, 120, 95);
        public static readonly Color Title = new(110, 50, 10);

        public static int LineHeight => (int)(Game1.smallFont.LineSpacing * Scale);
        public static int ButtonHeight => LineHeight + ButtonPadY * 2;

        /// <summary>
        /// Set while the tool draws things that lie under an open ring: they then see the cursor far off screen, so
        /// nothing behind the ring lights up or shows a tooltip. Only ever true during drawing.
        /// </summary>
        public static bool HoverBlocked;

        public static Point Mouse => HoverBlocked ? new Point(-100000, -100000) : RealMouse;

        /// <summary>Where the cursor really is (UI pixels).</summary>
        public static Point RealMouse => new(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));

        private static string PendingTooltip;

        public static float Measure(string text, bool bold = false)
        {
            return Game1.smallFont.MeasureString(text ?? "").X * Scale + (bold ? 1 : 0);
        }

        public static int MeasureButton(string label)
        {
            return (int)Measure(label, bold: true) + ButtonPadX * 2;
        }

        /// <summary>A window frame (the game's parchment menu box).</summary>
        public static void DrawBox(SpriteBatch b, Rectangle area)
        {
            IClickableMenu.drawTextureBox(b, area.X, area.Y, area.Width, area.Height, Color.White);
        }

        /// <param name="bold">Draw the text twice, one pixel apart, for a heavier stroke.</param>
        public static void DrawText(SpriteBatch b, string text, int x, int y, Color color, float scale = Scale, bool bold = false)
        {
            b.DrawString(Game1.smallFont, text ?? "", new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            if (bold)
                b.DrawString(Game1.smallFont, text ?? "", new Vector2(x + 1, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        /// <summary>A button in the game's style. Returns its area.</summary>
        public static Rectangle DrawButton(SpriteBatch b, string label, int x, int y, bool active = false, int width = -1)
        {
            if (width < 0)
                width = MeasureButton(label);
            var area = new Rectangle(x, y, width, ButtonHeight);
            Color tint = active ? new Color(150, 230, 140) : area.Contains(Mouse) ? Color.Wheat : Color.White;
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ButtonSource, area.X, area.Y, area.Width, area.Height, tint, 4f, drawShadow: false);
            DrawText(b, label, x + ButtonPadX, y + ButtonPadY, TextColor, bold: true);
            return area;
        }

        /// <summary>A name plate: the button frame with bold text, without the hover tint. Returns its area.</summary>
        public static Rectangle DrawLabel(SpriteBatch b, string text, int centreX, int y)
        {
            int width = MeasureButton(text);
            var area = new Rectangle(centreX - width / 2, y, width, ButtonHeight);
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ButtonSource, area.X, area.Y, area.Width, area.Height, Color.White, 4f, drawShadow: false);
            DrawText(b, text, area.X + ButtonPadX, y + ButtonPadY, Title, bold: true);
            return area;
        }

        /// <summary>A text field: the button frame, the text (or a faint hint when empty) and a blinking caret while it has the keyboard.</summary>
        public static void DrawField(SpriteBatch b, Rectangle area, string text, string hint, bool focused)
        {
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ButtonSource, area.X, area.Y, area.Width, area.Height, focused ? Color.White : area.Contains(Mouse) ? Color.Wheat : new Color(235, 225, 205), 4f, drawShadow: false);
            bool empty = string.IsNullOrEmpty(text);
            string shown = empty ? (focused ? "" : hint) : text;
            while (shown.Length > 1 && Measure(shown) > area.Width - ButtonPadX * 2 - 8)
                shown = shown.Substring(1);
            DrawText(b, shown, area.X + ButtonPadX, area.Y + ButtonPadY, empty ? Faint : TextColor);
            if (focused && Game1.currentGameTime.TotalGameTime.Milliseconds < 500)
                b.Draw(Game1.staminaRect, new Rectangle(area.X + ButtonPadX + (int)Measure(shown) + 2, area.Y + ButtonPadY + 2, 2, LineHeight - 4), TextColor);
        }

        /// <summary>Ask for a small text box near the cursor. It is drawn by <see cref="FlushTooltip"/>, after every window, so nothing covers it.</summary>
        public static void DrawTooltip(SpriteBatch b, string text)
        {
            if (!string.IsNullOrEmpty(text))
                PendingTooltip = text;
        }

        /// <summary>Draw the tooltip asked for this frame (if any) on top of everything drawn so far, kept on screen.</summary>
        public static void FlushTooltip(SpriteBatch b)
        {
            string text = PendingTooltip;
            PendingTooltip = null;
            if (string.IsNullOrEmpty(text) || HoverBlocked)
                return;

            Point mouse = RealMouse;
            int width = (int)Measure(text) + BoxPadding * 2;
            int height = LineHeight + BoxPadding * 2 - 8;
            int x = Math.Clamp(mouse.X + 24, 4, Math.Max(4, Game1.uiViewport.Width - width - 4));
            int y = mouse.Y + 32;
            if (y + height > Game1.uiViewport.Height - 4)
                y = mouse.Y - height - 8; // no room below the cursor: go above it
            y = Math.Clamp(y, 4, Math.Max(4, Game1.uiViewport.Height - height - 4));
            DrawBox(b, new Rectangle(x, y, width, height));
            DrawText(b, text, x + BoxPadding, y + BoxPadding - 4, TextColor);
        }

        /// <summary>The game's gold quality star, for a favourite.</summary>
        public static void DrawStar(SpriteBatch b, int x, int y, float scale = 3f)
        {
            b.Draw(Game1.mouseCursors, new Vector2(x, y), new Rectangle(346, 400, 8, 8), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        /// <summary>The colour for a node status, readable on parchment.</summary>
        public static Color StatusColor(string status)
        {
            return status switch
            {
                "winner" => new Color(150, 85, 0),
                "pass" => new Color(25, 110, 30),
                "fail" => new Color(170, 40, 40),
                "skipped" => Faint,
                "warning" => new Color(200, 80, 0),
                _ => TextColor
            };
        }

        /// <summary>The game's arrow cursor, for when the game doesn't draw it (its HUD is hidden).</summary>
        public static void DrawCursor(SpriteBatch b)
        {
            Point mouse = RealMouse;
            b.Draw(Game1.mouseCursors, new Vector2(mouse.X, mouse.Y), Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16), Color.White, 0f, Vector2.Zero, 4f + Game1.dialogueButtonScale / 150f, SpriteEffects.None, 1f);
        }
    }
}
