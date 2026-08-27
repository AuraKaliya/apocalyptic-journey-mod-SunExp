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
    private readonly RectTransform participantRoot;
    private readonly List<Image> roleImages = new();
    private readonly List<Image> plateImages = new();
    private readonly List<Image> borderImages = new();
    private readonly List<Image> groundImages = new();
    private readonly List<Text> nameLabels = new();
    private readonly List<Image> decorationImages = new();
    private IReadOnlyList<AuraCgSceneLayerPresentation> activeLayers = Array.Empty<AuraCgSceneLayerPresentation>();
    private Texture2D? gradientTexture;
    private Sprite? gradientSprite;
    private Texture2D? whiteTexture;
    private Sprite? whiteSprite;
    private AuraCgSceneTheme theme = AuraCgSceneTheme.Default;
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

        var decorationRoot = new GameObject("ProgrammaticComponents", typeof(RectTransform));
        decorationRoot.transform.SetParent(root.transform, false);
        Stretch(decorationRoot.GetComponent<RectTransform>());
        BuildDecoration(decorationRoot.transform);

        var participants = new GameObject("Participants", typeof(RectTransform));
        participants.transform.SetParent(root.transform, false);
        participantRoot = participants.GetComponent<RectTransform>();
        Stretch(participantRoot);
        root.SetActive(false);
    }

    internal bool Bind(
        Sprite? optionalBackground,
        IReadOnlyList<AuraCgSceneLayerPresentation> layers,
        AuraCgScenePlan plan)
    {
        if (disposed || layers == null || plan == null)
        {
            return false;
        }

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
        if (gradientSprite == null || !string.Equals(theme.Id, nextTheme.Id, StringComparison.Ordinal))
        {
            RebuildGradient(nextTheme);
        }
        theme = nextTheme;
        ApplyTheme(theme);
        customBackgroundImage.sprite = optionalBackground;
        customBackgroundImage.enabled = optionalBackground != null;
        if (optionalBackground != null && optionalBackground.rect.height > 0f)
        {
            var fitter = customBackgroundImage.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = optionalBackground.rect.width / optionalBackground.rect.height;
        }

        EnsureParticipantCount(activeLayers.Count);
        for (var index = 0; index < roleImages.Count; index++)
        {
            var active = index < activeLayers.Count;
            roleImages[index].enabled = active;
            plateImages[index].enabled = active;
            borderImages[index].enabled = active;
            groundImages[index].enabled = active;
            nameLabels[index].enabled = active && !string.IsNullOrWhiteSpace(activeLayers[index].DisplayName);
            if (!active)
            {
                roleImages[index].sprite = null;
                continue;
            }

            ConfigureParticipant(index, activeLayers[index]);
        }

        var siblingIndex = 0;
        foreach (var image in borderImages) image.transform.SetSiblingIndex(siblingIndex++);
        foreach (var image in plateImages) image.transform.SetSiblingIndex(siblingIndex++);
        foreach (var image in groundImages) image.transform.SetSiblingIndex(siblingIndex++);
        foreach (var image in roleImages) image.transform.SetSiblingIndex(siblingIndex++);
        foreach (var label in nameLabels) label.transform.SetSiblingIndex(siblingIndex++);

        root.SetActive(true);
        UpdateFrames(0f);
        return true;
    }

    internal void UpdateFrames(float elapsed)
    {
        if (disposed || !root.activeSelf)
        {
            return;
        }

        for (var index = 0; index < activeLayers.Count && index < roleImages.Count; index++)
        {
            var layer = activeLayers[index];
            if (layer.Frames.Count == 0)
            {
                continue;
            }

            var frameSeconds = Mathf.Max(0.01f, layer.FrameSeconds);
            var rawIndex = Math.Max(0, (int)(elapsed / frameSeconds));
            var frameIndex = layer.Loop
                ? rawIndex % layer.Frames.Count
                : Math.Min(rawIndex, layer.Frames.Count - 1);
            if (roleImages[index].sprite != layer.Frames[frameIndex])
            {
                roleImages[index].sprite = layer.Frames[frameIndex];
            }
        }
    }

    internal void Hide()
    {
        activeLayers = Array.Empty<AuraCgSceneLayerPresentation>();
        foreach (var image in roleImages)
        {
            image.sprite = null;
            image.enabled = false;
        }
        foreach (var image in plateImages) image.enabled = false;
        foreach (var image in borderImages) image.enabled = false;
        foreach (var image in groundImages) image.enabled = false;
        foreach (var label in nameLabels)
        {
            label.text = "";
            label.enabled = false;
        }
        customBackgroundImage.sprite = null;
        customBackgroundImage.enabled = false;
        root.SetActive(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Hide();
        if (gradientSprite != null) UnityEngine.Object.Destroy(gradientSprite);
        if (gradientTexture != null) UnityEngine.Object.Destroy(gradientTexture);
        if (whiteSprite != null) UnityEngine.Object.Destroy(whiteSprite);
        if (whiteTexture != null) UnityEngine.Object.Destroy(whiteTexture);
        UnityEngine.Object.Destroy(root);
    }

    private void EnsureRuntimeSprites()
    {
        whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.Scene.White"
        };
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply(false, true);
        whiteSprite = Sprite.Create(
            whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
    }

    private void RebuildGradient(AuraCgSceneTheme nextTheme)
    {
        if (gradientSprite != null) UnityEngine.Object.Destroy(gradientSprite);
        if (gradientTexture != null) UnityEngine.Object.Destroy(gradientTexture);
        const int width = 32;
        const int height = 32;
        gradientTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "AuraCg.Scene.Gradient." + nextTheme.Id,
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
                color = Color.Lerp(color, nextTheme.Edge, Mathf.Clamp01(horizontal * 0.55f));
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
        decorationImages.Add(CreateAnchoredImage(parent, "TopBand", new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.945f)));
        decorationImages.Add(CreateAnchoredImage(parent, "TopHairline", new Vector2(0.10f, 0.875f), new Vector2(0.90f, 0.88f)));
        decorationImages.Add(CreateAnchoredImage(parent, "BottomBand", new Vector2(0.04f, 0.055f), new Vector2(0.96f, 0.09f)));
        decorationImages.Add(CreateAnchoredImage(parent, "BottomHairline", new Vector2(0.10f, 0.12f), new Vector2(0.90f, 0.125f)));
        decorationImages.Add(CreateAnchoredImage(parent, "LeftRail", new Vector2(0.035f, 0.15f), new Vector2(0.041f, 0.84f)));
        decorationImages.Add(CreateAnchoredImage(parent, "RightRail", new Vector2(0.959f, 0.15f), new Vector2(0.965f, 0.84f)));
        decorationImages.Add(CreateAnchoredImage(parent, "StageGlow", new Vector2(0.09f, 0.13f), new Vector2(0.91f, 0.31f)));
    }

    private void ApplyTheme(AuraCgSceneTheme nextTheme)
    {
        gradientImage.color = Color.white;
        washImage.color = nextTheme.Wash;
        for (var index = 0; index < decorationImages.Count; index++)
        {
            decorationImages[index].color = index == decorationImages.Count - 1
                ? nextTheme.Stage
                : index % 2 == 0 ? nextTheme.Accent : nextTheme.AccentSoft;
        }
    }

    private void EnsureParticipantCount(int count)
    {
        while (roleImages.Count < count)
        {
            var index = roleImages.Count;
            var border = CreateImage(participantRoot, "ParticipantBorder." + index, theme.Accent);
            var plate = CreateImage(participantRoot, "ParticipantPlate." + index, theme.Plate);
            var ground = CreateImage(participantRoot, "ParticipantGround." + index, theme.Stage);
            var role = CreateImage(participantRoot, "ParticipantRole." + index, Color.white);
            role.preserveAspect = true;
            var labelObject = new GameObject("ParticipantName." + index, typeof(RectTransform));
            labelObject.transform.SetParent(participantRoot, false);
            var label = AuraUiComponents.ConfigureText(
                labelObject,
                "",
                16,
                12,
                TextAnchor.MiddleCenter,
                Color.white,
                resizeForBestFit: true);
            borderImages.Add(border);
            plateImages.Add(plate);
            groundImages.Add(ground);
            roleImages.Add(role);
            nameLabels.Add(label);
        }
    }

    private void ConfigureParticipant(int index, AuraCgSceneLayerPresentation layer)
    {
        var plan = layer.Plan;
        ConfigurePlanRect(borderImages[index].rectTransform, plan, 0.82f, 0.82f, 1.04f);
        ConfigurePlanRect(plateImages[index].rectTransform, plan, 0.80f, 0.80f, 1f);
        ConfigureGroundRect(groundImages[index].rectTransform, plan);
        ConfigurePlanRect(roleImages[index].rectTransform, plan, 1f, 1f, 1f);
        ConfigureNameRect(nameLabels[index].rectTransform, plan);
        borderImages[index].color = theme.Accent;
        plateImages[index].color = theme.Plate;
        groundImages[index].color = theme.Stage;
        roleImages[index].sprite = layer.Frames[0];
        roleImages[index].color = Color.white;
        roleImages[index].material = null;
        roleImages[index].raycastTarget = false;
        roleImages[index].rectTransform.localScale = new Vector3(plan.MirrorX ? -1f : 1f, 1f, 1f);
        nameLabels[index].text = layer.DisplayName;
        nameLabels[index].color = Color.white;
        nameLabels[index].raycastTarget = false;
        nameLabels[index].enabled = !string.IsNullOrWhiteSpace(layer.DisplayName);
    }

    private static void ConfigurePlanRect(
        RectTransform rect,
        AuraCgSceneParticipantPlan plan,
        float widthScale,
        float heightScale,
        float outerScale)
    {
        rect.anchorMin = new Vector2(plan.CenterX, plan.CenterY);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        var viewport = rect.parent is RectTransform parent
            ? parent.rect.size
            : new Vector2(Screen.width, Screen.height);
        if (viewport.x <= 1f || viewport.y <= 1f)
        {
            viewport = new Vector2(Math.Max(1, Screen.width), Math.Max(1, Screen.height));
        }
        rect.sizeDelta = new Vector2(
            viewport.x * plan.Width * plan.Scale * widthScale * outerScale,
            viewport.y * plan.Height * plan.Scale * heightScale * outerScale);
        rect.localScale = Vector3.one;
    }

    private static void ConfigureGroundRect(RectTransform rect, AuraCgSceneParticipantPlan plan)
    {
        ConfigurePlanRect(rect, plan, 0.70f, 0.035f, 1f);
        rect.anchoredPosition = new Vector2(0f, -rect.parent.GetComponent<RectTransform>().rect.height * plan.Height * 0.36f);
    }

    private static void ConfigureNameRect(RectTransform rect, AuraCgSceneParticipantPlan plan)
    {
        ConfigurePlanRect(rect, plan, 0.72f, 0.075f, 1f);
        var parentHeight = rect.parent is RectTransform parent ? parent.rect.height : Screen.height;
        rect.anchoredPosition = new Vector2(0f, -parentHeight * plan.Height * 0.43f);
        rect.localScale = Vector3.one;
    }

    private Image CreateAnchoredImage(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax)
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
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        gameObject.transform.SetParent(parent, false);
        var image = gameObject.GetComponent<Image>();
        image.sprite = whiteSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private readonly struct AuraCgSceneTheme
    {
        public static readonly AuraCgSceneTheme Default = new(
            "default",
            new Color(0.035f, 0.04f, 0.11f, 1f),
            new Color(0.08f, 0.07f, 0.18f, 1f),
            new Color(0.015f, 0.015f, 0.05f, 1f),
            new Color(0.86f, 0.69f, 0.30f, 0.82f),
            new Color(0.54f, 0.42f, 0.22f, 0.32f));

        public AuraCgSceneTheme(
            string id,
            Color bottom,
            Color top,
            Color edge,
            Color accent,
            Color stage)
        {
            Id = id;
            Bottom = bottom;
            Top = top;
            Edge = edge;
            Accent = accent;
            Stage = stage;
            AccentSoft = new Color(accent.r, accent.g, accent.b, 0.28f);
            Plate = new Color(edge.r, edge.g, edge.b, 0.70f);
            Wash = new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 0.16f);
        }

        public string Id { get; }
        public Color Bottom { get; }
        public Color Top { get; }
        public Color Edge { get; }
        public Color Accent { get; }
        public Color AccentSoft { get; }
        public Color Stage { get; }
        public Color Plate { get; }
        public Color Wash { get; }

        public static AuraCgSceneTheme Resolve(string presentationProfileId, string sceneId)
        {
            var key = ((presentationProfileId ?? "") + "|" + (sceneId ?? "")).ToLowerInvariant();
            if (key.Contains("midas"))
                return new AuraCgSceneTheme("midas", Gold(0.12f), Gold(0.30f), Gold(0.035f), new Color(1f, 0.77f, 0.20f, 0.92f), new Color(0.95f, 0.52f, 0.08f, 0.30f));
            if (key.Contains("ritual"))
                return new AuraCgSceneTheme("ritual", Violet(0.10f), Violet(0.28f), Violet(0.025f), new Color(0.50f, 0.86f, 1f, 0.88f), new Color(0.52f, 0.26f, 0.95f, 0.28f));
            if (key.Contains("curse"))
                return new AuraCgSceneTheme("curse", Crimson(0.08f), Crimson(0.24f), Crimson(0.018f), new Color(0.92f, 0.28f, 0.70f, 0.88f), new Color(0.65f, 0.12f, 0.42f, 0.32f));
            if (key.Contains("defeat"))
                return new AuraCgSceneTheme("defeat", Slate(0.07f), Slate(0.16f), Slate(0.018f), new Color(0.75f, 0.36f, 0.34f, 0.70f), new Color(0.34f, 0.14f, 0.18f, 0.26f));
            if (key.Contains("opening"))
                return new AuraCgSceneTheme("opening", Blue(0.08f), Blue(0.24f), Blue(0.02f), new Color(0.42f, 0.78f, 1f, 0.86f), new Color(0.15f, 0.48f, 0.82f, 0.26f));
            if (key.Contains("settlement"))
                return new AuraCgSceneTheme("settlement", Teal(0.08f), Teal(0.22f), Teal(0.02f), new Color(0.50f, 0.92f, 0.82f, 0.82f), new Color(0.12f, 0.58f, 0.55f, 0.25f));
            return Default;
        }

        private static Color Gold(float value) => new(value * 1.30f, value * 0.82f, value * 0.25f, 1f);
        private static Color Violet(float value) => new(value * 0.72f, value * 0.48f, value * 1.40f, 1f);
        private static Color Crimson(float value) => new(value * 1.35f, value * 0.32f, value * 0.82f, 1f);
        private static Color Slate(float value) => new(value * 0.82f, value * 0.88f, value, 1f);
        private static Color Blue(float value) => new(value * 0.35f, value * 0.68f, value * 1.35f, 1f);
        private static Color Teal(float value) => new(value * 0.28f, value * 1.05f, value * 0.92f, 1f);
    }
}
