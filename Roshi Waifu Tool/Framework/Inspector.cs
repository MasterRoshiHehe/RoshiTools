using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace RoshiWaifuTool.Framework
{
    /// <summary>Labels over NPCs, a detail box for the NPC under the cursor, and the diagnostics run when she's clicked.</summary>
    internal sealed class Inspector
    {
        private readonly IModHelper Helper;
        private readonly IMonitor Monitor;
        private readonly Func<ModConfig> Config;
        private readonly CommandRunner Commands;
        private ILiveAppearanceApi Laf;

        public Inspector(IModHelper helper, IMonitor monitor, Func<ModConfig> config, CommandRunner commands)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Config = config;
            this.Commands = commands;
        }

        /// <summary>Whether clicking an NPC inspects her (instead of talking to her).</summary>
        public bool Enabled { get; set; }

        public void HookLiveAppearance()
        {
            try
            {
                this.Laf = this.Helper.ModRegistry.GetApi<ILiveAppearanceApi>("MasterRoshiHehe.LiveAppearanceFramework");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't read Live Appearance Framework's API: {ex.Message}", LogLevel.Trace);
            }
        }

        /// <summary>The NPC under the cursor in the current location, if any.</summary>
        public NPC GetHovered()
        {
            if (Game1.currentLocation == null)
                return null;

            Vector2 cursor = this.Helper.Input.GetCursorPosition().AbsolutePixels;
            var point = new Point((int)cursor.X, (int)cursor.Y);
            return Game1.currentLocation.characters.FirstOrDefault(npc => !npc.IsInvisible && GetSpriteArea(npc).Contains(point));
        }

        /// <summary>Run the configured diagnostics for an NPC; their output goes to the SMAPI console.</summary>
        public void Inspect(NPC npc)
        {
            this.Monitor.Log($"──────── {npc.Name} at {TimeMachine.Format(Game1.timeOfDay)} ────────", LogLevel.Info);
            foreach (string line in this.Describe(npc))
                this.Monitor.Log(line, LogLevel.Info);

            int ran = 0;
            foreach (string raw in this.Config().InspectCommands ?? new List<string>())
            {
                string[] parts = string.Format(raw, npc.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && this.Commands.Run(parts[0], parts.Skip(1).ToArray()))
                    ran++;
            }

            Game1.addHUDMessage(new HUDMessage($"{npc.Name}: {(ran > 0 ? "details" : "basics")} in the SMAPI console") { noIcon = true });
        }

        /// <summary>Draw the labels (world space).</summary>
        public void DrawWorld(SpriteBatch b)
        {
            if (Game1.currentLocation == null || Game1.eventUp)
                return;

            NPC hovered = this.Enabled ? this.GetHovered() : null;

            if (this.Config().ShowLabels)
            {
                foreach (NPC npc in Game1.currentLocation.characters)
                {
                    if (npc.IsInvisible || !npc.IsVillager || npc == hovered)
                        continue;

                    string label = this.GetAppearanceId(npc) ?? "(default)";
                    string pose = this.GetPose(npc);
                    if (pose != null)
                        label += $" | {pose}";
                    Vector2 position = Game1.GlobalToLocal(Game1.viewport, new Vector2(npc.StandingPixel.X, npc.Position.Y - 104));
                    DrawBox(b, new[] { label }, position, centered: true, scale: 0.6f);
                }
            }

            if (hovered != null)
            {
                Rectangle area = GetSpriteArea(hovered);
                Vector2 position = Game1.GlobalToLocal(Game1.viewport, new Vector2(area.Right + 12, area.Top));
                DrawBox(b, this.Describe(hovered).Append("click: full report in the console").ToArray(), position, centered: false, scale: 0.7f);
            }
        }

        private IEnumerable<string> Describe(NPC npc)
        {
            yield return $"{npc.Name} - {npc.currentLocation?.NameOrUniqueName} {npc.TilePoint.X} {npc.TilePoint.Y}, facing {npc.FacingDirection}";
            yield return $"sprite: {npc.Sprite?.Texture?.Name ?? npc.Sprite?.textureName.Value ?? "(none)"}, frame {npc.Sprite?.currentFrame}{(npc.Sprite?.CurrentAnimation != null ? " (animating)" : "")}";
            yield return $"portrait: {SafePortraitName(npc)}";

            string applied = this.GetAppearanceId(npc) ?? "(default)";
            string expected = null;
            try { expected = this.Laf?.GetExpectedAppearanceId(npc); } catch { }
            yield return this.Laf != null
                ? $"appearance: {applied}{(expected != null && expected != applied ? $" (LAF expects {expected})" : "")}"
                : $"appearance: {applied}";

            string pose = this.GetPose(npc);
            if (pose != null)
                yield return $"pose: {pose}";

            if (npc.controller != null)
                yield return $"walking a schedule leg ({npc.controller.pathToEndPoint?.Count ?? 0} tiles left)";
            else if (npc.temporaryController != null)
                yield return "walking (temporary path, e.g. a detour)";

            if (npc.Schedule != null)
            {
                KeyValuePair<int, SchedulePathDescription> next = npc.Schedule.Where(pair => pair.Key > Game1.timeOfDay).OrderBy(pair => pair.Key).FirstOrDefault();
                yield return next.Value != null
                    ? $"next stop: {TimeMachine.Format(next.Key)} -> {next.Value.targetLocationName} {next.Value.targetTile.X} {next.Value.targetTile.Y}{(string.IsNullOrEmpty(next.Value.endOfRouteBehavior) ? "" : $" ({next.Value.endOfRouteBehavior})")}"
                    : "next stop: none left today";
            }
            else
                yield return "no schedule today";
        }

        private string GetAppearanceId(NPC npc)
        {
            try
            {
                return this.Laf?.GetAppearanceId(npc) ?? npc.LastAppearanceId;
            }
            catch
            {
                return npc.LastAppearanceId;
            }
        }

        private string GetPose(NPC npc)
        {
            try
            {
                if (this.Laf == null || !this.Laf.IsInPose(npc))
                    return null;
                string stage = this.Laf.GetPoseStageId(npc);
                return string.IsNullOrEmpty(stage) ? this.Laf.GetPoseId(npc) : $"{this.Laf.GetPoseId(npc)}/{stage}";
            }
            catch
            {
                return null;
            }
        }

        private static string SafePortraitName(NPC npc)
        {
            try
            {
                return npc.Portrait?.Name ?? "(none)";
            }
            catch
            {
                return "(unreadable)";
            }
        }

        private static Rectangle GetSpriteArea(NPC npc)
        {
            int width = (npc.Sprite?.SpriteWidth ?? 16) * 4;
            int height = (npc.Sprite?.SpriteHeight ?? 32) * 4;
            return new Rectangle((int)npc.Position.X + 32 - width / 2, (int)npc.Position.Y + 64 - height, width, height);
        }

        internal static void DrawBox(SpriteBatch b, string[] lines, Vector2 position, bool centered, float scale)
        {
            SpriteFont font = Game1.smallFont;
            float width = lines.Max(line => font.MeasureString(line).X) * scale;
            float lineHeight = font.LineSpacing * scale;
            if (centered)
                position.X -= width / 2f;

            b.Draw(Game1.staminaRect, new Rectangle((int)position.X - 6, (int)position.Y - 4, (int)width + 12, (int)(lineHeight * lines.Length) + 8), Color.Black * 0.75f);
            for (int i = 0; i < lines.Length; i++)
                b.DrawString(font, lines[i], new Vector2(position.X, position.Y + i * lineHeight), Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }
    }
}
