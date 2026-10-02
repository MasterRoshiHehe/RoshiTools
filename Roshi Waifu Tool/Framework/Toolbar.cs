using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>The row of buttons at the top of the screen while the workspace is open (drawn in UI space).</summary>
    internal sealed class Toolbar
    {
        public sealed class Button
        {
            public string Label;
            public string Tooltip;
            public Action OnClick;
            public bool Active;
            internal Rectangle Bounds;
        }

        private const float Scale = 0.8f;
        private List<Button> Buttons = new();

        /// <summary>Draw the status line and the buttons, and remember where they are for clicks.</summary>
        public void Draw(SpriteBatch b, string status, List<Button> buttons)
        {
            this.Buttons = buttons;
            SpriteFont font = Game1.smallFont;
            int lineHeight = (int)(font.LineSpacing * Scale);
            int screenWidth = Game1.uiViewport.Width;
            Point mouse = new(Game1.getMouseX(ui_scale: true), Game1.getMouseY(ui_scale: true));

            // layout: centred rows
            const int padX = 10, padY = 6, gap = 6, top = 8;
            var rows = new List<List<Button>> { new() };
            int rowWidth = 0;
            foreach (Button button in buttons)
            {
                int width = (int)(font.MeasureString(button.Label).X * Scale) + padX * 2;
                if (rowWidth > 0 && rowWidth + gap + width > screenWidth - 32)
                {
                    rows.Add(new List<Button>());
                    rowWidth = 0;
                }
                button.Bounds = new Rectangle(0, 0, width, lineHeight + padY * 2);
                rows[^1].Add(button);
                rowWidth += (rowWidth > 0 ? gap : 0) + width;
            }

            // status line
            Vector2 statusSize = font.MeasureString(status) * Scale;
            int y = top;
            b.Draw(Game1.staminaRect, new Rectangle((int)(screenWidth - statusSize.X) / 2 - padX, y, (int)statusSize.X + padX * 2, lineHeight + padY * 2), Color.Black * 0.8f);
            b.DrawString(font, status, new Vector2((screenWidth - statusSize.X) / 2f, y + padY), Color.Gold, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
            y += lineHeight + padY * 2 + gap;

            Button hovered = null;
            foreach (List<Button> row in rows)
            {
                int width = -gap;
                foreach (Button button in row)
                    width += button.Bounds.Width + gap;

                int x = (screenWidth - width) / 2;
                foreach (Button button in row)
                {
                    button.Bounds = new Rectangle(x, y, button.Bounds.Width, button.Bounds.Height);
                    bool hover = button.Bounds.Contains(mouse);
                    if (hover)
                        hovered = button;

                    Color back = button.Active ? new Color(40, 110, 60) : hover ? new Color(70, 70, 90) : new Color(30, 30, 40);
                    b.Draw(Game1.staminaRect, button.Bounds, back * 0.92f);
                    b.DrawString(font, button.Label, new Vector2(x + padX, y + padY), Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
                    x += button.Bounds.Width + gap;
                }
                y += lineHeight + padY * 2 + gap;
            }

            if (hovered?.Tooltip != null)
            {
                Vector2 size = font.MeasureString(hovered.Tooltip) * Scale;
                var position = new Vector2(Math.Clamp(mouse.X + 16, 8, Math.Max(8, screenWidth - (int)size.X - 16)), y + 2);
                b.Draw(Game1.staminaRect, new Rectangle((int)position.X - 6, (int)position.Y - 4, (int)size.X + 12, (int)size.Y + 8), Color.Black * 0.85f);
                b.DrawString(font, hovered.Tooltip, position, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
            }
        }

        /// <summary>Whether the cursor (UI pixels) is over a button.</summary>
        public Button GetButtonAt(Point uiPixel)
        {
            foreach (Button button in this.Buttons)
            {
                if (button.Bounds.Contains(uiPixel))
                    return button;
            }
            return null;
        }
    }
}
