using System;
using System.Collections.Generic;
using System.IO;
using Terrias.Dll.Application;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using Witch.Mod;

namespace Terrias.Dll.Hooks.Ui;

public static class SpiritArtifactWishPresenter
{
    private const string RegistryKey = "SpiritArtifactWish";
    private static readonly Color One = new(0.74f, 0.77f, 0.80f, 1f);
    private static readonly Color Two = new(0.38f, 0.78f, 0.43f, 1f);
    private static readonly Color Three = new(0.29f, 0.68f, 0.94f, 1f);
    private static GameObject? root;
    private static SpiritArtifactWishController? controller;
    private static string receiptToken = "";
    private static Action? closed;

    public static void Initialize(ModConfig modConfig)
    {
        TerriasTransientUiRegistry.Register(RegistryKey, _ => Close(acknowledge: false));
    }

    public static void Play(string token, IReadOnlyList<SpiritArtifactInstance> artifacts, Action onClosed)
    {
        Close(acknowledge: false);
        receiptToken = token ?? "";
        closed = onClosed;
        root = new GameObject("Terrias_SpiritArtifactWish", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var group = root.GetComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;

        var backdrop = TerriasUiComponents.CreateFillRect("Backdrop", root.transform);
        var backdropImage = backdrop.AddComponent<Image>();
        backdropImage.color = Color.black;
        backdropImage.raycastTarget = true;
        var surface = TerriasUiComponents.CreateFillRect("VideoSurface", root.transform).AddComponent<RawImage>();
        surface.color = Color.white;
        surface.raycastTarget = false;
        var status = OverlayText(root.transform, "祈愿准备中…", new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(420f, 42f), Vector2.zero, 18, Color.white);
        var skip = CreateCornerButton(
            root.transform,
            "跳过  ›",
            new Vector2(-34f, -30f),
            () => controller?.Skip(),
            new Vector2(140f, 48f));
        controller = root.AddComponent<SpiritArtifactWishController>();
        controller.Configure(
            VideoPath(),
            surface,
            status,
            skip.gameObject,
            artifacts,
            ShowResults);
    }

    private static void ShowResults(IReadOnlyList<SpiritArtifactInstance> artifacts)
    {
        if (root == null) return;
        controller?.DisposeVideo();
        for (var index = root.transform.childCount - 1; index >= 0; index--)
            UnityEngine.Object.Destroy(root.transform.GetChild(index).gameObject);

        var background = TerriasUiComponents.CreateFillRect("ResultBackground", root.transform);
        var backgroundImage = background.AddComponent<Image>();
        backgroundImage.sprite = TerriasResourceCache.Load<Sprite>(
            VisualRegistry.TexturePath("spirit.artifact.wish.background") ?? "", true, "spirit.artifact.result-background");
        backgroundImage.type = Image.Type.Simple;
        backgroundImage.color = backgroundImage.sprite == null ? new Color(0.04f, 0.06f, 0.10f, 1f) : Color.white;
        backgroundImage.raycastTarget = true;
        OverlayText(root.transform, "圣遗物祈愿结果", new Vector2(0.5f, 0.92f), new Vector2(0.5f, 0.5f),
            new Vector2(700f, 54f), Vector2.zero, 28, new Color(1f, 0.88f, 0.55f, 1f));
        OverlayText(root.transform, "按 Esc 或点击右上角【确认并返回  ×】", new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f),
            new Vector2(620f, 42f), Vector2.zero, 16, new Color(0.88f, 0.91f, 0.96f, 0.92f));

        var row = TerriasUiComponents.CreateRect("ResultCards", root.transform,
            new Vector2(0.5f, 0.50f), new Vector2(0.5f, 0.50f), new Vector2(0.5f, 0.5f), new Vector2(1650f, 570f));
        var layout = TerriasUiComponents.ConfigureHorizontalLayout(row, new RectOffset(8, 8, 8, 8), 8f,
            childControlWidth: true, childControlHeight: true, childForceExpandHeight: true);
        layout.childForceExpandWidth = false;
        var cardWidth = Mathf.Clamp((1650f - 18f * 8f) / Math.Max(1, artifacts.Count), 92f, 148f);
        for (var index = 0; index < artifacts.Count; index++)
            CreateResultCard(row.transform, artifacts[index], index, cardWidth);

        controller?.EnableResultClose(() => Close(acknowledge: true));
        CreateCornerButton(
            root.transform,
            "确认并返回  ×",
            new Vector2(-34f, -30f),
            () => controller?.RequestClose(),
            new Vector2(176f, 48f));
        var flash = TerriasUiComponents.CreateFillRect("ResultFlash", root.transform);
        var flashImage = flash.AddComponent<Image>();
        flashImage.color = Color.white;
        flashImage.raycastTarget = false;
        flash.AddComponent<SpiritArtifactWishFlashAnimator>();
    }

