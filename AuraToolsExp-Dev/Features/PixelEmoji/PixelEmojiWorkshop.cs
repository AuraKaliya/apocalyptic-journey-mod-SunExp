using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.PixelEmoji;

internal enum PixelEmojiTool
{
    Pencil,
    Eraser,
    Fill,
    Eyedropper,
    ReferenceMove
}

public static class PixelEmojiWorkshop
{
    private const string OverlayName = "AuraToolsPixelEmojiWorkshop";

    public static void Show(Transform parent)
    {
        var window = AuraToolsUi.CreateOverlay(OverlayName, parent, "像素表情工坊 · 24×24", maxWidth: 1080f);
        var controller = window.AddComponent<PixelEmojiWorkshopController>();
        controller.Build(window.transform);
    }
}

internal sealed class PixelEmojiWorkshopController : MonoBehaviour
{
    private readonly List<PixelEmojiAnimationSnapshot> undo = new();
    private readonly List<PixelEmojiAnimationSnapshot> redo = new();
    private readonly List<GameObject> frameSlots = new();
    private readonly List<PixelEmojiFrameBorderGraphic> frameSlotBorders = new();
    private readonly List<Image> frameSlotPreviewMarkers = new();
    private readonly List<Texture2D> frameSlotTextures = new();
    private readonly List<Text> frameSlotNumbers = new();
    private readonly List<Texture2D> libraryPreviewTextures = new();
    private readonly List<Sprite> libraryPreviewSprites = new();
    private List<byte[]> frames = new() { PixelEmojiCodec.Blank() };
    private int selectedFrameIndex;
    private PixelEmojiPlaybackMode playbackMode = PixelEmojiPlaybackMode.Loop;
    private bool previewPlaying;
    private int previewFrameIndex;
    private float previewElapsed;
    private string currentId = "";
    private string currentName = "未命名表情";
    private string pendingDeleteId = "";
    private byte selectedColor = 1;
    private PixelEmojiTool tool = PixelEmojiTool.Pencil;
    private PixelEmojiCanvas? canvas;
    private InputField? nameInput;
    private Text? status;
    private Transform? libraryContent;
    private RectTransform? referenceViewportRect;
    private RectTransform? referenceRect;
    private RawImage? referenceImage;
    private Texture2D? referenceTexture;
    private Slider? referenceScaleSlider;
    private Slider? referenceOpacitySlider;
    private Button? referenceMoveButton;
    private Button? movePreviousButton;
    private Button? moveNextButton;
    private Button? previewButton;
    private Button? loopButton;
    private Button? onceButton;
    private GameObject? referenceDetails;
    private LayoutElement? referencePanelElement;
    private bool referenceControlsExpanded;
    private int referenceScalePercent = PixelEmojiReferencePolicy.DefaultScalePercent;
    private int referenceOpacityPercent = PixelEmojiReferencePolicy.DefaultOpacityPercent;
    private int referenceLoadSequence;
    private PixelEmojiWorkshopLayoutMetrics layoutMetrics;

    private byte[] pixels
    {
        get => frames[Mathf.Clamp(selectedFrameIndex, 0, frames.Count - 1)];
        set => frames[Mathf.Clamp(selectedFrameIndex, 0, frames.Count - 1)] = value;
    }

    private byte[] displayPixels => previewPlaying
        ? frames[Mathf.Clamp(previewFrameIndex, 0, frames.Count - 1)]
        : pixels;

