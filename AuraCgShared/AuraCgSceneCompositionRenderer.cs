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
    private readonly RectTransform artRect;
    private readonly RectTransform motionRoot;
    private readonly Image backgroundImage;
    private readonly Image bottomShade;
    private readonly Text title;
    private readonly Text subtitle;
    private readonly Text signature;
    private readonly List<Image> imagePool = new();
    private readonly List<RectTransform> windowPool = new();
    private readonly List<RectMask2D> windowMasks = new();
    private readonly List<Drawable> drawables = new();
    private readonly Texture2D shadeTexture;
    private readonly Sprite shadeSprite;
    private AuraCgScenePresentation? presentation;
    private AuraCgScenePlan? plan;
    private Vector2 lastAvailable;
    private Vector2 artSize;
    private float motionDuration = 5f;
    private bool disposed;

    internal AuraCgSceneCompositionRenderer(Transform host, string name)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(host, false);
        Stretch(root.GetComponent<RectTransform>());
        var art = new GameObject("PosterCanvas", typeof(RectTransform), typeof(RectMask2D));
        art.transform.SetParent(root.transform, false);
        artRect = art.GetComponent<RectTransform>();
        artRect.anchorMin = artRect.anchorMax = artRect.pivot = new Vector2(0.5f, 0.5f);
        var motion = new GameObject("PosterMotion", typeof(RectTransform));
        motion.transform.SetParent(art.transform, false);
        motionRoot = motion.GetComponent<RectTransform>();
        Stretch(motionRoot);
        backgroundImage = CreateImage(motionRoot, "Background");
        bottomShade = CreateImage(motionRoot, "AtmosphereShade");
        shadeTexture = new Texture2D(1, 64, TextureFormat.RGBA32, false)
        { name = "AuraCg.Poster.BottomShade", wrapMode = TextureWrapMode.Clamp };
        var colors = new Color[64];
        for (var y = 0; y < colors.Length; y++)
            colors[y] = new Color(0.07f, 0.12f, 0.15f, Mathf.Clamp01(1f - y / 20f) * 0.48f);
        shadeTexture.SetPixels(colors);
        shadeTexture.Apply(false, true);
        shadeSprite = Sprite.Create(shadeTexture, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
        bottomShade.sprite = shadeSprite;
        Stretch(bottomShade.rectTransform);
        title = CreateText(motionRoot, "Title");
        subtitle = CreateText(motionRoot, "Subtitle");
        signature = CreateText(motionRoot, "TeamSignature");
        root.SetActive(false);
    }

    internal bool Bind(AuraCgScenePresentation value, AuraCgScenePlan scenePlan, float duration)
    {
        if (disposed || root == null || value.IsDisposed || !value.Ready
            || value.Background == null || value.Participants.Count != scenePlan.Participants.Count) return false;
        Hide();
        presentation = value;
        plan = scenePlan;
        motionDuration = Math.Max(0.1f, duration);
        backgroundImage.sprite = value.Background;
        backgroundImage.color = Color.white;
        var roles = value.Participants.OrderBy(layer => layer.Plan.ZIndex).ThenBy(layer => layer.Plan.SeatIndex).ToArray();
        foreach (var layer in value.SceneLayers.Where(layer => !layer.Spec.Foreground)) Add(layer, null);
        foreach (var role in roles)
            foreach (var layer in role.Attachments.Where(layer => !layer.Spec.Foreground)) Add(layer, role);
        foreach (var role in roles) Add(null, role);
        foreach (var role in roles)
            foreach (var layer in role.Attachments.Where(layer => layer.Spec.Foreground)) Add(layer, role);
        foreach (var layer in value.SceneLayers.Where(layer => layer.Spec.Foreground)) Add(layer, null);
        var identity = AuraCgSceneProfileIdentity.Resolve(scenePlan.PresentationProfileId, scenePlan.SceneId);
        title.text = identity.Id == "victory" ? "胜利" : identity.Title;
        subtitle.text = identity.Id == "victory" ? "旅途仍在继续" : identity.Subtitle;
        signature.text = string.Join("  ·  ", value.Participants.OrderBy(layer => layer.Plan.SeatIndex)
            .Select(layer => layer.DisplayName).Where(name => !string.IsNullOrWhiteSpace(name)));
        var ink = value.Artwork.DarkTitle ? new Color(0.12f, 0.20f, 0.25f, 1f) : new Color(0.96f, 0.93f, 0.85f, 1f);
        title.color = ink;
        subtitle.color = ink;
        signature.color = new Color(0.96f, 0.94f, 0.88f, 1f);
        backgroundImage.transform.SetAsFirstSibling();
        foreach (var drawable in drawables) drawable.Window.SetAsLastSibling();
        bottomShade.transform.SetAsLastSibling();
        title.transform.SetAsLastSibling();
        subtitle.transform.SetAsLastSibling();
        signature.transform.SetAsLastSibling();
        lastAvailable = Vector2.zero;
        root.SetActive(true);
        Canvas.ForceUpdateCanvases();
        UpdateFrames(0f);
        return true;
    }

    internal void UpdateFrames(float elapsed)
    {
        if (disposed || root == null || !root.activeSelf || presentation == null || presentation.IsDisposed || plan == null) return;
        var available = Viewport(root.transform as RectTransform);
        if ((available - lastAvailable).sqrMagnitude > 0.1f) ConfigureLayout(available);
        var progress = plan.MotionEnabled ? Mathf.Clamp01(elapsed / motionDuration) : 0f;
        motionRoot.localScale = Vector3.one * (1f + presentation.Artwork.CameraPush * progress);
        foreach (var drawable in drawables)
        {
            var frames = drawable.Art?.Frames ?? drawable.Role!.Frames;
            var seconds = Math.Max(0.01f, drawable.Art?.FrameSeconds ?? drawable.Role!.FrameSeconds);
            var loop = drawable.Art?.Loop ?? drawable.Role!.Loop;
            var raw = plan.MotionEnabled ? Math.Max(0, (int)(elapsed / seconds)) : 0;
            var index = loop ? raw % frames.Count : Math.Min(raw, frames.Count - 1);
            if (drawable.Image.sprite != frames[index]) drawable.Image.sprite = frames[index];
            var spec = drawable.Art?.Spec;
            if (spec == null) continue;
            drawable.Image.rectTransform.anchoredPosition = drawable.BasePosition
                + new Vector2(spec.MotionX * artSize.x, spec.MotionY * artSize.y) * progress;
            var alpha = spec.Opacity * (plan.MotionEnabled
                ? 1f - spec.Pulse * 0.5f + spec.Pulse * 0.5f * Mathf.Sin(elapsed * 1.2f) : 1f);
            drawable.Image.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    internal void Hide()
    {
        presentation = null;
        plan = null;
        drawables.Clear();
        if (root == null) return;
        foreach (var image in imagePool)
        {
            if (image == null) continue;
            image.sprite = null;
            image.gameObject.SetActive(false);
        }
        foreach (var window in windowPool) if (window != null) window.gameObject.SetActive(false);
        if (backgroundImage != null) backgroundImage.sprite = null;
        motionRoot.localScale = Vector3.one;
        root.SetActive(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        if (shadeSprite != null) UnityEngine.Object.Destroy(shadeSprite);
        if (shadeTexture != null) UnityEngine.Object.Destroy(shadeTexture);
        if (root != null) UnityEngine.Object.Destroy(root);
    }

    private void Add(AuraCgSceneArtLayerPresentation? art, AuraCgSceneLayerPresentation? role)
    {
        var index = drawables.Count;
        while (imagePool.Count <= index)
        {
            var obj = new GameObject("PortraitWindow." + imagePool.Count, typeof(RectTransform), typeof(RectMask2D));
            obj.transform.SetParent(motionRoot, false);
            windowPool.Add(obj.GetComponent<RectTransform>());
            windowMasks.Add(obj.GetComponent<RectMask2D>());
            imagePool.Add(CreateImage(obj.transform, "Artwork." + imagePool.Count));
        }
        var image = imagePool[index];
        image.name = art == null ? "Artwork.Role." + role!.Plan.SeatIndex
            : role == null ? "Artwork.Scene." + index : "Artwork.Attachment." + role.Plan.SeatIndex;
        windowPool[index].gameObject.SetActive(true);
        image.gameObject.SetActive(true);
        image.sprite = (art?.Frames ?? role!.Frames)[0];
        image.color = new Color(1, 1, 1, art?.Spec.Opacity ?? 1f);
        drawables.Add(new Drawable(image, windowPool[index], windowMasks[index], art, role));
    }

    private void ConfigureLayout(Vector2 available)
    {
        lastAvailable = available;
        var aspect = Math.Max(0.1f, plan!.LogicalWidth / (float)Math.Max(1, plan.LogicalHeight));
        var width = Math.Min(available.x, available.y * aspect);
        artSize = new Vector2(width, width / aspect);
        artRect.sizeDelta = artSize;
        artRect.anchoredPosition = Vector2.zero;
        var background = backgroundImage.sprite;
        if (background != null)
        {
            var scale = Math.Max(artSize.x / background.rect.width, artSize.y / background.rect.height);
            SetRect(backgroundImage.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(background.rect.width * scale, background.rect.height * scale), Vector2.zero);
        }
        foreach (var drawable in drawables)
        {
            var rect = drawable.Image.rectTransform;
            if (drawable.Role == null)
            {
                SetRect(drawable.Window, new Vector2(0.5f, 0.5f), artSize, Vector2.zero);
                drawable.Mask.enabled = false;
                SetRect(rect, new Vector2(0.5f, 0.5f), artSize, Vector2.zero);
            }
            else
            {
                var role = drawable.Role;
                var face = role.Portrait.Enabled ? role.Portrait : EstimateFace(role.VisibleBounds);
                var count = presentation!.Participants.Count;
                var windowWidth = count <= 1 ? 1.2f : count == 2 ? 0.60f : count == 3 ? 0.42f : count == 4 ? 0.32f : 0.245f;
                SetRect(drawable.Window, new Vector2(role.Plan.CenterX, role.Plan.CenterY),
                    new Vector2(artSize.x * windowWidth, artSize.y * 2f), Vector2.zero);
                drawable.Mask.enabled = count > 1;
                drawable.Mask.softness = new Vector2Int(Math.Max(6, Mathf.RoundToInt(artSize.x * 0.018f)), 0);
                var topLimit = 0.975f;
                var denseFront = count >= 5 && role.Plan.ZIndex >= 20;
                if (denseFront)
                    topLimit = presentation.Participants.Where(item => item.Plan.ZIndex < 20)
                        .Select(item => item.Plan.CenterY - item.Plan.Height * 0.6f - 0.02f).DefaultIfEmpty(topLimit).Min();
                if (denseFront)
                {
                    drawable.Window.pivot = new Vector2(0.5f, 1f - Math.Max(0.03f, topLimit - role.Plan.CenterY) * 0.5f);
                    drawable.Mask.softness = new Vector2Int(drawable.Mask.softness.x, Math.Max(4, Mathf.RoundToInt(artSize.y * 0.018f)));
                }
                var framing = AuraCgPortraitFramingMath.Fit(face, role.VisibleBounds, role.CanvasWidth, role.CanvasHeight,
                    artSize.x * role.Plan.Width * role.Plan.Scale, artSize.y * role.Plan.Height * role.Plan.Scale,
                    artSize.y * Math.Max(0.03f, 0.975f - role.Plan.CenterY));
                var mirrored = role.Plan.MirrorX && face.CanMirror;
                SetRect(rect, drawable.Window.pivot,
                    new Vector2(framing.ImageWidth, framing.ImageHeight), new Vector2(mirrored ? -framing.OffsetX : framing.OffsetX, framing.OffsetY));
                rect.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
            }
            drawable.BasePosition = rect.anchoredPosition;
        }
        AnchorText(title, new Vector2(0.05f, 0.855f), new Vector2(0.37f, 0.985f), Math.Max(18, Mathf.RoundToInt(artSize.y * 0.078f)));
        AnchorText(subtitle, new Vector2(0.053f, 0.82f), new Vector2(0.48f, 0.86f), Math.Max(10, Mathf.RoundToInt(artSize.y * 0.025f)));
        AnchorText(signature, new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.065f), Math.Max(10, Mathf.RoundToInt(artSize.y * 0.025f)));
    }

    private static AuraCgPortraitFraming EstimateFace(AuraCgNormalizedBounds bounds) => new()
    {
        Enabled = true, FaceX = bounds.X + bounds.Width * 0.5f,
        FaceY = 1f - bounds.Y - bounds.Height * 0.75f,
        FaceWidth = bounds.Width * 0.38f, FaceHeight = bounds.Height * 0.22f, CanMirror = false
    };

    private static Image CreateImage(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = false;
        return image;
    }

    private static Text CreateText(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var label = AuraUiComponents.ConfigureText(obj, "", 24, 10, TextAnchor.MiddleLeft, Color.white, resizeForBestFit: true);
        label.raycastTarget = false;
        var shadow = obj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
        shadow.effectDistance = new Vector2(1f, -1f);
        return label;
    }

    private static void AnchorText(Text label, Vector2 min, Vector2 max, int size)
    {
        label.rectTransform.anchorMin = min;
        label.rectTransform.anchorMax = max;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.fontSize = size;
        label.resizeTextMaxSize = size;
        label.resizeTextMinSize = Math.Min(size, Math.Max(9, size / 2));
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
    }

    private static Vector2 Viewport(RectTransform? rect)
    {
        if (rect != null && rect.rect.width > 1f && rect.rect.height > 1f) return rect.rect.size;
        return new Vector2(Math.Max(1, Screen.width), Math.Max(1, Screen.height));
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private sealed class Drawable
    {
        internal Drawable(Image image, RectTransform window, RectMask2D mask, AuraCgSceneArtLayerPresentation? art, AuraCgSceneLayerPresentation? role)
        { Image = image; Window = window; Mask = mask; Art = art; Role = role; }
        internal Image Image { get; }
        internal RectTransform Window { get; }
        internal RectMask2D Mask { get; }
        internal AuraCgSceneArtLayerPresentation? Art { get; }
        internal AuraCgSceneLayerPresentation? Role { get; }
        internal Vector2 BasePosition { get; set; }
    }
}