    private static void CreateResultCard(Transform parent, SpiritArtifactInstance artifact, int index, float width)
    {
        var card = TerriasUiComponents.CreateFillRect("Result-" + index, parent);
        var element = card.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.minWidth = width;
        element.flexibleWidth = 0f;
        var frame = card.AddComponent<Image>();
        frame.sprite = TerriasResourceCache.Load<Sprite>(
            VisualRegistry.TexturePath("spirit.artifact.wish.result-card") ?? "", true, "spirit.artifact.result-card");
        frame.type = Image.Type.Simple;
        frame.color = artifact.Rarity >= 3 ? Three : artifact.Rarity == 2 ? Two : One;
        frame.raycastTarget = false;
        var glow = card.AddComponent<Outline>();
        glow.effectColor = frame.color;
        glow.effectDistance = new Vector2(3f, -3f);
        var piece = SpiritArtifactRegistry.Piece(artifact.PieceId);
        var iconRoot = TerriasUiComponents.CreateRect("ArtifactIcon", card.transform,
            new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero);
        var icon = iconRoot.AddComponent<Image>();
        icon.sprite = piece == null ? null : TerriasResourceCache.Load<Sprite>(piece.IconPath, true, "spirit.artifact.result-icon");
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        OverlayText(card.transform, new string('★', artifact.Rarity), new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 8f, 28f), Vector2.zero, 15, new Color(1f, 0.84f, 0.28f, 1f));
        OverlayText(card.transform, SpiritArtifactRegistry.Name(piece), new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 10f, 68f), Vector2.zero, 12, Color.white);
        OverlayText(card.transform, SpiritArtifactStats.DisplayName(artifact.MainStat.StatId) + " +" + artifact.MainStat.Value,
            new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.5f), new Vector2(width - 10f, 34f), Vector2.zero, 10, Color.white);
        var group = card.AddComponent<CanvasGroup>();
        var animator = card.AddComponent<SpiritArtifactResultRevealAnimator>();
        animator.Configure(group, index * 0.09f);
    }

    private static Button CreateCornerButton(
        Transform parent,
        string label,
        Vector2 offset,
        Action action,
        Vector2? size = null)
    {
        var buttonSize = size ?? new Vector2(120f, 42f);
        var root = TerriasUiComponents.CreateRect(
            "TextAction-" + label,
            parent,
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            buttonSize);
        var rect = (RectTransform)root.transform;
        rect.anchoredPosition = offset;
        var hitTarget = root.AddComponent<Image>();
        hitTarget.color = new Color(1f, 1f, 1f, 0.001f);
        hitTarget.raycastTarget = true;
        var button = root.AddComponent<Button>();
        button.targetGraphic = hitTarget;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => action());
        var text = TerriasUiComponents.AddTextFill(
            root.transform,
            label,
            16,
            TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.86f));
        text.fontStyle = FontStyle.Bold;
        text.raycastTarget = false;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        shadow.effectDistance = new Vector2(1f, -1f);
        var feedback = root.AddComponent<SpiritArtifactWishTextActionFeedback>();
        feedback.Configure(text, rect);
        return button;
    }

    private static Text OverlayText(Transform parent, string value, Vector2 anchor, Vector2 pivot,
        Vector2 size, Vector2 offset, int fontSize, Color color)
    {
        var go = TerriasUiComponents.CreateRect("Text", parent, anchor, anchor, pivot, size);
        var rect = go.transform as RectTransform;
        if (rect != null) rect.anchoredPosition = offset;
        var text = TerriasUiComponents.ConfigureText(go, value, fontSize, TextAnchor.MiddleCenter, color);
        text.raycastTarget = false;
        return text;
    }

    private static string VideoPath()
        => VisualRegistry.ResolveContentPath(VisualRegistry.VideoPath("spirit.artifact.wish.video") ?? "");

    private static void Close(bool acknowledge)
    {
        var callback = closed;
        closed = null;
        controller?.DisposeVideo();
        controller = null;
        if (acknowledge && receiptToken.Length > 0)
        {
            var result = SpiritArtifactApplicationService.AcknowledgeReveal(receiptToken);
            if (!result.Success)
                TerriasLog.Warn("[SpiritArtifactWish] result receipt remains pending: " + result.Reason);
        }
        receiptToken = "";
        if (root != null)
        {
            var closing = root;
            root = null;
            TerriasUiSafety.CloseTransient(closing, "SpiritArtifactWish.Close", "[SpiritArtifactWish]");
        }
        callback?.Invoke();
    }
}