    public void Build(Transform parent)
    {
        var body = AuraToolsUi.CreateScroll(parent, "PixelEmojiWorkshopBody");
        var settingsRow = HorizontalRow(
            body,
            "WorkshopSettings",
            AuraToolsUi.InlineRowHeight);
        AuraToolsUi.AddToggle(
            settingsRow.transform,
            AuraToolsConfigService.PixelEmoji.SyncRemote,
            value =>
            {
                AuraToolsConfigService.PixelEmoji.SyncRemote = value;
                AuraToolsConfigService.SavePixelEmoji();
            });
        AuraToolsUi.AddText(
            settingsRow.transform,
            "联机展示收藏表情",
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        Canvas.ForceUpdateCanvases();
        var availableWidth = (parent as RectTransform)?.rect.width ?? 0f;
        layoutMetrics = PixelEmojiWorkshopLayoutPolicy.Resolve(availableWidth);
        var workspace = AuraToolsUi.CreateLayout("Workspace", body);
        AuraToolsUi.SetFixedHeight(workspace, layoutMetrics.WorkspaceHeight);
        HorizontalOrVerticalLayoutGroup workspaceLayout;
        if (layoutMetrics.StackVertically)
        {
            workspaceLayout = workspace.AddComponent<VerticalLayoutGroup>();
        }
        else
        {
            workspaceLayout = workspace.AddComponent<HorizontalLayoutGroup>();
        }
        workspaceLayout.spacing = PixelEmojiWorkshopLayoutPolicy.ColumnGap;
        workspaceLayout.childControlHeight = true;
        workspaceLayout.childControlWidth = true;
        workspaceLayout.childForceExpandHeight = false;
        workspaceLayout.childForceExpandWidth = layoutMetrics.StackVertically;

        BuildCanvas(workspace.transform);
        BuildTools(workspace.transform);

        var libraryHeader = AuraToolsUi.CreateLayout("LibraryHeader", body);
        AuraToolsUi.SetFixedHeight(libraryHeader, AuraToolsUi.ToolbarHeight);
        var libraryHeaderLayout = libraryHeader.AddComponent<HorizontalLayoutGroup>();
        libraryHeaderLayout.spacing = 8f;
        libraryHeaderLayout.childControlHeight = true;
        libraryHeaderLayout.childControlWidth = true;
        libraryHeaderLayout.childForceExpandWidth = false;
        AuraToolsUi.AddText(libraryHeader.transform, "作品库", AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 46f, 1f);
        AddCompactButton(libraryHeader.transform, "新建", NewDocument, 72f);
        ToolboxIconButtonV2.Create(
            libraryHeader.transform,
            "action.folder",
            "打开表情作品目录",
            () => FileResourceUtil.OpenDirectory(PixelEmojiLibraryStore.DataDirectory),
            42f,
            "夹");

        libraryContent = AuraToolsUi.CreateFixedScroll(body, "PixelEmojiLibrary", 180f);
        RebuildLibrary();
        RefreshCanvas();
    }

    private void BuildCanvas(Transform parent)
    {
        var column = AuraToolsUi.CreateLayout("CanvasColumn", parent);
        AuraToolsUi.SetFixedSize(
            column,
            layoutMetrics.CanvasColumnWidth,
            layoutMetrics.ContentHeight);
        var columnLayout = column.AddComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 8f;
        columnLayout.childControlWidth = true;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandWidth = false;
        columnLayout.childForceExpandHeight = false;

        var holder = AuraToolsUi.CreateLayout("CanvasHolder", column.transform);
        AuraToolsUi.SetFixedSize(
            holder,
            layoutMetrics.CanvasColumnWidth,
            layoutMetrics.CanvasColumnWidth);
        ToolboxSurfaceV2.ApplyControl(holder).raycastTarget = false;

        var canvasObject = AuraToolsUi.CreateRect(
            "PixelCanvas",
            holder.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(layoutMetrics.CanvasSize, layoutMetrics.CanvasSize));
        referenceViewportRect = canvasObject.GetComponent<RectTransform>();
        canvasObject.AddComponent<RectMask2D>();
        var checkerObject = AuraToolsUi.CreateRect(
            "CheckerboardLayer",
            canvasObject.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var checkerRect = checkerObject.GetComponent<RectTransform>();
        checkerRect.offsetMin = Vector2.zero;
        checkerRect.offsetMax = Vector2.zero;
        var checker = checkerObject.AddComponent<PixelEmojiCheckerboardGraphic>();
        checker.LogicalCellCount = PixelEmojiCodec.SourceSize;
        checker.raycastTarget = false;

        var referenceObject = AuraToolsUi.CreateRect(
            "ReferenceLayer",
            canvasObject.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero);
        referenceRect = referenceObject.GetComponent<RectTransform>();
        referenceImage = referenceObject.AddComponent<RawImage>();
        referenceImage.color = ReferenceTint();
        referenceImage.raycastTarget = false;
        referenceImage.enabled = false;

        var paintObject = AuraToolsUi.CreateRect(
            "PaintLayer",
            canvasObject.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var paintRect = paintObject.GetComponent<RectTransform>();
        paintRect.offsetMin = Vector2.zero;
        paintRect.offsetMax = Vector2.zero;
        var raw = paintObject.AddComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = true;
        canvas = paintObject.AddComponent<PixelEmojiCanvas>();
        canvas.Initialize(
            raw,
            () => displayPixels,
            () => tool,
            () => selectedColor,
            () => !previewPlaying,
            BeginMutation,
            SetSelectedColor,
            RefreshCanvas,
            MoveReference);

        var gridObject = AuraToolsUi.CreateRect("Grid", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        gridObject.GetComponent<RectTransform>().offsetMin = Vector2.zero;
        gridObject.GetComponent<RectTransform>().offsetMax = Vector2.zero;
        var grid = gridObject.AddComponent<PixelEmojiGridGraphic>();
        grid.raycastTarget = false;

        BuildAnimationPanel(column.transform);
    }

    private void BuildAnimationPanel(Transform parent)
    {
        var panel = AuraToolsUi.CreateLayout("AnimationFrames", parent);
        AuraToolsUi.SetFixedSize(
            panel,
            layoutMetrics.CanvasColumnWidth,
            layoutMetrics.AnimationPanelHeight);
        ToolboxSurfaceV2.ApplyControl(panel).raycastTarget = false;
        var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(6, 6, 6, 6);
        panelLayout.spacing = 4f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = false;
        panelLayout.childForceExpandHeight = false;

        var strip = HorizontalRow(panel.transform, "FrameStrip", 64f);
        var stripLayout = strip.GetComponent<HorizontalLayoutGroup>();
        stripLayout.padding = new RectOffset(0, 0, 0, 0);
        stripLayout.spacing = 4f;
        for (var index = 0; index < PixelEmojiAnimationCodec.MaximumFrames; index++)
        {
            AddFrameSlot(strip.transform, index);
        }

        var controls = HorizontalRow(panel.transform, "FrameControls", 44f);
        var controlsLayout = controls.GetComponent<HorizontalLayoutGroup>();
        controlsLayout.padding = new RectOffset(0, 0, 0, 0);
        controlsLayout.spacing = 4f;
        AddCompactButton(controls.transform, "+空", AddBlankFrame, 44f, 42f);
        AddCompactButton(controls.transform, "+继", DuplicateFrame, 44f, 42f);
        AddCompactButton(controls.transform, "删除", DeleteSelectedFrame, 52f, 42f);
        movePreviousButton = AddCompactButton(controls.transform, "←", () => MoveSelectedFrame(-1), 44f, 42f);
        moveNextButton = AddCompactButton(controls.transform, "→", () => MoveSelectedFrame(1), 44f, 42f);

        var playback = HorizontalRow(panel.transform, "PlaybackControls", 44f);
        var playbackLayout = playback.GetComponent<HorizontalLayoutGroup>();
        playbackLayout.padding = new RectOffset(0, 0, 0, 0);
        playbackLayout.spacing = 4f;
        previewButton = AddCompactButton(playback.transform, "播放", TogglePreview, 64f, 42f);
        loopButton = AddCompactButton(playback.transform, "循环", () => SetPlaybackMode(PixelEmojiPlaybackMode.Loop), 64f, 42f);
        onceButton = AddCompactButton(playback.transform, "单次", () => SetPlaybackMode(PixelEmojiPlaybackMode.Once), 64f, 42f);
        RefreshAnimationPanel(true);
    }

    private void AddFrameSlot(Transform parent, int frameIndex)
    {
        var slot = AuraToolsUi.CreateLayout("Frame-" + (frameIndex + 1), parent);
        AuraToolsUi.SetFixedSize(slot, layoutMetrics.FrameSlotWidth, 60f);
        var border = slot.AddComponent<PixelEmojiFrameBorderGraphic>();
        border.raycastTarget = false;

        var button = slot.AddComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => SelectFrame(frameIndex));

        var art = AuraToolsUi.CreateRect(
            "Art",
            slot.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(layoutMetrics.FrameArtSize, layoutMetrics.FrameArtSize));
        var artRect = art.GetComponent<RectTransform>();
        artRect.anchoredPosition = new Vector2(0f, -4f);
        var checker = art.AddComponent<PixelEmojiCheckerboardGraphic>();
        checker.TileSize = 6f;
        checker.raycastTarget = false;
        var imageObject = AuraToolsUi.CreateRect("Image", art.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var raw = imageObject.AddComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = false;
        var texture = new Texture2D(PixelEmojiCodec.SourceSize, PixelEmojiCodec.SourceSize, TextureFormat.RGBA32, false)
        {
            name = "AuraToolsPixelEmojiFrameThumbnail-" + (frameIndex + 1),
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        raw.texture = texture;

        var numberObject = AuraToolsUi.CreateRect(
            "Number",
            slot.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 14f));
        numberObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 2f);
        var number = AuraToolsUi.AddFillText(
            numberObject.transform,
            (frameIndex + 1).ToString(),
            AuraToolsUi.HintFontSize - 2,
            TextAnchor.MiddleCenter,
            AuraToolsUi.Text);
        number.raycastTarget = false;

        var previewMarkerObject = AuraToolsUi.CreateRect(
            "PreviewMarker",
            slot.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(-8f, 2f));
        previewMarkerObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 16f);
        var previewMarker = AuraToolsUi.AddImage(previewMarkerObject, AuraToolsUi.SuccessText);
        previewMarker.raycastTarget = false;
        previewMarker.enabled = false;

        var interactionObject = AuraToolsUi.CreateRect(
            "Interaction",
            slot.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        var interaction = AuraToolsUi.AddImage(interactionObject, Color.clear);
        interaction.raycastTarget = true;
        AuraUiButtonFeedback.Apply(
            button,
            interaction,
            Color.clear,
            new Color(1f, 1f, 1f, 0.08f),
            new Color(0f, 0f, 0f, 0.16f),
            new Color(0f, 0f, 0f, 0.12f));

        frameSlots.Add(slot);
        frameSlotBorders.Add(border);
        frameSlotPreviewMarkers.Add(previewMarker);
        frameSlotTextures.Add(texture);
        frameSlotNumbers.Add(number);
    }

    private void BuildTools(Transform parent)
    {
        var tools = AuraToolsUi.CreateLayout("Tools", parent);
        var toolsElement = AuraToolsUi.EnsureLayoutElement(tools);
        toolsElement.minWidth = layoutMetrics.ToolsMinimumWidth;
        toolsElement.preferredWidth = layoutMetrics.ToolsPreferredWidth;
        toolsElement.flexibleWidth = 1f;
        toolsElement.minHeight = layoutMetrics.ContentHeight;
        toolsElement.preferredHeight = layoutMetrics.ContentHeight;
        ToolboxSurfaceV2.Apply(tools).raycastTarget = false;
        var layout = tools.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AuraToolsUi.AddText(tools.transform, "有限色板 · 32 色（左上角透明）", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 32f);
        var palette = AuraToolsUi.CreateLayout("Palette", tools.transform);
        AuraToolsUi.SetFixedHeight(palette, layoutMetrics.PaletteHeight);
        var paletteGrid = palette.AddComponent<GridLayoutGroup>();
        paletteGrid.cellSize = new Vector2(
            layoutMetrics.PaletteCellWidth,
            layoutMetrics.PaletteCellHeight);
        paletteGrid.spacing = new Vector2(
            layoutMetrics.PaletteSpacing,
            layoutMetrics.PaletteSpacing);
        paletteGrid.childAlignment = TextAnchor.UpperCenter;
        paletteGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        paletteGrid.constraintCount = 8;
        for (byte index = 0; index < PixelEmojiCodec.PaletteRgba.Length; index++)
        {
            AddPaletteButton(palette.transform, index);
        }

        var toolRow = HorizontalRow(tools.transform, "ToolRow", 46f);
        var toolLayout = toolRow.GetComponent<HorizontalLayoutGroup>();
        toolLayout.padding = new RectOffset(0, 0, 0, 0);
        toolLayout.spacing = 6f;
        AddToolButton(toolRow.transform, "画笔", PixelEmojiTool.Pencil);
        AddToolButton(toolRow.transform, "橡皮", PixelEmojiTool.Eraser);
        AddToolButton(toolRow.transform, "填充", PixelEmojiTool.Fill);
        AddToolButton(toolRow.transform, "吸色", PixelEmojiTool.Eyedropper);

        var editRow = HorizontalRow(tools.transform, "EditRow", 42f);
        var editLayout = editRow.GetComponent<HorizontalLayoutGroup>();
        editLayout.padding = new RectOffset(0, 0, 0, 0);
        editLayout.spacing = 6f;
        AuraToolsUi.AddText(editRow.transform, "编辑", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 40f, 1f);
        ToolboxIconButtonV2.Create(editRow.transform, "history.undo", "撤销", Undo, 42f, "↶");
        ToolboxIconButtonV2.Create(editRow.transform, "history.redo", "重做", Redo, 42f, "↷");
        ToolboxIconButtonV2.Create(editRow.transform, "action.clear", "清空当前帧", Clear, 42f, "清");

        var referencePanel = AuraToolsUi.CreateLayout("ReferencePanel", tools.transform);
        referencePanelElement = AuraToolsUi.SetFixedHeight(referencePanel, 46f);
        ToolboxSurfaceV2.ApplyControl(referencePanel).raycastTarget = false;
        var referenceLayout = referencePanel.AddComponent<VerticalLayoutGroup>();
        referenceLayout.padding = new RectOffset(4, 4, 2, 2);
        referenceLayout.spacing = 4f;
        referenceLayout.childControlWidth = true;
        referenceLayout.childControlHeight = true;
        referenceLayout.childForceExpandWidth = true;
        referenceLayout.childForceExpandHeight = false;

        var referenceHeader = HorizontalRow(referencePanel.transform, "ReferenceHeader", 42f);
        var referenceHeaderLayout = referenceHeader.GetComponent<HorizontalLayoutGroup>();
        referenceHeaderLayout.padding = new RectOffset(4, 0, 0, 0);
        AuraToolsUi.AddText(referenceHeader.transform, "参考叠图", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 1f);
        AddCompactButton(referenceHeader.transform, "导入", ImportReference, 64f, 40f);
        ToolboxIconButtonV2.Create(
            referenceHeader.transform,
            "reference.expand",
            "展开或收起参考图控制",
            () => SetReferenceControlsExpanded(!referenceControlsExpanded),
            40f,
            "⌄");

        referenceDetails = AuraToolsUi.CreateLayout("ReferenceDetails", referencePanel.transform);
        AuraToolsUi.SetFixedHeight(referenceDetails, 104f);
        var detailsLayout = referenceDetails.AddComponent<VerticalLayoutGroup>();
        detailsLayout.spacing = 3f;
        detailsLayout.childControlWidth = true;
        detailsLayout.childControlHeight = true;
        detailsLayout.childForceExpandWidth = true;
        detailsLayout.childForceExpandHeight = false;
        var referenceActions = HorizontalRow(referenceDetails.transform, "ReferenceActions", 34f);
        var referenceActionLayout = referenceActions.GetComponent<HorizontalLayoutGroup>();
        referenceActionLayout.padding = new RectOffset(0, 0, 0, 0);
        referenceMoveButton = AddCompactButton(referenceActions.transform, "移动参考图", ToggleReferenceMove, 92f, 34f);
        AddCompactButton(referenceActions.transform, "居中", CenterReference, 64f, 34f);
        AddCompactButton(referenceActions.transform, "移除", RemoveReference, 64f, 34f);

        referenceScaleSlider = AddRangeSlider(
            referenceDetails.transform,
            "缩放",
            PixelEmojiReferencePolicy.MinimumScalePercent,
            PixelEmojiReferencePolicy.MaximumScalePercent,
            referenceScalePercent,
            value => SetReferenceScale((int)value),
            "%");
        referenceOpacitySlider = AddRangeSlider(
            referenceDetails.transform,
            "透明度",
            PixelEmojiReferencePolicy.MinimumOpacityPercent,
            PixelEmojiReferencePolicy.MaximumOpacityPercent,
            referenceOpacityPercent,
            value => SetReferenceOpacity((int)value),
            "%");

        var actionSpacer = AuraToolsUi.CreateLayout("PrimaryActionSpacer", tools.transform);
        var actionSpacerElement = AuraToolsUi.EnsureLayoutElement(actionSpacer);
        actionSpacerElement.minHeight = 0f;
        actionSpacerElement.preferredHeight = 0f;
        actionSpacerElement.flexibleHeight = 1f;

        var nameRow = HorizontalRow(tools.transform, "Name", 46f);
        var nameLayout = nameRow.GetComponent<HorizontalLayoutGroup>();
        nameLayout.padding = new RectOffset(0, 0, 0, 0);
        nameInput = AuraToolsUi.AddInput(nameRow.transform, currentName, value => currentName = NormalizeName(value), 100f, 42f);
        var nameElement = AuraToolsUi.EnsureLayoutElement(nameInput.gameObject);
        nameElement.minWidth = 100f;
        nameElement.preferredWidth = 100f;
        nameElement.minHeight = 42f;
        nameElement.preferredHeight = 42f;
        nameElement.flexibleWidth = 1f;
        ToolboxSurfaceV2.ApplyControl(nameInput.gameObject);
        var namePlaceholder = nameInput.placeholder as Text;
        if (namePlaceholder != null) namePlaceholder.text = "作品名称";
        AddCompactButton(nameRow.transform, "仅保存", () => Save(false), 64f, 42f);
        AddCompactButton(nameRow.transform, "保存收藏", () => Save(true), 104f, 42f);

        status = AuraToolsUi.AddText(tools.transform, "", AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.SuccessText, 36f);
        SetReferenceControlsExpanded(false);
        RefreshStatus("画笔 · 色板 #01");
    }

    private void AddToolButton(Transform parent, string label, PixelEmojiTool value)
    {
        AddCompactButton(parent, label, () =>
        {
            SetTool(value, label + " · 色板 #" + selectedColor.ToString("00"));
        }, 68f, 42f);
    }

    private void AddPaletteButton(Transform parent, byte index)
    {
        var packed = PixelEmojiCodec.PaletteRgba[index];
        var color = new Color32((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        var buttonObject = AuraToolsUi.CreateLayout("Palette-" + index, parent);
        AuraToolsUi.SetFixedSize(
            buttonObject,
            layoutMetrics.PaletteCellWidth,
            layoutMetrics.PaletteCellHeight);
        var image = AuraToolsUi.AddImage(buttonObject, index == 0 ? new Color(0.42f, 0.42f, 0.45f, 1f) : color);
        var button = buttonObject.AddComponent<Button>();
        AuraUiButtonFeedback.Apply(button, image, AuraToolsUi.Accent);
        button.onClick.AddListener(() => SetSelectedColor(index));
        if (index == 0)
        {
            AuraToolsUi.AddFillText(buttonObject.transform, "×", 22, TextAnchor.MiddleCenter, Color.white);
        }
    }

    private Slider AddRangeSlider(
        Transform parent,
        string label,
        float minimum,
        float maximum,
        float initial,
        Action<float> changed,
        string suffix)
    {
        var row = HorizontalRow(parent, label + "Row", 32f);
        var compactLayout = row.GetComponent<HorizontalLayoutGroup>();
        compactLayout.padding = new RectOffset(0, 0, 0, 0);
        compactLayout.childAlignment = TextAnchor.MiddleLeft;
        AddCompactText(row.transform, label, 52f, 32f, TextAnchor.MiddleLeft, AuraToolsUi.Text);

        var sliderObject = AuraToolsUi.CreateLayout(label + "Slider", row.transform);
        var sliderElement = AuraToolsUi.SetFixedHeight(sliderObject, 24f);
        sliderElement.minWidth = 116f;
        sliderElement.preferredWidth = 180f;
        sliderElement.flexibleWidth = 1f;
        ToolboxSurfaceV2.ApplyControl(sliderObject).raycastTarget = true;

        var trackObject = AuraToolsUi.CreateRect(
            "Track",
            sliderObject.transform,
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-20f, 4f));
        AuraToolsUi.AddImage(trackObject, new Color(0.18f, 0.17f, 0.23f, 1f)).raycastTarget = false;

        var fillArea = AuraToolsUi.CreateRect(
            "FillArea",
            sliderObject.transform,
            new Vector2(0f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-20f, 4f));
        var fillObject = AuraToolsUi.CreateRect("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var fillImage = AuraToolsUi.AddImage(fillObject, AuraToolsUi.Accent);
        fillImage.raycastTarget = false;

        var handleArea = AuraToolsUi.CreateRect(
            "HandleArea",
            sliderObject.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            new Vector2(-20f, 0f));
        var handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);
        var handleObject = AuraToolsUi.CreateRect(
            "Handle",
            handleArea.transform,
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(16f, 18f));
        var handleImage = AuraToolsUi.AddButtonImage(handleObject, new Color(0.24f, 0.20f, 0.31f, 1f));

        var valueText = AddCompactText(row.transform, "", 48f, 32f, TextAnchor.MiddleCenter, AuraToolsUi.Accent);
        var slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = true;
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillObject.GetComponent<RectTransform>();
        slider.handleRect = handleObject.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.onValueChanged.AddListener(value =>
        {
            valueText.text = Mathf.RoundToInt(value) + suffix;
            changed(value);
        });
        slider.value = Mathf.Clamp(initial, minimum, maximum);
        valueText.text = Mathf.RoundToInt(slider.value) + suffix;
        return slider;
    }

    private static Text AddCompactText(
        Transform parent,
        string value,
        float width,
        float height,
        TextAnchor anchor,
        Color color)
    {
        var container = AuraToolsUi.CreateLayout("CompactText", parent);
        AuraToolsUi.SetFixedSize(container, width, height);
        return AuraToolsUi.AddFillText(container.transform, value, AuraToolsUi.BodyFontSize, anchor, color);
    }

    private static Button AddCompactButton(
        Transform parent,
        string label,
        Action action,
        float width,
        float height = AuraToolsUi.ButtonHeight)
    {
        var button = AuraToolsUi.AddButton(parent, label, action, width, height);
        AuraToolsUi.SetFixedSize(button.gameObject, width, height);
        ToolboxSurfaceV2.ApplyControl(button.gameObject);
        return button;
    }

    private void SetReferenceControlsExpanded(bool expanded)
    {
        referenceControlsExpanded = expanded;
        if (referenceDetails != null)
        {
            referenceDetails.SetActive(expanded);
        }
        if (referencePanelElement != null)
        {
            referencePanelElement.minHeight = expanded ? 154f : 46f;
            referencePanelElement.preferredHeight = expanded ? 154f : 46f;
        }
        if (referencePanelElement?.transform.parent is RectTransform layoutRoot)
        {
            LayoutRebuilder.MarkLayoutForRebuild(layoutRoot);
        }
    }

    private void ImportReference()
    {
        RefreshStatus("正在打开参考图片选择器……");
        var initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var sequence = ++referenceLoadSequence;
        OptionalFileDialog.PickImageFileAsync(initialDirectory, result =>
        {
            if (this == null || sequence != referenceLoadSequence)
            {
                return;
            }
            if (result.Status == OptionalFileDialogStatus.Cancelled)
            {
                RefreshStatus("已取消导入参考图");
                return;
            }
            if (!result.Selected)
            {
                RefreshStatus("无法打开参考图片：" + result.Message, true);
                return;
            }

            LoadReference(result.Path, sequence);
        });
    }

    private void LoadReference(string path, int sequence)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > PixelEmojiReferencePolicy.MaximumSourceBytes)
            {
                RefreshStatus("参考图片不存在、为空或超过 32 MB。", true);
                return;
            }
            StartCoroutine(LoadReferenceCoroutine(file.FullName, file.Length, sequence));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[PixelEmoji] reference image load failed: " + ex.Message);
            RefreshStatus("参考图片读取失败：" + ex.Message, true);
        }
    }

    private IEnumerator LoadReferenceCoroutine(string path, long sourceBytes, int sequence)
    {
        RefreshStatus("正在读取参考图片……");
        UnityWebRequest? request = null;
        try
        {
            request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri, true);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[PixelEmoji] reference request creation failed: " + ex.Message);
            RefreshStatus("无法读取参考图片：" + ex.Message, true);
            yield break;
        }

        using (request)
        {
            yield return request.SendWebRequest();
            if (this == null || sequence != referenceLoadSequence)
            {
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                RefreshStatus("参考图片读取失败：" + request.error, true);
                yield break;
            }

            var loaded = DownloadHandlerTexture.GetContent(request);
            if (loaded == null
                || !PixelEmojiReferencePolicy.IsSupportedSource(sourceBytes, loaded.width, loaded.height))
            {
                if (loaded != null) Object.Destroy(loaded);
                RefreshStatus("参考图片无法读取，或尺寸超过 8192×8192。", true);
                yield break;
            }

            loaded.name = "AuraToolsPixelEmojiReference";
            loaded.filterMode = PixelEmojiReferencePolicy.ShouldUsePointFiltering(loaded.width, loaded.height)
                ? FilterMode.Point
                : FilterMode.Bilinear;
            loaded.wrapMode = TextureWrapMode.Clamp;
            ApplyReferenceTexture(loaded);
        }
    }

