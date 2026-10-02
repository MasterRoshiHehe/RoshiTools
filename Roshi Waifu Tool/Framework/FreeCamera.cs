using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// A camera detached from the farmer. Uses vanilla's Game1.viewportFreeze (the viewport stops following the player)
    /// and moves Game1.viewport itself; the movement keys are suppressed so the farmer stays put.
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

        /// <summary>Call before the game reads input, so the farmer doesn't walk while the camera moves.</summary>
        public void SuppressMovement()
        {
            if (!this.Enabled || Game1.activeClickableMenu != null)
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
            if (!this.Enabled || Game1.currentLocation?.Map == null)
                return;

            Game1.viewportFreeze = true; // warps and events reset it

            if (this.Follow != null)
            {
                if (this.Follow.currentLocation == Game1.currentLocation)
                {
                    Point center = this.Follow.StandingPixel;
                    Game1.viewport.X = center.X - Game1.viewport.Width / 2;
                    Game1.viewport.Y = center.Y - Game1.viewport.Height / 2;
                }
                return;
            }

            if (Game1.activeClickableMenu != null)
                return;

            int speed = System.Math.Max(1, this.Config().CameraSpeed);
            if (this.Helper.Input.IsDown(SButton.LeftShift) || this.Helper.Input.IsDown(SButton.RightShift))
                speed *= 3;

            var buttons = this.GetButtons();
            if (this.IsAnyDown(buttons["up"]))
                Game1.viewport.Y -= speed;
            if (this.IsAnyDown(buttons["down"]))
                Game1.viewport.Y += speed;
            if (this.IsAnyDown(buttons["left"]))
                Game1.viewport.X -= speed;
            if (this.IsAnyDown(buttons["right"]))
                Game1.viewport.X += speed;

            // keep at least half a screen of the map in view
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
