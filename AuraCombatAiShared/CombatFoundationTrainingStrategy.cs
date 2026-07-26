using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatFoundationSeedPlan
{
    public ulong RunSeed { get; set; }

    public ulong TrainingSeedStart { get; set; }

    public ulong ArenaSeedStart { get; set; }

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
    }

    public static Plan Evaluate(
        bool enabled,
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
                AdvancedShare = 0.5d
            };
        }
        var normalLower = WilsonLowerBound(normalWins, normalTrials);
        var advancedLower = WilsonLowerBound(advancedWins, advancedTrials);
        if (normalLower < 0.75d)
        {
            return new Plan
            {
                Stage = "normal-focus",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0d
            };
        }
        if (normalLower < 0.90d)
        {
            return new Plan
            {
                Stage = "advanced-introduction",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.10d
            };
        }
        if (normalLower < 0.97d)
        {
            return new Plan
            {
                Stage = "mixed-mastery",
                NormalWilsonLowerBound = normalLower,
                AdvancedWilsonLowerBound = advancedLower,
                AdvancedShare = 0.25d
            };
        }
        return new Plan
        {
            Stage = advancedLower >= 0.60d
                ? "balanced-mastery"
                : "advanced-mastery",
            NormalWilsonLowerBound = normalLower,
            AdvancedWilsonLowerBound = advancedLower,
            AdvancedShare = advancedLower >= 0.60d ? 0.50d : 0.40d
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
        return 0d;
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
            "normal-focus" => Math.Max(0.20d, value),
            "advanced-introduction" => Math.Max(0.15d, value),
            "mixed-mastery" => Math.Min(0.12d, value),
            "advanced-mastery" => Math.Min(0.10d, value),
            "balanced-mastery" => Math.Min(0.08d, value),
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
}

public sealed class CombatFoundationHardSeedPlan
{
    public int SourceCampaigns { get; set; }

    public List<CombatFoundationHardSeed> Seeds { get; set; } = new();

    public Dictionary<string, int> Clusters { get; set; } =
        new(StringComparer.Ordinal);
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
        var plan = new CombatFoundationHardSeedPlan();
        if (!enabled || campaignCount <= 0 || replayShare <= 0d)
        {
            return plan;
        }

        var campaigns = (source ?? Array.Empty<CombatEpisode>())
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
                return new CombatFoundationHardSeed
                {
                    WorldSeed = group.Key.WorldSeed,
                    DifficultyId = group.Key.Difficulty,
                    TerminalScenarioId =
                        terminal.Campaign.TerminalScenarioId ?? "",
                    CompletedBattles =
                        terminal.Campaign.CampaignCompletedBattles
                };
            })
            .OrderBy(item => item.WorldSeed)
            .ThenBy(item => item.DifficultyId, StringComparer.Ordinal)
            .ToList();
        plan.SourceCampaigns = campaigns.Count;
        if (campaigns.Count == 0)
        {
            return plan;
        }

        var target = Math.Min(
            campaigns.Count,
            Math.Max(
                1,
                (int)Math.Round(
                    campaignCount
                    * Math.Max(0d, Math.Min(0.75d, replayShare)),
                    MidpointRounding.AwayFromZero)));
        var clusters = campaigns
            .GroupBy(
                item => ClusterKey(item.TerminalScenarioId),
                StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(item => CombatFoundationSeedPlan.Mix(
                    runSeed
                    ^ item.WorldSeed
                    ^ ((ulong)Math.Max(0, iteration) << 32)))
                .ThenByDescending(item => item.CompletedBattles)
                .ThenBy(item => item.WorldSeed)
                .ToList())
            .ToList();
        for (var offset = 0; plan.Seeds.Count < target; offset++)
        {
            var added = false;
            foreach (var cluster in clusters)
            {
                if (offset >= cluster.Count)
                {
                    continue;
                }
                var selected = cluster[offset];
                plan.Seeds.Add(selected);
                var key = ClusterKey(selected.TerminalScenarioId);
                plan.Clusters[key] = plan.Clusters.TryGetValue(
                    key,
                    out var current)
                    ? current + 1
                    : 1;
                added = true;
                if (plan.Seeds.Count >= target)
                {
                    break;
                }
            }
            if (!added)
            {
                break;
            }
        }
        return plan;
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

public static class CombatFoundationReplaySampler
{
    public static CombatFoundationReplaySelection Select(
        IEnumerable<CombatEpisode> source,
        int episodeLimit,
        bool enabled)
    {
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
        var quotaShortfalls = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var selected = !enabled
            ? episodes.Skip(Math.Max(0, episodes.Count - limit)).ToList()
            : SelectCampaignFirst(
                campaigns,
                limit,
                targetNormalShare,
                quotaShortfalls);
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
        IDictionary<string, int> quotaShortfalls)
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
        if (result.Count < targetCount)
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
        foreach (var episode in selectedWins.Concat(selectedFailures))
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
