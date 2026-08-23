using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayExportControlsPresenter
{
    private static GameObject? root;
    private static Text? status;
    private static Text? actionLabel;

    internal static void Show()
    {
        Close();
        root = new GameObject("AuraToolsReplayExportControls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Object.DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32001;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var toolbar = AuraToolsUi.CreateRect("Toolbar", root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f), new Vector2(760f, 62f));
        toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 22f);
        AuraToolsUi.AddSectionImage(toolbar);
        var layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        status = AuraToolsUi.AddText(toolbar.transform, "准备导出...", AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddButton(toolbar.transform, "输出目录", () => FileResourceUtil.OpenDirectory(MatchRecordStorage.MediaDirectory), 92f);
        var action = AuraToolsUi.AddButton(toolbar.transform, "取消", MatchReplayVideoExporter.CancelOrDismiss, 82f);
        actionLabel = action.GetComponentInChildren<Text>();
    }

    internal static void Refresh(MatchReplayExportJob? job)
    {
        if (root == null || job == null) return;
        if (status != null)
        {
            status.text = StateLabel(job.State) + "   " + (job.Progress * 100f).ToString("0") + "%"
                          + (string.IsNullOrWhiteSpace(job.Message) ? "" : "   " + job.Message);
        }

        if (actionLabel != null)
        {
            actionLabel.text = job.State == MatchReplayExportStates.Ready
                               || job.State == MatchReplayExportStates.Corrupt
                               || job.State == MatchReplayExportStates.Failed
                               || job.State == MatchReplayExportStates.Cancelled
                ? "关闭"
                : "取消";
        }
    }

    internal static void Close()
    {
        if (root != null) Object.Destroy(root);
        root = null;
        status = null;
        actionLabel = null;
    }

    private static string StateLabel(string state)
    {
        return state == MatchReplayExportStates.Rendering ? "正在渲染"
            : state == MatchReplayExportStates.Encoding ? "正在编码"
            : state == MatchReplayExportStates.Validating ? "正在验证"
            : state == MatchReplayExportStates.Committing ? "正在提交"
            : state == MatchReplayExportStates.Ready ? "导出完成"
            : state == MatchReplayExportStates.Corrupt ? "媒体损坏"
            : state == MatchReplayExportStates.Failed ? "导出失败"
            : state == MatchReplayExportStates.Cancelled ? "已取消"
            : "正在准备";
    }
}