internal sealed class SpiritArtifactWishController : MonoBehaviour
{
    private VideoPlayer? player;
    private RenderTexture? texture;
    private RawImage? surface;
    private Text? status;
    private GameObject? skip;
    private IReadOnlyList<SpiritArtifactInstance> artifacts = Array.Empty<SpiritArtifactInstance>();
    private Action<IReadOnlyList<SpiritArtifactInstance>>? completed;
    private Action? resultClose;
    private readonly SpiritArtifactWishNavigationState navigation = new();
    private float prepareStarted;
    private bool finished;

    public void Configure(string path, RawImage target, Text statusText, GameObject skipButton,
        IReadOnlyList<SpiritArtifactInstance> values, Action<IReadOnlyList<SpiritArtifactInstance>> onCompleted)
    {
        surface = target;
        status = statusText;
        skip = skipButton;
        artifacts = values ?? Array.Empty<SpiritArtifactInstance>();
        completed = onCompleted;
        resultClose = null;
        navigation.Reset();
        if (!File.Exists(path)) { Finish("祈愿视频缺失，直接展示结果。"); return; }
        texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
        texture.Create();
        surface.texture = texture;
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.isLooping = false;
        player.source = VideoSource.Url;
        player.url = new Uri(path).AbsoluteUri;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.aspectRatio = VideoAspectRatio.FitInside;
        player.audioOutputMode = VideoAudioOutputMode.Direct;
        player.prepareCompleted += OnPrepared;
        player.loopPointReached += OnEnded;
        player.errorReceived += OnError;
        prepareStarted = Time.unscaledTime;
        player.Prepare();
    }

    public void Skip() => Finish("");

    public void EnableResultClose(Action closeAction)
    {
        resultClose = closeAction;
        navigation.MarkResultsVisible();
    }

    public void RequestClose()
    {
        ExecuteNavigation(navigation.RequestClose());
    }

