using Terrias.Dll.Hooks;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveInfoPanel : MonoBehaviour
{
    private Text? sectionTitle;
    private Text? nameValue;
    private Text? titleValue;
    private Text? summaryValue;
    private Text? backgroundValue;
    private ScrollRect? basicScroll;
    private ScrollRect? backgroundScroll;

    public static ArchiveInfoPanel Create(Transform parent)
    {
        var root = ArchiveUiFactory.CreateFromRect("InfoPanel", parent, ArchiveLayoutMetrics.InfoPanel);
        ArchiveUiFactory.ApplyPanel(root.gameObject, ArchiveUiTheme.PanelStrong, true);
        var accent = ArchiveUiFactory.CreateTopLeft("Accent", root, 0f, 0f, 3f, ArchiveLayoutMetrics.InfoPanel.height);
        ArchiveUiFactory.ApplyPanel(accent.gameObject, ArchiveUiTheme.AccentMuted, false);
        var titleRect = ArchiveUiFactory.CreateTopLeft("SectionTitle", root, 24f, 20f, 344f, 36f);
        var sectionTitle = ArchiveUiFactory.CreateText(
            "Value",
            titleRect,
            WitchArchiveStrings.Basic,
            24,
            TextAnchor.MiddleLeft,
            ArchiveUiTheme.TextPrimary,
            true);
        var divider = ArchiveUiFactory.CreateTopLeft("Divider", root, 24f, 72f, 360f, 2f);
        ArchiveUiFactory.ApplyPanel(divider.gameObject, ArchiveUiTheme.Divider, false);

        var contentHeight = ArchiveLayoutMetrics.InfoPanel.height - 112f;
        var basicHost = ArchiveUiFactory.CreateTopLeft("BasicHost", root, 16f, 88f, 376f, contentHeight);
        var basicScroll = ArchiveUiFactory.CreateVerticalScroll(
            "BasicScroll",
            basicHost,
            out var basicContent,
            Vector4.zero,
            true);
        var nameValue = CreateField(basicContent, WitchArchiveStrings.Name);
        var titleValue = CreateField(basicContent, WitchArchiveStrings.Title);
        CreateSubheading(basicContent, WitchArchiveStrings.Summary);
        var summaryValue = ArchiveUiFactory.CreateAutoHeightText(
            "Summary",
            basicContent,
            "",
            18,
            ArchiveUiTheme.TextPrimary,
            120f,
            1.2f);

        var backgroundHost = ArchiveUiFactory.CreateTopLeft("BackgroundHost", root, 16f, 88f, 376f, contentHeight);
        var backgroundScroll = ArchiveUiFactory.CreateVerticalScroll(
            "BackgroundScroll",
            backgroundHost,
            out var backgroundContent,
            Vector4.zero,
            true);
        var backgroundValue = ArchiveUiFactory.CreateAutoHeightText(
            "Background",
            backgroundContent,
            "",
            18,
            ArchiveUiTheme.TextPrimary,
            200f,
            1.35f);

        var view = root.gameObject.AddComponent<ArchiveInfoPanel>();
        view.sectionTitle = sectionTitle;
        view.nameValue = nameValue;
        view.titleValue = titleValue;
        view.summaryValue = summaryValue;
        view.backgroundValue = backgroundValue;
        view.basicScroll = basicScroll;
        view.backgroundScroll = backgroundScroll;
        view.SetSection(WitchArchiveSection.Basic);
        return view;
    }

    public void Bind(WitchArchiveDisplayEntry entry)
    {
        if (nameValue != null)
        {
            nameValue.text = entry.Name;
        }

        if (titleValue != null)
        {
            titleValue.text = entry.Title;
        }

        if (summaryValue != null)
        {
            summaryValue.text = entry.Summary;
        }

        if (backgroundValue != null)
        {
            backgroundValue.text = entry.Background;
        }

        ResetToTop(basicScroll);
        ResetToTop(backgroundScroll);
    }

    public void SetSection(WitchArchiveSection section)
    {
        var basic = section == WitchArchiveSection.Basic;
        basicScroll?.gameObject.SetActive(basic);
        backgroundScroll?.gameObject.SetActive(!basic);
        if (sectionTitle != null)
        {
            sectionTitle.text = basic ? WitchArchiveStrings.Basic : WitchArchiveStrings.Background;
        }

        ResetToTop(basic ? basicScroll : backgroundScroll);
    }

    private static void ResetToTop(ScrollRect? scroll)
    {
        ResetToTopImmediately(scroll);
        if (scroll != null)
        {
            TerriasFrameScheduler.RunOnceNextFrame(
                "WitchArchive.ScrollTop." + scroll.GetInstanceID(),
                () => ResetToTopImmediately(scroll));
        }
    }

    private static void ResetToTopImmediately(ScrollRect? scroll)
    {
        if (scroll == null || !scroll.gameObject.activeInHierarchy)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        if (scroll.content != null)
        {
            foreach (Transform child in scroll.content)
            {
                if (child is RectTransform childRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(childRect);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        }

        Canvas.ForceUpdateCanvases();
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }

    private static Text CreateField(Transform parent, string label)
    {
        var root = new GameObject("Field-" + label, typeof(RectTransform), typeof(VerticalLayoutGroup));
        root.transform.SetParent(parent, false);
        root.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
        var layout = root.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ArchiveUiFactory.CreateAutoHeightText(
            "Label",
            root.transform,
            label,
            14,
            ArchiveUiTheme.TextSecondary,
            20f);
        return ArchiveUiFactory.CreateAutoHeightText(
            "Value",
            root.transform,
            "",
            20,
            ArchiveUiTheme.TextPrimary,
            34f);
    }

    private static void CreateSubheading(Transform parent, string label)
    {
        var divider = new GameObject("Divider", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        divider.transform.SetParent(parent, false);
        var dividerElement = divider.GetComponent<LayoutElement>();
        dividerElement.minHeight = 2f;
        dividerElement.preferredHeight = 2f;
        divider.GetComponent<Image>().color = ArchiveUiTheme.Divider;
        ArchiveUiFactory.CreateAutoHeightText(
            "Heading-" + label,
            parent,
            label,
            16,
            ArchiveUiTheme.Accent,
            28f);
    }
}