    private void ApplyReferenceTexture(Texture2D loaded)
    {
        DestroyReferenceTexture();
        referenceTexture = loaded;
        if (referenceImage != null)
        {
            referenceImage.texture = referenceTexture;
            referenceImage.color = ReferenceTint();
            referenceImage.enabled = true;
        }

        referenceScalePercent = PixelEmojiReferencePolicy.DefaultScalePercent;
        referenceOpacityPercent = PixelEmojiReferencePolicy.DefaultOpacityPercent;
        if (referenceScaleSlider != null) referenceScaleSlider.value = referenceScalePercent;
        if (referenceOpacitySlider != null) referenceOpacitySlider.value = referenceOpacityPercent;
        SetReferenceControlsExpanded(true);
        UseReferenceLogicalSize();
        CenterReference(false);
        SetTool(PixelEmojiTool.ReferenceMove, "参考图已导入；100%=1源像素/1画布格，可拖动定位");
    }

    private void ToggleReferenceMove()
    {
        if (referenceTexture == null)
        {
            RefreshStatus("请先导入参考图。", true);
            return;
        }

        SetTool(
            tool == PixelEmojiTool.ReferenceMove ? PixelEmojiTool.Pencil : PixelEmojiTool.ReferenceMove,
            tool == PixelEmojiTool.ReferenceMove ? "已停止移动参考图" : "移动参考图：在画布内拖动定位");
    }

