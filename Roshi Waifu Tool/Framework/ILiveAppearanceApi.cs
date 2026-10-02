using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>The part of Live Appearance Framework's API RWT uses (optional; LAF isn't a dependency).</summary>
    public interface ILiveAppearanceApi
    {
        string GetAppearanceId(NPC npc);
        string GetExpectedAppearanceId(NPC npc);
        bool IsInPose(NPC npc);
        string GetPoseId(NPC npc);
        string GetPoseStageId(NPC npc);
    }
}
