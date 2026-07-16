using System;
using System.Collections;
using System.Collections.Generic;
using AuraCardUseFx.Shared;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Visual;

public static class StarScoreCardUseFxPresenter
{
    private static StarScoreCardUseFxRunner? runner;

    public static void Play(AuraCardUseFxSourceSnapshot sourceSnapshot, IReadOnlyList<StarScoreArrivalCue> cues, int overflowCount, string visualEffectId)
    {
        if (sourceSnapshot == null || !sourceSnapshot.IsValid || cues == null || cues.Count == 0)
        {
            return;
        }

        EnsureRunner().Play(sourceSnapshot, cues, Math.Max(0, overflowCount), visualEffectId);
    }

    public static void Clear(string source)
    {
        runner?.Clear(source);
    }

    private static StarScoreCardUseFxRunner EnsureRunner()
    {
        if (runner != null)
        {
            return runner;
        }

        var root = new GameObject("SunExp_CardUseFxRunner");
        UnityEngine.Object.DontDestroyOnLoad(root);
        runner = root.AddComponent<StarScoreCardUseFxRunner>();
        return runner;
    }
}

internal sealed class StarScoreCardUseFxRunner : MonoBehaviour
{
    private const float FaceSweepSeconds = 0.16f;
    private const float FirstEmitDelaySeconds = 0.10f;
    private const float FlightSeconds = 0.52f;
    private const float RibbonStaggerSeconds = 0.09f;
    private const float ArrivalSeconds = 0.18f;
    private const float CadenceArrivalPaddingSeconds = 0.12f;
    private const int MaxTargetRetryFrames = 12;
    private const int RibbonPoolCapacity = 8;
    private static readonly Color OuterIndigo = new(0.27f, 0.34f, 0.78f, 0.68f);
    private static readonly Color StarWhite = new(0.953f, 0.984f, 1f, 0.96f);
    private static readonly Color PaleGold = new(1f, 0.902f, 0.659f, 0.96f);
    private static readonly Color TurnAccent = new(0.91f, 0.35f, 0.68f, 0.92f);
    private static readonly Color CloseAccent = new(0.62f, 0.86f, 1f, 0.96f);

    private readonly Stack<AuraBezierRibbonGraphic> ribbonPool = new();
    private readonly HashSet<AuraBezierRibbonGraphic> activeRibbons = new();
    private RectTransform? overlayRect;
    private GameObject? overlayRoot;

    public void Play(AuraCardUseFxSourceSnapshot sourceSnapshot, IReadOnlyList<StarScoreArrivalCue> cues, int overflowCount, string visualEffectId)
    {
        if (sourceSnapshot == null || !sourceSnapshot.IsValid)
        {
            return;
        }

        EnsureOverlay();
        StartCoroutine(PlayFaceSweep(sourceSnapshot, visualEffectId));
        var lastArrival = Time.unscaledTime + FirstEmitDelaySeconds
                          + RibbonStaggerSeconds * Math.Max(0, cues.Count - 1) + FlightSeconds;
        if (HasCadenceCompletion(cues))
        {
            StarScoreHudRuntime.ExtendCadencePreviewUntil(lastArrival + CadenceArrivalPaddingSeconds);
        }

        for (var index = 0; index < cues.Count; index++)
        {
            var strength = index == cues.Count - 1 ? 1f + overflowCount * 0.28f : 1f;
            StartCoroutine(PlayRibbon(sourceSnapshot.ScreenPoint, cues[index], FirstEmitDelaySeconds + RibbonStaggerSeconds * index, strength, visualEffectId));
        }
    }

    public void Clear(string source)
    {
        StopAllCoroutines();
        activeRibbons.Clear();
        ribbonPool.Clear();
        if (overlayRoot != null)
        {
            Destroy(overlayRoot);
        }

        overlayRoot = null;
        overlayRect = null;
        SunExpLog.Debug("[CardUseFx] presentation cleared: " + source);
    }

    private IEnumerator PlayFaceSweep(AuraCardUseFxSourceSnapshot sourceSnapshot, string visualEffectId)
    {
        if (overlayRect == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                overlayRect,
                sourceSnapshot.ScreenPoint,
                null,
                out var localCenter))
        {
            yield break;
        }

