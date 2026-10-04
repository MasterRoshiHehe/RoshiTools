using StardewValley;

namespace RoshiWaifuTool.Framework
{
    /// <summary>
    /// The editor API of Live Appearance Framework (contract: claude/RWT-EditorApi-HANDOFF.md). Requested separately
    /// from <see cref="ILiveAppearanceApi"/>, so an older LAF without it still gives the basic API.
    /// </summary>
    public interface ILiveAppearanceEditorApi
    {
        int EditorApiVersion { get; }

        /// <summary>Why this NPC looks/behaves the way she does, as JSON: { Summary, Sections[node] }.</summary>
        string Explain(NPC npc);

        /// <summary>Makes LAF do its periodic scan now instead of waiting for its scan interval.</summary>
        void ScanNow();
    }

    /// <summary>The editor API of Animated Portrait Framework (same contract).</summary>
    public interface IAnimatedPortraitApi
    {
        int EditorApiVersion { get; }

        string Explain(NPC npc);
    }
}
