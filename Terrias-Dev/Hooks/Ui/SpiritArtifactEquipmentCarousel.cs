using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactEquipmentCarousel : MonoBehaviour
{
    private const int Capacity = 5;
    private readonly List<SlotBinding> bindings = new(Capacity);
    private readonly float[] depths = new float[Capacity];
    private readonly int[] order = new int[Capacity];
    private readonly int[] lastOrder = new int[Capacity];
    private RectTransform? portrait;
    private SpiritArtifactCarouselGeometry geometry;
    private float phaseDegrees;
    private float focusStartPhase;
    private float focusDelta;
    private float focusStarted;
    private float focusDuration;
    private float automaticOriginPhase;
    private float automaticStarted;
    private bool focusing;
    private bool holdAfterFocus;
    private bool automaticRunning;
    private bool layoutDirty = true;
    private int diagnosticFrame;

    private sealed class SlotBinding
    {
        public SlotBinding(string slotId, RectTransform rect, CanvasGroup group)
        {
            SlotId = slotId;
            Rect = rect;
            Group = group;
        }

        public string SlotId { get; }
        public RectTransform Rect { get; }
        public CanvasGroup Group { get; }
    }

    private void Awake()
    {
        for (var index = 0; index < lastOrder.Length; index++) lastOrder[index] = -1;
    }

    public void ResetBindings()
    {
        portrait = null;
        bindings.Clear();
        focusing = false;
        holdAfterFocus = false;
        automaticRunning = false;
        layoutDirty = true;
        for (var index = 0; index < lastOrder.Length; index++) lastOrder[index] = -1;
        enabled = false;
    }

    public void BindPortrait(RectTransform value)
    {
        portrait = value;
        layoutDirty = true;
    }

    public void BindSlot(string slotId, RectTransform value)
    {
        if (string.IsNullOrWhiteSpace(slotId) || value == null || bindings.Count >= Capacity) return;
        var group = value.GetComponent<CanvasGroup>() ?? value.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        group.interactable = true;
        bindings.Add(new SlotBinding(slotId, value, group));
        layoutDirty = true;
    }

    public void Apply()
    {
        layoutDirty = true;
        ApplyVisuals();
        if (bindings.Count > 0 && !holdAfterFocus && !focusing) StartAutomaticCycle();
        enabled = bindings.Count > 0 && (!holdAfterFocus || focusing);
    }

    public void Focus(string slotId, bool hold, bool animate)
    {
        if (!ContainsSlot(slotId))
        {
            Resume();
            return;
        }

        if (automaticRunning)
            phaseDegrees = SampleAutomaticPhase(Time.unscaledTime).PhaseDegrees;
        automaticRunning = false;
        holdAfterFocus = hold;
        var delta = SpiritArtifactCarouselPolicy.ShortestFocusDelta(phaseDegrees, slotId);
        if (!animate || Math.Abs(delta) < 0.01f)
        {
            phaseDegrees = SpiritArtifactCarouselPolicy.FocusTargetPhase(slotId);
            focusing = false;
            ApplyVisuals();
            if (!holdAfterFocus) StartAutomaticCycle();
            enabled = !holdAfterFocus;
            return;
        }

        focusStartPhase = phaseDegrees;
        focusDelta = delta;
        focusStarted = Time.unscaledTime;
        focusDuration = SpiritArtifactCarouselPolicy.FocusDuration(delta);
        focusing = true;
        enabled = true;
    }

    public void Resume()
    {
        if (focusing)
        {
            focusing = false;
            phaseDegrees = SpiritArtifactCarouselPolicy.Normalize360(phaseDegrees);
        }
        holdAfterFocus = false;
        if (bindings.Count > 0)
        {
            StartAutomaticCycle();
            enabled = true;
        }
    }

    private void LateUpdate()
    {
        var measure = TerriasPerformanceSettings.CountersEnabled && diagnosticFrame++ % 30 == 0;
        var measurementStarted = measure ? TerriasPerformanceCounters.Timestamp() : 0L;
        if (bindings.Count == 0)
        {
            enabled = false;
            return;
        }

        if (focusing)
        {
            var progress = focusDuration <= 0f
                ? 1f
                : Mathf.Clamp01((Time.unscaledTime - focusStarted) / focusDuration);
            var eased = SpiritArtifactCarouselPolicy.EaseOutCubic(progress);
            phaseDegrees = focusStartPhase + focusDelta * eased;
            if (progress >= 1f)
            {
                phaseDegrees = SpiritArtifactCarouselPolicy.Normalize360(focusStartPhase + focusDelta);
                focusing = false;
                if (!holdAfterFocus) StartAutomaticCycle();
            }
        }
        else if (!holdAfterFocus)
        {
            if (!automaticRunning) StartAutomaticCycle();
            var automatic = SampleAutomaticPhase(Time.unscaledTime);
            phaseDegrees = automatic.PhaseDegrees;
            if (!automatic.Moving)
            {
                if (automatic.CycleComplete)
                {
                    StartAutomaticCycle();
                    ApplyVisuals();
                    TerriasPerformanceCounters.Record(
                        "SpiritArtifact.Ui.Carousel.AutomaticStepCompleted");
                }
                return;
            }
        }

        ApplyVisuals();
        if (measure)
            TerriasPerformanceCounters.RecordDuration(
                "SpiritArtifact.Ui.Carousel.LateUpdate.Sampled",
                measurementStarted);
        if (!focusing && holdAfterFocus) enabled = false;
    }

    private void OnEnable()
    {
        layoutDirty = true;
        if (!focusing && !holdAfterFocus) StartAutomaticCycle();
        ApplyVisuals();
    }

    private void OnRectTransformDimensionsChange()
    {
        layoutDirty = true;
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        if (transform is not RectTransform canvas || bindings.Count == 0) return;
        var size = canvas.rect.size;
        if (size.x <= 1f || size.y <= 1f) return;
        var geometryChanged = layoutDirty;
        if (geometryChanged)
        {
            geometry = SpiritArtifactCarouselPolicy.CalculateGeometry(size.x, size.y);
            layoutDirty = false;
            if (portrait != null)
            {
                Center(portrait);
                portrait.sizeDelta = new Vector2(geometry.PortraitWidth, geometry.PortraitHeight);
                portrait.anchoredPosition = Vector2.zero;
                portrait.localScale = Vector3.one;
                portrait.SetAsFirstSibling();
            }
        }

        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            var point = SpiritArtifactCarouselPolicy.CalculatePoint(geometry, binding.SlotId, phaseDegrees);
            if (geometryChanged)
            {
                Center(binding.Rect);
                binding.Rect.sizeDelta = new Vector2(geometry.SlotSize, geometry.SlotSize);
                binding.Rect.localRotation = Quaternion.identity;
            }
            binding.Rect.anchoredPosition = new Vector2(point.X, point.Y);
            binding.Rect.localScale = new Vector3(point.Scale, point.Scale, 1f);
            binding.Group.alpha = point.Alpha;
            depths[index] = point.Depth;
            order[index] = index;
        }
        ApplyDepthOrder();
    }

    private void ApplyDepthOrder()
    {
        for (var index = 1; index < bindings.Count; index++)
        {
            var candidate = order[index];
            var cursor = index - 1;
            while (cursor >= 0 && ComesAfter(order[cursor], candidate))
            {
                order[cursor + 1] = order[cursor];
                cursor--;
            }
            order[cursor + 1] = candidate;
        }

        var changed = false;
        for (var index = 0; index < bindings.Count; index++)
        {
            if (lastOrder[index] == order[index]) continue;
            changed = true;
            break;
        }
        if (!changed) return;

        if (portrait != null) portrait.SetAsFirstSibling();
        for (var position = 0; position < bindings.Count; position++)
        {
            bindings[order[position]].Rect.SetSiblingIndex(position + 1);
            lastOrder[position] = order[position];
        }
        TerriasPerformanceCounters.Record("SpiritArtifact.Ui.Carousel.DepthOrderChanged");
    }

    private bool ComesAfter(int leftIndex, int rightIndex)
    {
        var delta = depths[leftIndex] - depths[rightIndex];
        return delta > 0.0001f || Math.Abs(delta) <= 0.0001f && leftIndex > rightIndex;
    }

    private bool ContainsSlot(string slotId)
    {
        foreach (var binding in bindings)
            if (string.Equals(binding.SlotId, slotId, StringComparison.Ordinal)) return true;
        return false;
    }

    private void StartAutomaticCycle()
    {
        automaticOriginPhase = SpiritArtifactCarouselPolicy.Normalize360(phaseDegrees);
        automaticStarted = Time.unscaledTime;
        automaticRunning = true;
    }

    private SpiritArtifactAutomaticMotionSample SampleAutomaticPhase(float now)
    {
        var elapsed = Math.Max(0f, now - automaticStarted);
        return SpiritArtifactCarouselPolicy.SampleAutomaticMotion(
            automaticOriginPhase,
            elapsed);
    }

    private static void Center(RectTransform value)
    {
        value.anchorMin = value.anchorMax = value.pivot = new Vector2(0.5f, 0.5f);
    }
}

