using UnityEngine;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveWindowShell
{
    private ArchiveWindowShell(
        GameObject root,
        RectTransform frame,
        RectTransform portraitLayer,
        RectTransform chromeLayer)
    {
        Root = root;
        Frame = frame;
        PortraitLayer = portraitLayer;
        ChromeLayer = chromeLayer;
    }

    public GameObject Root { get; }

    public RectTransform Frame { get; }

    public RectTransform PortraitLayer { get; }

    public RectTransform ChromeLayer { get; }

    public static ArchiveWindowShell Create(Transform parent)
    {
        var root = TerriasModalHost.CreateFullscreenRoot(
            "Terrias_WitchArchivePanel",
            parent,
            ArchiveUiTheme.Backdrop);
        var frame = TerriasUiBuilder.CreateRect(
            "ArchiveFrame",
            root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(ArchiveLayoutMetrics.ReferenceWidth, ArchiveLayoutMetrics.ReferenceHeight));
        var available = AvailableSize(parent);
        var scale = Mathf.Min(
            (available.x - ArchiveLayoutMetrics.EdgeMargin * 2f) / ArchiveLayoutMetrics.ReferenceWidth,
            (available.y - ArchiveLayoutMetrics.EdgeMargin * 2f) / ArchiveLayoutMetrics.ReferenceHeight);
        frame.localScale = Vector3.one * Mathf.Max(0.25f, scale);
        ArchiveUiFactory.ApplyPanel(frame.gameObject, ArchiveUiTheme.Frame, false);

        var portraitLayer = ArchiveUiFactory.CreateFill("PortraitLayer", frame, Vector4.zero);
        var chromeLayer = ArchiveUiFactory.CreateFill("ChromeLayer", frame, Vector4.zero);
        var topBar = ArchiveUiFactory.CreateTopLeft(
            "TopBar",
            chromeLayer,
            0f,
            0f,
            ArchiveLayoutMetrics.ReferenceWidth,
            ArchiveLayoutMetrics.TopBarHeight);
        ArchiveUiFactory.ApplyPanel(topBar.gameObject, ArchiveUiTheme.TopBar, false);

        var divider = ArchiveUiFactory.CreateTopLeft(
            "TopBarDivider",
            chromeLayer,
            0f,
            ArchiveLayoutMetrics.TopBarHeight - 2f,
            ArchiveLayoutMetrics.ReferenceWidth,
            2f);
        ArchiveUiFactory.ApplyPanel(divider.gameObject, ArchiveUiTheme.Divider, false);
        return new ArchiveWindowShell(root, frame, portraitLayer, chromeLayer);
    }

    private static Vector2 AvailableSize(Transform parent)
    {
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            return rect.rect.size;
        }

        return new Vector2(Mathf.Max(640f, Screen.width), Mathf.Max(360f, Screen.height));
    }
}
