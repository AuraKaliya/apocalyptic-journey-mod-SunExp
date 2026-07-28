using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSeedPlan
{
    public ulong RunSeed { get; set; }

    public ulong TrainingSeedStart { get; set; }

    public ulong ArenaSeedStart { get; set; }

    public ulong TuningSeedStart { get; set; }

    public ulong ValidationSeedStart { get; set; }

    public int ModelRandomSeed { get; set; }

    public static CombatFoundationSeedPlan Create(
        ulong runSeed,
        ulong canonicalValidationSeedStart)
    {
        var effectiveRunSeed = runSeed == 0UL
            ? 0x415552415F414931UL
            : runSeed;
        return new CombatFoundationSeedPlan
        {
            RunSeed = effectiveRunSeed,
            TrainingSeedStart =
                0x1000000000000000UL
                | (Mix(effectiveRunSeed ^ 0x545241494E494E47UL)
                   & 0x0FFFFFFFFFFFFFFFUL),
            ArenaSeedStart =
                0x3000000000000000UL
                | (Mix(effectiveRunSeed ^ 0x4152454E41534545UL)
                   & 0x0FFFFFFFFFFFFFFFUL),
            TuningSeedStart =
                0x2000000000000000UL
                | (Mix(effectiveRunSeed ^ 0x54554E494E475345UL)
                   & 0x0FFFFFFFFFFFFFFFUL),
            ValidationSeedStart = canonicalValidationSeedStart,
            ModelRandomSeed = unchecked(
                (int)(Mix(effectiveRunSeed ^ 0x4D4F44454C534545UL)
                      & 0x7FFFFFFFUL))
        };
    }

    internal static ulong Mix(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    internal static int ToRandomSeed(ulong value)
    {
        return unchecked((int)(Mix(value) & 0x7FFFFFFFUL));
    }
}

public static class CombatFoundationCurriculum
{
    public sealed class Plan
    {
        public string Stage { get; set; } = "normal-focus";

        public double NormalWilsonLowerBound { get; set; }

        public double AdvancedWilsonLowerBound { get; set; }

        public double AdvancedShare { get; set; }

        public double MinimumAdvancedShare { get; set; }

        public double MaximumAdvancedShare { get; set; }
    }

    public static Plan Evaluate(
        bool enabled,
        int normalWins,
        int normalTrials,
        int advancedWins,
        int advancedTrials)
    {
        return Evaluate(
            enabled,
            iteration: 0,
            normalWins,
            normalTrials,
            advancedWins,
            advancedTrials);
    }

    public static Plan Evaluate(
        bool enabled,
        int iteration,
        int normalWins,
        int normalTrials,
        int advancedWins,
        int advancedTrials)
    {
        if (!enabled)
        {
            return new Plan
            {
                Stage = "disabled-balanced",
                NormalWilsonLowerBound = WilsonLowerBound(
                    normalWins,
                    normalTrials),
                AdvancedWilsonLowerBound = WilsonLowerBound(
                    advancedWins,
                    advancedTrials),
                AdvancedShare = 0.5d,
                MinimumAdvancedShare = 0.5d,
                MaximumAdvancedShare = 0.5d
            };
        }
        var normalLower = WilsonLowerBound(normalWins, normalTrials);
        var advancedLower = WilsonLowerBound(advancedWins, advancedTrials);
        if (iteration <= 0)
        {
            return new Plan
            {
                Stage = "normal-foundation",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0d,
                MinimumAdvancedShare = 0d,
                MaximumAdvancedShare = 0d
            };
        }
        if (iteration <= 2)
        {
            return new Plan
            {
                Stage = "advanced-introduction",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.25d,
                MinimumAdvancedShare = 0.25d,
                MaximumAdvancedShare = 0.25d
            };
        }
        if (normalLower < 0.75d)
        {
            return new Plan
            {
                Stage = "normal-recovery-with-advanced-floor",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.35d,
                MinimumAdvancedShare = 0.30d,
                MaximumAdvancedShare = 0.45d
            };
        }
        if (advancedLower < 0.25d)
        {
            return new Plan
            {
                Stage = "advanced-recovery",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.35d,
                MinimumAdvancedShare = 0.15d,
                MaximumAdvancedShare = 0.35d
            };
        }
        if (advancedLower < 0.50d)
        {
            return new Plan
            {
                Stage = "advanced-strengthening",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.30d,
                MinimumAdvancedShare = 0.15d,
                MaximumAdvancedShare = 0.35d
            };
        }
        return new Plan
        {
            Stage = advancedLower >= 0.70d
                ? "balanced-maintenance"
                : "mixed-mastery",
            NormalWilsonLowerBound = normalLower,
            AdvancedWilsonLowerBound = advancedLower,
            AdvancedShare = advancedLower >= 0.70d ? 0.20d : 0.25d,
            MinimumAdvancedShare = 0.15d,
            MaximumAdvancedShare = 0.35d
        };
    }

    public static IReadOnlyList<string> BuildDifficulties(
        int campaignCount,
        int iteration,
        int totalIterations,
        ulong runSeed,
        bool enabled,
        double priorNormalWinRate = double.NaN,
        int priorNormalTrials = 0,
        double priorAdvancedWinRate = double.NaN,
        int priorAdvancedTrials = 0)
    {
        var count = Math.Max(0, campaignCount);
        if (count == 0)
        {
            return Array.Empty<string>();
        }

        var normalWins = double.IsNaN(priorNormalWinRate)
            ? 0
            : (int)Math.Round(
                Math.Max(0d, Math.Min(1d, priorNormalWinRate))
                * Math.Max(0, priorNormalTrials));
        var advancedWins = double.IsNaN(priorAdvancedWinRate)
            ? 0
            : (int)Math.Round(
                Math.Max(0d, Math.Min(1d, priorAdvancedWinRate))
                * Math.Max(0, priorAdvancedTrials));
        var plan = Evaluate(
            enabled,
            iteration,
            normalWins,
            priorNormalTrials,
            advancedWins,
            priorAdvancedTrials);
        var advancedCount = (int)Math.Round(
            count * plan.AdvancedShare,
            MidpointRounding.AwayFromZero);
        if (plan.AdvancedShare > 0d && advancedCount == 0)
        {
            advancedCount = 1;
        }
        if (plan.AdvancedShare < 1d && count > 1)
        {
            advancedCount = Math.Min(count - 1, advancedCount);
        }

        var advancedIndexes = Enumerable.Range(0, count)
            .Select(index => new
            {
                Index = index,
                Rank = CombatFoundationSeedPlan.Mix(
                    runSeed
                    ^ ((ulong)Math.Max(0, iteration) << 32)
                    ^ (ulong)index)
            })
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Index)
            .Take(advancedCount)
            .Select(item => item.Index)
            .ToHashSet();
        return Enumerable.Range(0, count)
            .Select(index => advancedIndexes.Contains(index)
                ? "advanced"
                : "normal")
            .ToList();
    }

    public static double AdvancedShare(int iteration, int totalIterations)
    {
        if (iteration <= 0)
        {
            return 0d;
        }
        if (iteration <= 2)
        {
            return 0.25d;
        }
        return 0.35d;
    }

    public static double WilsonLowerBound(
        int successes,
        int trials,
        double z = 1.959963984540054d)
    {
        if (trials <= 0)
        {
            return 0d;
        }
        var n = Math.Max(1, trials);
        var p = Math.Max(0d, Math.Min(1d, successes / (double)n));
        var z2 = z * z;
        var denominator = 1d + z2 / n;
        var centre = p + z2 / (2d * n);
        var margin = z * Math.Sqrt(
            (p * (1d - p) + z2 / (4d * n)) / n);
        return Math.Max(0d, Math.Min(1d, (centre - margin) / denominator));
    }

    public static double ExplorationProbability(
        Plan plan,
        double configured)
    {
        var value = Math.Max(0d, Math.Min(1d, configured));
        if (value <= 0d)
        {
            return 0d;
        }
        return plan.Stage switch
        {
            "normal-foundation" => Math.Max(0.20d, value),
            "normal-recovery-with-advanced-floor" => Math.Max(0.18d, value),
            "advanced-introduction" => Math.Max(0.15d, value),
            "mixed-mastery" => Math.Min(0.12d, value),
            "advanced-recovery" => Math.Min(0.12d, value),
            "advanced-strengthening" => Math.Min(0.10d, value),
            "balanced-maintenance" => Math.Min(0.08d, value),
            _ => value
        };
    }
}

