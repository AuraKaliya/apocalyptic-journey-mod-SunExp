using System;
using System.Collections.Generic;
using System.Linq;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraCg.Shared;

internal sealed class AuraCgSceneCompositionRenderer : IDisposable
{
    private readonly GameObject root;
    private readonly Image gradientImage;
    private readonly Image customBackgroundImage;
    private readonly Image washImage;
    private readonly Image stageFloorImage;
    private readonly Image stageLineImage;
    private readonly Image stageGlowImage;
    private readonly Image captionBackgroundImage;
    private readonly Image captionAccentImage;
    private readonly Text titleLabel;
    private readonly Text subtitleLabel;
    private readonly Text participantCountLabel;
    private readonly RectTransform participantRoot;
    private readonly List<Image> railImages = new();
    private readonly List<Image> motifImages = new();
    private readonly List<Image> panelBorderImages = new();
    private readonly List<Image> panelImages = new();
    private readonly List<Image> groundShadowImages = new();
    private readonly List<Image> namePlateImages = new();
    private readonly List<RectTransform> roleRoots = new();
    private readonly List<RectMask2D> roleMasks = new();
    private readonly List<Image> roleImages = new();
    private readonly List<Text> nameLabels = new();
    private IReadOnlyList<AuraCgSceneLayerPresentation> activeLayers = Array.Empty<AuraCgSceneLayerPresentation>();
    private Texture2D? gradientTexture;
    private Sprite? gradientSprite;
    private Texture2D? whiteTexture;
    private Sprite? whiteSprite;
    private Texture2D? ellipseTexture;
    private Sprite? ellipseSprite;
    private Texture2D? diamondTexture;
    private Sprite? diamondSprite;
    private AuraCgSceneTheme theme = AuraCgSceneTheme.Default;
    private bool usePortraitPanels;
    private bool disposed;

    internal AuraCgSceneCompositionRenderer(Transform host, string name)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(host, false);
        root.transform.SetAsFirstSibling();
        Stretch(root.GetComponent<RectTransform>());

        EnsureRuntimeSprites();
        gradientImage = CreateImage(root.transform, "ProgrammaticGradient", Color.white);
        Stretch(gradientImage.rectTransform);
        customBackgroundImage = CreateImage(root.transform, "OptionalBackground", Color.white);
        Stretch(customBackgroundImage.rectTransform);
        customBackgroundImage.preserveAspect = true;
        var fitter = customBackgroundImage.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        washImage = CreateImage(root.transform, "ThemeWash", Color.clear);
        Stretch(washImage.rectTransform);

        stageFloorImage = CreateAnchoredImage(
            root.transform,
            "SharedStageFloor",
            new Vector2(0f, 0.04f),
            new Vector2(1f, 0.52f));
        stageLineImage = CreateAnchoredImage(
            root.transform,
            "SharedStageLine",
            new Vector2(0.04f, 0.52f),
            new Vector2(0.96f, 0.522f));
        stageGlowImage = CreateAnchoredImage(
            root.transform,
            "SharedStageGlow",
            new Vector2(0.10f, 0.08f),
            new Vector2(0.90f, 0.34f));
        stageGlowImage.sprite = ellipseSprite;

        BuildDecoration(root.transform);
        var caption = BuildCaption(root.transform);
        captionBackgroundImage = caption.Background;
        captionAccentImage = caption.Accent;
        titleLabel = caption.Title;
        subtitleLabel = caption.Subtitle;
        participantCountLabel = caption.ParticipantCount;