    private void SetReferenceScale(int value)
    {
        referenceScalePercent = PixelEmojiReferencePolicy.ClampScalePercent(value);
        if (referenceRect != null)
        {
            var scale = referenceScalePercent / 100f;
            referenceRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void SetReferenceOpacity(int value)
    {
        referenceOpacityPercent = PixelEmojiReferencePolicy.ClampOpacityPercent(value);
        if (referenceImage != null)
        {
            referenceImage.color = ReferenceTint();
        }
    }

    private void MoveReference(Vector2 localDelta)
    {
        if (tool == PixelEmojiTool.ReferenceMove && referenceTexture != null && referenceRect != null)
        {
            referenceRect.anchoredPosition += localDelta;
        }
    }

    private void CenterReference()
    {
        CenterReference(true);
    }

    private void CenterReference(bool showStatus)
    {
        if (referenceTexture == null || referenceRect == null)
        {
            if (showStatus) RefreshStatus("当前没有参考图。", true);
            return;
        }

        referenceRect.anchoredPosition = Vector2.zero;
        if (showStatus) RefreshStatus("参考图已居中");
    }

    private void RemoveReference()
    {
        referenceLoadSequence++;
        if (referenceTexture == null)
        {
            RefreshStatus("当前没有参考图；未完成的导入已取消。", true);
            return;
        }

        DestroyReferenceTexture();
        if (referenceImage != null)
        {
            referenceImage.texture = null;
            referenceImage.enabled = false;
        }
        if (referenceRect != null)
        {
            referenceRect.sizeDelta = Vector2.zero;
            referenceRect.anchoredPosition = Vector2.zero;
            referenceRect.localScale = Vector3.one;
        }
        SetTool(PixelEmojiTool.Pencil, "参考图已移除");
    }

    private void UseReferenceLogicalSize()
    {
        if (referenceTexture == null || referenceRect == null || referenceViewportRect == null)
        {
            return;
        }

        var viewport = referenceViewportRect.rect;
        PixelEmojiReferencePolicy.MapToLogicalCanvas(
            referenceTexture.width,
            referenceTexture.height,
            viewport.width,
            viewport.height,
            PixelEmojiCodec.SourceSize,
            out var width,
            out var height);
        referenceRect.sizeDelta = new Vector2(width, height);
        SetReferenceScale(referenceScalePercent);
    }

    private Color ReferenceTint()
    {
        return new Color(1f, 1f, 1f, referenceOpacityPercent / 100f);
    }

    private void SetTool(PixelEmojiTool value, string message)
    {
        StopPreview(true);
        tool = value;
        if (referenceMoveButton != null)
        {
            var label = referenceMoveButton.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = tool == PixelEmojiTool.ReferenceMove ? "停止移动" : "移动参考图";
            }
        }
        RefreshStatus(message);
    }

    private void DestroyReferenceTexture()
    {
        if (referenceTexture != null)
        {
            Object.Destroy(referenceTexture);
            referenceTexture = null;
        }
    }

    private void RebuildLibrary()
    {
        if (libraryContent == null)
        {
            return;
        }
        DestroyLibraryPreviews();
        for (var index = libraryContent.childCount - 1; index >= 0; index--)
        {
            var child = libraryContent.GetChild(index).gameObject;
            UiRaycastSafeDestroyRuntime.DisableAndHide(child, "PixelEmoji library rebuild");
            Object.Destroy(child);
        }

        var items = PixelEmojiLibraryStore.GetItems();
        if (items.Count == 0)
        {
            AuraToolsUi.AddText(libraryContent, "暂无作品。先在上方画布创作，然后选择保存。", AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 54f);
            return;
        }

        foreach (var item in items)
        {
            if (!item.TryReadFrames(out var itemFrames))
            {
                continue;
            }
            var row = HorizontalRow(libraryContent, "Item-" + item.Id, 78f);
            AuraToolsUi.AddPanelImage(row, AuraToolsUi.Row);
            var previewObject = AuraToolsUi.CreateLayout("Preview", row.transform);
            AuraToolsUi.SetFixedSize(previewObject, 66f, 66f);
            var previewBackground = previewObject.AddComponent<PixelEmojiCheckerboardGraphic>();
            previewBackground.TileSize = 8f;
            previewBackground.raycastTarget = false;
            var previewImageObject = AuraToolsUi.CreateRect(
                "Artwork",
                previewObject.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            var previewRect = previewImageObject.GetComponent<RectTransform>();
            previewRect.offsetMin = Vector2.zero;
            previewRect.offsetMax = Vector2.zero;
            var preview = AuraToolsUi.AddImage(previewImageObject, Color.white);
            var previewTexture = CreateSourceTexture("Library-" + item.Id, itemFrames[0]);
            var previewSprite = Sprite.Create(
                previewTexture,
                new Rect(0f, 0f, PixelEmojiCodec.SourceSize, PixelEmojiCodec.SourceSize),
                new Vector2(0.5f, 0.5f),
                PixelEmojiCodec.SourceSize);
            previewSprite.name = previewTexture.name;
            libraryPreviewTextures.Add(previewTexture);
            libraryPreviewSprites.Add(previewSprite);
            preview.sprite = previewSprite;
            preview.color = Color.white;
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            var playbackLabel = item.PlaybackMode == PixelEmojiPlaybackMode.Loop ? "循环" : "单次";
            AuraToolsUi.AddText(
                row.transform,
                item.Name + " · " + itemFrames.Count + "帧 · " + playbackLabel,
                AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.Text,
                66f,
                1f);
            var favorite = AuraToolsConfigService.PixelEmoji.IsFavorite(item.Id);
            AuraToolsUi.AddButton(row.transform, favorite ? "取消收藏" : "收藏", () => ToggleFavorite(item.Id, !favorite), 104f);
            AuraToolsUi.AddButton(row.transform, "编辑", () => LoadDocument(item), 76f);
            AuraToolsUi.AddButton(row.transform, pendingDeleteId == item.Id ? "确认删除" : "删除", () => DeleteDocument(item.Id), 94f);
        }
    }

    private void NewDocument()
    {
        StopPreview(false);
        currentId = "";
        currentName = "未命名表情";
        frames = new List<byte[]> { PixelEmojiCodec.Blank() };
        selectedFrameIndex = 0;
        playbackMode = PixelEmojiPlaybackMode.Loop;
        undo.Clear();
        redo.Clear();
        pendingDeleteId = "";
        if (nameInput != null) nameInput.text = currentName;
        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus("已新建空白作品");
    }

    private void LoadDocument(PixelEmojiDocument item)
    {
        if (!item.TryReadFrames(out var loadedFrames))
        {
            RefreshStatus("作品数据损坏，无法编辑", true);
            return;
        }
        StopPreview(false);
        currentId = item.Id;
        currentName = item.Name;
        frames = PixelEmojiAnimationCodec.CloneFrames(loadedFrames);
        selectedFrameIndex = 0;
        playbackMode = item.PlaybackMode;
        undo.Clear();
        redo.Clear();
        pendingDeleteId = "";
        if (nameInput != null) nameInput.text = currentName;
        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus("正在编辑：" + currentName + "（" + frames.Count + "帧）");
    }

    private void Save(bool favorite)
    {
        StopPreview(true);
        currentName = NormalizeName(nameInput?.text ?? currentName);
        if (!PixelEmojiLibraryStore.Save(currentId, currentName, frames, playbackMode, out var saved, out var error))
        {
            RefreshStatus(error, true);
            return;
        }
        currentId = saved.Id;
        currentName = saved.Name;
        var exportError = "";
        try
        {
            var pngFrames = PixelEmojiAssetCache.EncodePngSequence(frames);
            if (!PixelEmojiLibraryStore.WriteRenderedSequence(saved.Id, saved.Name, pngFrames, out exportError))
            {
                AuraToolsLog.Warn("[PixelEmoji] rendered sequence write failed: " + exportError);
            }
        }
        catch (Exception ex)
        {
            exportError = ex.Message;
            AuraToolsLog.Warn("[PixelEmoji] rendered sequence encode failed: " + ex.Message);
        }

        if (favorite && !AuraToolsPixelEmojiRuntime.SetFavorite(saved.Id, true, out error))
        {
            RefreshStatus("作品已保存，但收藏失败：" + error, true);
        }
        else if (exportError.Length > 0)
        {
            RefreshStatus("作品已保存，但PNG序列导出失败：" + exportError, true);
        }
        else
        {
            RefreshStatus(
                (favorite ? "已保存并加入冒险表情列表尾部" : "作品已保存（未收藏）")
                + "；已导出 " + frames.Count + " 帧PNG");
        }
        RebuildLibrary();
    }

    private void ToggleFavorite(string itemId, bool favorite)
    {
        pendingDeleteId = "";
        if (!AuraToolsPixelEmojiRuntime.SetFavorite(itemId, favorite, out var error))
        {
            RefreshStatus(error, true);
        }
        else
        {
            RefreshStatus(favorite ? "已收藏，下次打开冒险表情列表时生效" : "已取消收藏");
        }
        RebuildLibrary();
    }

    private void DeleteDocument(string itemId)
    {
        if (!string.Equals(pendingDeleteId, itemId, StringComparison.OrdinalIgnoreCase))
        {
            pendingDeleteId = itemId;
            RefreshStatus("再次点击“确认删除”将永久删除该作品", true);
            RebuildLibrary();
            return;
        }

        pendingDeleteId = "";
        if (!AuraToolsPixelEmojiRuntime.Delete(itemId, out var error))
        {
            RefreshStatus(error, true);
            return;
        }
        if (string.Equals(currentId, itemId, StringComparison.OrdinalIgnoreCase))
        {
            NewDocument();
        }
        RefreshStatus("作品已删除");
        RebuildLibrary();
    }

    private void BeginMutation()
    {
        StopPreview(true);
        undo.Add(CaptureSnapshot());
        if (undo.Count > 64) undo.RemoveAt(0);
        redo.Clear();
    }

    private void Undo()
    {
        if (undo.Count == 0) return;
        StopPreview(false);
        redo.Add(CaptureSnapshot());
        RestoreSnapshot(undo[undo.Count - 1]);
        undo.RemoveAt(undo.Count - 1);
        RefreshCanvas();
        RefreshAnimationPanel(true);
    }

    private void Redo()
    {
        if (redo.Count == 0) return;
        StopPreview(false);
        undo.Add(CaptureSnapshot());
        RestoreSnapshot(redo[redo.Count - 1]);
        redo.RemoveAt(redo.Count - 1);
        RefreshCanvas();
        RefreshAnimationPanel(true);
    }

    private void Clear()
    {
        if (pixels.All(value => value == 0)) return;
        BeginMutation();
        pixels = PixelEmojiCodec.Blank();
        RefreshCanvas();
        RefreshStatus("画布已清空，可撤销");
    }

    private void AddBlankFrame()
    {
        if (frames.Count >= PixelEmojiAnimationCodec.MaximumFrames)
        {
            RefreshStatus("动画帧已达到8帧上限。", true);
            return;
        }

        BeginMutation();
        selectedFrameIndex++;
        frames.Insert(selectedFrameIndex, PixelEmojiCodec.Blank());
        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus("已添加空白帧 " + (selectedFrameIndex + 1) + "/" + frames.Count);
    }

    private void DuplicateFrame()
    {
        if (frames.Count >= PixelEmojiAnimationCodec.MaximumFrames)
        {
            RefreshStatus("动画帧已达到8帧上限。", true);
            return;
        }

        BeginMutation();
        var inherited = (byte[])pixels.Clone();
        selectedFrameIndex++;
        frames.Insert(selectedFrameIndex, inherited);
        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus("已继承上一帧为第 " + (selectedFrameIndex + 1) + " 帧");
    }

    private void DeleteSelectedFrame()
    {
        if (frames.Count <= PixelEmojiAnimationCodec.MinimumFrames)
        {
            RefreshStatus("作品必须至少保留1帧。", true);
            return;
        }

        BeginMutation();
        frames.RemoveAt(selectedFrameIndex);
        selectedFrameIndex = Mathf.Clamp(selectedFrameIndex, 0, frames.Count - 1);
        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus("已删除所选帧，可撤销");
    }

    private void MoveSelectedFrame(int direction)
    {
        if (!PixelEmojiAnimationCodec.CanSwapAdjacent(frames, selectedFrameIndex, direction))
        {
            RefreshStatus("所选帧已经位于边界。", true);
            return;
        }

        var previousIndex = selectedFrameIndex;
        BeginMutation();
        if (!PixelEmojiAnimationCodec.TrySwapAdjacent(
                frames,
                selectedFrameIndex,
                direction,
                out selectedFrameIndex))
        {
            undo.RemoveAt(undo.Count - 1);
            RefreshStatus("帧交换失败，作品顺序未改变。", true);
            return;
        }

        RefreshCanvas();
        RefreshAnimationPanel(true);
        RefreshStatus(
            "已交换第 " + (previousIndex + 1) + " 帧与第 " + (selectedFrameIndex + 1) + " 帧");
    }

    private void SelectFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= frames.Count)
        {
            return;
        }

        StopPreview(false);
        selectedFrameIndex = frameIndex;
        RefreshCanvas();
        RefreshAnimationPanel(false);
        RefreshStatus("正在编辑第 " + (selectedFrameIndex + 1) + "/" + frames.Count + " 帧");
    }

