using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class DifficultyFieldCandidate
{
    public DifficultyFieldCandidate(string hardTagId, SunExpFieldId field, int stacks)
    {
        HardTagId = hardTagId ?? "";
        Field = field;
        Stacks = Math.Max(0, stacks);
    }

    public string HardTagId { get; }

    public SunExpFieldId Field { get; }

    public int Stacks { get; }
}

public static class DifficultyFieldPoolService
{
    private const int DifficultyTagMaxStacks = 4;

    private static readonly IReadOnlyList<DifficultyFieldSourceDefinition> Definitions =
        new[]
        {
            new DifficultyFieldSourceDefinition(
                SunExpHardTagIds.ScorchedWorld,
                SunExpFieldId.ScorchingCanopy),
            new DifficultyFieldSourceDefinition(
                SunExpHardTagIds.SamsaraGarden,
                SunExpFieldId.SamsaraGarden)
        };

    public static IReadOnlyList<DifficultyFieldCandidate> BuildCandidates()
    {
        var byField = new Dictionary<SunExpFieldId, DifficultyFieldCandidate>();
        foreach (var definition in Definitions)
        {
            var level = Math.Min(
                DifficultyTagMaxStacks,
                Math.Max(0, SunExpHardTagState.Level(definition.HardTagId)));
            if (level <= 0)
            {
                continue;
            }

            if (byField.TryGetValue(definition.Field, out var current))
            {
                level = Math.Min(
                    FieldEffectRegistry.MaxStacks(definition.Field),
                    current.Stacks + level);
            }

            byField[definition.Field] = new DifficultyFieldCandidate(
                definition.HardTagId,
                definition.Field,
                level);
        }

        return byField.Values
            .OrderBy(candidate => (int)candidate.Field)
            .ToArray();
    }

    public static DifficultyFieldCandidate? DrawEqualCandidate(string source)
    {
        var candidates = BuildCandidates();
        if (candidates.Count == 0)
        {
            return null;
        }

        var selected = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        SunExpLog.Debug("[DifficultyFieldPool] selected "
            + FieldEffectRegistry.FieldSlug(selected.Field)
            + " "
            + selected.Stacks
            + "/"
            + FieldEffectRegistry.MaxStacks(selected.Field)
            + " from "
            + candidates.Count
            + " equal candidates; source="
            + (source ?? ""));
        return selected;
    }

    private sealed class DifficultyFieldSourceDefinition
    {
        public DifficultyFieldSourceDefinition(string hardTagId, SunExpFieldId field)
        {
            HardTagId = hardTagId;
            Field = field;
        }

        public string HardTagId { get; }

        public SunExpFieldId Field { get; }
    }
}