        var participants = new GameObject("Participants", typeof(RectTransform));
        participants.transform.SetParent(root.transform, false);
        participantRoot = participants.GetComponent<RectTransform>();
        Stretch(participantRoot);
        captionBackgroundImage.transform.SetAsLastSibling();
        participantCountLabel.transform.SetAsLastSibling();
        root.SetActive(false);
    }

    internal bool Bind(
        Sprite? optionalBackground,
        IReadOnlyList<AuraCgSceneLayerPresentation> layers,
        AuraCgScenePlan plan)
    {
        if (disposed || root == null || layers == null || plan == null) return false;

        activeLayers = layers
            .Where(layer => layer != null && layer.Frames != null && layer.Frames.Count > 0)
            .OrderBy(layer => layer.Plan.ZIndex)
            .ThenBy(layer => layer.Plan.SeatIndex)
            .ToArray();
        if (activeLayers.Count == 0)
        {
            Hide();
            return false;
        }

        var nextTheme = AuraCgSceneTheme.Resolve(plan.PresentationProfileId, plan.SceneId);
        if (gradientSprite == null || !string.Equals(theme.Identity.Id, nextTheme.Identity.Id, StringComparison.Ordinal))
            RebuildGradient(nextTheme);
        theme = nextTheme;
        usePortraitPanels = AuraCgSceneLayoutFallbackPolicy.UsePortraitPanels(
            activeLayers.Count,
            activeLayers.Select(layer => AuraCgSceneFramingMath.VisibleAspect(
                layer.VisibleBounds,
                layer.CanvasWidth,
                layer.CanvasHeight)));
        ApplyTheme(theme, activeLayers.Count);

        customBackgroundImage.sprite = optionalBackground;
        customBackgroundImage.enabled = optionalBackground != null;
        customBackgroundImage.color = optionalBackground == null
            ? Color.clear
            : new Color(0.78f, 0.80f, 0.94f, 0.42f);
        if (optionalBackground != null && optionalBackground.rect.height > 0f)
        {
            var backgroundFitter = customBackgroundImage.GetComponent<AspectRatioFitter>();
            backgroundFitter.aspectRatio = optionalBackground.rect.width / optionalBackground.rect.height;
        }

        EnsureParticipantCount(activeLayers.Count);
        for (var index = 0; index < roleImages.Count; index++)
        {
            var active = index < activeLayers.Count;
            SetParticipantActive(index, active);
            if (active) ConfigureParticipant(index, activeLayers[index]);
        }
        ApplyParticipantSiblingOrder();

        root.SetActive(true);
        UpdateFrames(0f);
        return true;
    }

    internal void UpdateFrames(float elapsed)
    {
        if (disposed || root == null || !root.activeSelf) return;
        for (var index = 0; index < activeLayers.Count && index < roleImages.Count; index++)
        {
            var image = roleImages[index];
            var layer = activeLayers[index];
            if (image == null || layer.Frames.Count == 0) continue;
            var frameSeconds = Mathf.Max(0.01f, layer.FrameSeconds);
            var rawIndex = Math.Max(0, (int)(elapsed / frameSeconds));
            var frameIndex = layer.Loop
                ? rawIndex % layer.Frames.Count
                : Math.Min(rawIndex, layer.Frames.Count - 1);
            if (image.sprite != layer.Frames[frameIndex]) image.sprite = layer.Frames[frameIndex];
        }
    }

    internal void Hide()
    {
        activeLayers = Array.Empty<AuraCgSceneLayerPresentation>();
        if (root == null) return;
        for (var index = 0; index < roleImages.Count; index++) SetParticipantActive(index, false);
        if (customBackgroundImage != null)
        {
            customBackgroundImage.sprite = null;
            customBackgroundImage.enabled = false;
        }
        root.SetActive(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        activeLayers = Array.Empty<AuraCgSceneLayerPresentation>();
        if (gradientSprite != null) UnityEngine.Object.Destroy(gradientSprite);
        if (gradientTexture != null) UnityEngine.Object.Destroy(gradientTexture);
        if (whiteSprite != null) UnityEngine.Object.Destroy(whiteSprite);
        if (whiteTexture != null) UnityEngine.Object.Destroy(whiteTexture);
        if (ellipseSprite != null) UnityEngine.Object.Destroy(ellipseSprite);
        if (ellipseTexture != null) UnityEngine.Object.Destroy(ellipseTexture);
        if (diamondSprite != null) UnityEngine.Object.Destroy(diamondSprite);
        if (diamondTexture != null) UnityEngine.Object.Destroy(diamondTexture);
        if (root != null) UnityEngine.Object.Destroy(root);
    }

    private void EnsureRuntimeSprites()
    {
        whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "AuraCg.Scene.White" };
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply(false, true);
        whiteSprite = Sprite.Create(whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);

        ellipseTexture = new Texture2D(64, 24, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.Scene.Ellipse",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var ellipsePixels = new Color[64 * 24];
        for (var y = 0; y < 24; y++)
        for (var x = 0; x < 64; x++)
        {
            var nx = (x + 0.5f) / 32f - 1f;
            var ny = (y + 0.5f) / 12f - 1f;
            var distance = nx * nx + ny * ny;
            var alpha = distance >= 1f ? 0f : Mathf.Pow(1f - distance, 2f);
            ellipsePixels[y * 64 + x] = new Color(1f, 1f, 1f, alpha);
        }
        ellipseTexture.SetPixels(ellipsePixels);
        ellipseTexture.Apply(false, true);
        ellipseSprite = Sprite.Create(ellipseTexture, new Rect(0f, 0f, 64f, 24f), new Vector2(0.5f, 0.5f), 100f);

        diamondTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.Scene.Diamond",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var diamondPixels = new Color[32 * 32];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
        {
            var distance = Math.Abs(x - 15.5f) + Math.Abs(y - 15.5f);
            var alpha = distance >= 13f && distance <= 16f ? 1f : 0f;
            diamondPixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha);
        }
        diamondTexture.SetPixels(diamondPixels);
        diamondTexture.Apply(false, true);
        diamondSprite = Sprite.Create(diamondTexture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 100f);
    }

    private void RebuildGradient(AuraCgSceneTheme nextTheme)
    {
        if (gradientSprite != null) UnityEngine.Object.Destroy(gradientSprite);
        if (gradientTexture != null) UnityEngine.Object.Destroy(gradientTexture);
        const int width = 64;
        const int height = 64;
        gradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.Scene.Gradient." + nextTheme.Identity.Id,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var vertical = y / (height - 1f);
            for (var x = 0; x < width; x++)
            {
                var horizontal = Math.Abs(x / (width - 1f) - 0.5f) * 2f;
                var color = Color.Lerp(nextTheme.Bottom, nextTheme.Top, vertical);
                color = Color.Lerp(color, nextTheme.Edge, Mathf.Clamp01(horizontal * 0.48f));
                pixels[y * width + x] = color;
            }
        }
        gradientTexture.SetPixels(pixels);
        gradientTexture.Apply(false, true);
        gradientSprite = Sprite.Create(
            gradientTexture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            1f);
        gradientImage.sprite = gradientSprite;
    }

    private void BuildDecoration(Transform parent)
    {
        railImages.Add(CreateAnchoredImage(parent, "TopHairline", new Vector2(0.06f, 0.91f), new Vector2(0.94f, 0.913f)));
        railImages.Add(CreateAnchoredImage(parent, "BottomHairline", new Vector2(0.06f, 0.09f), new Vector2(0.94f, 0.093f)));
        railImages.Add(CreateAnchoredImage(parent, "LeftRail", new Vector2(0.035f, 0.16f), new Vector2(0.038f, 0.84f)));
        railImages.Add(CreateAnchoredImage(parent, "RightRail", new Vector2(0.962f, 0.16f), new Vector2(0.965f, 0.84f)));
        foreach (var placement in new[]
                 {
                     ("Motif.LeftTop", new Vector2(0.09f, 0.76f)),
                     ("Motif.RightTop", new Vector2(0.91f, 0.72f)),
                     ("Motif.LeftMid", new Vector2(0.16f, 0.49f)),
                     ("Motif.RightMid", new Vector2(0.84f, 0.46f))
                 })
        {
            var image = CreateImage(parent, placement.Item1, Color.white);
            image.sprite = diamondSprite;
            var rect = image.rectTransform;
            rect.anchorMin = placement.Item2;
            rect.anchorMax = placement.Item2;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(18f, 18f);
            rect.anchoredPosition = Vector2.zero;
            motifImages.Add(image);
        }
    }

    private (Image Background, Image Accent, Text Title, Text Subtitle, Text ParticipantCount) BuildCaption(Transform parent)
    {
        var captionObject = new GameObject("SceneCaption", typeof(RectTransform));
        captionObject.transform.SetParent(parent, false);
        var captionRect = captionObject.GetComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0.055f, 0.79f);
        captionRect.anchorMax = new Vector2(0.46f, 0.90f);
        captionRect.offsetMin = Vector2.zero;
        captionRect.offsetMax = Vector2.zero;
        var background = captionObject.AddComponent<Image>();
        background.sprite = whiteSprite;
        background.raycastTarget = false;

        var accent = CreateAnchoredImage(captionObject.transform, "Accent", new Vector2(0f, 0f), new Vector2(0.008f, 1f));
        var titleObject = new GameObject("Title", typeof(RectTransform));
        titleObject.transform.SetParent(captionObject.transform, false);
        var titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.05f, 0.38f);
        titleRect.anchorMax = new Vector2(0.96f, 0.94f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        var title = AuraUiComponents.ConfigureText(
            titleObject,
            "",
            34,
            18,
            TextAnchor.MiddleLeft,
            Color.white,
            resizeForBestFit: true);
        title.fontStyle = FontStyle.Bold;
        title.raycastTarget = false;

        var subtitleObject = new GameObject("Subtitle", typeof(RectTransform));
        subtitleObject.transform.SetParent(captionObject.transform, false);
        var subtitleRect = subtitleObject.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0.05f, 0.04f);
        subtitleRect.anchorMax = new Vector2(0.96f, 0.42f);
        subtitleRect.offsetMin = Vector2.zero;
        subtitleRect.offsetMax = Vector2.zero;
        var subtitle = AuraUiComponents.ConfigureText(
            subtitleObject,
            "",
            15,
            11,
            TextAnchor.MiddleLeft,
            Color.white,
            resizeForBestFit: true);
        subtitle.raycastTarget = false;

        var countObject = new GameObject("ParticipantCount", typeof(RectTransform));
        countObject.transform.SetParent(parent, false);
        var countRect = countObject.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.72f, 0.84f);
        countRect.anchorMax = new Vector2(0.94f, 0.90f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = Vector2.zero;
        var count = AuraUiComponents.ConfigureText(
            countObject,
            "",
            15,
            11,
            TextAnchor.MiddleRight,
            Color.white,
            resizeForBestFit: true);
        count.raycastTarget = false;
        return (background, accent, title, subtitle, count);
    }

    private void ApplyTheme(AuraCgSceneTheme nextTheme, int participantCount)
    {
        gradientImage.color = Color.white;
        washImage.color = nextTheme.Wash;
        stageFloorImage.color = nextTheme.Stage;
        stageLineImage.color = nextTheme.StageLine;
        stageGlowImage.color = nextTheme.Glow;
        captionBackgroundImage.color = nextTheme.Caption;
        captionAccentImage.color = nextTheme.Accent;
        titleLabel.text = nextTheme.Identity.Title;
        titleLabel.color = nextTheme.Text;
        subtitleLabel.text = nextTheme.Identity.Subtitle;
        subtitleLabel.color = nextTheme.Muted;
        participantCountLabel.text = participantCount + " 位冒险者" + (usePortraitPanels ? " · 肖像构图" : " · 群像构图");
        participantCountLabel.color = nextTheme.Muted;
        foreach (var rail in railImages) if (rail != null) rail.color = nextTheme.AccentSoft;
        foreach (var motif in motifImages) if (motif != null) motif.color = nextTheme.AccentSoft;
    }

    private void EnsureParticipantCount(int count)
    {
        while (roleImages.Count < count)
        {
            var index = roleImages.Count;
            var border = CreateImage(participantRoot, "ParticipantPanelBorder." + index, theme.AccentSoft);
            var panel = CreateImage(participantRoot, "ParticipantPanel." + index, theme.Panel);
            var shadow = CreateImage(participantRoot, "ParticipantShadow." + index, theme.Shadow);
            shadow.sprite = ellipseSprite;
            var roleRootObject = new GameObject("ParticipantRoleRoot." + index, typeof(RectTransform), typeof(RectMask2D));
            roleRootObject.transform.SetParent(participantRoot, false);
            var roleRoot = roleRootObject.GetComponent<RectTransform>();
            var mask = roleRootObject.GetComponent<RectMask2D>();
            mask.enabled = false;
            var role = CreateImage(roleRootObject.transform, "Role", Color.white);
            role.preserveAspect = false;
            var roleShadow = role.gameObject.AddComponent<Shadow>();
            roleShadow.effectColor = new Color(0f, 0f, 0f, 0.58f);
            roleShadow.effectDistance = new Vector2(5f, -6f);
            roleShadow.useGraphicAlpha = true;
            var plate = CreateImage(participantRoot, "ParticipantNamePlate." + index, theme.NamePlate);
            var labelObject = new GameObject("ParticipantName." + index, typeof(RectTransform));
            labelObject.transform.SetParent(participantRoot, false);
            var label = AuraUiComponents.ConfigureText(
                labelObject,
                "",
                15,
                10,
                TextAnchor.MiddleCenter,
                Color.white,
                resizeForBestFit: true);
            label.raycastTarget = false;
            panelBorderImages.Add(border);
            panelImages.Add(panel);
            groundShadowImages.Add(shadow);
            roleRoots.Add(roleRoot);
            roleMasks.Add(mask);
            roleImages.Add(role);
            namePlateImages.Add(plate);
            nameLabels.Add(label);
        }
    }

    private void ConfigureParticipant(int index, AuraCgSceneLayerPresentation layer)
    {
        var plan = layer.Plan;
        var slot = usePortraitPanels
            ? PanelSlot(plan.SeatIndex, activeLayers.Count)
            : new NormalizedSlot(plan.CenterX, plan.CenterY, plan.Width * plan.Scale, plan.Height * plan.Scale);
        ConfigureNormalizedRect(panelBorderImages[index].rectTransform, slot, usePortraitPanels ? 1.025f : 1f);
        ConfigureNormalizedRect(panelImages[index].rectTransform, slot, 1f);
        ConfigureNormalizedRect(roleRoots[index], slot, usePortraitPanels ? 0.92f : 1f);
        ConfigureGroundRect(groundShadowImages[index].rectTransform, slot);
        ConfigureNamePlateRect(namePlateImages[index].rectTransform, slot);
        ConfigureNamePlateRect(nameLabels[index].rectTransform, slot);

        panelBorderImages[index].enabled = usePortraitPanels;
        panelImages[index].enabled = usePortraitPanels;
        panelBorderImages[index].color = theme.AccentSoft;
        panelImages[index].color = theme.Panel;
        groundShadowImages[index].color = theme.Shadow;
        namePlateImages[index].color = theme.NamePlate;
        roleMasks[index].enabled = usePortraitPanels;

        var roleRect = roleImages[index].rectTransform;
        roleRect.anchorMin = new Vector2(0.5f, 0f);
        roleRect.anchorMax = roleRect.anchorMin;
        roleRect.pivot = new Vector2(0.5f, 0f);
        var framing = AuraCgSceneFramingMath.FitVisibleBounds(
            layer.VisibleBounds,
            layer.CanvasWidth,
            layer.CanvasHeight,
            Math.Max(1f, roleRoots[index].sizeDelta.x * (usePortraitPanels ? 0.92f : 0.96f)),
            Math.Max(1f, roleRoots[index].sizeDelta.y * (usePortraitPanels ? 0.92f : 0.95f)));
        roleRect.sizeDelta = new Vector2(framing.ImageWidth, framing.ImageHeight);
        roleRect.anchoredPosition = new Vector2(framing.OffsetX, framing.OffsetY);
        roleRect.localScale = Vector3.one;
        roleImages[index].sprite = layer.Frames[0];
        roleImages[index].color = theme.Identity.Id == "defeat"
            ? new Color(0.76f, 0.76f, 0.82f, 1f)
            : Color.white;
        roleImages[index].material = null;
        roleImages[index].raycastTarget = false;
        roleRoots[index].localScale = new Vector3(plan.MirrorX ? -1f : 1f, 1f, 1f);

        nameLabels[index].text = layer.DisplayName;
        nameLabels[index].color = theme.Text;
        nameLabels[index].enabled = !string.IsNullOrWhiteSpace(layer.DisplayName);
        namePlateImages[index].enabled = nameLabels[index].enabled;
    }

    private void ApplyParticipantSiblingOrder()
    {
        var sibling = 0;
        foreach (var border in panelBorderImages) if (border != null) border.transform.SetSiblingIndex(sibling++);
        foreach (var panel in panelImages) if (panel != null) panel.transform.SetSiblingIndex(sibling++);
        foreach (var shadow in groundShadowImages) if (shadow != null) shadow.transform.SetSiblingIndex(sibling++);
        foreach (var roleRoot in roleRoots) if (roleRoot != null) roleRoot.SetSiblingIndex(sibling++);
        foreach (var plate in namePlateImages) if (plate != null) plate.transform.SetSiblingIndex(sibling++);
        foreach (var label in nameLabels) if (label != null) label.transform.SetSiblingIndex(sibling++);
    }

    private void SetParticipantActive(int index, bool active)
    {
        if (index < panelBorderImages.Count && panelBorderImages[index] != null) panelBorderImages[index].enabled = active && usePortraitPanels;
        if (index < panelImages.Count && panelImages[index] != null) panelImages[index].enabled = active && usePortraitPanels;
        if (index < groundShadowImages.Count && groundShadowImages[index] != null) groundShadowImages[index].enabled = active;
        if (index < roleRoots.Count && roleRoots[index] != null) roleRoots[index].gameObject.SetActive(active);
        if (index < roleImages.Count && roleImages[index] != null)
        {
            roleImages[index].enabled = active;
            if (!active) roleImages[index].sprite = null;
        }
        if (index < namePlateImages.Count && namePlateImages[index] != null) namePlateImages[index].enabled = active;
        if (index < nameLabels.Count && nameLabels[index] != null)
        {
            nameLabels[index].enabled = active;
            if (!active) nameLabels[index].text = "";
        }
    }

    private void ConfigureNormalizedRect(RectTransform rect, NormalizedSlot slot, float outerScale)
    {
        rect.anchorMin = new Vector2(slot.CenterX, slot.CenterY);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        var viewport = ViewportSize(rect.parent as RectTransform);
        rect.sizeDelta = new Vector2(
            viewport.x * slot.Width * outerScale,
            viewport.y * slot.Height * outerScale);
        rect.localScale = Vector3.one;
    }

    private void ConfigureGroundRect(RectTransform rect, NormalizedSlot slot)
    {
        var ground = new NormalizedSlot(
            slot.CenterX,
            Math.Max(0.02f, slot.CenterY - slot.Height * 0.48f),
            slot.Width * (usePortraitPanels ? 0.82f : 0.66f),
            usePortraitPanels ? 0.026f : 0.034f);
        ConfigureNormalizedRect(rect, ground, 1f);
    }

    private void ConfigureNamePlateRect(RectTransform rect, NormalizedSlot slot)
    {
        var plate = new NormalizedSlot(
            slot.CenterX,
            Math.Max(0.025f, slot.CenterY - slot.Height * 0.46f),
            slot.Width * (usePortraitPanels ? 0.92f : 0.72f),
            usePortraitPanels ? 0.040f : 0.036f);
        ConfigureNormalizedRect(rect, plate, 1f);
    }

    private static NormalizedSlot PanelSlot(int seatIndex, int participantCount)
    {
        var topRow = seatIndex < 4;
        var rowIndex = topRow ? seatIndex : seatIndex - 4;
        var rowCount = topRow ? Math.Min(4, participantCount) : Math.Max(1, participantCount - 4);
        var start = rowCount switch
        {
            1 => 0.5f,
            2 => 0.36f,
            3 => 0.24f,
            _ => 0.14f
        };
        var end = rowCount switch
        {
            1 => 0.5f,
            2 => 0.64f,
            3 => 0.76f,
            _ => 0.86f
        };
        var step = rowCount <= 1 ? 0f : (end - start) / (rowCount - 1);
        return new NormalizedSlot(start + step * rowIndex, topRow ? 0.64f : 0.30f, 0.205f, 0.30f);
    }

    private Image CreateAnchoredImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var image = CreateImage(parent, name, Color.white);
        var rect = image.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return image;
    }

    private Image CreateImage(Transform parent, string name, Color color)
    {
        var value = new GameObject(name, typeof(RectTransform), typeof(Image));
        value.transform.SetParent(parent, false);
        var image = value.GetComponent<Image>();
        image.sprite = whiteSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Vector2 ViewportSize(RectTransform? rect)
    {
        var size = rect == null ? Vector2.zero : rect.rect.size;
        return size.x > 1f && size.y > 1f
            ? size
            : new Vector2(Math.Max(1, Screen.width), Math.Max(1, Screen.height));
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private readonly struct NormalizedSlot
    {
        internal NormalizedSlot(float centerX, float centerY, float width, float height)
        {
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
        }

        internal float CenterX { get; }
        internal float CenterY { get; }
        internal float Width { get; }
        internal float Height { get; }
    }

    private readonly struct AuraCgSceneTheme
    {
        internal static readonly AuraCgSceneTheme Default = Resolve("default", "victory.standard");

        private AuraCgSceneTheme(
            AuraCgSceneProfileIdentity identity,
            Color bottom,
            Color top,
            Color edge,
            Color accent,
            Color stageLine)
        {
            Identity = identity;
            Bottom = bottom;
            Top = top;
            Edge = edge;
            Accent = accent;
            AccentSoft = new Color(accent.r, accent.g, accent.b, 0.34f);
            Stage = new Color(edge.r * 0.72f, edge.g * 0.72f, edge.b * 0.82f, 0.88f);
            StageLine = stageLine;
            Glow = new Color(accent.r, accent.g, accent.b, 0.18f);
            Panel = new Color(edge.r * 1.45f, edge.g * 1.45f, edge.b * 1.55f, 0.76f);
            NamePlate = new Color(edge.r * 0.62f, edge.g * 0.62f, edge.b * 0.72f, 0.84f);
            Caption = new Color(edge.r * 0.50f, edge.g * 0.50f, edge.b * 0.62f, 0.78f);
            Shadow = new Color(0f, 0f, 0f, 0.48f);
            Wash = new Color(accent.r * 0.20f, accent.g * 0.20f, accent.b * 0.20f, 0.12f);
            Text = new Color(0.94f, 0.92f, 0.82f, 1f);
            Muted = new Color(0.76f, 0.73f, 0.62f, 1f);
        }

        internal AuraCgSceneProfileIdentity Identity { get; }
        internal Color Bottom { get; }
        internal Color Top { get; }
        internal Color Edge { get; }
        internal Color Accent { get; }
        internal Color AccentSoft { get; }
        internal Color Stage { get; }
        internal Color StageLine { get; }
        internal Color Glow { get; }
        internal Color Panel { get; }
        internal Color NamePlate { get; }
        internal Color Caption { get; }
        internal Color Shadow { get; }
        internal Color Wash { get; }
        internal Color Text { get; }
        internal Color Muted { get; }

        internal static AuraCgSceneTheme Resolve(string presentationProfileId, string sceneId)
        {
            var identity = AuraCgSceneProfileIdentity.Resolve(presentationProfileId, sceneId);
            return identity.Id switch
            {
                "midas" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.055f, 0.030f, 0.045f, 1f),
                    new Color(0.18f, 0.10f, 0.06f, 1f),
                    new Color(0.018f, 0.012f, 0.022f, 1f),
                    new Color(0.92f, 0.70f, 0.22f, 0.86f),
                    new Color(0.88f, 0.60f, 0.18f, 0.48f)),
                "ritual" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.030f, 0.025f, 0.10f, 1f),
                    new Color(0.12f, 0.075f, 0.24f, 1f),
                    new Color(0.012f, 0.010f, 0.040f, 1f),
                    new Color(0.42f, 0.76f, 0.96f, 0.82f),
                    new Color(0.54f, 0.42f, 0.90f, 0.48f)),
                "curse" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.055f, 0.012f, 0.055f, 1f),
                    new Color(0.18f, 0.035f, 0.12f, 1f),
                    new Color(0.018f, 0.006f, 0.024f, 1f),
                    new Color(0.86f, 0.28f, 0.60f, 0.82f),
                    new Color(0.72f, 0.20f, 0.48f, 0.46f)),
                "defeat" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.025f, 0.028f, 0.045f, 1f),
                    new Color(0.070f, 0.072f, 0.095f, 1f),
                    new Color(0.010f, 0.011f, 0.018f, 1f),
                    new Color(0.62f, 0.32f, 0.34f, 0.62f),
                    new Color(0.44f, 0.38f, 0.42f, 0.34f)),
                "opening" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.020f, 0.040f, 0.090f, 1f),
                    new Color(0.055f, 0.13f, 0.24f, 1f),
                    new Color(0.008f, 0.016f, 0.038f, 1f),
                    new Color(0.38f, 0.72f, 0.94f, 0.80f),
                    new Color(0.26f, 0.58f, 0.84f, 0.44f)),
                "settlement" => new AuraCgSceneTheme(
                    identity,
                    new Color(0.018f, 0.055f, 0.070f, 1f),
                    new Color(0.055f, 0.15f, 0.17f, 1f),
                    new Color(0.008f, 0.022f, 0.030f, 1f),
                    new Color(0.42f, 0.82f, 0.72f, 0.76f),
                    new Color(0.34f, 0.72f, 0.66f, 0.42f)),
                _ => new AuraCgSceneTheme(
                    identity,
                    new Color(0.025f, 0.030f, 0.090f, 1f),
                    new Color(0.080f, 0.070f, 0.18f, 1f),
                    new Color(0.010f, 0.010f, 0.035f, 1f),
                    new Color(0.82f, 0.68f, 0.36f, 0.78f),
                    new Color(0.76f, 0.60f, 0.28f, 0.40f))
            };
        }
    }
}