public sealed class CombatFoundationHardSeed
{
    public ulong WorldSeed { get; set; }

    public string DifficultyId { get; set; } = "normal";

    public string TerminalScenarioId { get; set; } = "";

    public int CompletedBattles { get; set; }

    public string SourceCategory { get; set; } = "hard-diversity";

    public double PriorityScore { get; set; }

    public CombatCampaignCheckpoint? FailureEncounterCheckpoint { get; set; }
}

public sealed class CombatFoundationHardSeedPlan
{
    public int SourceCampaigns { get; set; }

    public List<CombatFoundationHardSeed> Seeds { get; set; } = new();

    public Dictionary<string, int> Clusters { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> SourceCategories { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class CombatFoundationHardSeedHistoryEntry
{
    public ulong WorldSeed { get; set; }

    public string DifficultyId { get; set; } = "normal";

    public string TerminalScenarioId { get; set; } = "";

    public int CompletedBattles { get; set; }

    public int FirstSeenIteration { get; set; }

    public int LastSeenIteration { get; set; }

    public int FailureOccurrences { get; set; }

    public int TrainingAttempts { get; set; }

    public int RecoverySuccesses { get; set; }

    public int LastTrainedIteration { get; set; }

    public bool Resolved { get; set; }

    public CombatCampaignCheckpoint? FailureEncounterCheckpoint { get; set; }
}

public static class CombatFoundationHardSeedCurriculum
{
    public static CombatFoundationHardSeedPlan Select(
        IEnumerable<CombatEpisode> source,
        int campaignCount,
        double replayShare,
        int iteration,
        ulong runSeed,
        bool enabled)
    {
        var history = BuildHistory(source, iteration);
        return Select(
            history,
            campaignCount,
            replayShare,
            iteration,
            runSeed,
            enabled);
    }

    public static CombatFoundationHardSeedPlan Select(
        IEnumerable<CombatFoundationHardSeedHistoryEntry> source,
        int campaignCount,
        double replayShare,
        int iteration,
        ulong runSeed,
        bool enabled)
    {
        var plan = new CombatFoundationHardSeedPlan();
        if (!enabled || campaignCount <= 0 || replayShare <= 0d)
        {
            return plan;
        }

        var campaigns = (source
                         ?? Array.Empty<CombatFoundationHardSeedHistoryEntry>())
            .Where(item => item != null
                           && item.WorldSeed > 0UL
                           && !item.Resolved
                           && item.FailureOccurrences > 0
                           && (item.TrainingAttempts < 2
                               || item.RecoverySuccesses > 0
                               || item.LastTrainedIteration
                                  < Math.Max(0, iteration - 1)))
            .GroupBy(item => new
            {
                item.WorldSeed,
                Difficulty = NormalizeDifficulty(item.DifficultyId)
            })
            .Select(group => group
                .OrderByDescending(item => item.LastSeenIteration)
                .ThenByDescending(item => item.FailureOccurrences)
                .First())
            .OrderBy(item => item.WorldSeed)
            .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
            .ToList();
        plan.SourceCampaigns = campaigns.Count;
        if (campaigns.Count == 0)
        {
            return plan;
        }

        var target = Math.Min(
            campaignCount,
            Math.Max(
                1,
                (int)Math.Round(
                    campaignCount
                    * Math.Max(0d, Math.Min(0.75d, replayShare)),
                    MidpointRounding.AwayFromZero)));
        var all = campaigns
            .Select(item => ToSeed(item, iteration, runSeed))
            .ToList();
        var recurrent = all
            .GroupBy(item => ClusterKey(item.TerminalScenarioId),
                StringComparer.Ordinal)
            .OrderByDescending(group => group.Sum(item => item.PriorityScore))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(item => item.PriorityScore)
                .ThenBy(item => StableRank(item, iteration, runSeed))
                .ThenBy(item => item.WorldSeed)
                .Take(4))
            .ToList();
        var recent = all
            .Where(item => HistoryFor(campaigns, item).LastSeenIteration
                           >= Math.Max(0, iteration - 1))
            .OrderByDescending(item => item.PriorityScore)
            .ThenBy(item => StableRank(item, iteration, runSeed))
            .ToList();
        var late = all
            .Where(item => item.CompletedBattles >= 21
                           || IsBossTerminal(item.TerminalScenarioId))
            .OrderByDescending(item => item.CompletedBattles)
            .ThenByDescending(item => item.PriorityScore)
            .ThenBy(item => StableRank(item, iteration, runSeed))
            .ToList();
        var diversity = all
            .OrderBy(item => StableRank(item, iteration, runSeed))
            .ThenByDescending(item => item.PriorityScore)
            .ToList();

        var quotas = AllocateQuotas(target, new[]
        {
            0.50d, 0.25d, 0.15d, 0.10d
        });
        AddCategory(plan, recurrent, quotas[0], "hard-recurrent");
        AddCategory(plan, recent, quotas[1], "hard-recent");
        AddCategory(plan, late, quotas[2], "hard-late-boss");
        AddCategory(plan, diversity, quotas[3], "hard-diversity");
        if (plan.Seeds.Count < target)
        {
            AddCategory(
                plan,
                all.OrderByDescending(item => item.PriorityScore)
                    .ThenBy(item => StableRank(item, iteration, runSeed)),
                target - plan.Seeds.Count,
                "hard-fill");
        }
        return plan;
    }

    public static List<CombatFoundationHardSeedHistoryEntry> BuildHistory(
        IEnumerable<CombatEpisode> source,
        int iteration)
    {
        return (source ?? Array.Empty<CombatEpisode>())
            .Where(episode =>
                episode?.Campaign != null
                && episode.Campaign.WorldSeed > 0UL
                && episode.Campaign.IntegrityValid
                && string.Equals(
                    episode.Campaign.OutcomeClass,
                    "defeat",
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(episode => new
            {
                episode.Campaign.WorldSeed,
                Difficulty = NormalizeDifficulty(
                    episode.Campaign.DifficultyId)
            })
            .Select(group =>
            {
                var terminal = group
                    .OrderByDescending(item => item.JourneyBattleIndex)
                    .First();
                return new CombatFoundationHardSeedHistoryEntry
                {
                    WorldSeed = group.Key.WorldSeed,
                    DifficultyId = group.Key.Difficulty,
                    TerminalScenarioId =
                        terminal.Campaign.TerminalScenarioId ?? "",
                    CompletedBattles =
                        terminal.Campaign.CampaignCompletedBattles,
                    FirstSeenIteration = Math.Max(
                        0,
                        terminal.Campaign.TrainingIteration),
                    LastSeenIteration = Math.Max(
                        Math.Max(0, terminal.Campaign.TrainingIteration),
                        iteration),
                    FailureOccurrences = 1
                };
            })
            .ToList();
    }

    private static CombatFoundationHardSeed ToSeed(
        CombatFoundationHardSeedHistoryEntry item,
        int iteration,
        ulong runSeed)
    {
        var unresolvedAge = Math.Max(0, iteration - item.FirstSeenIteration);
        var recurrence = Math.Max(1, item.FailureOccurrences);
        var severity = item.CompletedBattles >= 31
            ? 4d
            : item.CompletedBattles >= 21
                ? 2d
                : 0d;
        return new CombatFoundationHardSeed
        {
            WorldSeed = item.WorldSeed,
            DifficultyId = NormalizeDifficulty(item.DifficultyId),
            TerminalScenarioId = item.TerminalScenarioId ?? "",
            CompletedBattles = item.CompletedBattles,
            FailureEncounterCheckpoint = item.FailureEncounterCheckpoint,
            PriorityScore = recurrence * 10d
                            + unresolvedAge * 2d
                            + severity
                            + (IsBossTerminal(item.TerminalScenarioId) ? 5d : 0d)
                            + (CombatFoundationSeedPlan.Mix(
                                   runSeed ^ item.WorldSeed) & 0xFFFFUL)
                              / 65536d
        };
    }

    private static CombatFoundationHardSeedHistoryEntry HistoryFor(
        IReadOnlyList<CombatFoundationHardSeedHistoryEntry> history,
        CombatFoundationHardSeed seed)
    {
        return history.First(item =>
            item.WorldSeed == seed.WorldSeed
            && string.Equals(
                NormalizeDifficulty(item.DifficultyId),
                seed.DifficultyId,
                StringComparison.Ordinal));
    }

    private static ulong StableRank(
        CombatFoundationHardSeed item,
        int iteration,
        ulong runSeed)
    {
        return CombatFoundationSeedPlan.Mix(
            runSeed
            ^ item.WorldSeed
            ^ ((ulong)Math.Max(0, iteration) << 32));
    }

    private static int[] AllocateQuotas(int total, IReadOnlyList<double> shares)
    {
        var result = new int[shares.Count];
        var fractions = new List<(int Index, double Fraction)>();
        var assigned = 0;
        for (var index = 0; index < shares.Count; index++)
        {
            var exact = Math.Max(0d, shares[index]) * Math.Max(0, total);
            result[index] = (int)Math.Floor(exact);
            assigned += result[index];
            fractions.Add((index, exact - result[index]));
        }
        foreach (var item in fractions
                     .OrderByDescending(item => item.Fraction)
                     .ThenBy(item => item.Index)
                     .Take(Math.Max(0, total - assigned)))
        {
            result[item.Index]++;
        }
        return result;
    }

    private static void AddCategory(
        CombatFoundationHardSeedPlan plan,
        IEnumerable<CombatFoundationHardSeed> source,
        int count,
        string category)
    {
        var added = 0;
        foreach (var item in source)
        {
            if (added >= count)
            {
                break;
            }
            if (plan.Seeds.Any(existing =>
                    existing.WorldSeed == item.WorldSeed
                    && string.Equals(
                        existing.DifficultyId,
                        item.DifficultyId,
                        StringComparison.Ordinal)))
            {
                continue;
            }
            item.SourceCategory = category;
            plan.Seeds.Add(item);
            added++;
            var cluster = ClusterKey(item.TerminalScenarioId);
            plan.Clusters[cluster] = plan.Clusters.TryGetValue(
                cluster,
                out var clusterCount)
                ? clusterCount + 1
                : 1;
            plan.SourceCategories[category] =
                plan.SourceCategories.TryGetValue(category, out var current)
                    ? current + 1
                    : 1;
        }
    }

    private static bool IsBossTerminal(string? value)
    {
        var normalized = value ?? "";
        return normalized.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf(
                   "final",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeDifficulty(string value)
    {
        return string.Equals(
            value,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
    }

    private static string ClusterKey(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown-terminal"
            : value.Trim();
    }
}

public sealed class CombatFoundationTrainingSlot
{
    public int Index { get; set; }

    public ulong WorldSeed { get; set; }

    public string DifficultyId { get; set; } = "normal";

    public string SourceCategory { get; set; } = "fresh-normal";

    public string FailureCluster { get; set; } = "";

    public double PriorityScore { get; set; }

    public bool HardSeed { get; set; }

    public CombatCampaignCheckpoint? FailureEncounterCheckpoint { get; set; }
}

public static class CombatFoundationTrainingSchedule
{
    public static IReadOnlyList<CombatFoundationTrainingSlot> Build(
        int campaignCount,
        ulong freshSeedStart,
        ulong runSeed,
        int iteration,
        CombatFoundationCurriculum.Plan curriculum,
        CombatFoundationHardSeedPlan hardSeeds)
    {
        var count = Math.Max(0, campaignCount);
        if (count == 0)
        {
            return Array.Empty<CombatFoundationTrainingSlot>();
        }
        var result = Enumerable.Range(0, count)
            .Select(index => new CombatFoundationTrainingSlot
            {
                Index = index,
                WorldSeed = freshSeedStart + (ulong)index
            })
            .ToArray();
        var hardSeedItems = hardSeeds?.Seeds
                            ?? new List<CombatFoundationHardSeed>();
        var hardPositions = Enumerable.Range(0, count)
            .OrderBy(index => CombatFoundationSeedPlan.Mix(
                runSeed
                ^ ((ulong)Math.Max(0, iteration) << 32)
                ^ (ulong)index
                ^ 0x48415244534C4F54UL))
            .Take(Math.Min(count, hardSeedItems.Count))
            .ToArray();
        for (var index = 0; index < hardPositions.Length; index++)
        {
            var hard = hardSeedItems[index];
            result[hardPositions[index]] = new CombatFoundationTrainingSlot
            {
                Index = hardPositions[index],
                WorldSeed = hard.WorldSeed,
                DifficultyId = NormalizeDifficulty(hard.DifficultyId),
                SourceCategory = hard.SourceCategory,
                FailureCluster = string.IsNullOrWhiteSpace(
                    hard.TerminalScenarioId)
                    ? "unknown-terminal"
                    : hard.TerminalScenarioId,
                PriorityScore = hard.PriorityScore,
                HardSeed = true,
                FailureEncounterCheckpoint =
                    hard.FailureEncounterCheckpoint
            };
        }

        var desiredAdvanced = (int)Math.Round(
            count * Math.Max(
                curriculum.MinimumAdvancedShare,
                Math.Min(
                    curriculum.MaximumAdvancedShare <= 0d
                        ? curriculum.AdvancedShare
                        : curriculum.MaximumAdvancedShare,
                    curriculum.AdvancedShare)),
            MidpointRounding.AwayFromZero);
        if (curriculum.AdvancedShare > 0d && desiredAdvanced == 0)
        {
            desiredAdvanced = 1;
        }
        var existingAdvanced = result.Count(slot =>
            slot.HardSeed
            && string.Equals(
                slot.DifficultyId,
                "advanced",
                StringComparison.Ordinal));
        var neededAdvanced = Math.Max(0, desiredAdvanced - existingAdvanced);
        var freshPositions = result
            .Where(slot => !slot.HardSeed)
            .OrderBy(slot => CombatFoundationSeedPlan.Mix(
                runSeed
                ^ ((ulong)Math.Max(0, iteration) << 32)
                ^ (ulong)slot.Index))
            .Select(slot => slot.Index)
            .ToList();
        var advancedPositions = freshPositions
            .Take(Math.Min(neededAdvanced, freshPositions.Count))
            .ToHashSet();
        foreach (var position in freshPositions)
        {
            var advanced = advancedPositions.Contains(position);
            result[position].DifficultyId = advanced ? "advanced" : "normal";
            result[position].SourceCategory = advanced
                ? "fresh-advanced"
                : "fresh-normal";
        }
        return result;
    }

    private static string NormalizeDifficulty(string value)
    {
        return string.Equals(
            value,
            "advanced",
            StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "normal";
    }
}

public sealed class CombatFoundationReplaySelection
{
    public List<CombatEpisode> Episodes { get; set; } = new();

    public int SourceEpisodes { get; set; }

    public int NormalEpisodes { get; set; }

    public int AdvancedEpisodes { get; set; }

    public int SuccessfulEpisodes { get; set; }

    public int DroppedDuplicateEpisodes { get; set; }

    public double TargetNormalShare { get; set; }

    public int SourceCampaigns { get; set; }

    public int SelectedCampaigns { get; set; }

    public int SuccessfulCampaigns { get; set; }

    public Dictionary<string, int> QuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class CombatFoundationReplayBalanceOptions
{
    public double MinimumAdvancedShare { get; set; } = 0.35d;

    public bool AllowCrossDifficultyBackfill { get; set; }
}

public static class CombatFoundationReplaySampler
{
    public static CombatFoundationReplaySelection Select(
        IEnumerable<CombatEpisode> source,
        int episodeLimit,
        bool enabled,
        CombatFoundationReplayBalanceOptions? balance = null)
    {
        balance ??= new CombatFoundationReplayBalanceOptions();
        var sourceEpisodes = (source ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null)
            .OrderBy(StableKey, StringComparer.Ordinal)
            .ToList();
        var episodes = sourceEpisodes
            .GroupBy(StableKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var limit = Math.Max(1, episodeLimit);
        var campaigns = BuildCampaigns(episodes);
        var targetNormalShare = DetermineNormalShare(campaigns);
        targetNormalShare = Math.Min(
            targetNormalShare,
            1d - Math.Max(
                0d,
                Math.Min(0.90d, balance.MinimumAdvancedShare)));
        var quotaShortfalls = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var selected = !enabled
            ? episodes.Skip(Math.Max(0, episodes.Count - limit)).ToList()
            : SelectCampaignFirst(
                campaigns,
                limit,
                targetNormalShare,
                quotaShortfalls,
                balance.AllowCrossDifficultyBackfill);
        return new CombatFoundationReplaySelection
        {
            Episodes = selected,
            SourceEpisodes = sourceEpisodes.Count,
            NormalEpisodes = selected.Count(episode =>
                !IsAdvanced(episode)),
            AdvancedEpisodes = selected.Count(IsAdvanced),
            SuccessfulEpisodes = selected.Count(IsSuccessful),
            DroppedDuplicateEpisodes = sourceEpisodes.Count - episodes.Count,
            TargetNormalShare = targetNormalShare,
            SourceCampaigns = campaigns.Count,
            SelectedCampaigns = selected
                .Select(CampaignKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            SuccessfulCampaigns = selected
                .Where(IsSuccessful)
                .Select(CampaignKey)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            QuotaShortfalls = quotaShortfalls
        };
    }

    private static List<CombatEpisode> SelectCampaignFirst(
        IReadOnlyList<ReplayCampaign> campaigns,
        int limit,
        double targetNormalShare,
        IDictionary<string, int> quotaShortfalls,
        bool allowCrossDifficultyBackfill)
    {
        var representatives = campaigns.ToDictionary(
            campaign => campaign.Key,
            SelectRepresentativeEpisodes,
            StringComparer.Ordinal);
        var availableCount = representatives.Values.Sum(items => items.Count);
        var targetCount = Math.Min(limit, availableCount);
        if (targetCount == 0)
        {
            return new List<CombatEpisode>();
        }
        var hasNormal = campaigns.Any(campaign => !campaign.Advanced);
        var hasAdvanced = campaigns.Any(campaign => campaign.Advanced);
        var normalTarget = hasNormal && hasAdvanced
            ? Math.Max(
                1,
                Math.Min(
                    targetCount - 1,
                    (int)Math.Round(targetCount * targetNormalShare)))
            : hasNormal
                ? targetCount
                : 0;
        var advancedTarget = targetCount - normalTarget;
        var result = new List<CombatEpisode>(targetCount);
        AddDifficultySelection(
            result,
            campaigns.Where(campaign => !campaign.Advanced).ToList(),
            representatives,
            normalTarget,
            "normal",
            quotaShortfalls);
        AddDifficultySelection(
            result,
            campaigns.Where(campaign => campaign.Advanced).ToList(),
            representatives,
            advancedTarget,
            "advanced",
            quotaShortfalls);
        if (allowCrossDifficultyBackfill && result.Count < targetCount)
        {
            var selectedKeys = result
                .Select(StableKey)
                .ToHashSet(StringComparer.Ordinal);
            var remaining = campaigns
                .SelectMany(campaign => representatives[campaign.Key])
                .Where(episode => !selectedKeys.Contains(StableKey(episode)))
                .OrderBy(episode => CampaignKey(episode), StringComparer.Ordinal)
                .ThenBy(episode => episode.JourneyBattleIndex)
                .ThenBy(StableKey, StringComparer.Ordinal)
                .Take(targetCount - result.Count);
            result.AddRange(remaining);
        }
        return result
            .OrderBy(StableKey, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddDifficultySelection(
        ICollection<CombatEpisode> result,
        IReadOnlyList<ReplayCampaign> campaigns,
        IReadOnlyDictionary<string, List<CombatEpisode>> representatives,
        int target,
        string difficulty,
        IDictionary<string, int> quotaShortfalls)
    {
        if (target <= 0)
        {
            return;
        }
        var wins = campaigns.Where(campaign => campaign.Successful).ToList();
        var failures = campaigns.Where(campaign => !campaign.Successful).ToList();
        var winTarget = wins.Count > 0 && failures.Count > 0
            ? target / 2
            : wins.Count > 0
                ? target
                : 0;
        var failureTarget = target - winTarget;
        var selectedWins = TakeCampaignRoundRobin(
            wins,
            representatives,
            winTarget);
        var selectedFailures = TakeFailureDepthBalanced(
            failures,
            representatives,
            failureTarget);
        var selected = selectedWins
            .Concat(selectedFailures)
            .ToList();
        foreach (var episode in selected)
        {
            result.Add(episode);
        }
        RecordShortfall(
            quotaShortfalls,
            difficulty + ":victory",
            winTarget - selectedWins.Count);
        RecordShortfall(
            quotaShortfalls,
            difficulty + ":defeat",
            failureTarget - selectedFailures.Count);
        if (selected.Count < target)
        {
            var selectedKeys = selected
                .Select(StableKey)
                .ToHashSet(StringComparer.Ordinal);
            var sameDifficultyBackfill = campaigns
                .SelectMany(campaign => representatives[campaign.Key])
                .Where(episode =>
                    !selectedKeys.Contains(StableKey(episode)))
                .OrderBy(StableKey, StringComparer.Ordinal)
                .Take(target - selected.Count)
                .ToList();
            foreach (var episode in sameDifficultyBackfill)
            {
                result.Add(episode);
            }
            RecordShortfall(
                quotaShortfalls,
                difficulty + ":total",
                target - selected.Count - sameDifficultyBackfill.Count);
        }
    }

    private static double DetermineNormalShare(
        IReadOnlyList<ReplayCampaign> campaigns)
    {
        var normal = campaigns.Where(campaign => !campaign.Advanced).ToList();
        if (normal.Count == 0)
        {
            return 0d;
        }
        var normalSuccessRate =
            normal.Count(campaign => campaign.Successful) / (double)normal.Count;
        return normalSuccessRate < 0.05d
            ? 0.75d
            : normalSuccessRate < 0.20d
                ? 0.65d
                : normalSuccessRate < 0.50d
                    ? 0.58d
                    : 0.50d;
    }

    private static bool IsAdvanced(CombatEpisode episode)
    {
        var difficultyId = episode.Campaign?.DifficultyId;
        if (!string.IsNullOrWhiteSpace(difficultyId))
        {
            return string.Equals(
                difficultyId,
                "advanced",
                StringComparison.OrdinalIgnoreCase);
        }
        return (episode.JourneyRunId ?? "").IndexOf(
            ":advanced:",
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSuccessful(CombatEpisode episode)
    {
        return episode.Campaign?.FinalBossVictory == true
               || string.Equals(
                   episode.Campaign?.OutcomeClass,
                   "victory",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   episode.Campaign?.OutcomeClass,
                   "encounter-victory",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static List<ReplayCampaign> BuildCampaigns(
        IReadOnlyList<CombatEpisode> episodes)
    {
        return episodes
            .GroupBy(CampaignKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(episode => episode.JourneyBattleIndex)
                    .ThenBy(StableKey, StringComparer.Ordinal)
                    .ToList();
                var last = ordered.Last();
                return new ReplayCampaign
                {
                    Key = group.Key,
                    Episodes = ordered,
                    Advanced = ordered.Any(IsAdvanced),
                    Successful = ordered.Any(IsSuccessful),
                    TerminalScenarioId =
                        last.Campaign?.TerminalScenarioId ?? "",
                    CompletedBattles = Math.Max(
                        ordered.Count,
                        last.Campaign?.CampaignCompletedBattles ?? 0)
                };
            })
            .OrderBy(campaign => campaign.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static List<CombatEpisode> SelectRepresentativeEpisodes(
        ReplayCampaign campaign)
    {
        const int maximumEpisodesPerCampaign = 8;
        var selected = new List<CombatEpisode>();
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        void Add(CombatEpisode episode)
        {
            if (selected.Count < maximumEpisodesPerCampaign
                && selectedKeys.Add(StableKey(episode)))
            {
                selected.Add(episode);
            }
        }

        foreach (var episode in campaign.Episodes
                     .OrderByDescending(item => item.JourneyBattleIndex)
                     .Take(3))
        {
            Add(episode);
        }
        var layerBoundaryIndexes = new HashSet<int>
        {
            4, 9, 14, 19, 24, 29, 36
        };
        foreach (var episode in campaign.Episodes
                     .Where(item => layerBoundaryIndexes.Contains(
                         item.JourneyBattleIndex))
                     .OrderBy(item => item.JourneyBattleIndex))
        {
            Add(episode);
        }
        foreach (var episode in campaign.Episodes
                     .OrderBy(item => CombatFoundationSeedPlan.Mix(
                         item.Seed
                         ^ (ulong)Math.Max(0, item.JourneyBattleIndex)))
                     .ThenBy(StableKey, StringComparer.Ordinal))
        {
            Add(episode);
        }
        return selected
            .OrderBy(item => item.JourneyBattleIndex)
            .ThenBy(StableKey, StringComparer.Ordinal)
            .ToList();
    }

    private static List<CombatEpisode> TakeCampaignRoundRobin(
        IReadOnlyList<ReplayCampaign> campaigns,
        IReadOnlyDictionary<string, List<CombatEpisode>> representatives,
        int count)
    {
        var result = new List<CombatEpisode>();
        var ordered = campaigns
            .OrderBy(campaign => campaign.Key, StringComparer.Ordinal)
            .ToList();
        for (var offset = 0; result.Count < count; offset++)
        {
            var added = false;
            foreach (var campaign in ordered)
            {
                var items = representatives[campaign.Key];
                if (offset >= items.Count)
                {
                    continue;
                }
                result.Add(items[offset]);
                added = true;
                if (result.Count >= count)
                {
                    break;
                }
            }
            if (!added)
            {
                break;
            }
        }
        return result;
    }

    private static List<CombatEpisode> TakeFailureDepthBalanced(
        IReadOnlyList<ReplayCampaign> failures,
        IReadOnlyDictionary<string, List<CombatEpisode>> representatives,
        int count)
    {
        var clusterCounts = failures
            .GroupBy(
                campaign => FailureCluster(campaign.TerminalScenarioId),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var ordered = failures
            .OrderByDescending(campaign =>
                clusterCounts[FailureCluster(
                    campaign.TerminalScenarioId)])
            .ThenBy(campaign => DepthBucket(campaign.CompletedBattles))
            .ThenByDescending(campaign => campaign.CompletedBattles)
            .ThenBy(campaign => campaign.Key, StringComparer.Ordinal)
            .ToList();
        return TakeCampaignRoundRobin(
            ordered,
            representatives,
            count);
    }

    private static string FailureCluster(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown-terminal"
            : value.Trim();
    }

    private static int DepthBucket(int completedBattles)
    {
        return completedBattles <= 5
            ? 0
            : completedBattles <= 10
                ? 1
                : completedBattles <= 20
                    ? 2
                    : completedBattles <= 30
                        ? 3
                        : 4;
    }

    private static void RecordShortfall(
        IDictionary<string, int> shortfalls,
        string key,
        int value)
    {
        if (value > 0)
        {
            shortfalls[key] = value;
        }
    }

    private static string CampaignKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + StableKey(episode)
            : episode.JourneyRunId;
    }

    private static string StableKey(CombatEpisode episode)
    {
        return (episode.JourneyRunId ?? "")
               + "|"
               + episode.JourneyBattleIndex.ToString("D4")
               + "|"
               + episode.Seed.ToString("D20")
               + "|"
               + (episode.ScenarioId ?? "")
               + "|"
               + (episode.EpisodeId ?? "");
    }

    private sealed class ReplayCampaign
    {
        public string Key { get; set; } = "";

        public List<CombatEpisode> Episodes { get; set; } = new();

        public bool Advanced { get; set; }

        public bool Successful { get; set; }

        public string TerminalScenarioId { get; set; } = "";

        public int CompletedBattles { get; set; }
    }
}