    private void SetPlaybackMode(PixelEmojiPlaybackMode mode)
    {
        if (playbackMode == mode || !PixelEmojiAnimationCodec.IsValidPlaybackMode(mode))
        {
            return;
        }

        BeginMutation();
        playbackMode = mode;
        RefreshAnimationPanel(false);
        RefreshStatus(mode == PixelEmojiPlaybackMode.Loop ? "动画将循环播放" : "动画将单次播放并停在末帧");
    }

    private void TogglePreview()
    {
        if (previewPlaying)
        {
            StopPreview(true);
            RefreshStatus("动画预览已停止");
            return;
        }

        previewPlaying = true;
        previewFrameIndex = 0;
        previewElapsed = 0f;
        canvas?.Refresh();
        RefreshAnimationPanel(false);
        RefreshStatus("正在以0.2秒/帧预览 · " + (playbackMode == PixelEmojiPlaybackMode.Loop ? "循环" : "单次"));
    }

    private void StopPreview(bool refresh)
    {
        if (!previewPlaying)
        {
            return;
        }

        previewPlaying = false;
        previewFrameIndex = 0;
        previewElapsed = 0f;
        if (refresh)
        {
            canvas?.Refresh();
            RefreshAnimationPanel(false);
        }
    }

    private void Update()
    {
        if (!previewPlaying || frames.Count == 0)
        {
            return;
        }

        previewElapsed += Time.unscaledDeltaTime;
        if (previewElapsed < PixelEmojiAnimationCodec.FrameDurationSeconds)
        {
            return;
        }

        var elapsedFrames = Mathf.FloorToInt(previewElapsed / PixelEmojiAnimationCodec.FrameDurationSeconds);
        previewElapsed %= PixelEmojiAnimationCodec.FrameDurationSeconds;
        if (playbackMode == PixelEmojiPlaybackMode.Loop)
        {
            previewFrameIndex = (previewFrameIndex + elapsedFrames) % frames.Count;
        }
        else if (previewFrameIndex + elapsedFrames >= frames.Count)
        {
            previewPlaying = false;
            previewFrameIndex = 0;
            previewElapsed = 0f;
            canvas?.Refresh();
            RefreshAnimationPanel(false);
            RefreshStatus("单次预览已完成，已返回编辑帧");
            return;
        }
        else
        {
            previewFrameIndex += elapsedFrames;
        }

        canvas?.Refresh();
        RefreshAnimationPanel(false);
    }

