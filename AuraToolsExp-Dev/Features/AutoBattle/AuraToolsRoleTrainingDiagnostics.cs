using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public static class AuraToolsRoleTrainingDiagnostics
{
    public static Dictionary<string, double> Analyze(
        IEnumerable<CombatEpisode> source)
    {
        var episodes = (source ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null)
            .ToList();
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var frames = episodes.SelectMany(episode => episode.Frames).ToList();
        var terminalEpisodes = episodes
            .GroupBy(episode => string.IsNullOrWhiteSpace(episode.JourneyRunId)
                ? "episode:" + episode.EpisodeId
                : "journey:" + episode.JourneyRunId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(episode => episode.JourneyBattleIndex)
                .ThenByDescending(episode => episode.CreatedUtc)
                .First())
            .ToList();
        result["episodes"] = episodes.Count;
        result["frames"] = frames.Count;
        result["battle-final-max-hp.mean"] = episodes.Count == 0
            ? 0d
            : episodes.Average(episode => episode.FinalPlayerMaxHp);
        result["battle-final-max-hp.maximum"] = episodes.Count == 0
            ? 0d
            : episodes.Max(episode => episode.FinalPlayerMaxHp);
        result["journey-terminal-episodes"] = terminalEpisodes.Count;
        result["journey-final-max-hp.mean"] = terminalEpisodes.Count == 0
            ? 0d
            : terminalEpisodes.Average(episode => episode.FinalPlayerMaxHp);
        result["journey-final-max-hp.maximum"] = terminalEpisodes.Count == 0
            ? 0d
            : terminalEpisodes.Max(episode => episode.FinalPlayerMaxHp);
        // Keep the established keys, but make their meaning match the UI label:
        // final adventure health, not the average of every intermediate battle.
        result["final-max-hp.mean"] = result["journey-final-max-hp.mean"];
        result["final-max-hp.maximum"] =
            result["journey-final-max-hp.maximum"];

        var devours = 0;
        var transforms = 0;
        var earlyTransforms = 0;
        var finales = 0;
        var certifiedFinales = 0;
        var bleedingActions = 0;
        var positiveBleedOpportunityDevours = 0;
        var transformAfterRecentDevour = 0;
        var transformAfterDevourSameTurn = 0;
        var transformAfterDevourNextTurn = 0;
        var firstTransforms = 0;
        var repeatTransforms = 0;
        var bankIntents = 0;
        var calamityActions = 0;
        var roleEligibleFrames = 0;
        var rolePreparedFrames = 0;
        var roleObservedFrames = 0;
        var roleNonActionableFrames = 0;
        var selectedStrategicallyProhibitedActions = 0;
        var prematureDevours = 0;
        var selectedGrowthBuilders = 0;
        var safeGrowthWindowFrames = 0;
        var devourDoomGains = new List<double>();
        var devourMaximumHpGains = new List<double>();
        var firstTransformDoom = new List<double>();
        foreach (var episode in episodes)
        {
            CombatEpisodeFrame? lastDevour = null;
            var nanaEpisode = IsNanaEpisode(episode);
            foreach (var frame in episode.Frames
                         .OrderBy(item => item.ActionSequence))
            {
                if (nanaEpisode)
                {
                    roleObservedFrames++;
                    if (Value(
                            frame.StateFeatures,
                            "roleStrategy:nana.safe-growth-window") > 0.5d)
                    {
                        safeGrowthWindowFrames++;
                    }
                    if (frame.Candidates.Count == 0)
                    {
                        roleNonActionableFrames++;
                    }
                    else
                    {
                        roleEligibleFrames++;
                        if (Value(
                                frame.StateFeatures,
                                CombatRoleStrategyFeatureNames.Active) > 0.5d
                            || frame.Candidates.Any(candidate => Value(
                                candidate.Features,
                                CombatRoleStrategyFeatureNames.Active) > 0.5d))
                        {
                            rolePreparedFrames++;
                        }
                    }
                }
                var selected = frame.Candidates.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.CandidateId,
                        frame.ExecutedCandidateId,
                        StringComparison.Ordinal));
                if (selected == null)
                {
                    continue;
                }
                if (Value(
                        selected.Features,
                        CombatRoleStrategyFeatureNames.StrategicallyProhibited) > 0.5d)
                {
                    selectedStrategicallyProhibitedActions++;
                }
                if (IdEquals(selected.SourceId, "careercard_2"))
                {
                    devours++;
                    lastDevour = frame;
                    devourDoomGains.Add(Value(
                        selected.Features,
                        "nana:projected-doom-gain"));
                    devourMaximumHpGains.Add(Value(
                        selected.Features,
                        "nana:projected-max-hp-gain"));
                    if (Value(
                            selected.Features,
                            "roleStrategy:nana.defer-harvest-same-turn") > 0.5d
                        || Value(
                            selected.Features,
                            "roleStrategy:nana.defer-harvest-cross-turn") > 0.5d)
                    {
                        prematureDevours++;
                    }
                    if (Value(
                            selected.Features,
                            "roleStrategy:nana.bleed-opportunity-cost") > 0d)
                    {
                        positiveBleedOpportunityDevours++;
                    }
                }
                else if (IdEquals(selected.SourceId, "careercard_3"))
                {
                    transforms++;
                    var repeated = Value(
                                       selected.Features,
                                       "nana:repeat-transform") > 0.5d
                                   || Value(
                                       frame.StateFeatures,
                                       "playerRole:career_4") > 0.5d
                                   || Value(
                                       frame.StateFeatures,
                                       "playerStatus:SpecialBuff_CalamityIncarnates") > 0.5d;
                    if (repeated)
                    {
                        repeatTransforms++;
                    }
                    else
                    {
                        firstTransforms++;
                        firstTransformDoom.Add(Math.Max(
                            Value(
                                selected.Features,
                                "nana:doom-at-transform"),
                            Value(
                                frame.StateFeatures,
                                "roleStrategy:nana.doom")));
                    }
                    if (Value(
                            selected.Features,
                            "roleStrategy:nana.early-transform") > 0.5d)
                    {
                        earlyTransforms++;
                    }
                    if (!repeated && lastDevour != null)
                    {
                        var turnDelta = frame.Turn - lastDevour.Turn;
                        if (turnDelta is >= 0 and <= 1)
                        {
                            transformAfterRecentDevour++;
                            if (turnDelta == 0)
                            {
                                transformAfterDevourSameTurn++;
                            }
                            else
                            {
                                transformAfterDevourNextTurn++;
                            }
                        }
                    }
                    lastDevour = null;
                }
                else if (IdEquals(
                             selected.SourceId,
                             AuraToolsNanaRoleStrategyProvider.FinaleCardId))
                {
                    finales++;
                    if (Value(
                            selected.Features,
                            CombatRoleStrategyFeatureNames
                                .SafeContinuationCertified) > 0.5d)
                    {
                        certifiedFinales++;
                    }
                }
                if (selected.SourceId.StartsWith(
                        "blood_",
                        StringComparison.OrdinalIgnoreCase))
                {
                    bleedingActions++;
                }
                if (Value(
                        selected.Features,
                        "roleStrategy:nana.intent-bank") > 0.5d)
                {
                    bankIntents++;
                }
                if (Value(
                        selected.Features,
                        "roleStrategy:nana.calamity-action") > 0.5d)
                {
                    calamityActions++;
                }
                if (Value(
                        selected.Features,
                        "roleStrategy:nana.growth-builder") > 0.5d)
                {
                    selectedGrowthBuilders++;
                }
            }
        }
        result["nana.devours"] = devours;
        result["nana.transforms"] = transforms;
        result["nana.first-transforms"] = firstTransforms;
        result["nana.repeat-transforms"] = repeatTransforms;
        result["nana.early-transforms"] = earlyTransforms;
        result["nana.transform-after-devour-within-one-turn"] =
            transformAfterRecentDevour;
        result["nana.transform-after-devour-same-turn"] =
            transformAfterDevourSameTurn;
        result["nana.transform-after-devour-next-turn"] =
            transformAfterDevourNextTurn;
        result["nana.finales"] = finales;
        result["nana.certified-finales"] = certifiedFinales;
        result["nana.bleeding-actions"] = bleedingActions;
        result["nana.enemy-bleed-opportunity-devours"] =
            positiveBleedOpportunityDevours;
        result["nana.bank-intents"] = bankIntents;
        result["nana.calamity-actions"] = calamityActions;
        result["nana.selected-growth-builders"] = selectedGrowthBuilders;
        result["nana.safe-growth-window-frames"] = safeGrowthWindowFrames;
        result["nana.premature-devours"] = prematureDevours;
        result["nana.premature-devour-rate"] = devours == 0
            ? 0d
            : prematureDevours / (double)devours;
        result["nana.selected-strategically-prohibited-actions"] =
            selectedStrategicallyProhibitedActions;
        result["nana.devour-doom-gain.mean"] = Mean(devourDoomGains);
        result["nana.devour-doom-gain.median"] = Median(devourDoomGains);
        result["nana.devour-doom-gain.maximum"] = Maximum(devourDoomGains);
        result["nana.devour-max-hp-gain.mean"] =
            Mean(devourMaximumHpGains);
        result["nana.devour-max-hp-gain.median"] =
            Median(devourMaximumHpGains);
        result["nana.devour-max-hp-gain.maximum"] =
            Maximum(devourMaximumHpGains);
        result["nana.first-transform-doom.mean"] = Mean(firstTransformDoom);
        result["nana.first-transform-doom.median"] = Median(firstTransformDoom);
        result["nana.first-transform-doom.maximum"] =
            Maximum(firstTransformDoom);
        result["nana.role-strategy-observed-frames"] = roleObservedFrames;
        result["nana.role-strategy-non-actionable-frames"] =
            roleNonActionableFrames;
        result["nana.role-strategy-eligible-frames"] = roleEligibleFrames;
        result["nana.role-strategy-prepared-frames"] = rolePreparedFrames;
        result["nana.role-strategy-frame-coverage"] = roleEligibleFrames == 0
            ? 0d
            : rolePreparedFrames / (double)roleEligibleFrames;
        result["nana.early-transform-rate"] = transforms == 0
            ? 0d
            : earlyTransforms / (double)transforms;
        result["nana.devour-transform-link-rate"] = firstTransforms == 0
            ? 0d
            : transformAfterRecentDevour / (double)firstTransforms;
        result["nana.finale-certification-rate"] = finales == 0
            ? 0d
            : certifiedFinales / (double)finales;
        return result;
    }

    private static double Mean(IReadOnlyCollection<double> values)
    {
        return values.Count == 0 ? 0d : values.Average();
    }

    private static double Maximum(IReadOnlyCollection<double> values)
    {
        return values.Count == 0 ? 0d : values.Max();
    }

    private static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
    }

    private static bool IsNanaEpisode(CombatEpisode episode)
    {
        return episode.Frames.Any(frame =>
            Value(frame.StateFeatures, "playerRole:career_2") > 0.5d
            || Value(frame.StateFeatures, "playerRole:career_4") > 0.5d
            || frame.Candidates.Any(candidate =>
                IdEquals(candidate.SourceId, "careercard_2")
                || IdEquals(candidate.SourceId, "careercard_3")));
    }

    private static double Value(
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

    private static bool IdEquals(string? left, string? right)
    {
        return string.Equals(
            left,
            right,
            StringComparison.OrdinalIgnoreCase);
    }
}
