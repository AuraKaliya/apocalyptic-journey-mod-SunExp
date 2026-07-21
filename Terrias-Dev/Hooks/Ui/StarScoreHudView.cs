using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class StarScoreHudView : MonoBehaviour
{
    public const string RootName = "SunExp_StarScoreHud";

    private const float RootWidth = 148f;
    private const float RootHeight = 326f;
    private const float LayoutScale = 0.61f;
    private const float CadenceHoldSeconds = 0.58f;
    private static readonly Vector2 RootAnchoredPosition = new(6f, 42f);
    private static readonly Color MissingSpriteTint = new(0.14f, 0.14f, 0.22f, 0.18f);
    private static readonly Color HotspotTint = Color.clear;
    private static readonly float[] SlotTops = { 0f, 146f, 226f };
    private static readonly float[] SlotHeights = { 146f, 80f, 100f };

    private readonly Image[] noteIcons = new Image[3];
    private readonly CanvasGroup[] litSlotGroups = new CanvasGroup[3];
    private readonly Image[] litSlotImages = new Image[3];
    private RectTransform? rectTransform;
    private StarScoreHudTooltipView? tooltip;
    private StarScoreHudShaderController? shaderController;
    private StarScoreDisplaySnapshot? currentSnapshot;
    private StarScoreDisplaySnapshot? pendingSnapshot;
    private float holdUntil;
    private bool pointerInside;

    public static StarScoreHudView Create(Transform parent)
    {
        var go = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);

        var view = go.AddComponent<StarScoreHudView>();
        view.Build();
        return view;
    }

    public void ApplySnapshot(StarScoreDisplaySnapshot snapshot)
    {
        if (!snapshot.HasNotes)
        {
            pendingSnapshot = null;
            holdUntil = 0f;
            Render(snapshot);
            return;
        }

        if (snapshot.IsCadencePreview && snapshot.Notes.Count >= 3)
        {
            pendingSnapshot = null;
            holdUntil = Time.unscaledTime + CadenceHoldSeconds;
            Render(snapshot);
            return;
        }

        if (holdUntil > Time.unscaledTime)
        {
            pendingSnapshot = snapshot;
            return;
        }

        Render(snapshot);
    }

    public void Close(string source)
    {
        pendingSnapshot = null;
        holdUntil = 0f;
        pointerInside = false;
        CloseTooltip(source + ":tooltip");
        SunExpUiSafety.CloseTransient(gameObject, source, "[StarScoreHud]");
    }

    public bool TryGetSlotScreenPoint(int slotIndex, out Vector2 screenPoint)
    {
        screenPoint = default;
        if (slotIndex < 0 || slotIndex >= noteIcons.Length || noteIcons[slotIndex] == null)
        {
            return false;
        }

        var slotRect = noteIcons[slotIndex].rectTransform;
        var canvas = GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        screenPoint = RectTransformUtility.WorldToScreenPoint(camera, slotRect.TransformPoint(slotRect.rect.center));
        return true;
    }

    public void PulseSlot(int slotIndex, float strength)
    {
        shaderController?.PulseSlot(slotIndex, strength);
    }

    public void ExtendCadencePreviewUntil(float unscaledTime)
    {
        if (currentSnapshot is { IsCadencePreview: true } && holdUntil > Time.unscaledTime)
        {
            holdUntil = Mathf.Max(holdUntil, unscaledTime);
        }
    }

    private void Update()
    {
        if (pendingSnapshot != null && Time.unscaledTime >= holdUntil)
        {
            var snapshot = pendingSnapshot;
            pendingSnapshot = null;
            holdUntil = 0f;
            Render(snapshot);
        }
    }

    private void Build()
    {
        rectTransform = GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(RootWidth, RootHeight);
        rectTransform.anchoredPosition = RootAnchoredPosition;
        rectTransform.localScale = new Vector3(LayoutScale, LayoutScale, 1f);

        var group = GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        CreateImage(transform, "Background", Vector2.zero, new Vector2(RootWidth, RootHeight), StarScoreHudAssets.Load(StarScoreHudAssets.BackgroundPath), preserveAspect: false);
        var frameSprite = StarScoreHudAssets.Load(StarScoreHudAssets.FullPath);
        var dimFrame = CreateImage(transform, "DimFrame", Vector2.zero, new Vector2(RootWidth, RootHeight), frameSprite, preserveAspect: false);

        for (var i = 0; i < litSlotGroups.Length; i++)
        {
            litSlotImages[i] = CreateLitSlot(i, frameSprite, out litSlotGroups[i]);
        }

        shaderController = gameObject.AddComponent<StarScoreHudShaderController>();
        shaderController.Configure(dimFrame, litSlotGroups, litSlotImages);

        noteIcons[0] = CreateIcon("Icon1", 46f, 85f + 5f, 50f);
        noteIcons[1] = CreateIcon("Icon2", 46f, 165f + 6f, 49f);
        noteIcons[2] = CreateIcon("Icon3", 45f, 245f + 6f, 49f);
        CreateHoverHotspot();
        SetIconsInactive();
        tooltip = StarScoreHudTooltipView.Create(transform.parent, rectTransform);
    }

    private Image CreateLitSlot(int index, Sprite? sprite, out CanvasGroup group)
    {
        var top = SlotTops[index];
        var height = SlotHeights[index];
        var go = new GameObject("LitSlot" + (index + 1), typeof(RectTransform), typeof(RectMask2D), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(RootWidth, height);
        rect.anchoredPosition = new Vector2(0f, -top);

        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        return CreateImage(go.transform, "LitFrame", new Vector2(0f, top), new Vector2(RootWidth, RootHeight), sprite, preserveAspect: false);
    }

    private Image CreateIcon(string name, float left, float top, float size)
    {
        var innerSize = Mathf.Max(1f, size - 4f);
        return CreateImage(
            transform,
            name,
            new Vector2(left + size * 0.5f, -(top + size * 0.5f)),
            new Vector2(innerSize, innerSize),
            null,
            preserveAspect: true,
            centered: true);
    }

    private static Image CreateImage(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        Vector2 size,
        Sprite? sprite,
        bool preserveAspect,
        bool centered = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = centered ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = anchoredPosition;

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = false;
        image.color = sprite == null ? MissingSpriteTint : Color.white;
        return image;
    }

    private void CreateHoverHotspot()
    {
        var go = new GameObject("HoverHotspot", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.SetAsLastSibling();

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(RootWidth, RootHeight);
        rect.anchoredPosition = Vector2.zero;

        var image = go.AddComponent<Image>();
        image.color = HotspotTint;
        image.raycastTarget = true;

        var probe = go.AddComponent<StarScoreHudHoverProbe>();
        probe.Configure(this);
    }

    private void Render(StarScoreDisplaySnapshot snapshot)
    {
        currentSnapshot = snapshot;
        if (!snapshot.HasNotes)
        {
            tooltip?.Hide();
        }

        gameObject.SetActive(snapshot.HasNotes);
        for (var i = 0; i < noteIcons.Length; i++)
        {
            var image = noteIcons[i];
            if (i >= snapshot.Notes.Count)
            {
                image.gameObject.SetActive(false);
                continue;
            }

            var sprite = StarScoreHudAssets.IconFor(snapshot.Notes[i]);
            image.sprite = sprite;
            image.color = sprite == null ? MissingSpriteTint : Color.white;
            image.gameObject.SetActive(sprite != null);
        }

        RefreshTooltip();
        shaderController?.ApplySnapshot(snapshot);
    }

    internal void HandlePointerEntered()
    {
        pointerInside = true;
        RefreshTooltip();
    }

    internal void HandlePointerExited()
    {
        pointerInside = false;
        tooltip?.Hide();
    }

    private void RefreshTooltip()
    {
        if (!pointerInside || tooltip == null || rectTransform == null || currentSnapshot is not { HasNotes: true })
        {
            tooltip?.Hide();
            return;
        }

        tooltip.AlignTo(rectTransform);
        tooltip.Show(currentSnapshot);
    }

    private void OnDestroy()
    {
        CloseTooltip("StarScoreHudView.OnDestroy");
    }

    private void OnDisable()
    {
        pointerInside = false;
        tooltip?.Hide();
    }

    private void SetIconsInactive()
    {
        foreach (var image in noteIcons)
        {
            image.gameObject.SetActive(false);
        }
    }

    private void CloseTooltip(string source)
    {
        if (tooltip == null)
        {
            return;
        }

        var root = tooltip.gameObject;
        tooltip = null;
        SunExpUiSafety.CloseTransient(root, source, "[StarScoreHud]");
    }
}