    private void RefreshAnimationPanel(bool refreshAllFrames)
    {
        for (var index = 0; index < frameSlots.Count; index++)
        {
            var active = index < frames.Count;
            frameSlots[index].SetActive(active);
            if (!active) continue;

            if (refreshAllFrames || index == selectedFrameIndex)
            {
                UpdateFrameThumbnail(index);
            }
            var selected = index == selectedFrameIndex;
            frameSlotBorders[index].SetSelected(selected);
            frameSlotPreviewMarkers[index].enabled = previewPlaying && index == previewFrameIndex;
            frameSlotNumbers[index].text = (index + 1).ToString();
            frameSlotNumbers[index].color = selected ? AuraToolsUi.Accent : AuraToolsUi.Text;
        }

        if (movePreviousButton != null)
        {
            movePreviousButton.interactable = PixelEmojiAnimationCodec.CanSwapAdjacent(frames, selectedFrameIndex, -1);
        }
        if (moveNextButton != null)
        {
            moveNextButton.interactable = PixelEmojiAnimationCodec.CanSwapAdjacent(frames, selectedFrameIndex, 1);
        }

        if (previewButton != null)
        {
            var label = previewButton.GetComponentInChildren<Text>(true);
            if (label != null) label.text = previewPlaying ? "停止" : "播放";
        }
        SetModeButtonState(loopButton, playbackMode == PixelEmojiPlaybackMode.Loop);
        SetModeButtonState(onceButton, playbackMode == PixelEmojiPlaybackMode.Once);
    }

