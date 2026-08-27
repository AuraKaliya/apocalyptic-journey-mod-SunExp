using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV12.Core;

internal sealed class ReplayPovReducerV12
{
    private readonly Dictionary<string, ReplayPublicCardStateV12> cards = new(StringComparer.Ordinal);
    private long lastSequence;

    internal IReadOnlyList<ReplayPublicCardStateV12> Cards => cards.Values
        .OrderBy(item => item.Zone, StringComparer.Ordinal)
        .ThenBy(item => item.Order)
        .ThenBy(item => item.CardInstanceId, StringComparer.Ordinal)
        .Select(ReplayCanonicalJsonV12.Clone)
        .ToList();

    internal void Reset()
    {
        cards.Clear();
        lastSequence = 0;
    }

    internal void Apply(ReplayPovEventV12 value)
    {
        if (value == null || value.Sequence != lastSequence + 1)
            throw new InvalidOperationException("Replay POV event order is invalid.");
        switch (value.Kind)
        {
            case ReplayPovEventKindsV12.UpsertPrivateCard:
                if (value.Card == null || string.IsNullOrWhiteSpace(value.Card.CardInstanceId))
                    throw new InvalidOperationException("Replay POV card payload is missing.");
                cards[value.Card.CardInstanceId] = ReplayCanonicalJsonV12.Clone(value.Card);
                break;
            case ReplayPovEventKindsV12.RemovePrivateCard:
                if (string.IsNullOrWhiteSpace(value.CardInstanceId))
                    throw new InvalidOperationException("Replay POV card identity is missing.");
                cards.Remove(value.CardInstanceId);
                break;
            default:
                throw new InvalidOperationException("Unsupported replay POV event: " + value.Kind);
        }
        lastSequence = value.Sequence;
    }
}
