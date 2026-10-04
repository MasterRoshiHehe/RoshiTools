using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// A camera detached from the farmer. Uses vanilla's Game1.viewportFreeze (the viewport stops following the player)
    /// and moves Game1.viewport itself, with the arrow keys or by dragging the map with the left mouse button. The
    /// movement keys keep moving the farmer.
    /// </summary>
    internal sealed class FreeCamera
    {
        private readonly IModHelper Helper;
        private readonly System.Func<ModConfig> Config;

        public FreeCamera(IModHelper helper, System.Func<ModConfig> config)
        {
            this.Helper = helper;
            this.Config = config;
        }

        public bool Enabled { get; private set; }

        /// <summary>The NPC the camera stays centred on, if any.</summary>
        public NPC Follow { get; set; }

        public void Set(bool enabled)
        {
            if (this.Enabled == enabled)
                return;

            this.Enabled = enabled;
            this.Follow = null;
            Game1.viewportFreeze = enabled;
            if (!enabled)
                Game1.forceSnapOnNextViewportUpdate = true;
        }

        private bool Grabbed;
        private bool Dragging;
        private Point GrabStart;
        private Point GrabLast;

        /// <summary>Whether the map is being dragged with the mouse right now.</summary>
        public bool IsDragging => this.Dragging;

        /// <summary>
        /// The left button went down on the world (not on a window or button). If the mouse then moves while the button
        /// is held, the map is dragged along with it (as in The Sims); the free camera switches on by itself.
        /// </summary>
        public void BeginGrab()
        {
            if (this.Follow != null)
                return; // the camera is locked on someone
            this.Grabbed = true;
            this.Dragging = false;
            this.GrabStart = this.GrabLast = new Point(Game1.getMouseX(), Game1.getMouseY());
        }

        private void UpdateGrab()
        {
            if (!this.Grabbed)
                return;

            bool held = this.Helper.Input.IsDown(SButton.MouseLeft) || this.Helper.Input.IsSuppressed(SButton.MouseLeft);
            if (!held || this.Follow != null || Game1.activeClickableMenu != null)
            {
                this.Grabbed = false;
                this.Dragging = false;
                return;
            }

            var mouse = new Point(Game1.getMouseX(), Game1.getMouseY());
            if (!this.Dragging)
            {
                // a few pixels of slack, so a plain click doesn't move the map
                if (System.Math.Abs(mouse.X - this.GrabStart.X) + System.Math.Abs(mouse.Y - this.GrabStart.Y) < 8)
                    return;
                this.Dragging = true;
                this.Set(true);
                this.GrabLast = mouse;
                return;
            }

            Game1.viewport.X -= mouse.X - this.GrabLast.X;
            Game1.viewport.Y -= mouse.Y - this.GrabLast.Y;
            this.GrabLast = mouse;
        }

        /// <summary>Call before the game reads input while the farmer must stay put (a jump runs, or the farmer is hidden while following).</summary>
        public void SuppressMovement()
        {
            if (Game1.activeClickableMenu != null)
                return;

            foreach (SButton button in this.GetButtons().SelectMany(pair => pair.Value))
            {
                if (this.Helper.Input.IsDown(button) || this.Helper.Input.IsSuppressed(button))
                    this.Helper.Input.Suppress(button);
            }
        }

        /// <summary>Call every tick.</summary>
        public void Update()
        {
            if (Game1.currentLocation?.Map == null)
                return;

            this.UpdateGrab();
            if (!this.Enabled)
                return;

            Game1.viewportFreeze = true; // warps and events reset it

            if (this.Follow != null)
            {
                if (this.Follow.currentLocation == Game1.currentLocation)
                {
                    Point center = this.Follow.StandingPixel;
                    Game1.viewport.X = FitToMap(center.X - Game1.viewport.Width / 2, Game1.viewport.Width, Game1.currentLocation.Map.DisplayWidth);
                    Game1.viewport.Y = FitToMap(center.Y - Game1.viewport.Height / 2, Game1.viewport.Height, Game1.currentLocation.Map.DisplayHeight);
                }
                return;
            }

            if (Game1.activeClickableMenu != null)
                return;

            this.MoveWithKeys();
            this.KeepMapInView();
        }

        /// <summary>
        /// Keep the view inside the map, as the game's own camera does: it stops at the map's edge instead of showing
        /// the black outside, and a map smaller than the screen is centred.
        /// </summary>
        private static int FitToMap(int position, int viewSize, int mapSize)
        {
            return mapSize <= viewSize ? (mapSize - viewSize) / 2 : System.Math.Clamp(position, 0, mapSize - viewSize);
        }

        private void MoveWithKeys()
        {
            int speed = System.Math.Max(1, this.Config().CameraSpeed);
            if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                speed *= 3;

            // the arrow keys only: the movement keys keep moving the farmer
            if (this.Helper.Input.IsDown(SButton.Up))
                Game1.viewport.Y -= speed;
            if (this.Helper.Input.IsDown(SButton.Down))
                Game1.viewport.Y += speed;
            if (this.Helper.Input.IsDown(SButton.Left))
                Game1.viewport.X -= speed;
            if (this.Helper.Input.IsDown(SButton.Right))
                Game1.viewport.X += speed;
        }

        /// <summary>Keep at least half a screen of the map in view.</summary>
        private void KeepMapInView()
        {
            int mapWidth = Game1.currentLocation.Map.DisplayWidth;
            int mapHeight = Game1.currentLocation.Map.DisplayHeight;
            Game1.viewport.X = System.Math.Clamp(Game1.viewport.X, -Game1.viewport.Width / 2, System.Math.Max(-Game1.viewport.Width / 2, mapWidth - Game1.viewport.Width / 2));
            Game1.viewport.Y = System.Math.Clamp(Game1.viewport.Y, -Game1.viewport.Height / 2, System.Math.Max(-Game1.viewport.Height / 2, mapHeight - Game1.viewport.Height / 2));
        }

        private bool IsAnyDown(IEnumerable<SButton> buttons)
        {
            return buttons.Any(button => this.Helper.Input.IsDown(button) || this.Helper.Input.IsSuppressed(button));
        }

        private Dictionary<string, List<SButton>> GetButtons()
        {
            static List<SButton> From(InputButton[] bound, SButton arrow)
            {
                var list = bound?.Select(button => button.ToSButton()).ToList() ?? new List<SButton>();
                list.Add(arrow);
                return list;
            }

            return new Dictionary<string, List<SButton>>
            {
                ["up"] = From(Game1.options.moveUpButton, SButton.Up),
                ["down"] = From(Game1.options.moveDownButton, SButton.Down),
                ["left"] = From(Game1.options.moveLeftButton, SButton.Left),
                ["right"] = From(Game1.options.moveRightButton, SButton.Right)
            };
        }
    }
}