    private void UpdateFrameThumbnail(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= frames.Count || frameIndex >= frameSlotTextures.Count)
        {
            return;
        }
        ApplyPixels(frameSlotTextures[frameIndex], frames[frameIndex]);
    }

    private static void SetModeButtonState(Button? button, bool selected)
    {
        if (button?.targetGraphic != null)
        {
            button.targetGraphic.color = selected ? AuraToolsUi.Accent : new Color(0.16f, 0.13f, 0.22f, 0.98f);
        }
    }

    private PixelEmojiAnimationSnapshot CaptureSnapshot()
    {
        return new PixelEmojiAnimationSnapshot
        {
            Frames = PixelEmojiAnimationCodec.CloneFrames(frames),
            SelectedFrameIndex = selectedFrameIndex,
            PlaybackMode = playbackMode
        };
    }

    private void RestoreSnapshot(PixelEmojiAnimationSnapshot snapshot)
    {
        frames = PixelEmojiAnimationCodec.CloneFrames(snapshot.Frames);
        selectedFrameIndex = Mathf.Clamp(snapshot.SelectedFrameIndex, 0, frames.Count - 1);
        playbackMode = PixelEmojiAnimationCodec.IsValidPlaybackMode(snapshot.PlaybackMode)
            ? snapshot.PlaybackMode
            : PixelEmojiPlaybackMode.Loop;
    }

    private void SetSelectedColor(byte color)
    {
        selectedColor = color;
        if (tool == PixelEmojiTool.Eraser || tool == PixelEmojiTool.ReferenceMove)
        {
            SetTool(PixelEmojiTool.Pencil, "色板 #" + selectedColor.ToString("00"));
            return;
        }
        RefreshStatus("色板 #" + selectedColor.ToString("00"));
    }

    private void RefreshCanvas()
    {
        canvas?.Refresh();
        UpdateFrameThumbnail(selectedFrameIndex);
    }

    private void RefreshStatus(string message, bool warning = false)
    {
        if (status == null) return;
        status.text = message;
        status.color = warning ? AuraToolsUi.WarningText : AuraToolsUi.SuccessText;
    }

    private static string NormalizeName(string value)
    {
        var name = (value ?? "").Trim();
        if (name.Length == 0) name = "未命名表情";
        return name.Length <= 32 ? name : name.Substring(0, 32);
    }

    private static Texture2D CreateSourceTexture(string name, byte[] source)
    {
        var texture = new Texture2D(PixelEmojiCodec.SourceSize, PixelEmojiCodec.SourceSize, TextureFormat.RGBA32, false)
        {
            name = "AuraToolsPixelEmoji-" + name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        ApplyPixels(texture, source);
        return texture;
    }

    private static void ApplyPixels(Texture2D texture, byte[] source)
    {
        if (texture == null || !PixelEmojiCodec.IsValid(source))
        {
            return;
        }

        var values = new Color32[PixelEmojiCodec.PixelCount];
        for (var index = 0; index < source.Length; index++)
        {
            var packed = PixelEmojiCodec.PaletteRgba[source[index]];
            values[index] = new Color32((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        }
        texture.SetPixels32(values);
        texture.Apply(false, false);
    }

    private void DestroyLibraryPreviews()
    {
        foreach (var sprite in libraryPreviewSprites) if (sprite != null) Object.Destroy(sprite);
        foreach (var texture in libraryPreviewTextures) if (texture != null) Object.Destroy(texture);
        libraryPreviewSprites.Clear();
        libraryPreviewTextures.Clear();
    }

    private static GameObject HorizontalRow(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 4, 4);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }

    private void OnDestroy()
    {
        referenceLoadSequence++;
        previewPlaying = false;
        DestroyReferenceTexture();
        DestroyLibraryPreviews();
        foreach (var texture in frameSlotTextures) if (texture != null) Object.Destroy(texture);
        frameSlotTextures.Clear();
    }
}

internal sealed class PixelEmojiAnimationSnapshot
{
    public List<byte[]> Frames { get; set; } = new();
    public int SelectedFrameIndex { get; set; }
    public PixelEmojiPlaybackMode PlaybackMode { get; set; } = PixelEmojiPlaybackMode.Loop;
}

internal sealed class PixelEmojiCanvas : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private RawImage? image;
    private Texture2D? texture;
    private Func<byte[]>? pixels;
    private Func<PixelEmojiTool>? tool;
    private Func<byte>? color;
    private Func<bool>? canEdit;
    private Action? beginMutation;
    private Action<byte>? pickedColor;
    private Action? changed;
    private Action<Vector2>? moveReference;
    private bool drawing;
    private bool movingReference;
    private int lastX;
    private int lastY;
    private Vector2 lastLocalPointer;

    public void Initialize(
        RawImage rawImage,
        Func<byte[]> pixelsSource,
        Func<PixelEmojiTool> toolSource,
        Func<byte> colorSource,
        Func<bool> canEditSource,
        Action begin,
        Action<byte> picked,
        Action refresh,
        Action<Vector2> moveReferenceAction)
    {
        image = rawImage;
        pixels = pixelsSource;
        tool = toolSource;
        color = colorSource;
        canEdit = canEditSource;
        beginMutation = begin;
        pickedColor = picked;
        changed = refresh;
        moveReference = moveReferenceAction;
        texture = new Texture2D(PixelEmojiCodec.SourceSize, PixelEmojiCodec.SourceSize, TextureFormat.RGBA32, false)
        {
            name = "AuraToolsPixelEmojiEditor",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        image.texture = texture;
    }

    public void Refresh()
    {
        if (texture == null || pixels == null) return;
        var source = pixels();
        var values = new Color32[PixelEmojiCodec.PixelCount];
        for (var index = 0; index < source.Length; index++)
        {
            var packed = PixelEmojiCodec.PaletteRgba[source[index]];
            values[index] = new Color32((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        }
        texture.SetPixels32(values);
        texture.Apply(false, false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (canEdit != null && !canEdit())
        {
            return;
        }
        if (tool?.Invoke() == PixelEmojiTool.ReferenceMove)
        {
            if (TryLocal(eventData, out lastLocalPointer))
            {
                movingReference = true;
            }
            return;
        }

        if (!TryCell(eventData, out var x, out var y) || pixels == null || tool == null || color == null) return;
        var selectedTool = tool();
        if (selectedTool == PixelEmojiTool.Eyedropper)
        {
            pickedColor?.Invoke(pixels()[y * PixelEmojiCodec.SourceSize + x]);
            return;
        }
        beginMutation?.Invoke();
        if (selectedTool == PixelEmojiTool.Fill)
        {
            PixelEmojiCodec.FloodFill(pixels(), x, y, color());
            changed?.Invoke();
            return;
        }

        drawing = true;
        lastX = x;
        lastY = y;
        DrawTo(x, y);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (canEdit != null && !canEdit())
        {
            drawing = false;
            movingReference = false;
            return;
        }
        if (movingReference)
        {
            if (TryLocal(eventData, out var local))
            {
                moveReference?.Invoke(local - lastLocalPointer);
                lastLocalPointer = local;
            }
            return;
        }
        if (!drawing || !TryCell(eventData, out var x, out var y)) return;
        DrawTo(x, y);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        drawing = false;
        movingReference = false;
    }

    private void DrawTo(int x, int y)
    {
        if (pixels == null || tool == null || color == null) return;
        var drawColor = tool() == PixelEmojiTool.Eraser ? (byte)0 : color();
        PixelEmojiCodec.DrawLine(pixels(), lastX, lastY, x, y, drawColor);
        lastX = x;
        lastY = y;
        changed?.Invoke();
    }

    private bool TryCell(PointerEventData eventData, out int x, out int y)
    {
        x = 0;
        y = 0;
        var rect = transform as RectTransform;
        if (rect == null || !TryLocal(eventData, out var local))
        {
            return false;
        }
        var normalizedX = (local.x - rect.rect.xMin) / rect.rect.width;
        var normalizedY = (local.y - rect.rect.yMin) / rect.rect.height;
        x = Mathf.Clamp(Mathf.FloorToInt(normalizedX * PixelEmojiCodec.SourceSize), 0, PixelEmojiCodec.SourceSize - 1);
        y = Mathf.Clamp(Mathf.FloorToInt(normalizedY * PixelEmojiCodec.SourceSize), 0, PixelEmojiCodec.SourceSize - 1);
        return normalizedX >= 0f && normalizedX <= 1f && normalizedY >= 0f && normalizedY <= 1f;
    }

    private bool TryLocal(PointerEventData eventData, out Vector2 local)
    {
        local = Vector2.zero;
        var rect = transform as RectTransform;
        return rect != null
               && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                   rect,
                   eventData.position,
                   eventData.pressEventCamera,
                   out local);
    }

    private void OnDestroy()
    {
        if (texture != null) Object.Destroy(texture);
    }
}

internal sealed class PixelEmojiFrameBorderGraphic : MaskableGraphic
{
    private bool selected;

    public void SetSelected(bool value)
    {
        if (selected == value)
        {
            return;
        }

        selected = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var bounds = GetPixelAdjustedRect();
        PixelEmojiGridGraphic.AddQuad(
            vh,
            new Vector2(bounds.xMin, bounds.yMin),
            new Vector2(bounds.xMax, bounds.yMax),
            new Color32(23, 22, 36, 255));

        var scaleFactor = canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
        AddRing(
            vh,
            bounds,
            0f,
            (selected ? 2f : 1f) / scaleFactor,
            selected ? new Color32(217, 179, 107, 255) : new Color32(87, 78, 103, 255));
        AddRing(
            vh,
            bounds,
            3f / scaleFactor,
            1f / scaleFactor,
            new Color32(12, 11, 20, 255));
    }

    private static void AddRing(VertexHelper vh, Rect bounds, float inset, float thickness, Color32 color)
    {
        var xMin = bounds.xMin + inset;
        var xMax = bounds.xMax - inset;
        var yMin = bounds.yMin + inset;
        var yMax = bounds.yMax - inset;
        if (thickness <= 0f || xMax - xMin <= thickness * 2f || yMax - yMin <= thickness * 2f)
        {
            return;
        }

        PixelEmojiGridGraphic.AddQuad(vh, new Vector2(xMin, yMin), new Vector2(xMin + thickness, yMax), color);
        PixelEmojiGridGraphic.AddQuad(vh, new Vector2(xMax - thickness, yMin), new Vector2(xMax, yMax), color);
        PixelEmojiGridGraphic.AddQuad(vh, new Vector2(xMin + thickness, yMin), new Vector2(xMax - thickness, yMin + thickness), color);
        PixelEmojiGridGraphic.AddQuad(vh, new Vector2(xMin + thickness, yMax - thickness), new Vector2(xMax - thickness, yMax), color);
    }
}

internal sealed class PixelEmojiGridGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var bounds = GetPixelAdjustedRect();
        var scaleFactor = canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
        for (var index = 0; index <= PixelEmojiCodec.SourceSize; index++)
        {
            var border = index == 0 || index == PixelEmojiCodec.SourceSize;
            var major = border || index % 4 == 0;
            var screenWidth = border ? 1.5f : major ? 1.25f : 1f;
            var halfWidth = screenWidth * 0.5f / scaleFactor;
            var lineColor = border
                ? new Color32(14, 14, 20, 184)
                : major
                    ? new Color32(14, 14, 20, 148)
                    : new Color32(18, 18, 24, 108);
            var x = PixelEmojiGridGeometry.Boundary(
                bounds,
                index,
                PixelEmojiCodec.SourceSize,
                horizontal: true,
                scaleFactor);
            AddQuad(vh, new Vector2(x - halfWidth, bounds.yMin), new Vector2(x + halfWidth, bounds.yMax), lineColor);
            var y = PixelEmojiGridGeometry.Boundary(
                bounds,
                index,
                PixelEmojiCodec.SourceSize,
                horizontal: false,
                scaleFactor);
            AddQuad(vh, new Vector2(bounds.xMin, y - halfWidth), new Vector2(bounds.xMax, y + halfWidth), lineColor);
        }
    }

    internal static void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color32 color)
    {
        var start = vh.currentVertCount;
        vh.AddVert(new Vector3(min.x, min.y), color, Vector2.zero);
        vh.AddVert(new Vector3(min.x, max.y), color, Vector2.zero);
        vh.AddVert(new Vector3(max.x, max.y), color, Vector2.zero);
        vh.AddVert(new Vector3(max.x, min.y), color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}

internal static class PixelEmojiGridGeometry
{
    public static float Boundary(
        Rect bounds,
        int index,
        int cellCount,
        bool horizontal,
        float scaleFactor)
    {
        var safeCount = Mathf.Max(1, cellCount);
        var start = horizontal ? bounds.xMin : bounds.yMin;
        var end = horizontal ? bounds.xMax : bounds.yMax;
        var value = Mathf.Lerp(start, end, Mathf.Clamp01(index / (float)safeCount));
        var safeScale = Mathf.Max(0.01f, scaleFactor);
        return Mathf.Round(value * safeScale) / safeScale;
    }
}

internal sealed class PixelEmojiCheckerboardGraphic : MaskableGraphic
{
    private float tileSize = 12f;
    private int logicalCellCount;

    public float TileSize
    {
        get => tileSize;
        set
        {
            tileSize = Mathf.Max(4f, value);
            SetVerticesDirty();
        }
    }

    public int LogicalCellCount
    {
        get => logicalCellCount;
        set
        {
            logicalCellCount = Mathf.Max(0, value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var bounds = GetPixelAdjustedRect();
        if (logicalCellCount > 0)
        {
            PopulateLogicalGrid(vh, bounds);
            return;
        }

        var tile = tileSize;
        var columns = Mathf.Max(1, Mathf.CeilToInt(bounds.width / tile));
        var rows = Mathf.Max(1, Mathf.CeilToInt(bounds.height / tile));
        var light = new Color32(91, 90, 101, 255);
        var dark = new Color32(50, 49, 59, 255);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var min = new Vector2(bounds.xMin + x * tile, bounds.yMin + y * tile);
                var max = new Vector2(
                    Mathf.Min(bounds.xMax, min.x + tile),
                    Mathf.Min(bounds.yMax, min.y + tile));
                PixelEmojiGridGraphic.AddQuad(vh, min, max, (x + y) % 2 == 0 ? light : dark);
            }
        }
    }

    private void PopulateLogicalGrid(VertexHelper vh, Rect bounds)
    {
        var scaleFactor = canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
        var light = new Color32(91, 90, 101, 255);
        var dark = new Color32(50, 49, 59, 255);
        for (var y = 0; y < logicalCellCount; y++)
        {
            var yMin = PixelEmojiGridGeometry.Boundary(bounds, y, logicalCellCount, horizontal: false, scaleFactor);
            var yMax = PixelEmojiGridGeometry.Boundary(bounds, y + 1, logicalCellCount, horizontal: false, scaleFactor);
            for (var x = 0; x < logicalCellCount; x++)
            {
                var xMin = PixelEmojiGridGeometry.Boundary(bounds, x, logicalCellCount, horizontal: true, scaleFactor);
                var xMax = PixelEmojiGridGeometry.Boundary(bounds, x + 1, logicalCellCount, horizontal: true, scaleFactor);
                PixelEmojiGridGraphic.AddQuad(
                    vh,
                    new Vector2(xMin, yMin),
                    new Vector2(xMax, yMax),
                    (x + y) % 2 == 0 ? light : dark);
            }
        }
    }
}