        var material = CardUseFxMaterials.CreateFaceSweepMaterial(visualEffectId);
        var container = new GameObject("SunExp_CardUseFx_FaceSweepClip", typeof(RectTransform), typeof(RectMask2D));
        container.transform.SetParent(overlayRect, false);
        var containerRect = container.GetComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = localCenter;
        containerRect.sizeDelta = new Vector2(
            Mathf.Max(16f, sourceSnapshot.ScreenSize.x),
            Mathf.Max(16f, sourceSnapshot.ScreenSize.y));
        containerRect.localRotation = Quaternion.Euler(0f, 0f, sourceSnapshot.RotationZ);

        var go = new GameObject("SunExp_CardUseFx_FaceSweep", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(containerRect, false);
        go.transform.SetAsLastSibling();
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<RawImage>();
        image.texture = Texture2D.whiteTexture;
        image.material = material;
        image.raycastTarget = false;

        var fallbackSweep = material == null;
        var fallbackWidth = 0f;
        if (fallbackSweep)
        {
            fallbackWidth = Mathf.Max(1f, containerRect.rect.width);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(fallbackWidth * 0.22f, Mathf.Max(1f, containerRect.rect.height) * 1.35f);
            rect.localRotation = Quaternion.Euler(0f, 0f, -17f);
            rect.anchoredPosition = new Vector2(-fallbackWidth * 0.7f, 0f);
            image.color = new Color(0.86f, 0.94f, 1f, 0.24f);
        }

        var elapsed = 0f;
        while (elapsed < FaceSweepSeconds && container != null)
        {
            elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            var progress = Mathf.Clamp01(elapsed / FaceSweepSeconds);
            if (material != null)
            {
                image.color = new Color(1f, 1f, 1f, Mathf.Sin(progress * Mathf.PI));
            }
            else if (fallbackSweep)
            {
                rect.anchoredPosition = new Vector2(Mathf.Lerp(-fallbackWidth * 0.7f, fallbackWidth * 0.7f, progress), 0f);
                var alpha = Mathf.Sin(progress * Mathf.PI) * 0.24f;
                image.color = new Color(0.86f, 0.94f, 1f, alpha);
            }
            yield return null;
        }

        if (container != null) Destroy(container);
        if (material != null) Destroy(material);
    }