internal sealed class SpiritArtifactEquipmentSlotView : MonoBehaviour
{
    private Image? border;
    private Image? icon;
    private RectTransform? iconRect;
    private Button? button;
    private Action<string, string>? clicked;
    private string slotId = "";
    private string artifactUid = "";

    public void Configure(
        string nextSlotId,
        Image borderImage,
        Image iconImage,
        RectTransform iconTransform,
        Button slotButton,
        Action<string, string> click)
    {
        slotId = nextSlotId ?? "";
        border = borderImage;
        icon = iconImage;
        iconRect = iconTransform;
        button = slotButton;
        clicked = click;
        button.onClick.RemoveListener(HandleClick);
        button.onClick.AddListener(HandleClick);
    }

    public void Bind(
        string nextArtifactUid,
        Sprite? sprite,
        Color borderColor,
        Color iconColor,
        float iconSize)
    {
        artifactUid = nextArtifactUid ?? "";
        if (border != null && border.color != borderColor) border.color = borderColor;
        if (icon == null) return;
        if (iconRect != null && iconRect.sizeDelta != Vector2.one * iconSize)
            iconRect.sizeDelta = Vector2.one * iconSize;
        if (icon.sprite != sprite) icon.sprite = sprite;
        if (icon.color != iconColor) icon.color = iconColor;
    }

    private void HandleClick()
        => clicked?.Invoke(slotId, artifactUid);
}
