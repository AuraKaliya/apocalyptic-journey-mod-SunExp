using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

internal sealed class ReplayHandProjectionV17 : IDisposable
{
    private readonly GameObject root;
    private readonly RectTransform canvas;
    private readonly Vector2 recordedResolution;
    private readonly IReadOnlyDictionary<string, ReplayCardDescriptorV17> descriptors;
    private readonly ReplayUiTemplateCacheV17 templates;
    private readonly Dictionary<string, GameObject> cardsById = new(StringComparer.Ordinal);
    private HashSet<string> visibleHandIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> movingSources = new(StringComparer.Ordinal);
    private string stateHash = "";

    internal ReplayHandProjectionV17(
        Transform parent, RectTransform canvas, Vector2 recordedResolution,
        IReadOnlyDictionary<string, ReplayCardDescriptorV17> descriptors,
        ReplayUiTemplateCacheV17 templates,
        bool visible)
    {
        this.descriptors = descriptors;
        this.templates = templates;
        this.canvas = canvas;
        this.recordedResolution = recordedResolution;
        root = parent.gameObject;
        root.SetActive(visible);
    }

    internal void SetMovingSources(IEnumerable<string> sources)
    {
        movingSources.Clear();
        foreach (var source in sources) movingSources.Add(source);
        foreach (var pair in cardsById)
        {
            var show = visibleHandIds.Contains(pair.Key) && !movingSources.Contains(pair.Key);
            if (pair.Value.activeSelf != show) pair.Value.SetActive(show);
        }
    }

    internal void Apply(string perspectivePlayerId, IReadOnlyList<ReplayVisibleCardStateV17> cards)
    {
        var hand = (cards ?? Array.Empty<ReplayVisibleCardStateV17>())
            .Where(item => string.Equals(item.Zone, "Hand", StringComparison.OrdinalIgnoreCase)
                           && (string.IsNullOrWhiteSpace(perspectivePlayerId)
                               || string.Equals(item.OwnerPlayerId, perspectivePlayerId, StringComparison.Ordinal)))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.CardInstanceId, StringComparer.Ordinal)
            .ToList();
        var nextHash = ReplayCanonicalJsonV17.Sha256(hand);
        if (string.Equals(nextHash, stateHash, StringComparison.Ordinal)) return;
        stateHash = nextHash;
        var activeIds = hand.Select(item => item.CardInstanceId).ToHashSet(StringComparer.Ordinal);
        visibleHandIds = activeIds;
        foreach (var pair in cardsById) pair.Value.SetActive(activeIds.Contains(pair.Key));
        for (var index = 0; index < hand.Count; index++)
        {
            var value = hand[index];
            if (!value.HasMeasuredLayout)
                throw new InvalidOperationException("Replay hand card has no measured layout: " + value.CardInstanceId);
            descriptors.TryGetValue(value.DescriptorId ?? "", out var descriptor);
            if (descriptor == null)
                throw new InvalidOperationException("Replay hand card descriptor is missing: " + value.DescriptorId);
            _ = ReplayResourceResolverV17.RequiredSprite(
                string.IsNullOrWhiteSpace(descriptor.ResolvedSkinFrameResourcePath)
                    ? descriptor.FrameResourcePath
                    : descriptor.ResolvedSkinFrameResourcePath,
                "card-frame:" + descriptor.DescriptorId);
            if (value.IsRevealed)
                _ = ReplayResourceResolverV17.RequiredTextureOrSprite(
                    descriptor.IconResourcePath,
                    "card-artwork:" + descriptor.DescriptorId);
            if (!string.IsNullOrWhiteSpace(value.EnchantIconResourcePath))
                _ = ReplayResourceResolverV17.RequiredTextureOrSprite(
                    value.EnchantIconResourcePath,
                    "card-enchant:" + value.CardInstanceId);
            if (!cardsById.TryGetValue(value.CardInstanceId, out var card))
            {
                card = ReplayUiV17.CreateCard(
                    root.transform,
                    value,
                    descriptor,
                    new Vector2(
                        ReplayPresentationPrimitivesV17.FromQ16(value.CanvasSize.X),
                        ReplayPresentationPrimitivesV17.FromQ16(value.CanvasSize.Y)),
                    templates.CardTemplate);
                cardsById[value.CardInstanceId] = card;
            }
            ReplayUiV17.UpdateCard(card, value, descriptor);
            var rect = card.GetComponent<RectTransform>();
            ReplayCanvasSpaceV17.Apply(rect, canvas, recordedResolution, new Vector2(
                ReplayPresentationPrimitivesV17.FromQ16(value.CanvasPosition.X),
                ReplayPresentationPrimitivesV17.FromQ16(value.CanvasPosition.Y)), new Vector2(
                ReplayPresentationPrimitivesV17.FromQ16(value.CanvasSize.X),
                ReplayPresentationPrimitivesV17.FromQ16(value.CanvasSize.Y)),
                ReplayPresentationPrimitivesV17.Vector(value.LocalScale),
                ReplayPresentationPrimitivesV17.FromQ16(value.RotationZQ16));
            card.transform.SetSiblingIndex(index);
            card.SetActive(true);
        }
    }

    internal void SetVisible(bool visible) => root.SetActive(visible);

    public void Dispose()
    {
        foreach (var value in cardsById.Values)
            if (value != null) Object.Destroy(value);
        cardsById.Clear();
        if (root != null) root.SetActive(false);
    }
}

