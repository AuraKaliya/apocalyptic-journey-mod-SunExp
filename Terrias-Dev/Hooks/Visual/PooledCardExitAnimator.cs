using System;
using System.Collections;
using System.Collections.Generic;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrias.Dll.Hooks.Visual;

public sealed class PooledCardExitAnimator : MonoBehaviour
{
    private static readonly string[] BurnRendererPaths =
    {
        "Front/icon",
        "Back/background",
        "Front/background",
        "Front/FrontBack",
        "Front/Icons/Ench/Item"
    };

    private readonly List<RendererBinding> burnBindings = new();
    private readonly List<TextBinding> burnTextBindings = new();
    private Coroutine? running;
    private CanvasGroup? canvasGroup;
    private SortingGroup? sortingGroup;
    private int originalSortingOrder;
    private long burnAnimationStarted;

    public bool Play(
        CardItem card,
        PooledCardExitKind kind,
        string targetPath,
        Action onComplete)
    {
        ResetVisual();
        canvasGroup = card.GetComponent<CanvasGroup>() ?? card.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        sortingGroup = card.GetComponent<SortingGroup>();
        originalSortingOrder = sortingGroup?.sortingOrder ?? 0;

        switch (kind)
        {
            case PooledCardExitKind.Burn:
                var burnBindingCount = PrepareBurnMaterials(card.transform);
                if (burnBindingCount == 0)
                {
                    TerriasPerformanceCounters.Record("PooledCardExit.BurnMaterialBindingMiss");
                    TerriasLog.Warn("[CombatCardViewPool] pooled burn has no compatible mesh renderers: "
                        + CardConfigApi.Id(card.dataConfig));
                    running = StartCoroutine(PlayBurn(card, onComplete, useBurnShader: false));
                    TerriasPerformanceCounters.Record("PooledCardExit.BurnFallbackStarted");
                    return true;
                }

                TerriasPerformanceCounters.Record("PooledCardExit.BurnMaterialBindings." + burnBindingCount);
                running = StartCoroutine(PlayBurn(card, onComplete, useBurnShader: true));
                return true;
            case PooledCardExitKind.MoveToDiscard:
            case PooledCardExitKind.MoveToDrawPile:
                var target = GameObject.Find(targetPath)?.transform;
                if (target == null)
                {
                    TerriasLog.Warn("[CombatCardViewPool] pooled exit target unavailable: " + targetPath);
                    return false;
                }

                running = StartCoroutine(PlayMove(card, target, onComplete));
                return true;
            default:
                return false;
        }
    }

