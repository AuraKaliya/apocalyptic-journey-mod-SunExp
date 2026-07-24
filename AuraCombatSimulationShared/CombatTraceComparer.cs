using System;
using System.Collections.Generic;

namespace AuraCombatSimulation.Shared;

public sealed class CombatTraceDifference
{
    public int Index { get; set; }

    public long ExpectedSequence { get; set; }

    public long ActualSequence { get; set; }

    public string Field { get; set; } = "";

    public string Expected { get; set; } = "";

    public string Actual { get; set; } = "";
}

public sealed class CombatTraceComparisonResult
{
    public bool Equivalent { get; set; }

    public int ComparedEvents { get; set; }

    public CombatTraceDifference? FirstDifference { get; set; }
}

public static class CombatTraceComparer
{
    public static CombatTraceComparisonResult Compare(
        IReadOnlyList<CombatSimulationEvent>? expected,
        IReadOnlyList<CombatSimulationEvent>? actual,
        bool compareStateHashes = true)
    {
        expected ??= Array.Empty<CombatSimulationEvent>();
        actual ??= Array.Empty<CombatSimulationEvent>();
        var count = Math.Min(expected.Count, actual.Count);
        for (var i = 0; i < count; i++)
        {
            var left = expected[i];
            var right = actual[i];
            var difference =
                Difference(i, left, right, "Kind", left.Kind.ToString(), right.Kind.ToString())
                ?? Difference(i, left, right, "Turn", left.Turn.ToString(), right.Turn.ToString())
                ?? Difference(i, left, right, "Phase", left.Phase.ToString(), right.Phase.ToString())
                ?? Difference(i, left, right, "SourceActorId", left.SourceActorId.ToString(), right.SourceActorId.ToString())
                ?? Difference(i, left, right, "TargetActorId", left.TargetActorId.ToString(), right.TargetActorId.ToString())
                ?? Difference(i, left, right, "CardInstanceId", left.CardInstanceId.ToString(), right.CardInstanceId.ToString())
                ?? Difference(i, left, right, "DefinitionId", left.DefinitionId, right.DefinitionId)
                ?? Difference(i, left, right, "Amount", left.Amount.ToString(), right.Amount.ToString())
                ?? Difference(i, left, right, "RandomStreamId", left.RandomStreamId, right.RandomStreamId)
                ?? Difference(i, left, right, "RandomCounter", left.RandomCounter.ToString(), right.RandomCounter.ToString())
                ?? Difference(i, left, right, "RandomValue", left.RandomValue.ToString(), right.RandomValue.ToString());
            if (difference == null
                && compareStateHashes
                && (!string.IsNullOrWhiteSpace(left.AfterHash)
                    || !string.IsNullOrWhiteSpace(right.AfterHash)))
            {
                difference = Difference(i, left, right, "AfterHash", left.AfterHash, right.AfterHash);
            }
            if (difference != null)
            {
                return new CombatTraceComparisonResult
                {
                    ComparedEvents = i,
                    FirstDifference = difference
                };
            }
        }

        if (expected.Count != actual.Count)
        {
            return new CombatTraceComparisonResult
            {
                ComparedEvents = count,
                FirstDifference = new CombatTraceDifference
                {
                    Index = count,
                    ExpectedSequence = count < expected.Count ? expected[count].Sequence : 0,
                    ActualSequence = count < actual.Count ? actual[count].Sequence : 0,
                    Field = "EventCount",
                    Expected = expected.Count.ToString(),
                    Actual = actual.Count.ToString()
                }
            };
        }
        return new CombatTraceComparisonResult
        {
            Equivalent = true,
            ComparedEvents = count
        };
    }

    private static CombatTraceDifference? Difference(
        int index,
        CombatSimulationEvent expected,
        CombatSimulationEvent actual,
        string field,
        string expectedValue,
        string actualValue)
    {
        return string.Equals(expectedValue ?? "", actualValue ?? "", StringComparison.Ordinal)
            ? null
            : new CombatTraceDifference
            {
                Index = index,
                ExpectedSequence = expected.Sequence,
                ActualSequence = actual.Sequence,
                Field = field,
                Expected = expectedValue ?? "",
                Actual = actualValue ?? ""
            };
    }
}
