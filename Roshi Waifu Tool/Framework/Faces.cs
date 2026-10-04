using System;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// Finds a character's face on her overworld sprite sheet: frame 0 (facing down), skipping the empty rows above
    /// her head, as a square. Because it reads the sheet she shows right now, the face matches the outfit she wears.
    /// The pixels are read once per texture and the result is remembered.
    /// </summary>
    internal static class Faces
    {
        private sealed class Box
        {
            public Rectangle Area;
            public Rectangle Head;
            /// <summary>Her head on the frames facing right (1) and left (3), once looked up; an empty area = the sheet has no such frame.</summary>
            public readonly Rectangle?[] Side = new Rectangle?[4];
            public int Width;
            public int Height;
        }

        private static readonly ConditionalWeakTable<Texture2D, Box> Cache = new();

        /// <summary>Forget every face (after sheets may have been repainted, e.g. a patch reload).</summary>
        public static void Clear()
        {
            Cache.Clear();
        }

        /// <summary>The texture and source area of an NPC's face, or false if she has no usable sprite.</summary>
        public static bool TryGet(NPC npc, out Texture2D texture, out Rectangle source)
        {
            texture = npc?.Sprite?.Texture;
            source = Rectangle.Empty;
            if (texture == null || texture.IsDisposed)
                return false;

            int width = Math.Clamp(npc.Sprite.SpriteWidth, 1, texture.Width);
            int height = Math.Clamp(npc.Sprite.SpriteHeight, 1, texture.Height);
            if (Cache.TryGetValue(texture, out Box box) && box.Width == width && box.Height == height)
            {
                source = box.Area;
                return true;
            }

            return Read(npc, texture, width, height, out source, out _);
        }

        /// <summary>
        /// The texture and source area of only her head (no shoulders): from the top of her hair down 11 rows on a
        /// normal 16 px wide sprite (more on bigger sprites), cut to the pixels that are there. Not square.
        /// </summary>
        public static bool TryGetHead(NPC npc, out Texture2D texture, out Rectangle source)
        {
            texture = npc?.Sprite?.Texture;
            source = Rectangle.Empty;
            if (texture == null || texture.IsDisposed)
                return false;

            int width = Math.Clamp(npc.Sprite.SpriteWidth, 1, texture.Width);
            int height = Math.Clamp(npc.Sprite.SpriteHeight, 1, texture.Height);
            if (Cache.TryGetValue(texture, out Box box) && box.Width == width && box.Height == height)
            {
                source = box.Head;
                return true;
            }
            return Read(npc, texture, width, height, out _, out source);
        }

        private static bool Read(NPC npc, Texture2D texture, int width, int height, out Rectangle face, out Rectangle head)
        {
            if (!Find(texture, width, height, out face, out head))
            {
                face = Fallback(npc, texture, width, height);
                head = new Rectangle(face.X, face.Y, face.Width, Math.Max(1, face.Height * 11 / 16));
            }
            Cache.Remove(texture);
            Cache.Add(texture, new Box { Area = face, Head = head, Width = width, Height = height });
            return true;
        }

        private static bool Find(Texture2D texture, int width, int height, out Rectangle face, out Rectangle head)
        {
            face = Rectangle.Empty;
            head = Rectangle.Empty;
            try
            {
                var pixels = new Color[width * height];
                texture.GetData(0, new Rectangle(0, 0, width, height), pixels, 0, pixels.Length);

                // the first row with something on it is the top of her head (or hair, or hat)
                int top = -1;
                for (int y = 0; y < height && top < 0; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[y * width + x].A > 16)
                        {
                            top = y;
                            break;
                        }
                    }
                }
                if (top < 0)
                    return false;

                // how wide the head is: the visible pixels in the rows just below the top
                int rows = Math.Min(height - top, 12);
                int left = width, right = -1;
                for (int y = top; y < top + rows; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[y * width + x].A > 16)
                        {
                            left = Math.Min(left, x);
                            right = Math.Max(right, x);
                        }
                    }
                }
                if (right < left)
                    return false;

                // a square from the top of the head, as wide as the head (at least 16 px on normal sprites)
                int side = Math.Clamp(Math.Max(right - left + 1, Math.Min(16, width)), 8, Math.Min(width, height - top));
                int centre = (left + right + 1) / 2;
                int squareLeft = Math.Clamp(centre - side / 2, 0, width - side);
                face = new Rectangle(squareLeft, top, side, side);

                // only the head: the rows down to the chin, and only as wide as the pixels in them
                int headRows = Math.Clamp((int)Math.Round(11 * Math.Max(1f, width / 16f)), 1, height - top);
                int headLeft = width, headRight = -1;
                for (int y = top; y < top + headRows; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[y * width + x].A > 16)
                        {
                            headLeft = Math.Min(headLeft, x);
                            headRight = Math.Max(headRight, x);
                        }
                    }
                }
                head = headRight >= headLeft ? new Rectangle(headLeft, top, headRight - headLeft + 1, headRows) : face;
                return true;
            }
            catch
            {
                return false; // textures that can't be read back (rare): use the game's own head area
            }
        }

        private static Rectangle Fallback(NPC npc, Texture2D texture, int width, int height)
        {
            Rectangle area = npc.getMugShotSourceRect();
            int side = Math.Min(Math.Min(area.Width, area.Height), Math.Min(width, height));
            return new Rectangle(Math.Clamp(area.X, 0, texture.Width - side), Math.Clamp(area.Y, 0, texture.Height - side), side, side);
        }

        /// <summary>Draw an NPC's face into a square area (nothing if she has no sprite).</summary>
        public static void Draw(SpriteBatch b, NPC npc, Rectangle area)
        {
            if (TryGet(npc, out Texture2D texture, out Rectangle source))
                b.Draw(texture, area, source, Color.White);
        }

        /// <summary>
        /// Her head on the first frame of a row of her sheet (1 = facing right, 3 = facing left on a normal sheet), or
        /// false if the sheet has no such row or nothing is drawn there.
        /// </summary>
        private static bool TryGetSideHead(NPC npc, int row, out Texture2D texture, out Rectangle source)
        {
            source = Rectangle.Empty;
            if (!TryGetHead(npc, out texture, out _) || !Cache.TryGetValue(texture, out Box box))
                return false;

            if (box.Side[row] == null)
            {
                Rectangle found = Rectangle.Empty;
                if (texture.Height >= (row + 1) * box.Height)
                {
                    try
                    {
                        var pixels = new Color[box.Width * box.Height];
                        texture.GetData(0, new Rectangle(0, row * box.Height, box.Width, box.Height), pixels, 0, pixels.Length);
                        int top = -1, left = box.Width, right = -1;
                        int rows = Math.Max(1, box.Head.Height);
                        for (int y = 0; y < box.Height && (top < 0 || y < top + rows); y++)
                        {
                            for (int x = 0; x < box.Width; x++)
                            {
                                if (pixels[y * box.Width + x].A <= 16)
                                    continue;
                                if (top < 0)
                                    top = y;
                                left = Math.Min(left, x);
                                right = Math.Max(right, x);
                            }
                        }
                        if (top >= 0 && right >= left)
                            found = new Rectangle(left, row * box.Height + top, right - left + 1, Math.Min(rows, box.Height - top));
                    }
                    catch
                    {
                        // unreadable texture: no side view
                    }
                }
                box.Side[row] = found;
            }

            source = box.Side[row].Value;
            return !source.IsEmpty;
        }

        /// <summary>
        /// Draw only her head, with no frame, centred on a point at the largest whole pixel scale that fits
        /// <paramref name="maxSize"/>. A dark edge keeps it readable on any map. Returns the area it covers (as if
        /// facing the front and not turned, so the clickable area doesn't jump).
        /// </summary>
        /// <param name="side">-1 = she looks left, 1 = right, 0 = at you.</param>
        /// <param name="rotation">How far the head is tilted, in radians (clockwise).</param>
        public static Rectangle DrawHead(SpriteBatch b, NPC npc, Point centre, int maxSize, bool highlight, int side = 0, float rotation = 0f)
        {
            if (!TryGetHead(npc, out Texture2D texture, out Rectangle front))
                return Rectangle.Empty;

            int scale = Math.Max(1, maxSize / Math.Max(front.Width, front.Height));
            var area = new Rectangle(centre.X - front.Width * scale / 2, centre.Y - front.Height * scale / 2, front.Width * scale, front.Height * scale);

            // the frame to show: the side view if the sheet has one (a missing left view is the right one mirrored)
            Rectangle source = front;
            SpriteEffects effects = SpriteEffects.None;
            if (side > 0 && TryGetSideHead(npc, 1, out _, out Rectangle right))
                source = right;
            else if (side < 0)
            {
                if (TryGetSideHead(npc, 3, out _, out Rectangle left))
                    source = left;
                else if (TryGetSideHead(npc, 1, out _, out right))
                {
                    source = right;
                    effects = SpriteEffects.FlipHorizontally;
                }
            }

            var origin = new Vector2(source.Width / 2f, source.Height / 2f);
            var position = new Vector2(centre.X, centre.Y);
            int edge = Math.Max(2, scale / 2);
            foreach (Vector2 offset in new[] { new Vector2(-edge, 0), new Vector2(edge, 0), new Vector2(0, -edge), new Vector2(0, edge) })
                b.Draw(texture, position + offset, source, Color.Black * 0.45f, rotation, origin, scale, effects, 1f);
            b.Draw(texture, position, source, highlight ? Color.White : new Color(235, 235, 235), rotation, origin, scale, effects, 1f);
            return area;
        }
    }
}