    private IEnumerator PlayRibbon(Vector2 sourceScreenPoint, StarScoreArrivalCue cue, float delay, float arrivalStrength, string visualEffectId)
    {
        var wait = 0f;
        while (wait < delay)
        {
            wait += Mathf.Max(0f, Time.unscaledDeltaTime);
            yield return null;
        }

        var retry = 0;
        while (!StarScoreHudRuntime.TryGetSlotScreenPoint(cue.SlotIndex, out _) && retry++ < MaxTargetRetryFrames)
        {
            yield return null;
        }

        if (!StarScoreHudRuntime.TryGetSlotScreenPoint(cue.SlotIndex, out var targetScreenPoint)
            || overlayRect == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, sourceScreenPoint, null, out var localStart)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, targetScreenPoint, null, out var localEnd))
        {
            yield break;
        }

        var ribbon = AcquireRibbon();
        var controls = Controls(localStart, localEnd, cue.SlotIndex, cue.Sequence);
        var scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.5f);
        ribbon.Configure(localStart, controls.First, controls.Second, localEnd, 16f * scale, 3f * scale,
            SunExpPerformanceSettings.CardUseFxRibbonSamples, 0.31f, NoteOuterColor(cue.Note), NoteCoreColor(cue.Note));
        ribbon.material = null;
        ribbon.SetProgress(0f);

        var elapsed = 0f;
        while (elapsed < FlightSeconds && ribbon != null)
        {
            elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            ribbon.SetProgress(SmootherStep(Mathf.Clamp01(elapsed / FlightSeconds)));
            yield return null;
        }

        ReleaseRibbon(ribbon);
        StarScoreHudRuntime.PulseSlot(cue.SlotIndex, arrivalStrength);
        StartCoroutine(PlayArrivalFlash(localEnd, arrivalStrength));
    }

    private IEnumerator PlayArrivalFlash(Vector2 center, float strength)
    {
        if (overlayRect == null) yield break;
        var go = new GameObject("SunExp_CardUseFx_Arrival", typeof(RectTransform), typeof(StarScoreArrivalFlashGraphic));
        go.transform.SetParent(overlayRect, false);
        Stretch(go.GetComponent<RectTransform>());
        var graphic = go.GetComponent<StarScoreArrivalFlashGraphic>();
        graphic.Configure(center, strength);
        var elapsed = 0f;
        while (elapsed < ArrivalSeconds && go != null)
        {
            elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            graphic.SetProgress(Mathf.Clamp01(elapsed / ArrivalSeconds));
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private void EnsureOverlay()
    {
        if (overlayRoot != null && overlayRect != null) return;
        overlayRoot = new GameObject("SunExp_CardUseFxOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        DontDestroyOnLoad(overlayRoot);
        overlayRect = overlayRoot.GetComponent<RectTransform>();
        Stretch(overlayRect);
        var canvas = overlayRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32740;
        var group = overlayRoot.GetComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private AuraBezierRibbonGraphic AcquireRibbon()
    {
        EnsureOverlay();
        AuraBezierRibbonGraphic ribbon;
        if (ribbonPool.Count > 0)
        {
            ribbon = ribbonPool.Pop();
            ribbon.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("SunExp_CardUseFx_Ribbon", typeof(RectTransform), typeof(AuraBezierRibbonGraphic));
            go.transform.SetParent(overlayRect, false);
            Stretch(go.GetComponent<RectTransform>());
            ribbon = go.GetComponent<AuraBezierRibbonGraphic>();
        }
        ribbon.transform.SetAsLastSibling();
        activeRibbons.Add(ribbon);
        return ribbon;
    }

    private void ReleaseRibbon(AuraBezierRibbonGraphic? ribbon)
    {
        if (ribbon == null) return;
        activeRibbons.Remove(ribbon);
        ribbon.material = null;
        ribbon.SetProgress(0f);
        ribbon.gameObject.SetActive(false);
        if (ribbonPool.Count < RibbonPoolCapacity) ribbonPool.Push(ribbon);
        else Destroy(ribbon.gameObject);
    }

    private static (Vector2 First, Vector2 Second) Controls(Vector2 start, Vector2 end, int slot, long sequence)
    {
        var delta = end - start;
        var distance = Mathf.Max(1f, delta.magnitude);
        var normal = new Vector2(-delta.y, delta.x).normalized;
        var jitter = (((sequence * 1103515245L + 12345L) & 1023L) / 1023f - 0.5f) * distance * 0.03f;
        return slot switch
        {
            0 => (start + delta * 0.28f + normal * (distance * 0.18f + jitter), start + delta * 0.72f + normal * (distance * 0.08f + jitter * 0.5f)),
            1 => (start + delta * 0.30f + normal * (distance * 0.08f + jitter), start + delta * 0.70f - normal * (distance * 0.06f - jitter * 0.5f)),
            _ => (start + delta * 0.28f - normal * (distance * 0.10f - jitter), start + delta * 0.72f - normal * (distance * 0.15f - jitter * 0.5f))
        };
    }

    private static Color NoteOuterColor(StarScoreNote note)
    {
        var accent = note switch { StarScoreNote.Sustain => PaleGold, StarScoreNote.Turn => TurnAccent, StarScoreNote.Close => CloseAccent, _ => StarWhite };
        return Color.Lerp(OuterIndigo, accent, 0.23f);
    }

    private static Color NoteCoreColor(StarScoreNote note)
    {
        return note switch
        {
            StarScoreNote.Sustain => Color.Lerp(StarWhite, PaleGold, 0.28f),
            StarScoreNote.Turn => Color.Lerp(StarWhite, TurnAccent, 0.22f),
            StarScoreNote.Close => Color.Lerp(StarWhite, CloseAccent, 0.24f),
            _ => StarWhite
        };
    }

    private static bool HasCadenceCompletion(IReadOnlyList<StarScoreArrivalCue> cues)
    {
        for (var i = 0; i < cues.Count; i++) if (cues[i].CompletesCadence) return true;
        return false;
    }

    private static float SmootherStep(float value) => value * value * value * (value * (value * 6f - 15f) + 10f);

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
