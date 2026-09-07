using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayCardInstructionProjectionV17 : IDisposable
{
    private readonly Transform parent;
    private readonly RectTransform canvas;
    private readonly Vector2 recordedResolution;
    private readonly IReadOnlyDictionary<string, ReplayCardDescriptorV17> descriptors;
    private readonly ReplayUiTemplateCacheV17 templates;
    private readonly List<Motion> motions = new();

    internal ReplayCardInstructionProjectionV17(
        Transform parent, RectTransform canvas, Vector2 recordedResolution,
        IReadOnlyDictionary<string, ReplayCardDescriptorV17> descriptors,
        ReplayUiTemplateCacheV17 templates)
    {
        this.parent = parent;
        this.canvas = canvas;
        this.recordedResolution = recordedResolution;
        this.descriptors = descriptors;
        this.templates = templates;
        // Recorded poses own these children; the native centre-row layout must not
        // reposition them after the manual replay clock has applied its samples.
        foreach (var layout in parent.GetComponents<LayoutGroup>()) layout.enabled = false;
        foreach (var fitter in parent.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
    }

    internal void Show(ReplayPresentationMessageV17 message, long logicalTicks)
    {
        if (!descriptors.TryGetValue(message.DescriptorId ?? "", out var descriptor))
            throw new InvalidOperationException("Recorded card motion has no card descriptor: " + message.DescriptorId);
        var samples = (message.TransformSamples ?? new List<ReplayTransformSampleV17>())
            .OrderBy(item => item.OffsetTicks).ToArray();
        if (samples.Length == 0)
            throw new InvalidOperationException("Recorded card motion has no observed trajectory: " + message.SourceInstanceId);
        for (var index = motions.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(motions[index].VisualId, message.VisualInstanceId ?? message.SourceInstanceId, StringComparison.Ordinal)) continue;
            motions[index].Dispose();
            motions.RemoveAt(index);
        }
        motions.Add(new Motion(parent, canvas, recordedResolution, templates, descriptor, message, samples, logicalTicks));
    }

    internal void Tick(long logicalTicks)
    {
        for (var index = motions.Count - 1; index >= 0; index--)
        {
            if (motions[index].Tick(logicalTicks)) continue;
            motions[index].Dispose();
            motions.RemoveAt(index);
        }
    }

    internal void Clear()
    {
        foreach (var motion in motions) motion.Dispose();
        motions.Clear();
    }

    public void Dispose() => Clear();
    internal IEnumerable<string> ActiveSourceIds => motions.Select(item => item.SourceId);

    private sealed class Motion : IDisposable
    {
        private readonly GameObject card;
        private readonly RectTransform rect;
        private readonly RectTransform canvas;
        private readonly Vector2 recordedResolution;
        private readonly CanvasGroup opacity;
        private readonly ReplayTransformSampleV17[] samples;
        private readonly long startedAt;
        private readonly long hideAt;
        private bool burnPrepared;

        internal Motion(
            Transform parent, RectTransform canvas, Vector2 recordedResolution,
            ReplayUiTemplateCacheV17 templates, ReplayCardDescriptorV17 descriptor,
            ReplayPresentationMessageV17 message, ReplayTransformSampleV17[] samples, long start)
        {
            SourceId = message.SourceInstanceId ?? "";
            VisualId = message.VisualInstanceId ?? SourceId;
            this.canvas = canvas;
            this.recordedResolution = recordedResolution;
            this.samples = samples;
            startedAt = start;
            hideAt = start + Math.Max(1L, message.DurationTicks);
            var cardState = message.CardView == null ? new ReplayVisibleCardStateV17
            {
                CardInstanceId = SourceId,
                DescriptorId = descriptor.DescriptorId,
                DisplayedCost = checked((int)Math.Max(0L, message.Value)),
                IsRevealed = true
            } : message.CardView;
            card = ReplayUiV17.CreateCard(parent, cardState, descriptor, Size(samples[0]), templates.CardTemplate);
            try
            {
                rect = card.GetComponent<RectTransform>();
                opacity = card.GetComponent<CanvasGroup>();
                if (opacity == null) opacity = card.AddComponent<CanvasGroup>();
                opacity.interactable = false;
                opacity.blocksRaycasts = false;
                Tick(start);
            }
            catch
            {
                card.SetActive(false);
                Object.Destroy(card);
                throw;
            }
        }

        internal string SourceId { get; }
        internal string VisualId { get; }

        internal bool Tick(long time)
        {
            if (time >= hideAt) return false;
            var offset = Math.Max(0L, time - startedAt);
            var rightIndex = 0;
            while (rightIndex < samples.Length && samples[rightIndex].OffsetTicks < offset) rightIndex++;
            var right = samples[Math.Min(samples.Length - 1, rightIndex)];
            var left = samples[Math.Max(0, rightIndex - 1)];
            var amount = right.OffsetTicks <= left.OffsetTicks ? 0f
                : Mathf.Clamp01((offset - left.OffsetTicks) / (float)(right.OffsetTicks - left.OffsetTicks));
            ReplayCanvasSpaceV17.Apply(rect, canvas, recordedResolution,
                Vector2.LerpUnclamped(Position(left), Position(right), amount),
                Vector2.LerpUnclamped(Size(left), Size(right), amount),
                Vector3.LerpUnclamped(ReplayPresentationPrimitivesV17.Vector(left.LocalScale),
                    ReplayPresentationPrimitivesV17.Vector(right.LocalScale), amount),
                Mathf.LerpAngle(ReplayPresentationPrimitivesV17.FromQ16(left.RotationZQ16),
                    ReplayPresentationPrimitivesV17.FromQ16(right.RotationZQ16), amount));
            opacity.alpha = Mathf.Lerp(ReplayPresentationPrimitivesV17.FromQ16(left.AlphaQ16),
                ReplayPresentationPrimitivesV17.FromQ16(right.AlphaQ16), amount);
            var burning = left.HasMaterialFade || right.HasMaterialFade && offset >= right.OffsetTicks;
            if (burning && !burnPrepared)
            {
                ReplayNativeCardPresentationApi.PrepareBurn(card.transform);
                burnPrepared = true;
            }
            if (burnPrepared)
            {
                var a = left.HasMaterialFade ? left.MaterialFadeQ16 : right.MaterialFadeQ16;
                var b = right.HasMaterialFade ? right.MaterialFadeQ16 : left.MaterialFadeQ16;
                ReplayNativeCardPresentationApi.SetBurnFade(card.transform, Mathf.Lerp(
                    ReplayPresentationPrimitivesV17.FromQ16(a), ReplayPresentationPrimitivesV17.FromQ16(b), amount));
            }
            return true;
        }

        private static Vector2 Position(ReplayTransformSampleV17 sample) => new(
            ReplayPresentationPrimitivesV17.FromQ16(sample.CanvasPosition.X),
            ReplayPresentationPrimitivesV17.FromQ16(sample.CanvasPosition.Y));

        private static Vector2 Size(ReplayTransformSampleV17 sample) => new(
            ReplayPresentationPrimitivesV17.FromQ16(sample.CanvasSize.X),
            ReplayPresentationPrimitivesV17.FromQ16(sample.CanvasSize.Y));

        public void Dispose()
        {
            if (card == null) return;
            card.SetActive(false);
            Object.Destroy(card);
        }
    }
}
