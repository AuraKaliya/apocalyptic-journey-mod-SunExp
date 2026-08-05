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

    public int SelectedFrames { get; set; }

    public int UnsafeEndTurnFrames { get; set; }

    public int DroppedFrames { get; set; }

    public bool StrategyQuotaActive { get; set; }

    public bool StrategyQuotaPassed { get; set; } = true;

    public Dictionary<string, int> StrategyFrames { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, double> MinimumStrategyShares { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, double> MaximumStrategyShares { get; set; } =
        new(StringComparer.Ordinal);

    public Dictionary<string, int> StrategyQuotaShortfalls { get; set; } =
        new(StringComparer.Ordinal);
}

public static class CombatTrainingReplayWindowSelector
{
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
                    options.MaximumFramesPerEpisode)
                .Select(frame => new FrameEntry(
                    episode,
                    frame,
                    CombatPolicyValueBatchTrainer.StrategicFrameStratum(
                        frame.StateFeatures),
                    UnsafeEndTurn(frame))))
            .Where(entry => Eligible(
                entry.Frame,
                options.RequireMultipleCandidates))
            .ToList();
        var result = new CombatTrainingReplayWindowResult
        {
            SourceFrames = entries.Count
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
                     .SelectMany(entry => entry.Frame.StateFeatures
                                         ?? new Dictionary<string, double>())
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
                                     || result.MaximumStrategyShares.Count > 0;
    }

    private static IEnumerable<FrameEntry> Ranked(IEnumerable<FrameEntry> source)
    {
        return source
            .OrderBy(item => item.UnsafeEndTurn ? 1 : 0)
            .ThenBy(item => StableHash(item.Identity))
            .ThenBy(item => item.Identity, StringComparer.Ordinal);
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
                    .OrderByDescending(item => StableHash(item.Identity))
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
                .OrderByDescending(item => StableHash(item.Identity))
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
            endTurn.Features,
            CombatTurnFeatureNames.EndTurnDominated) > 0.5d;
        var executedEndTurn = IsEndTurn(executed);
        var playableAlternative = candidates.Any(candidate =>
            candidate.Legal && !IsEndTurn(candidate));
        var unusedPower = Feature(frame.StateFeatures, "power") > 0.5d;
        return dominated
               || executedEndTurn && playableAlternative && unusedPower;
    }

    private static bool IsEndTurn(CombatEpisodeCandidate candidate)
    {
        return string.Equals(
                   candidate.SourceId,
                   "simulation:end-turn",
                   StringComparison.OrdinalIgnoreCase)
               || Feature(candidate.Features, "actionKindEndTurn") > 0.5d;
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
        int maximumFrames)
    {
        var frames = episode.Frames ?? new List<CombatEpisodeFrame>();
        if (frames.Count <= maximumFrames)
        {
            return frames;
        }
        var selected = new List<CombatEpisodeFrame>(maximumFrames);
        for (var index = 0; index < maximumFrames; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (frames.Count - 1d) / (maximumFrames - 1d),
                MidpointRounding.AwayFromZero);
            selected.Add(frames[sourceIndex]);
        }
        return selected;
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

        public string Identity { get; }
    }
}