    public void DisposeVideo()
    {
        if (player != null)
        {
            player.prepareCompleted -= OnPrepared;
            player.loopPointReached -= OnEnded;
            player.errorReceived -= OnError;
            player.Stop();
            player.targetTexture = null;
            Destroy(player);
            player = null;
        }
        if (surface != null) surface.texture = null;
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
            texture = null;
        }
    }

    private void Update()
    {
        if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.Escape))
            ExecuteNavigation(navigation.RequestEscape());
        if (!finished && player != null && !player.isPrepared && Time.unscaledTime - prepareStarted > 8f)
            Finish("祈愿视频准备超时，直接展示结果。");
    }

    private void OnPrepared(VideoPlayer source)
    {
        if (status != null) status.text = "";
        source.Play();
    }

    private void OnEnded(VideoPlayer _) => Finish("");
    private void OnError(VideoPlayer _, string error) => Finish("祈愿视频无法播放：" + error);

    private void Finish(string message)
    {
        if (finished) return;
        finished = true;
        if (status != null && message.Length > 0) status.text = message;
        if (skip != null) skip.SetActive(false);
        var callback = completed;
        completed = null;
        callback?.Invoke(artifacts);
    }

    private void ExecuteNavigation(SpiritArtifactWishNavigationAction action)
    {
        if (action == SpiritArtifactWishNavigationAction.SkipToResults)
        {
            Finish("");
            return;
        }
        if (action != SpiritArtifactWishNavigationAction.AcknowledgeAndClose) return;
        var callback = resultClose;
        resultClose = null;
        callback?.Invoke();
    }

    private void OnDestroy()
    {
        resultClose = null;
        DisposeVideo();
    }
}

internal sealed class SpiritArtifactResultRevealAnimator : MonoBehaviour
{
    private CanvasGroup? group;
    private float delay;
    private float started;

    public void Configure(CanvasGroup value, float seconds)
    {
        group = value;
        delay = Math.Max(0f, seconds);
        started = Time.unscaledTime;
        group.alpha = 0f;
        transform.localScale = new Vector3(0.72f, 0.72f, 1f);
    }

    private void Update()
    {
        if (group == null) return;
        var elapsed = Time.unscaledTime - started - delay;
        if (elapsed <= 0f) return;
        var t = Mathf.Clamp01(elapsed / 0.32f);
        group.alpha = t;
        var eased = 1f - Mathf.Pow(1f - t, 3f);
        var scale = Mathf.Lerp(0.72f, 1.04f, eased);
        if (t > 0.82f) scale = Mathf.Lerp(1.04f, 1f, (t - 0.82f) / 0.18f);
        transform.localScale = new Vector3(scale, scale, 1f);
        if (t >= 1f) enabled = false;
    }
}

internal sealed class SpiritArtifactWishFlashAnimator : MonoBehaviour
{
    private Image? image;
    private float started;

    private void Awake()
    {
        image = GetComponent<Image>();
        started = Time.unscaledTime;
    }

    private void Update()
    {
        if (image == null) { Destroy(gameObject); return; }
        var t = Mathf.Clamp01((Time.unscaledTime - started) / 0.72f);
        var color = image.color;
        color.a = 1f - t;
        image.color = color;
        if (t >= 1f) Destroy(gameObject);
    }
}

internal sealed class SpiritArtifactWishTextActionFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    private Text? label;
    private RectTransform? rect;
    private bool hovered;
    private float targetAlpha = 0.86f;
    private float targetScale = 1f;

    public void Configure(Text value, RectTransform target)
    {
        label = value;
        rect = target;
        Apply(0.86f, 1f);
        enabled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        SetTargets(1f, 1f);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        SetTargets(0.86f, 1f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        SetTargets(1f, 0.94f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        SetTargets(hovered ? 1f : 0.86f, 1f);
    }

    private void SetTargets(float alpha, float scale)
    {
        targetAlpha = alpha;
        targetScale = scale;
        enabled = true;
    }

    private void Update()
    {
        if (label == null || rect == null)
        {
            enabled = false;
            return;
        }
        var currentAlpha = label.color.a;
        var currentScale = rect.localScale.x;
        var alpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.unscaledDeltaTime / 0.10f);
        var scale = Mathf.MoveTowards(currentScale, targetScale, Time.unscaledDeltaTime / 0.10f);
        Apply(alpha, scale);
        if (Mathf.Abs(alpha - targetAlpha) < 0.001f
            && Mathf.Abs(scale - targetScale) < 0.001f)
            enabled = false;
    }

    private void Apply(float alpha, float scale)
    {
        if (label != null)
        {
            var color = label.color;
            color.a = alpha;
            label.color = color;
        }
        if (rect != null) rect.localScale = new Vector3(scale, scale, 1f);
    }
}