    public void ResetVisual()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
            if (burnAnimationStarted > 0L)
            {
                TerriasPerformanceCounters.RecordDuration("PooledCardExit.BurnWallDurationCancelled", burnAnimationStarted);
                burnAnimationStarted = 0L;
            }
        }

        foreach (var binding in burnBindings)
        {
            binding.Restore();
        }

        foreach (var binding in burnTextBindings)
        {
            binding.Restore();
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        if (sortingGroup != null)
        {
            sortingGroup.sortingOrder = originalSortingOrder;
        }
    }

    public void RefreshTextBindings(Transform root)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        foreach (var binding in burnTextBindings)
        {
            binding.Restore();
        }

        burnTextBindings.Clear();
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            burnTextBindings.Add(new TextBinding(text));
        }

        TerriasPerformanceCounters.Record("PooledCardExit.BurnTextBindingsRefreshed");
        TerriasPerformanceCounters.RecordDuration("PooledCardExit.BurnTextBindingRefresh", start);
    }

    private IEnumerator PlayBurn(CardItem card, Action onComplete, bool useBurnShader)
    {
        burnAnimationStarted = TerriasPerformanceCounters.Timestamp();
        TryPlayAudio("Effect/burn");
        PrepareBurnTexts(card.transform);
        var rect = card.GetComponent<RectTransform>();
        var startPosition = rect.anchoredPosition;
        var moveToUsePosition = card.selectContainer != null && card.transform.parent != card.selectContainer.transform;
        var targetPosition = moveToUsePosition ? new Vector2(0f, 600f) : startPosition;
        var movementDuration = GameSpeed.Duration(0.3f);
        var burnDelay = useBurnShader ? GameSpeed.Duration(0.3f) : 0f;
        var burnDuration = useBurnShader ? GameSpeed.Duration(1.5f) : GameSpeed.Duration(0.45f);
        var duration = burnDelay + burnDuration;
        var elapsed = 0f;
        while (elapsed < duration && card != null)
        {
            var frameCpuStarted = TerriasPerformanceCounters.Timestamp();
            elapsed += Math.Max(0f, Time.deltaTime);
            var progress = Mathf.Clamp01(elapsed / duration);
            var burnProgress = Mathf.Clamp01((elapsed - burnDelay) / Math.Max(0.001f, burnDuration));
            var fade = Mathf.Lerp(50f, -90f, burnProgress);
            foreach (var binding in burnBindings)
            {
                binding.SetFade(fade);
            }

            foreach (var binding in burnTextBindings)
            {
                binding.Hide();
            }

            var movementProgress = Mathf.Clamp01(elapsed / Math.Max(0.001f, movementDuration));
            rect.anchoredPosition = Vector2.Lerp(
                startPosition,
                targetPosition,
                Mathf.SmoothStep(0f, 1f, movementProgress));
            if (canvasGroup != null)
            {
                canvasGroup.alpha = useBurnShader
                    ? 1f - Mathf.Clamp01((progress - 0.85f) / 0.15f)
                    : 1f - progress;
            }

            TerriasPerformanceCounters.RecordDuration("PooledCardExit.BurnFrameCpu", frameCpuStarted);
            yield return null;
        }

        running = null;
        TerriasPerformanceCounters.RecordDuration("PooledCardExit.BurnWallDuration", burnAnimationStarted);
        burnAnimationStarted = 0L;
        onComplete();
    }

    private IEnumerator PlayMove(CardItem card, Transform target, Action onComplete)
    {
        TryPlayAudio("Cards/cardShove");
        if (sortingGroup != null)
        {
            sortingGroup.sortingOrder = -25;
        }

        var startPosition = card.transform.position;
        var startScale = card.transform.localScale;
        var duration = 0.8f;
        var elapsed = 0f;
        while (elapsed < duration && card != null && target != null)
        {
            elapsed += Math.Max(0f, Time.deltaTime);
            var progress = Mathf.Clamp01(elapsed / duration);
            var eased = Mathf.SmoothStep(0f, 1f, progress);
            card.transform.position = Vector3.Lerp(startPosition, target.position, eased);
            card.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f - Mathf.Clamp01((progress - 0.65f) / 0.35f);
            }

            yield return null;
        }

        running = null;
        onComplete();
    }

    private int PrepareBurnMaterials(Transform root)
    {
        if (burnBindings.Count < BurnRendererPaths.Length)
        {
            var template = TerriasResourceCache.Load<Material>("Material/CardBurn", false, "combat-card-exit");
            if (template == null)
            {
                return 0;
            }

            while (burnBindings.Count < BurnRendererPaths.Length)
            {
                burnBindings.Add(new RendererBinding(new Material(template)));
            }
        }

        var canvasScale = CanvasScale();
        var bound = 0;
        for (var index = 0; index < BurnRendererPaths.Length; index++)
        {
            var renderer = root.Find(BurnRendererPaths[index])?.GetComponent<MeshRenderer>();
            if (burnBindings[index].Apply(renderer, root.position.y, canvasScale))
            {
                bound++;
            }
        }

        return bound;
    }

    private void PrepareBurnTexts(Transform root)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        if (burnTextBindings.Count == 0)
        {
            RefreshTextBindings(root);
            TerriasPerformanceCounters.Record("PooledCardExit.BurnTextEmergencyRefresh");
        }

        foreach (var binding in burnTextBindings)
        {
            binding.Capture();
            binding.Hide();
        }

        TerriasPerformanceCounters.Record("PooledCardExit.BurnTextHidden");
        TerriasPerformanceCounters.RecordDuration("PooledCardExit.BurnTextPrepare", start);
    }

    private static float CanvasScale()
    {
        return GameObject.Find("Canvas")?.GetComponent<RectTransform>()?.localScale.x ?? 1f;
    }

    private static void TryPlayAudio(string path)
    {
        try
        {
            AudioManager.Instance?.PlayEffect(path);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[CombatCardViewPool] exit audio failed: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        foreach (var binding in burnBindings)
        {
            binding.Dispose();
        }

        burnBindings.Clear();
        foreach (var binding in burnTextBindings)
        {
            binding.Restore();
        }

        burnTextBindings.Clear();
    }

    private sealed class RendererBinding
    {
        private readonly Material burnMaterial;
        private readonly AuraPresentationMaterialLeaseState materialLease = new();
        private MeshRenderer? renderer;
        private Material? originalMaterial;

        public RendererBinding(Material burnMaterial)
        {
            this.burnMaterial = burnMaterial;
        }

        public bool Apply(MeshRenderer? nextRenderer, float startY, float canvasScale)
        {
            Restore();
            renderer = nextRenderer;
            if (renderer == null)
            {
                return false;
            }

            originalMaterial = renderer.sharedMaterial;
            burnMaterial.mainTexture = originalMaterial?.mainTexture;
            SetIfPresent("_Fade", 50f);
            SetIfPresent("_canvasScale", canvasScale);
            SetIfPresent("_startY", startY);
            materialLease.Bind(
                renderer.GetInstanceID(),
                MaterialInstanceId(originalMaterial),
                MaterialInstanceId(burnMaterial));
            renderer.sharedMaterial = burnMaterial;
            return true;
        }

        public void SetFade(float value)
        {
            SetIfPresent("_Fade", value);
        }

        public void Restore()
        {
            if (renderer != null)
            {
                var targetInstanceId = renderer.GetInstanceID();
                var detach = materialLease.PlanDetach(
                    targetInstanceId,
                    MaterialInstanceId(renderer.sharedMaterial));
                if (detach.RestoreOriginal)
                {
                    renderer.sharedMaterial = originalMaterial;
                }
                else if (detach.BlockedByForeignMaterial)
                {
                    TerriasLog.WarnOnce(
                        "pooled-card-exit-material-detach-blocked:"
                        + targetInstanceId
                        + ":"
                        + materialLease.AppliedMaterialInstanceId,
                        "[CombatCardViewPool] exit animation skipped stale material restoration because a newer presentation owner is active");
                }
            }

            materialLease.Clear();
            renderer = null;
            originalMaterial = null;
        }

        public void Dispose()
        {
            Restore();
            if (burnMaterial != null)
            {
                UnityEngine.Object.Destroy(burnMaterial);
            }
        }

        private void SetIfPresent(string property, float value)
        {
            if (burnMaterial.HasProperty(property))
            {
                burnMaterial.SetFloat(property, value);
            }
        }

        private static int MaterialInstanceId(Material? material)
        {
            return material == null ? 0 : material.GetInstanceID();
        }
    }

    private sealed class TextBinding
    {
        private readonly TMP_Text text;
        private Color originalColor;
        private bool originalEnabled;
        private bool captured;

        public TextBinding(TMP_Text text)
        {
            this.text = text;
        }

        public void Capture()
        {
            if (text == null)
            {
                captured = false;
                return;
            }

            originalColor = text.color;
            originalEnabled = text.enabled;
            captured = true;
        }

        public void Hide()
        {
            if (!captured || text == null)
            {
                return;
            }

            text.enabled = false;
        }

        public void Restore()
        {
            if (captured && text != null)
            {
                text.color = originalColor;
                text.enabled = originalEnabled;
            }

            captured = false;
        }
    }
}
