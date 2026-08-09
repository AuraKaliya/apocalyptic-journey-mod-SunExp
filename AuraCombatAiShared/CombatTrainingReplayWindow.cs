using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public sealed class CombatTrainingReplayWindowOptions
{
    public int MaximumFrames { get; set; } = 10000;

    public int MaximumFramesPerEpisode { get; set; } = 96;

    public double MaximumUnsafeEndTurnShare { get; set; } = 0.30d;

    public bool RequireMultipleCandidates { get; set; }

    public Dictionary<string, int> RequiredStrategyClassFrames { get; set; } =
        new(StringComparer.Ordinal);

    public CombatTrainingReplayWindowOptions Normalized()
    {
        MaximumFrames = Math.Max(64, Math.Min(100000, MaximumFrames));
        MaximumFramesPerEpisode = Math.Max(
            8,
            Math.Min(512, MaximumFramesPerEpisode));
        MaximumUnsafeEndTurnShare = Clamp(
            MaximumUnsafeEndTurnShare,
            0.05d,
            0.80d,
            0.30d);
        RequiredStrategyClassFrames = (RequiredStrategyClassFrames
                                       ?? new Dictionary<string, int>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Key)
                           && item.Value > 0)
            .ToDictionary(
                item => item.Key.Trim().ToLowerInvariant(),
                item => Math.Max(1, Math.Min(MaximumFrames, item.Value)),
                StringComparer.Ordinal);
        return this;
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum,
        double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

public sealed class CombatTrainingReplayWindowResult
{
    public List<CombatEpisode> Episodes { get; set; } = new();

    public int SourceFrames { get; set; }

    public int AvailableSourceFrames { get; set; }

    public int SelectedFrames { get; set; }

    public int UnsafeEndTurnFrames { get; set; }

    public int DroppedFrames { get; set; }

    public double SourcePriorityMean { get; set; }

    public double SelectedPriorityMean { get; set; }

    public int SelectedHighPriorityFrames { get; set; }

    public bool StrategyQuotaActive { get; set; }

    public bool StrategyQuotaPassed { get; set; } = true;

    public Dictionary<string, int> StrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> AvailableStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> SourceStrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, double> MinimumStrategyShares { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, double> MaximumStrategyShares { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> StrategyQuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> RequiredStrategyClassFrames { get; set; } =
        new(StringComparer.Ordinal);

    public bool StrategyQuotaRepairAttempted { get; set; }

    public int StrategyQuotaRepairSourceEpisodes { get; set; }

    public int StrategyQuotaRepairAddedEpisodes { get; set; }
}

public static class CombatTrainingReplayWindowSelector
{
    public static CombatTrainingReplayWindowResult RepairStrategyQuota(
        CombatTrainingReplayWindowResult initial,
        IEnumerable<CombatEpisode> additionalSource,
        CombatTrainingReplayWindowOptions? selectionOptions = null)
    {
        if (initial == null) throw new ArgumentNullException(nameof(initial));
        if (!initial.StrategyQuotaActive
            || initial.StrategyQuotaPassed
            || initial.StrategyQuotaShortfalls.Count == 0)
        {
            return initial;
        }
        var missing = initial.StrategyQuotaShortfalls.Keys
            .ToHashSet(StringComparer.Ordinal);
        var existing = (initial.Episodes ?? new List<CombatEpisode>())
            .Where(episode => (episode.Frames?.Count ?? 0) > 0)
            .ToList();
        var existingKeys = existing.Select(StableRunEpisodeKey)
            .ToHashSet(StringComparer.Ordinal);
        var targeted = (additionalSource ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null
                              && !existingKeys.Contains(
                                  StableRunEpisodeKey(episode))
                              && (episode.Frames
                                  ?? new List<CombatEpisodeFrame>())
                              .Any(frame => missing.Any(key =>
                                  FrameMatchesStrategyClass(frame, key))))
            .OrderByDescending(episode =>
                (episode.Frames ?? new List<CombatEpisodeFrame>())
                .Count(frame => missing.Any(key =>
                    FrameMatchesStrategyClass(frame, key))))
            .ThenBy(StableRunEpisodeKey, StringComparer.Ordinal)
            .ToList();
        var repaired = Select(existing.Concat(targeted), selectionOptions);
        repaired.StrategyQuotaRepairAttempted = true;
        repaired.StrategyQuotaRepairSourceEpisodes = targeted.Count;
        repaired.StrategyQuotaRepairAddedEpisodes = repaired.Episodes.Count(
            episode => (episode.Frames?.Count ?? 0) > 0
                       && !existingKeys.Contains(
                           StableRunEpisodeKey(episode)));
        return repaired;
    }

    public static CombatTrainingReplayWindowResult Select(
        IEnumerable<CombatEpisode> source,
        CombatTrainingReplayWindowOptions? selectionOptions = null)
    {
        var options = (selectionOptions
                       ?? new CombatTrainingReplayWindowOptions()).Normalized();
        var episodes = (source ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null
                              && episode.Authoritative
                              && (episode.Campaign?.IntegrityValid ?? true))
            .OrderBy(StableRunKey, StringComparer.Ordinal)
            .ThenBy(episode => episode.JourneyBattleIndex)
            .ThenBy(episode => episode.Seed)
            .ThenBy(episode => episode.EpisodeId, StringComparer.Ordinal)
            .ToList();
        var entries = episodes
            .SelectMany(episode => SelectEpisodeFrames(
                    episode,
                    options.MaximumFramesPerEpisode,
                    options.RequiredStrategyClassFrames)
                .Select(frame => new FrameEntry(
                    episode,
                    frame,
                    CombatPolicyValueBatchTrainer.StrategicFrameStratumForFrame(
                        frame),
                    UnsafeEndTurn(frame))))
            .Where(entry => Eligible(
                entry.Frame,
                options.RequireMultipleCandidates))
            .ToList();
        var availableFrames = episodes
            .SelectMany(episode => episode.Frames
                                   ?? new List<CombatEpisodeFrame>())
            .Where(frame => Eligible(
                frame,
                options.RequireMultipleCandidates))
            .ToList();
        var fingerprintCounts = entries
            .GroupBy(entry => entry.Frame.StateFingerprint ?? "",
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            entry.PriorityScore = InformationPriority(
                entry.Frame,
                fingerprintCounts.TryGetValue(
                    entry.Frame.StateFingerprint ?? "",
                    out var frequency)
                    ? frequency
                    : 1);
        }
        var result = new CombatTrainingReplayWindowResult
        {
            SourceFrames = entries.Count,
            AvailableSourceFrames = availableFrames.Count,
            AvailableStrategyFrames = StrategyCounts(availableFrames),
            SourceStrategyFrames = StrategyCounts(
                entries.Select(item => item.Frame)),
            RequiredStrategyClassFrames = new Dictionary<string, int>(
                options.RequiredStrategyClassFrames,
                StringComparer.Ordinal),
            SourcePriorityMean = entries.Count == 0
                ? 0d
                : entries.Average(entry => entry.PriorityScore)
        };
        if (entries.Count == 0)
        {
            return result;
        }

        ReadQuotas(entries, result);
        var capacity = Math.Min(options.MaximumFrames, entries.Count);
        var maximumUnsafe = Math.Max(
            1,
            (int)Math.Floor(capacity * options.MaximumUnsafeEndTurnShare));
        var selected = new HashSet<FrameEntry>();
        var selectedStrategyCounts = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var selectedUnsafe = 0;
        foreach (var required in result.RequiredStrategyClassFrames
                     .OrderByDescending(item => item.Value)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var entry in Ranked(entries.Where(item =>
                         FrameMatchesStrategyClass(
                             item.Frame,
                             required.Key))))
            {
                if (selected.Count >= capacity
                    || selectedUnsafe >= maximumUnsafe
                       && entry.UnsafeEndTurn)
                {
                    continue;
                }
                if (selected.Add(entry))
                {
                    selectedStrategyCounts[entry.Strategy] =
                        Count(selectedStrategyCounts, entry.Strategy) + 1;
                    if (entry.UnsafeEndTurn) selectedUnsafe++;
                }
                if (selected.Count(item => FrameMatchesStrategyClass(
                        item.Frame,
                        required.Key)) >= required.Value)
                {
                    break;
                }
            }
        }
        foreach (var quota in result.MinimumStrategyShares
                     .OrderByDescending(item => item.Value)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            var target = Math.Max(1, (int)Math.Ceiling(capacity * quota.Value));
            foreach (var entry in Ranked(entries.Where(item =>
                         string.Equals(
                             item.Strategy,
                             quota.Key,
                             StringComparison.Ordinal))))
            {
                if (selected.Count >= capacity
                    || selectedUnsafe >= maximumUnsafe
                       && entry.UnsafeEndTurn)
                {
                    continue;
                }
                if (!selected.Add(entry))
                {
                    continue;
                }
                selectedStrategyCounts[entry.Strategy] =
                    Count(selectedStrategyCounts, entry.Strategy)
                    + 1;
                if (entry.UnsafeEndTurn)
                {
                    selectedUnsafe++;
                }
                if (Count(selectedStrategyCounts, quota.Key)
                    >= target)
                {
                    break;
                }
            }
        }

        foreach (var entry in Ranked(entries
                     .Where(item => !selected.Contains(item))
                     .OrderBy(item => IsMaximumQuotaStrategy(
                         item.Strategy,
                         result) ? 1 : 0)))
        {
            if (selected.Count >= capacity)
            {
                break;
            }
            if (entry.UnsafeEndTurn && selectedUnsafe >= maximumUnsafe)
            {
                continue;
            }
            if (WouldExceedMaximumQuota(
                    entry,
                    selectedStrategyCounts,
                    capacity,
                    result))
            {
                continue;
            }
            if (selected.Add(entry))
            {
                selectedStrategyCounts[entry.Strategy] =
                    Count(selectedStrategyCounts, entry.Strategy)
                    + 1;
                if (entry.UnsafeEndTurn)
                {
                    selectedUnsafe++;
                }
            }
        }

        EnforceMaximumShares(selected, result);
        EnforceUnsafeShare(selected, options.MaximumUnsafeEndTurnShare);
        var selectedByEpisode = selected
            .GroupBy(item => item.Episode)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Frame).ToHashSet());
        result.Episodes = episodes
            .Select(episode => CloneEpisode(
                episode,
                (episode.Frames ?? new List<CombatEpisodeFrame>())
                .Where(frame => selectedByEpisode.TryGetValue(
                                    episode,
                                    out var selectedFrames)
                                && selectedFrames.Contains(frame))
                .ToList()))
            .ToList();
        result.SelectedFrames = selected.Count;
        result.SelectedPriorityMean = selected.Count == 0
            ? 0d
            : selected.Average(entry => entry.PriorityScore);
        result.SelectedHighPriorityFrames = selected.Count(entry =>
            entry.PriorityScore >= 2d);
        result.UnsafeEndTurnFrames = CountUnsafe(selected);
        result.DroppedFrames = Math.Max(0, result.SourceFrames - selected.Count);
        result.StrategyFrames = selected
            .GroupBy(item => item.Strategy, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        foreach (var quota in result.MinimumStrategyShares)
        {
            var target = Math.Max(1, (int)Math.Ceiling(capacity * quota.Value));
            var actual = result.StrategyFrames.TryGetValue(
                quota.Key,
                out var count)
                ? count
                : 0;
            if (actual < target)
            {
                result.StrategyQuotaShortfalls[quota.Key] = target - actual;
            }
        }
        foreach (var required in result.RequiredStrategyClassFrames)
        {
            var actual = selected.Count(item => FrameMatchesStrategyClass(
                item.Frame,
                required.Key));
            if (actual < required.Value)
            {
                result.StrategyQuotaShortfalls[required.Key] =
                    required.Value - actual;
            }
        }
        result.StrategyQuotaPassed =
            result.StrategyQuotaShortfalls.Count == 0
            && MaximumSharesPassed(result);
        return result;
    }

    private static void ReadQuotas(
        IReadOnlyList<FrameEntry> entries,
        CombatTrainingReplayWindowResult result)
    {
        foreach (var pair in entries
                     .SelectMany(entry =>
                         entry.Frame.EnumerateStateFeatures())
                     .Where(pair => pair.Key.StartsWith(
                         CombatRoleStrategyFeatureNames.TrainingQuotaPrefix,
                         StringComparison.OrdinalIgnoreCase)))
        {
            var key = pair.Key.Substring(
                CombatRoleStrategyFeatureNames.TrainingQuotaPrefix.Length);
            var minimumSuffix = ":minimum-share";
            var maximumSuffix = ":maximum-share";
            if (key.EndsWith(minimumSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var strategy = key.Substring(0, key.Length - minimumSuffix.Length)
                    .ToLowerInvariant();
                result.MinimumStrategyShares[strategy] = Math.Max(
                    result.MinimumStrategyShares.TryGetValue(
                        strategy,
                        out var current) ? current : 0d,
                    ClampShare(pair.Value));
            }
            else if (key.EndsWith(
                         maximumSuffix,
                         StringComparison.OrdinalIgnoreCase))
            {
                var strategy = key.Substring(0, key.Length - maximumSuffix.Length)
                    .ToLowerInvariant();
                var share = ClampShare(pair.Value);
                result.MaximumStrategyShares[strategy] =
                    result.MaximumStrategyShares.TryGetValue(
                        strategy,
                        out var current)
                        ? Math.Min(current, share)
                        : share;
            }
        }
        result.StrategyQuotaActive = result.MinimumStrategyShares.Count > 0
                                     || result.MaximumStrategyShares.Count > 0
                                     || result.RequiredStrategyClassFrames.Count
                                     > 0;
    }

    private static IEnumerable<FrameEntry> Ranked(IEnumerable<FrameEntry> source)
    {
        return source
            .OrderByDescending(item => item.PriorityScore)
            .ThenBy(item => item.UnsafeEndTurn ? 1 : 0)
            .ThenBy(item => StableHash(item.Identity))
            .ThenBy(item => item.Identity, StringComparer.Ordinal);
    }

    internal static double InformationPriority(
        CombatEpisodeFrame frame,
        int stateFrequency = 1)
    {
        if (frame == null)
        {
            return 0d;
        }
        var legal = (frame.Candidates ?? new List<CombatEpisodeCandidate>())
            .Where(candidate => candidate != null && candidate.Legal)
            .ToList();
        if (legal.Count == 0)
        {
            return 0d;
        }
        var totalVisits = legal.Sum(candidate => Math.Max(0, candidate.SearchVisits));
        var entropy = 0d;
        if (totalVisits > 0 && legal.Count > 1)
        {
            foreach (var candidate in legal)
            {
                var probability = Math.Max(0, candidate.SearchVisits)
                                  / (double)totalVisits;
                if (probability > 0d)
                {
                    entropy -= probability * Math.Log(probability);
                }
            }
            entropy /= Math.Log(legal.Count);
        }
        var searchBest = legal
            .OrderByDescending(candidate => candidate.SearchVisits)
            .ThenByDescending(candidate => candidate.SearchValue)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .First();
        var disagreement = string.IsNullOrWhiteSpace(frame.ExecutedCandidateId)
                           || string.Equals(
                               frame.ExecutedCandidateId,
                               searchBest.CandidateId,
                               StringComparison.Ordinal)
            ? 0d
            : 1d;
        var minimumRisk = legal.Min(candidate => Finite(candidate.SearchDeathRisk));
        var maximumRisk = legal.Max(candidate => Finite(candidate.SearchDeathRisk));
        var riskSpread = Math.Max(0d, Math.Min(1d, maximumRisk - minimumRisk));
        var returnUncertainty = Math.Min(
            1d,
            legal.Max(candidate => Math.Max(
                0d,
                Finite(candidate.SearchReturnStandardError))));
        var stateUncertainty = Math.Min(
            1d,
            Math.Max(0d, Feature(frame, "uncertainty")));
        var critical = Math.Max(
            frame.DeathTarget,
            riskSpread >= 0.20d ? 1d : 0d);
        var terminalProximity =
            frame.RemainingTurnsTarget is >= 0d and <= 2d
            ? 1d
            : 0d;
        var novelty = 1d / Math.Sqrt(Math.Max(1, stateFrequency));
        var forcedPenalty = legal.Count <= 1 ? 0.35d : 1d;
        return forcedPenalty * (
            disagreement * 2.5d
            + entropy * 1.5d
            + riskSpread * 1.5d
            + returnUncertainty
            + stateUncertainty
            + critical
            + terminalProximity * 0.5d
            + novelty * 0.75d);
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }

    private static bool WouldExceedMaximumQuota(
        FrameEntry entry,
        IReadOnlyDictionary<string, int> selectedStrategyCounts,
        int capacity,
        CombatTrainingReplayWindowResult result)
    {
        if (!result.MaximumStrategyShares.TryGetValue(
                entry.Strategy,
                out var maximum))
        {
            return false;
        }
        var maximumCount = Math.Max(1, (int)Math.Floor(capacity * maximum));
        return Count(selectedStrategyCounts, entry.Strategy)
               >= maximumCount;
    }

    private static bool IsMaximumQuotaStrategy(
        string strategy,
        CombatTrainingReplayWindowResult result)
    {
        return result.MaximumStrategyShares.ContainsKey(strategy);
    }

    private static void EnforceMaximumShares(
        HashSet<FrameEntry> selected,
        CombatTrainingReplayWindowResult result)
    {
        foreach (var quota in result.MaximumStrategyShares)
        {
            while (selected.Count > 1)
            {
                var matching = selected.Where(item => string.Equals(
                        item.Strategy,
                        quota.Key,
                        StringComparison.Ordinal))
                    .OrderBy(item => item.PriorityScore)
                    .ThenByDescending(item => StableHash(item.Identity))
                    .ToList();
                if (matching.Count / (double)selected.Count
                    <= quota.Value + 0.0000001d)
                {
                    break;
                }
                selected.Remove(matching[0]);
            }
        }
    }

    private static void EnforceUnsafeShare(
        HashSet<FrameEntry> selected,
        double maximumShare)
    {
        while (selected.Count > 1)
        {
            var unsafeFrames = selected.Where(item => item.UnsafeEndTurn)
                .OrderBy(item => item.PriorityScore)
                .ThenByDescending(item => StableHash(item.Identity))
                .ToList();
            if (unsafeFrames.Count / (double)selected.Count
                <= maximumShare + 0.0000001d)
            {
                break;
            }
            selected.Remove(unsafeFrames[0]);
        }
    }

    private static bool MaximumSharesPassed(
        CombatTrainingReplayWindowResult result)
    {
        return result.MaximumStrategyShares.All(quota =>
        {
            var count = result.StrategyFrames.TryGetValue(
                quota.Key,
                out var actual) ? actual : 0;
            return result.SelectedFrames == 0
                   || count / (double)result.SelectedFrames
                      <= quota.Value + 0.0000001d;
        });
    }

    private static int Count(
        IReadOnlyDictionary<string, int> counts,
        string key)
    {
        return counts.TryGetValue(key, out var value) ? value : 0;
    }

    private static string StableRunEpisodeKey(CombatEpisode episode)
    {
        return StableRunKey(episode)
               + ":"
               + episode.JourneyBattleIndex
               + ":"
               + episode.Seed
               + ":"
               + (episode.EpisodeId ?? "");
    }

    private static bool Eligible(
        CombatEpisodeFrame frame,
        bool requireMultipleCandidates)
    {
        if (!CombatPolicyValueBatchTrainer.PolicyIntegrityValidForTraining(frame))
        {
            return false;
        }
        var candidates = frame.Candidates ?? new List<CombatEpisodeCandidate>();
        return candidates.Count(candidate => candidate != null && candidate.Legal)
               >= (requireMultipleCandidates ? 2 : 1);
    }

    private static bool UnsafeEndTurn(CombatEpisodeFrame frame)
    {
        var candidates = frame.Candidates ?? new List<CombatEpisodeCandidate>();
        var executed = candidates.FirstOrDefault(candidate => string.Equals(
            candidate.CandidateId,
            frame.ExecutedCandidateId,
            StringComparison.Ordinal));
        var endTurn = candidates.FirstOrDefault(IsEndTurn);
        if (executed == null || endTurn == null)
        {
            return false;
        }
        var dominated = Feature(
            endTurn,
            CombatTurnFeatureNames.EndTurnDominated) > 0.5d;
        var executedEndTurn = IsEndTurn(executed);
        var playableAlternative = candidates.Any(candidate =>
            candidate.Legal && !IsEndTurn(candidate));
        var unusedPower = Feature(frame, "power") > 0.5d;
        return dominated
               || executedEndTurn && playableAlternative && unusedPower;
    }

    private static bool IsEndTurn(CombatEpisodeCandidate candidate)
    {
        return string.Equals(
                   candidate.SourceId,
                   "simulation:end-turn",
                   StringComparison.OrdinalIgnoreCase)
               || Feature(candidate, "actionKindEndTurn") > 0.5d;
    }

    private static double Feature(CombatEpisodeFrame frame, string key)
    {
        return frame != null
               && frame.TryGetStateFeature(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static double Feature(
        CombatEpisodeCandidate candidate,
        string key)
    {
        return candidate != null
               && candidate.TryGetFeature(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static double Feature(
        IReadOnlyDictionary<string, double>? features,
        string key)
    {
        return features != null
               && features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private static IReadOnlyList<CombatEpisodeFrame> SelectEpisodeFrames(
        CombatEpisode episode,
        int maximumFrames,
        IReadOnlyDictionary<string, int> requiredStrategyClasses)
    {
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        if (frames.Count <= maximumFrames)
        {
            return frames;
        }
        var pinned = frames.Where(frame =>
                IsScarceStrategy(
                    CombatPolicyValueBatchTrainer
                        .StrategicFrameStratumForFrame(frame))
                || (requiredStrategyClasses
                    ?? new Dictionary<string, int>()).Keys.Any(key =>
                    FrameMatchesStrategyClass(frame, key)))
            .ToList();
        var selected = EvenlySpaced(
            pinned,
            Math.Min(maximumFrames, pinned.Count));
        var selectedSet = selected.ToHashSet();
        var remaining = frames.Where(frame => !selectedSet.Contains(frame))
            .ToList();
        selected.AddRange(EvenlySpaced(
            remaining,
            maximumFrames - selected.Count));
        return selected
            .OrderBy(frame => frames.IndexOf(frame))
            .ToList();
    }

    private static List<CombatEpisodeFrame> EvenlySpaced(
        IReadOnlyList<CombatEpisodeFrame> frames,
        int count)
    {
        var take = Math.Max(0, Math.Min(count, frames.Count));
        if (take == 0)
        {
            return new List<CombatEpisodeFrame>();
        }
        if (take == frames.Count)
        {
            return frames.ToList();
        }
        var selected = new List<CombatEpisodeFrame>(take);
        for (var index = 0; index < take; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (frames.Count - 1d) / Math.Max(1d, take - 1d),
                MidpointRounding.AwayFromZero);
            selected.Add(frames[sourceIndex]);
        }
        return selected;
    }

    private static bool IsScarceStrategy(string strategy)
    {
        return string.Equals(strategy, "strategy-finale", StringComparison.Ordinal)
               || string.Equals(
                   strategy,
                   "strategy-bank",
                   StringComparison.Ordinal)
               || string.Equals(
                   strategy,
                   "strategy-transform",
                   StringComparison.Ordinal)
               || string.Equals(
                   strategy,
                   "strategy-growth",
                   StringComparison.Ordinal);
    }

    internal static bool FrameMatchesStrategyClass(
        CombatEpisodeFrame? frame,
        string? strategyClass)
    {
        var normalized = (strategyClass ?? "").Trim().ToLowerInvariant();
        if (frame == null || !normalized.StartsWith(
                "strategy-",
                StringComparison.Ordinal))
        {
            return false;
        }
        var negative = normalized.EndsWith(
            "-negative",
            StringComparison.Ordinal);
        if (!negative
            && string.Equals(
                CombatPolicyValueBatchTrainer.StrategicFrameStratumForFrame(
                    frame),
                normalized,
                StringComparison.Ordinal))
        {
            return true;
        }
        var label = normalized.Substring("strategy-".Length);
        if (negative)
        {
            label = label.Substring(
                0,
                label.Length - "-negative".Length);
        }
        var supervision = CombatPolicyValueBatchTrainer
            .StrategicFrameSupervisionForExecutedAction(frame);
        if (!supervision.Known
            || !supervision.ApplicableLabels.Contains(
                label,
                StringComparer.Ordinal))
        {
            return false;
        }
        var positive = supervision.PositiveLabels.Contains(
            label,
            StringComparer.Ordinal);
        return negative ? !positive : positive;
    }

    private static Dictionary<string, int> StrategyCounts(
        IEnumerable<CombatEpisodeFrame> frames)
    {
        return (frames ?? Array.Empty<CombatEpisodeFrame>())
            .GroupBy(
                CombatPolicyValueBatchTrainer.StrategicFrameStratumForFrame,
                StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
    }

    private static CombatEpisode CloneEpisode(
        CombatEpisode source,
        List<CombatEpisodeFrame> frames)
    {
        return new CombatEpisode
        {
            ModelProtocol = source.ModelProtocol,
            FeatureSchemaVersion = source.FeatureSchemaVersion,
            EpisodeId = source.EpisodeId,
            ScenarioId = source.ScenarioId,
            JourneyRunId = source.JourneyRunId,
            BattleSessionId = source.BattleSessionId,
            JourneyBattleIndex = source.JourneyBattleIndex,
            Campaign = source.Campaign,
            Seed = source.Seed,
            RulesetHash = source.RulesetHash,
            OwnerModSetHash = source.OwnerModSetHash,
            ContentSetHash = source.ContentSetHash,
            BaseModelId = source.BaseModelId,
            ActiveAdapterIds = source.ActiveAdapterIds,
            PolicyId = source.PolicyId,
            DecisionProfile = source.DecisionProfile,
            Frames = frames,
            Outcome = source.Outcome,
            Turns = source.Turns,
            FinalPlayerHp = source.FinalPlayerHp,
            FinalPlayerMaxHp = source.FinalPlayerMaxHp,
            DamageTaken = source.DamageTaken,
            SemanticCoverage = source.SemanticCoverage,
            Authoritative = source.Authoritative,
            Provenance = source.Provenance,
            CreatedUtc = source.CreatedUtc
        };
    }

    private static int CountUnsafe(IEnumerable<FrameEntry> source)
    {
        return source.Count(item => item.UnsafeEndTurn);
    }

    private static string StableRunKey(CombatEpisode episode)
    {
        return string.IsNullOrWhiteSpace(episode.JourneyRunId)
            ? "episode:" + (episode.EpisodeId ?? "")
            : "journey:" + episode.JourneyRunId;
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in value ?? "")
            {
                hash ^= character;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static double ClampShare(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? 0d
            : Math.Max(0d, Math.Min(0.95d, value));
    }

    private sealed class FrameEntry
    {
        public FrameEntry(
            CombatEpisode episode,
            CombatEpisodeFrame frame,
            string strategy,
            bool unsafeEndTurn)
        {
            Episode = episode;
            Frame = frame;
            Strategy = strategy;
            UnsafeEndTurn = unsafeEndTurn;
            Identity = StableRunKey(episode)
                       + "|"
                       + episode.JourneyBattleIndex
                       + "|"
                       + frame.Turn
                       + "|"
                       + frame.ActionSequence
                       + "|"
                       + frame.StateFingerprint;
        }

        public CombatEpisode Episode { get; }

        public CombatEpisodeFrame Frame { get; }

        public string Strategy { get; }

        public bool UnsafeEndTurn { get; }

        public double PriorityScore { get; set; }

        public string Identity { get; }
    }
}
