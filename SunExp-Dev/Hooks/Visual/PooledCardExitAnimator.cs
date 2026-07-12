using System;
using System.Collections;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SunExp.Dll.Hooks.Visual;

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
                running = StartCoroutine(PlayBurn(card, onComplete));
                return true;
            case PooledCardExitKind.MoveToDiscard:
            case PooledCardExitKind.MoveToDrawPile:
                var target = GameObject.Find(targetPath)?.transform;
                if (target == null)
                {
                    SunExpLog.Warn("[CombatCardViewPool] pooled exit target unavailable: " + targetPath);
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
                SunExpPerformanceCounters.RecordDuration("PooledCardExit.BurnAnimationCancelled", burnAnimationStarted);
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
        var start = SunExpPerformanceCounters.Timestamp();
        foreach (var binding in burnTextBindings)
        {
            binding.Restore();
        }

        burnTextBindings.Clear();
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            burnTextBindings.Add(new TextBinding(text));
        }

        SunExpPerformanceCounters.Record("PooledCardExit.BurnTextBindingsRefreshed");
        SunExpPerformanceCounters.RecordDuration("PooledCardExit.BurnTextBindingRefresh", start);
    }

    private IEnumerator PlayBurn(CardItem card, Action onComplete)
    {
        burnAnimationStarted = SunExpPerformanceCounters.Timestamp();
        TryPlayAudio("Effect/burn");
        PrepareBurnMaterials(card.transform);
        PrepareBurnTexts(card.transform);
        var rect = card.GetComponent<RectTransform>();
        var startPosition = rect.anchoredPosition;
        var duration = 1.5f;
        var elapsed = 0f;
        while (elapsed < duration && card != null)
        {
            elapsed += Math.Max(0f, Time.deltaTime);
            var progress = Mathf.Clamp01(elapsed / duration);
            var fade = Mathf.Lerp(50f, -90f, Mathf.Clamp01((progress - 0.18f) / 0.82f));
            foreach (var binding in burnBindings)
            {
                binding.SetFade(fade);
            }

            foreach (var binding in burnTextBindings)
            {
                binding.Hide();
            }

            rect.anchoredPosition = startPosition + (Vector2.up * 220f * Mathf.SmoothStep(0f, 1f, progress));
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f - Mathf.Clamp01((progress - 0.55f) / 0.45f);
            }

            yield return null;
        }

        running = null;
        SunExpPerformanceCounters.RecordDuration("PooledCardExit.BurnAnimation", burnAnimationStarted);
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

    private void PrepareBurnMaterials(Transform root)
    {
        if (burnBindings.Count < BurnRendererPaths.Length)
        {
            var template = SunExpResourceCache.Load<Material>("Material/CardBurn", false, "combat-card-exit");
            if (template == null)
            {
                return;
            }

            while (burnBindings.Count < BurnRendererPaths.Length)
            {
                burnBindings.Add(new RendererBinding(new Material(template)));
            }
        }

        var canvasScale = CanvasScale();
        for (var index = 0; index < BurnRendererPaths.Length; index++)
        {
            var renderer = root.Find(BurnRendererPaths[index])?.GetComponent<MeshRenderer>();
            burnBindings[index].Apply(renderer, root.position.y, canvasScale);
        }
    }

    private void PrepareBurnTexts(Transform root)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        if (burnTextBindings.Count == 0)
        {
            RefreshTextBindings(root);
            SunExpPerformanceCounters.Record("PooledCardExit.BurnTextEmergencyRefresh");
        }

        foreach (var binding in burnTextBindings)
        {
            binding.Capture();
            binding.Hide();
        }

        SunExpPerformanceCounters.Record("PooledCardExit.BurnTextHidden");
        SunExpPerformanceCounters.RecordDuration("PooledCardExit.BurnTextPrepare", start);
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
            SunExpLog.Debug("[CombatCardViewPool] exit audio failed: " + ex.Message);
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
        private MeshRenderer? renderer;
        private Material? originalMaterial;

        public RendererBinding(Material burnMaterial)
        {
            this.burnMaterial = burnMaterial;
        }

        public void Apply(MeshRenderer? nextRenderer, float startY, float canvasScale)
        {
            Restore();
            renderer = nextRenderer;
            if (renderer == null)
            {
                return;
            }

            originalMaterial = renderer.sharedMaterial;
            burnMaterial.mainTexture = originalMaterial?.mainTexture;
            SetIfPresent("_Fade", 50f);
            SetIfPresent("_canvasScale", canvasScale);
            SetIfPresent("_startY", startY);
            renderer.sharedMaterial = burnMaterial;
        }

        public void SetFade(float value)
        {
            SetIfPresent("_Fade", value);
        }

        public void Restore()
        {
            if (renderer != null)
            {
                renderer.sharedMaterial = originalMaterial;
            }

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
