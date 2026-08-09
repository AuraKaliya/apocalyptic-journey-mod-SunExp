using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatLiveEpisodeAssembler
{
    public const string LiveScenarioId = "witch.world-simulation.live";

    public static List<CombatEpisode> Assemble(IEnumerable<CombatTrainingSample>? source)
    {
        return (source ?? Array.Empty<CombatTrainingSample>())
            .Where(sample => CombatTrainingProtocol.IsCompatible(sample)
                             && sample.BattleSessionId > 0)
            .GroupBy(sample => sample.BattleSessionId)
            .Select(group => TryAssemble(group.Key, group, out var episode) ? episode : null)
            .Where(episode => episode != null)
            .Cast<CombatEpisode>()
            .OrderBy(episode => episode.CreatedUtc)
            .ToList();
    }

    public static bool TryAssemble(
        long battleSessionId,
        IEnumerable<CombatTrainingSample>? source,
        out CombatEpisode episode)
    {
        episode = new CombatEpisode();
        if (battleSessionId <= 0)
        {
            return false;
        }

        var samples = (source ?? Array.Empty<CombatTrainingSample>())
            .Where(sample => CombatTrainingProtocol.IsCompatible(sample)
                             && sample.BattleSessionId == battleSessionId
                             && IsCompleted(sample))
            .OrderBy(sample => sample.DecisionIndex)
            .ThenBy(sample => sample.Sequence)
            .ThenBy(sample => sample.CreatedUtc)
            .GroupBy(FrameIdentity, StringComparer.Ordinal)
            .Select(PreferHumanSample)
            .OrderBy(sample => sample.DecisionIndex)
            .ThenBy(sample => sample.Sequence)
            .ThenBy(sample => sample.CreatedUtc)
            .ToList();
        if (samples.Count == 0)
        {
            return false;
        }

        var terminal = samples
            .Where(sample => sample.Terminal && IsKnownOutcome(sample.BattleOutcome))
            .OrderByDescending(sample => sample.CreatedUtc)
            .FirstOrDefault();
        if (terminal == null)
        {
            return false;
        }

        var victory = string.Equals(
            terminal.BattleOutcome,
            "victory",
            StringComparison.OrdinalIgnoreCase);
        var finalHp = FinalPlayerHp(terminal);
        var maximumHp = Math.Max(
            1,
            (int)Math.Round(Feature(terminal.StateFeatures, "playerMaxHp")));
        var policyIds = samples
            .Select(sample => sample.Selection.ExecutedBy)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var profile = samples
            .Select(sample => sample.DecisionProfile)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "balanced";

        episode = new CombatEpisode
        {
            EpisodeId = "live-battle:"
                        + battleSessionId.ToString(CultureInfo.InvariantCulture)
                        + ":"
                        + terminal.CreatedUtc.ToUniversalTime().Ticks.ToString(
                            CultureInfo.InvariantCulture),
            ScenarioId = LiveScenarioId,
            BattleSessionId = battleSessionId,
            RulesetHash = "live-game:" + (terminal.GameBuild ?? ""),
            OwnerModSetHash = terminal.OwnerModSetHash,
            ContentSetHash = terminal.ContentSetHash,
            BaseModelId = terminal.BaseModelId,
            ActiveAdapterIds = (terminal.ActiveAdapterIds ?? new List<string>())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PolicyId = policyIds.Count == 1 ? policyIds[0] : "mixed",
            DecisionProfile = profile,
            Outcome = victory ? "victory" : "defeat",
            Turns = Math.Max(1, MaximumTurn(samples)),
            FinalPlayerHp = finalHp,
            FinalPlayerMaxHp = maximumHp,
            DamageTaken = DamageTaken(samples),
            SemanticCoverage = 1d,
            Authoritative = true,
            Provenance = "live-world-simulation",
            CreatedUtc = samples.Min(sample => sample.CreatedUtc)
        };

        for (var index = 0; index < samples.Count; index++)
        {
            var frame = ToFrame(samples[index], index);
            frame.BattleSessionId = battleSessionId;
            frame.DecisionSequence = index + 1L;
            episode.Frames.Add(frame);
        }
        ApplyTerminalTargets(episode, victory);
        CombatPolicyValueEpisodeMigration.NormalizeSemanticsInPlace(episode);
        return episode.Frames.Count > 0;
    }

    private static CombatEpisodeFrame ToFrame(CombatTrainingSample sample, int index)
    {
        var frame = new CombatEpisodeFrame
        {
            Turn = Math.Max(
                1,
                (int)Math.Round(Feature(sample.StateFeatures, "turn", index + 1))),
            ActionSequence = sample.Sequence,
            StateFingerprint = sample.StateFingerprint ?? "",
            StateFeatures = CopyFinite(sample.StateFeatures),
            ExecutedCandidateId = sample.Selection.ExecutedCandidateId
        };
        foreach (var candidate in sample.Candidates ?? new List<CombatTrainingCandidate>())
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.CandidateId))
            {
                continue;
            }
            var features = CopyFinite(candidate.Features);
            features["cost"] = candidate.Cost;
            features["ruleScore"] = Finite(candidate.RuleScore);
            features["baseRuleScore"] = Finite(candidate.BaseRuleScore);
            features["planScore"] = Finite(candidate.PlanScore);
            var semantics = candidate.Semantics ?? new CombatActionSemantics();
            features["damage"] = Finite(semantics.Damage);
            features["trueDamage"] = Finite(semantics.TrueDamage);
            features["damageOverTime"] = Finite(semantics.DamageOverTime);
            features["hitCount"] = Finite(semantics.HitCount);
            features["defend"] = Finite(semantics.Defend);
            features["heal"] = Finite(semantics.Heal);
            features["draw"] = Finite(semantics.Draw);
            features["energyGain"] = Finite(semantics.EnergyGain);
            features["buff"] = Finite(semantics.Buff);
            features["debuff"] = Finite(semantics.Debuff);
            features["cleanse"] = Finite(semantics.Cleanse);
            features["costReduction"] = Finite(semantics.CostReduction);
            features["cardGeneration"] = Finite(semantics.CardGeneration);
            features["persistentValue"] = Finite(semantics.PersistentValue);
            features["scaling"] = Finite(semantics.Scaling);
            features["risk"] = Finite(semantics.Risk);
            features["uncertainty"] = Finite(semantics.Uncertainty);
            frame.Candidates.Add(new CombatEpisodeCandidate
            {
                CandidateId = candidate.CandidateId,
                SourceId = candidate.SourceId ?? "",
                OwnerModId = candidate.OwnerModId ?? "",
                Legal = candidate.Legal,
                SearchVisits = Math.Max(0, candidate.SearchVisits),
                SearchPrior = Finite(candidate.SearchPrior),
                SearchValue = Finite(candidate.PlanScore),
                SearchDeathRisk = Finite(candidate.SearchDeathRisk),
                SearchMeanReturn = Finite(candidate.SearchMeanReturn),
                SearchReturnStandardError =
                    Finite(candidate.SearchReturnStandardError),
                SearchLowerTailMean = Finite(candidate.SearchLowerTailMean),
                SearchReturnQuantiles = candidate.SearchReturnQuantiles
                    .Select(Finite)
                    .Take(16)
                    .ToList(),
                Features = features
            });
        }
        return frame;
    }

    private static void ApplyTerminalTargets(CombatEpisode episode, bool victory)
    {
        var terminal = victory ? 1d : -1d;
        var hpRatio = Math.Max(
            0d,
            Math.Min(1d, (double)episode.FinalPlayerHp / episode.FinalPlayerMaxHp));
        for (var index = 0; index < episode.Frames.Count; index++)
        {
            var remaining = episode.Frames.Count - index - 1;
            var frame = episode.Frames[index];
            frame.LongTermReturn = terminal * Math.Pow(0.99d, remaining);
            frame.WinTarget = victory ? 1d : 0d;
            frame.DeathTarget = victory ? 0d : 1d;
            frame.RemainingHpRatioTarget = hpRatio;
            frame.RemainingTurnsTarget = remaining;
        }
    }

    private static CombatTrainingSample PreferHumanSample(
        IGrouping<string, CombatTrainingSample> group)
    {
        return group
            .OrderByDescending(sample => string.Equals(
                sample.Selection.ExecutedBy,
                "human",
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(sample => sample.Terminal)
            .ThenByDescending(sample => sample.CreatedUtc)
            .First();
    }

    private static string FrameIdentity(CombatTrainingSample sample)
    {
        return sample.DecisionIndex.ToString(CultureInfo.InvariantCulture)
               + "|" + sample.Sequence.ToString(CultureInfo.InvariantCulture)
               + "|" + (sample.StateFingerprint ?? "")
               + "|" + sample.Selection.ExecutedCandidateId;
    }

    private static bool IsCompleted(CombatTrainingSample sample)
    {
        return string.Equals(
                   sample.CompletionState,
                   "Completed",
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   sample.Selection.Protocol,
                   "aura.combat-ai.selection.v1",
                   StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(
                   sample.Selection.ExecutedCandidateId);
    }

    private static bool IsKnownOutcome(string? value)
    {
        return string.Equals(value, "victory", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "defeat", StringComparison.OrdinalIgnoreCase);
    }

    private static int FinalPlayerHp(CombatTrainingSample terminal)
    {
        var before = Feature(terminal.StateFeatures, "playerHp");
        var change = terminal.RewardComponents?.PlayerHpChange ?? 0d;
        return Math.Max(0, (int)Math.Round(before + change));
    }

    private static int MaximumTurn(IReadOnlyList<CombatTrainingSample> samples)
    {
        var result = 0;
        for (var index = 0; index < samples.Count; index++)
        {
            result = Math.Max(
                result,
                (int)Math.Round(Feature(samples[index].StateFeatures, "turn", index + 1)));
        }
        return result;
    }

    private static int DamageTaken(IEnumerable<CombatTrainingSample> samples)
    {
        var total = 0d;
        foreach (var sample in samples)
        {
            total += Math.Max(0d, -(sample.RewardComponents?.PlayerHpChange ?? 0d));
        }
        return (int)Math.Round(total);
    }

    private static Dictionary<string, double> CopyFinite(
        IReadOnlyDictionary<string, double>? source)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source ?? new Dictionary<string, double>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                result[pair.Key] = Finite(pair.Value);
            }
        }
        return result;
    }

    private static double Feature(
        IReadOnlyDictionary<string, double>? values,
        string key,
        double fallback = 0d)
    {
        return values != null
               && values.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private static double Finite(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
    }
}
