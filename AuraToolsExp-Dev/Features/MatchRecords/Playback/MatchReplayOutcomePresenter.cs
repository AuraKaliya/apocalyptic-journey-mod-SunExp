using AuraToolsExp.Dll.Features.Settings;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayOutcomePresenter
{
    internal static void Show(string result)
    {
        var caption = WitchUiManager.Instance?.ShowUI<CaptionUI>("CaptionUI");
        if (caption == null) throw new System.InvalidOperationException("原生 CaptionUI 不可用。");
        caption.ShowCaption(
            AuraToolsPlayerDisplay.BattleResult(result),
            CaptionStyle.Center,
            0.45f,
            0f,
            3);
    }

    internal static void Clear()
    {
        // CaptionUI owns its native animation and is tracked by replay UI ownership.
    }
}
