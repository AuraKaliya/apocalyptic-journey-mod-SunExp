using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public static class AuraToolsRoleTrainingDiagnostics
{
    public static Dictionary<string, double> Analyze(
        IEnumerable<CombatEpisode> source,
        IEnumerable<CombatFoundationCampaignObservation>? campaignObservations = null)
    {
        var episodes = (source ?? Array.Empty<CombatEpisode>())
            .Where(episode => episode != null)
            .ToList();
        var result = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var frames = episodes.SelectMany(episode => episode.Frames).ToList();
        var terminalSnapshots = (campaignObservations
                                 ?? Array.Empty<CombatFoundationCampaignObservation>())
            .Where(observation => observation != null
                                  && string.Equals(
                                      observation.SourceStage,
                                      "training",
                                      StringComparison.OrdinalIgnoreCase))
            .Select(observation => new TerminalSnapshot
            {
                DifficultyId = observation.DifficultyId,
                Victory = observation.FinalBossVictory,
                PlayerHp = Math.Max(0, observation.FinalHp),
                PlayerMaxHp = Math.Max(1, observation.FinalMaxHp),
                DoomPower = Math.Max(0, observation.FinalDoomPower)
            })
            .ToList();
        if (terminalSnapshots.Count == 0)
        {
            terminalSnapshots = episodes
                .Where(episode =>
                    episode.Campaign?.TerminalSnapshotKnown == true)
                .GroupBy(episode => string.IsNullOrWhiteSpace(
                        episode.JourneyRunId)
                    ? "episode:" + episode.EpisodeId
                    : "journey:" + episode.JourneyRunId,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(episode => episode.JourneyBattleIndex)
                    .ThenByDescending(episode => episode.CreatedUtc)
                    .First())
                .Select(episode => new TerminalSnapshot
                {
                    DifficultyId = episode.Campaign.DifficultyId,
                    Victory = episode.Campaign.FinalBossVictory,
                    PlayerHp = episode.Campaign.TerminalPlayerHp,
                    PlayerMaxHp = episode.Campaign.TerminalPlayerMaxHp,
                    DoomPower = episode.Campaign.TerminalDoomPower
                })
                .ToList();
        }
        result["episodes"] = episodes.Count;
        result["frames"] = frames.Count;
        result["battle-final-max-hp.mean"] = episodes.Count == 0
            ? 0d
            : episodes.Average(episode => episode.FinalPlayerMaxHp);
        result["battle-final-max-hp.maximum"] = episodes.Count == 0
            ? 0d
            : episodes.Max(episode => episode.FinalPlayerMaxHp);
        result["journey-terminal-episodes"] = terminalSnapshots.Count;
        result["journey-terminal-snapshots"] = terminalSnapshots.Count;
        result["journey-final-max-hp.mean"] = terminalSnapshots.Count == 0
            ? 0d
            : terminalSnapshots.Average(snapshot => snapshot.PlayerMaxHp);
        result["journey-final-max-hp.median"] = Median(
            terminalSnapshots.Select(snapshot =>
                    (double)snapshot.PlayerMaxHp)
                .ToList());
        result["journey-final-max-hp.maximum"] = terminalSnapshots.Count == 0
            ? 0d
            : terminalSnapshots.Max(snapshot => snapshot.PlayerMaxHp);
        result["journey-final-hp.mean"] = terminalSnapshots.Count == 0
            ? 0d
            : terminalSnapshots.Average(snapshot => snapshot.PlayerHp);
        result["journey-final-doom.mean"] = terminalSnapshots.Count == 0
            ? 0d
            : terminalSnapshots.Average(snapshot => snapshot.DoomPower);
        result["journey-final-doom.median"] = Median(
            terminalSnapshots.Select(snapshot =>
                    (double)snapshot.DoomPower)
                .ToList());
        result["journey-final-doom.maximum"] = terminalSnapshots.Count == 0
            ? 0d
            : terminalSnapshots.Max(snapshot => snapshot.DoomPower);
        AddTerminalBreakdown(result, terminalSnapshots, "normal", true);
        AddTerminalBreakdown(result, terminalSnapshots, "normal", false);
        AddTerminalBreakdown(result, terminalSnapshots, "advanced", true);
        AddTerminalBreakdown(result, terminalSnapshots, "advanced", false);
        // Established aliases now point only at exact campaign snapshots.
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
        var survivalOverrideFrames = 0;
        var selectedSurvivalActions = 0;
        var selectedNonPositiveDevours = 0;
        var selectedUnderpreparedTransforms = 0;
        var selectedNightmareBuilders = 0;
        var selectedNightmareExpectedExtraStacks = 0d;
        var selectedNightmareExpectedThresholdGain = 0d;
        var skillTimingEvaluatedCandidates = 0;
        var skillTimingPositiveOpportunityFrames = 0;
        var skillTimingEpisodesWithPositiveOpportunity = 0;
        var skillTimingPositiveSkillEpisodes = 0;
        var skillTimingExpiredPositiveSkills = 0;
        var skillTimingSelectedActivations = 0;
        var skillTimingSelectedPositiveActivations = 0;
        var skillTimingSelectedBetterToWait = 0;
        var skillTimingSelectedRedundant = 0;
        var selectedSkillTimingAdvantages = new List<double>();
        var skillTimingBySkill = AuraToolsWitchSkillTimingProvider.Cooldowns.Keys
            .ToDictionary(
                id => id,
                _ => new SkillTimingMetric(),
                StringComparer.OrdinalIgnoreCase);
        var devourDoomGains = new List<double>();
        var devourMaximumHpGains = new List<double>();
        var firstTransformDoom = new List<double>();
        foreach (var episode in episodes)
        {
            CombatEpisodeFrame? lastDevour = null;
            var nanaEpisode = IsNanaEpisode(episode);
            var positiveSkillIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var activatedSkillIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var frame in episode.Frames
                         .OrderBy(item => item.ActionSequence))
            {
                var timingCandidates = frame.Candidates.Where(candidate =>
                        Value(
                            candidate.Features,
                            CombatSkillTimingFeatureNames.Active) > 0.5d)
                    .ToList();
                skillTimingEvaluatedCandidates += timingCandidates.Count;
                foreach (var group in timingCandidates.GroupBy(
                             SkillIdentity,
                             StringComparer.OrdinalIgnoreCase))
                {
                    var metric = GetSkillMetric(skillTimingBySkill, group.Key);
                    metric.EvaluatedFrames++;
                    metric.EvaluatedCandidates += group.Count();
                }
                var positiveTimingCandidates = timingCandidates.Where(candidate =>
                        Value(
                            candidate.Features,
                            CombatSkillTimingFeatureNames.PositiveOpportunity) > 0.5d)
                    .ToList();
                if (positiveTimingCandidates.Count > 0)
                {
                    skillTimingPositiveOpportunityFrames++;
                    foreach (var group in positiveTimingCandidates.GroupBy(
                                 SkillIdentity,
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        var skillId = group.Key;
                        positiveSkillIds.Add(skillId);
                        GetSkillMetric(skillTimingBySkill, skillId)
                            .PositiveOpportunityFrames++;
                    }
                }
                if (nanaEpisode)
                {
                    roleObservedFrames++;
                    if (Value(
                            frame.StateFeatures,
                            "roleStrategy:nana.safe-growth-window") > 0.5d)
                    {
                        safeGrowthWindowFrames++;
                    }
                    if (Value(
                            frame.StateFeatures,
                            "roleStrategy:nana.survival-override") > 0.5d)
                    {
                        survivalOverrideFrames++;
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
                        CombatSkillTimingFeatureNames.Active) > 0.5d)
                {
                    skillTimingSelectedActivations++;
                    var selectedSkillId = SkillIdentity(selected);
                    activatedSkillIds.Add(selectedSkillId);
                    var selectedMetric = GetSkillMetric(
                        skillTimingBySkill,
                        selectedSkillId);
                    selectedMetric.SelectedActivations++;
                    var timingAdvantage = Value(
                        selected.Features,
                        CombatSkillTimingFeatureNames.TimingAdvantage);
                    selectedSkillTimingAdvantages.Add(timingAdvantage);
                    if (timingAdvantage > 0d)
                    {
                        skillTimingSelectedPositiveActivations++;
                        selectedMetric.SelectedPositiveActivations++;
                    }
                    if (Value(
                            selected.Features,
                            CombatSkillTimingFeatureNames.BetterToWait) > 0.5d)
                    {
                        skillTimingSelectedBetterToWait++;
                        selectedMetric.SelectedBetterToWait++;
                    }
                    if (Value(
                            selected.Features,
                            CombatSkillTimingFeatureNames.RedundancyCost) > 0d)
                    {
                        skillTimingSelectedRedundant++;
                        selectedMetric.SelectedRedundant++;
                    }
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
                    if (Value(
                            selected.Features,
                            "nana:devour-net-value") <= 0d)
                    {
                        selectedNonPositiveDevours++;
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
                        if (Value(
                                selected.Features,
                                "roleStrategy:nana.transform-ready") <= 0.5d)
                        {
                            selectedUnderpreparedTransforms++;
                        }
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
                if (Value(
                        selected.Features,
                        "roleStrategy:nana.survival-action") > 0.5d)
                {
                    selectedSurvivalActions++;
                }
                if (Value(
                        selected.Features,
                        "nightmare:eligible-negative-events") > 0d)
                {
                    selectedNightmareBuilders++;
                    selectedNightmareExpectedExtraStacks += Value(
                        selected.Features,
                        "nightmare:expected-extra-stacks");
                    selectedNightmareExpectedThresholdGain += Value(
                        selected.Features,
                        "nightmare:expected-devour-threshold-gain");
                }
            }
            if (positiveSkillIds.Count > 0)
            {
                skillTimingEpisodesWithPositiveOpportunity++;
                skillTimingPositiveSkillEpisodes += positiveSkillIds.Count;
                skillTimingExpiredPositiveSkills += positiveSkillIds.Count(id =>
                    !activatedSkillIds.Contains(id));
                foreach (var skillId in positiveSkillIds.Where(id =>
                             !activatedSkillIds.Contains(id)))
                {
                    GetSkillMetric(skillTimingBySkill, skillId)
                        .ExpiredPositiveSkillEpisodes++;
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
        result["nana.survival-override-frames"] = survivalOverrideFrames;
        result["nana.selected-survival-actions"] = selectedSurvivalActions;
        result["nana.premature-devours"] = prematureDevours;
        result["nana.premature-devour-rate"] = devours == 0
            ? 0d
            : prematureDevours / (double)devours;
        result["nana.selected-strategically-prohibited-actions"] =
            selectedStrategicallyProhibitedActions;
        result["nana.selected-nonpositive-devours"] =
            selectedNonPositiveDevours;
        result["nana.selected-underprepared-transforms"] =
            selectedUnderpreparedTransforms;
        result["nana.selected-nightmare-builders"] =
            selectedNightmareBuilders;
        result["nana.selected-nightmare-expected-extra-stacks"] =
            selectedNightmareExpectedExtraStacks;
        result["nana.selected-nightmare-expected-threshold-gain"] =
            selectedNightmareExpectedThresholdGain;
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
        result["nana.underprepared-transform-rate"] = firstTransforms == 0
            ? 0d
            : selectedUnderpreparedTransforms / (double)firstTransforms;
        result["nana.devour-transform-link-rate"] = firstTransforms == 0
            ? 0d
            : transformAfterRecentDevour / (double)firstTransforms;
        result["nana.finale-certification-rate"] = finales == 0
            ? 0d
            : certifiedFinales / (double)finales;
        result["skill-timing.evaluated-candidates"] =
            skillTimingEvaluatedCandidates;
        result["skill-timing.positive-opportunity-frames"] =
            skillTimingPositiveOpportunityFrames;
        result["skill-timing.episodes-with-positive-opportunity"] =
            skillTimingEpisodesWithPositiveOpportunity;
        result["skill-timing.positive-skill-episodes"] =
            skillTimingPositiveSkillEpisodes;
        result["skill-timing.expired-positive-skills"] =
            skillTimingExpiredPositiveSkills;
        result["skill-timing.expired-positive-skill-rate"] =
            skillTimingPositiveSkillEpisodes == 0
                ? 0d
                : skillTimingExpiredPositiveSkills
                  / (double)skillTimingPositiveSkillEpisodes;
        result["skill-timing.selected-activations"] =
            skillTimingSelectedActivations;
        result["skill-timing.selected-positive-activations"] =
            skillTimingSelectedPositiveActivations;
        result["skill-timing.selected-better-to-wait"] =
            skillTimingSelectedBetterToWait;
        result["skill-timing.selected-redundant"] =
            skillTimingSelectedRedundant;
        result["skill-timing.selected-advantage.mean"] =
            Mean(selectedSkillTimingAdvantages);
        foreach (var pair in skillTimingBySkill.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var prefix = "skill-timing.skill." + pair.Key + ".";
            result[prefix + "evaluated-frames"] = pair.Value.EvaluatedFrames;
            result[prefix + "evaluated-candidates"] =
                pair.Value.EvaluatedCandidates;
            result[prefix + "positive-opportunity-frames"] =
                pair.Value.PositiveOpportunityFrames;
            result[prefix + "expired-positive-skill-episodes"] =
                pair.Value.ExpiredPositiveSkillEpisodes;
            result[prefix + "selected-activations"] =
                pair.Value.SelectedActivations;
            result[prefix + "selected-positive-activations"] =
                pair.Value.SelectedPositiveActivations;
            result[prefix + "selected-better-to-wait"] =
                pair.Value.SelectedBetterToWait;
            result[prefix + "selected-redundant"] =
                pair.Value.SelectedRedundant;
        }
        return result;
    }

    private static SkillTimingMetric GetSkillMetric(
        IDictionary<string, SkillTimingMetric> metrics,
        string skillId)
    {
        var normalized = string.IsNullOrWhiteSpace(skillId)
            ? "unknown"
            : skillId.Trim();
        if (!metrics.TryGetValue(normalized, out var metric))
        {
            metric = new SkillTimingMetric();
            metrics[normalized] = metric;
        }
        return metric;
    }

    private static string SkillIdentity(CombatEpisodeCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.SourceId)
            ? candidate.CandidateId ?? ""
            : candidate.SourceId;
    }

    private static void AddTerminalBreakdown(
        IDictionary<string, double> result,
        IReadOnlyCollection<TerminalSnapshot> terminalSnapshots,
        string difficultyId,
        bool victory)
    {
        var subset = terminalSnapshots.Where(snapshot =>
                string.Equals(
                    snapshot.DifficultyId,
                    difficultyId,
                    StringComparison.OrdinalIgnoreCase)
                && snapshot.Victory == victory)
            .ToList();
        var outcome = victory ? "victory" : "failure";
        var prefix = "journey-" + difficultyId + "-" + outcome;
        result[prefix + "-terminal-snapshots"] = subset.Count;
        result[prefix + "-final-max-hp.mean"] = subset.Count == 0
            ? 0d
            : subset.Average(snapshot => snapshot.PlayerMaxHp);
        result[prefix + "-final-doom.mean"] = subset.Count == 0
            ? 0d
            : subset.Average(snapshot => snapshot.DoomPower);
    }

    private sealed class TerminalSnapshot
    {
        public string DifficultyId { get; set; } = "";

        public bool Victory { get; set; }

        public int PlayerHp { get; set; }

        public int PlayerMaxHp { get; set; }

        public int DoomPower { get; set; }
    }

    private sealed class SkillTimingMetric
    {
        public int EvaluatedFrames { get; set; }
        public int EvaluatedCandidates { get; set; }
        public int PositiveOpportunityFrames { get; set; }
        public int ExpiredPositiveSkillEpisodes { get; set; }
        public int SelectedActivations { get; set; }
        public int SelectedPositiveActivations { get; set; }
        public int SelectedBetterToWait { get; set; }
        public int SelectedRedundant { get; set; }
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
