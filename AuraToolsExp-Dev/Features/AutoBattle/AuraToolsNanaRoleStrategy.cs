using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal sealed class AuraToolsNanaRoleStrategyProvider :
    ICombatRoleStrategyProvider
{
    internal const string FinaleCardId = "Crowdfundingcard_43";

    private const string DoomStatusId = "buff_DoomPower";
    private const string BleedingStatusId = "buff_bleeding";
    private const string ToxinStatusId = "buff_toxin";
    private const string CalamityStatusId = "SpecialBuff_CalamityIncarnates";
    private const string NightmareBlessingId = "blessing_40";
    private const double NightmareDuplicateProbability = 0.20d;
    private const double PreferredDevourNetValue = 2d;

    private enum NanaPhase
    {
        Build = 0,
        Harvest = 1,
        PrepareBurst = 2,
        CalamityBurst = 3,
        SurvivalOverride = 4
    }

    public bool TryEnrich(CombatStateObservation state)
    {
        if (!IsNana(state))
        {
            return false;
        }
        state.Features ??= new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var actions = state.Actions
            .Where(action => action != null)
            .ToList();
        foreach (var action in actions)
        {
            action.Features ??= new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase);
        }

        var doom = StatusLevel(state.Player, DoomStatusId);
        var transformed = string.Equals(
                              state.Player.DefinitionId,
                              "career_4",
                              StringComparison.OrdinalIgnoreCase)
                          || StatusLevel(state.Player, CalamityStatusId) > 0;
        var nightmareActive = IsNightmareActive(state);
        var campaignContextKnown = StateFeature(
            state,
            CombatCampaignContextFeatureNames.ContextKnown,
            0d) > 0.5d;
        var campaignProgress = ClampUnit(StateFeature(
            state,
            CombatCampaignContextFeatureNames.Progress,
            0d));
        var finalBoss = StateFeature(
                            state,
                            CombatCampaignContextFeatureNames.FinalBoss,
                            0d) > 0.5d;
        var growthOpportunityAdventure = campaignContextKnown
                                         && !finalBoss
                                         && campaignProgress < 0.97d;
        var growthTargetDoom = GrowthTargetDoom(
            campaignContextKnown,
            campaignProgress,
            finalBoss);
        var growthGap = Math.Max(0, growthTargetDoom - doom);
        var safeToBank = state.ExpectedIncomingDamage
                         < state.Player.CurrentHp + state.Player.Defend;
        var survivalOverride = IsSurvivalOverride(state);
        var bleedPackage = state.DeckCardIds.Count(IsBleedingCard);

        var devours = actions
            .Where(action => action.Legal
                             && IdEquals(action.SourceId, "careercard_2"))
            .ToList();
        var devourAssessments = devours
            .Select(action => AssessDevour(
                state,
                actions,
                action,
                bleedPackage,
                growthOpportunityAdventure,
                safeToBank))
            .ToList();
        var remainingDevourOpportunities = devourAssessments.Count(item =>
            item.Gain > 0d && item.ConservativeTargetEligible);
        var bestDevour = devourAssessments
            .Where(item => item.Gain > 0d && item.ConservativeTargetEligible)
            .OrderByDescending(item => item.NetValue)
            .ThenByDescending(item => item.MaximumHpGain)
            .FirstOrDefault();
        var bestDevourGain = bestDevour?.Gain ?? 0d;
        var bestDevourCount = bestDevour?.StatusCount ?? 0d;
        var bestDevourNetValue = bestDevour?.NetValue ?? 0d;
        var reliableBuilderPriority = bestDevour?.ReliableSameTurnBuilder == true;
        var safeGrowthWindow = growthOpportunityAdventure
                               && safeToBank
                               && !survivalOverride
                               && devourAssessments.Any(item =>
                                   item.ReliableSameTurnBuilder
                                   || item.CrossTurnBuilder);

        var transform = actions.FirstOrDefault(action =>
            action.Legal && IdEquals(action.SourceId, "careercard_3"));
        var burst = BuildBurstPlan(
            state,
            actions,
            transform,
            doom);
        var nextTurnPower = Math.Max(
            state.MaxPower,
            (int)Math.Round(StateFeature(
                state,
                "nextTurnPowerOnEnd",
                state.MaxPower)));
        var nextTurnBurstActions = CountExecutableBurstActions(
            actions,
            nextTurnPower);
        var bankForNextTurn = transform != null
                              && !transformed
                              && nextTurnBurstActions > burst.ExecutableActions
                              && nextTurnPower > state.CurrentPower
                              && safeToBank
                              && !survivalOverride;
        var selfDevour = devours.FirstOrDefault(action =>
            action.TargetKind == CombatTargetKind.Self
            && action.TargetRuntimeId == state.Player.RuntimeId);
        var finale = actions.FirstOrDefault(action =>
            action.Legal && IdEquals(action.SourceId, FinaleCardId));
        var finaleSafe = FinaleSafe(
            state,
            finale,
            selfDevour,
            Math.Max(0, state.CurrentPower));
        var enemyBleeding = state.Enemies.Sum(enemy =>
            StatusLevel(enemy, BleedingStatusId));
        var phase = ResolvePhase(
            transformed,
            survivalOverride,
            reliableBuilderPriority,
            bestDevour,
            transform,
            doom);

        var nightmareAssessments = actions.ToDictionary(
            action => action,
            action => AssessNightmare(state, action, nightmareActive));
        var bestNightmare = nightmareAssessments.Values
            .OrderByDescending(item => item.ExpectedDevourThresholdGain)
            .ThenByDescending(item => item.ExpectedExtraStacks)
            .FirstOrDefault() ?? new NightmareAssessment();

        state.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
        state.Features[CombatRoleStrategyFeatureNames.Phase] = (double)phase;
        state.Features["roleStrategy:nana.doom"] = doom;
        state.Features["roleStrategy:nana.next-doom-stack-max-hp-gain"] =
            AuraToolsNanaDoomProgression.MaximumHpGainAfterAdd(doom, 1);
        state.Features["roleStrategy:nana.campaign-context-known"] =
            campaignContextKnown ? 1d : 0d;
        state.Features["roleStrategy:nana.campaign-progress"] = campaignProgress;
        state.Features["roleStrategy:nana.growth-target-doom"] = growthTargetDoom;
        state.Features["roleStrategy:nana.growth-gap"] = growthGap;
        state.Features["roleStrategy:nana.safe-growth-window"] =
            safeGrowthWindow ? 1d : 0d;
        state.Features["roleStrategy:nana.best-devour-gain"] = bestDevourGain;
        state.Features["roleStrategy:nana.best-devour-status-count"] =
            bestDevourCount;
        state.Features["roleStrategy:nana.best-devour-net-value"] =
            bestDevourNetValue;
        state.Features["roleStrategy:nana.survival-override"] =
            survivalOverride ? 1d : 0d;
        state.Features["roleStrategy:nana.transformed"] = transformed ? 1d : 0d;
        state.Features["roleStrategy:nana.burst-actions-now"] =
            burst.ExecutableActions;
        state.Features["roleStrategy:nana.next-turn-power"] = nextTurnPower;
        state.Features["roleStrategy:nana.bank-for-next-turn"] =
            bankForNextTurn ? 1d : 0d;
        state.Features["roleStrategy:nana.pig-score"] =
            Math.Max(0d, bestDevourNetValue) + bestDevourCount * 0.5d;
        state.Features["roleStrategy:nana.bleeding-package"] = bleedPackage;
        state.Features["roleStrategy:nana.enemy-bleeding"] = enemyBleeding;
        state.Features["roleStrategy:nana.finale-safe"] = finaleSafe ? 1d : 0d;
        state.Features["nana:remaining-devour-opportunities"] =
            remainingDevourOpportunities;
        state.Features["nana:post-transform-max-hp"] = burst.PostTransformMaxHp;
        state.Features["nana:post-transform-damage-per-action"] =
            burst.PassiveDamagePerAction;
        state.Features["nana:executable-burst-actions"] = burst.ExecutableActions;
        state.Features["nana:next-transform-damage-threshold-max-hp"] =
            burst.NextPreTransformThreshold;
        state.Features["nana:transform-threshold-distance"] =
            burst.ThresholdDistance;
        state.Features["nightmare:active"] = nightmareActive ? 1d : 0d;
        state.Features["nightmare:eligible-negative-events"] =
            bestNightmare.EligibleEvents;
        state.Features["nightmare:expected-extra-stacks"] =
            bestNightmare.ExpectedExtraStacks;
        state.Features["nightmare:expected-devour-threshold-gain"] =
            bestNightmare.ExpectedDevourThresholdGain;

        foreach (var action in actions)
        {
            action.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
            action.Features[CombatRoleStrategyFeatureNames.Phase] = (double)phase;
            action.Features["roleStrategy:nana.doom"] = doom;
            action.Features["roleStrategy:nana.survival-override"] =
                survivalOverride ? 1d : 0d;
            action.Features["nana:remaining-devour-opportunities"] =
                remainingDevourOpportunities;
            EnrichNightmareAction(action, nightmareAssessments[action]);

            var devour = devourAssessments.FirstOrDefault(item =>
                ReferenceEquals(item.Action, action));
            if (devour != null)
            {
                EnrichDevour(
                    action,
                    devour,
                    ReferenceEquals(devour, bestDevour),
                    remainingDevourOpportunities,
                    survivalOverride);
            }
            else if (IdEquals(action.SourceId, "careercard_3"))
            {
                EnrichTransform(
                    state,
                    action,
                    burst,
                    bestDevour,
                    bankForNextTurn,
                    transformed,
                    survivalOverride);
            }
            else if (IdEquals(action.SourceId, FinaleCardId))
            {
                EnrichFinale(action, finaleSafe);
            }
            else if (IsBleedingCard(action.SourceId))
            {
                EnrichBleedingAction(
                    action,
                    bleedPackage,
                    enemyBleeding,
                    transformed);
            }

            if (IsDebuffBuilder(action)
                && BuilderPreservesADevourTarget(state, devours, action)
                && (reliableBuilderPriority
                    || growthOpportunityAdventure && safeToBank))
            {
                EnrichGrowthBuilder(
                    action,
                    doom,
                    nightmareAssessments[action],
                    reliableBuilderPriority);
            }
            if (transformed
                && action.Kind != CombatActionKind.EndTurn
                && !IdEquals(action.SourceId, "careercard_3"))
            {
                EnrichCalamityAction(
                    action,
                    Math.Max(0, state.Player.MaxHp / 50),
                    Math.Max(1, state.Enemies.Count(enemy => enemy.Alive)));
            }
            if (survivalOverride)
            {
                EnrichSurvivalAction(state, action);
            }
            if (action.Kind == CombatActionKind.EndTurn && bankForNextTurn)
            {
                SetMax(action, CombatRoleStrategyFeatureNames.Continuation, 4d);
                SetMax(action, CombatRoleStrategyFeatureNames.Coordination, 2d);
                action.Features["roleStrategy:nana.intent-bank"] = 1d;
            }
        }
        return true;
    }

    private static NanaPhase ResolvePhase(
        bool transformed,
        bool survivalOverride,
        bool reliableBuilderPriority,
        DevourAssessment? bestDevour,
        CombatActionObservation? transform,
        int doom)
    {
        if (survivalOverride)
        {
            return NanaPhase.SurvivalOverride;
        }
        if (transformed)
        {
            return NanaPhase.CalamityBurst;
        }
        if (reliableBuilderPriority)
        {
            return NanaPhase.Build;
        }
        if (bestDevour?.NetValue >= PreferredDevourNetValue)
        {
            return NanaPhase.Harvest;
        }
        return transform != null && doom > 0
            ? NanaPhase.PrepareBurst
            : NanaPhase.Build;
    }

    private static DevourAssessment AssessDevour(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> actions,
        CombatActionObservation action,
        int bleedPackage,
        bool growthOpportunityAdventure,
        bool safeToGrow)
    {
        var target = FindTarget(state, action);
        var negativeStatuses = NegativeStatuses(target).ToList();
        var gain = Feature(action, "nana:projected-doom-gain");
        var maximumHpGain = Feature(action, "nana:projected-max-hp-gain");
        var targetWillDie = TargetWillDieThisTurn(state, actions, action, target);
        var enemyTarget = action.TargetKind == CombatTargetKind.Enemy;
        var friendlyTarget = action.TargetKind == CombatTargetKind.Friendly;
        var selfTarget = action.TargetKind == CombatTargetKind.Self;
        var statusCount = negativeStatuses.Count;
        var negativeStacks = negativeStatuses.Sum(status => Math.Max(0, status.Level));
        var enemyFutureValue = enemyTarget
            ? EnemyNegativeFutureValue(negativeStatuses, bleedPackage)
              * (targetWillDie ? 0.10d : 1d)
            : 0d;
        var cleanseValue = selfTarget || friendlyTarget
            ? Math.Min(12d, negativeStacks * 0.65d + statusCount)
            : 0d;
        var immediateValue = enemyTarget ? 5d : 0d;
        var friendlyDamageCost = friendlyTarget ? 7.5d : 0d;
        var cooldownCost = Math.Max(0d, action.Semantics.CooldownTurns) * 0.30d;
        var netValue = immediateValue
                       + gain * 0.50d
                       + PersistentGrowthUtility(maximumHpGain)
                       + cleanseValue
                       - enemyFutureValue
                       - friendlyDamageCost
                       - cooldownCost;
        var reliableSameTurnBuilder = HasPlayableDebuffBuilder(
            state,
            actions,
            action,
            sameTurnOnly: true,
            reliableOnly: true);
        var randomSameTurnBuilder = !reliableSameTurnBuilder
                                    && HasPlayableDebuffBuilder(
                                        state,
                                        actions,
                                        action,
                                        sameTurnOnly: true,
                                        reliableOnly: false);
        var crossTurnBuilder = !reliableSameTurnBuilder
                               && growthOpportunityAdventure
                               && safeToGrow
                               && HasPlayableDebuffBuilder(
                                   state,
                                   actions,
                                   action,
                                   sameTurnOnly: false,
                                   reliableOnly: true);
        var conservativeTargetEligible = !enemyTarget
                                         || statusCount >= 2
                                         || targetWillDie;
        return new DevourAssessment
        {
            Action = action,
            Gain = gain,
            MaximumHpGain = maximumHpGain,
            StatusCount = statusCount,
            NegativeStacks = negativeStacks,
            NetValue = netValue,
            EnemyFutureValue = enemyFutureValue,
            CleanseValue = cleanseValue,
            TargetWillDie = targetWillDie,
            ConservativeTargetEligible = conservativeTargetEligible,
            ReliableSameTurnBuilder = reliableSameTurnBuilder,
            RandomSameTurnBuilder = randomSameTurnBuilder,
            CrossTurnBuilder = crossTurnBuilder,
            FriendlyLethal = friendlyTarget
                             && target != null
                             && target.CurrentHp <= 5
        };
    }

    private static void EnrichDevour(
        CombatActionObservation action,
        DevourAssessment assessment,
        bool bestHarvest,
        int remainingDevourOpportunities,
        bool survivalOverride)
    {
        var preferred = bestHarvest
                        && assessment.NetValue >= PreferredDevourNetValue
                        && !assessment.ReliableSameTurnBuilder;
        action.Features["roleStrategy:nana.harvest"] =
            assessment.Gain > 0d ? 1d : 0d;
        action.Features["roleStrategy:nana.harvest-value"] =
            assessment.NetValue;
        action.Features["roleStrategy:nana.preferred-harvest"] =
            preferred ? 1d : 0d;
        action.Features["roleStrategy:nana.defer-harvest-same-turn"] =
            assessment.ReliableSameTurnBuilder ? 1d : 0d;
        action.Features["roleStrategy:nana.defer-harvest-random-builder"] =
            assessment.RandomSameTurnBuilder ? 1d : 0d;
        action.Features["roleStrategy:nana.defer-harvest-cross-turn"] =
            assessment.CrossTurnBuilder ? 1d : 0d;
        action.Features["nana:devour-net-value"] = assessment.NetValue;
        action.Features["nana:devour-event-max-hp-gain"] =
            assessment.MaximumHpGain;
        action.Features["nana:remaining-devour-opportunities"] =
            remainingDevourOpportunities;
        action.Features["nana:enemy-negative-future-value"] =
            assessment.EnemyFutureValue;
        action.Features["nana:target-will-die-this-turn"] =
            assessment.TargetWillDie ? 1d : 0d;
        action.Features["nana:conservative-devour-target"] =
            assessment.ConservativeTargetEligible ? 1d : 0d;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Scaling,
            PersistentGrowthUtility(assessment.MaximumHpGain)
            + assessment.Gain * 0.35d
            + assessment.StatusCount * 0.25d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            preferred ? 3d : assessment.Gain > 0d ? 1d : 0d);
        if (preferred)
        {
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Synergy,
                3d + Math.Min(6d, assessment.NetValue * 0.5d));
        }
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Risk,
            Math.Max(0d, assessment.EnemyFutureValue * 0.4d)
            + (assessment.RandomSameTurnBuilder ? 2d : 0d)
            + (assessment.CrossTurnBuilder ? 1.5d : 0d));

        if (assessment.ReliableSameTurnBuilder && !survivalOverride)
        {
            Prohibit(action, 10d, -8d);
        }
        else if (!assessment.ConservativeTargetEligible
                 || assessment.FriendlyLethal
                 || assessment.Gain <= 0d
                 || assessment.NetValue <= 0d
                    && !(survivalOverride && assessment.CleanseValue > 0d))
        {
            Prohibit(action, 8d, -5d);
        }
        else if (assessment.CrossTurnBuilder)
        {
            SetMin(action, CombatRoleStrategyFeatureNames.Synergy, -1.5d);
        }
    }

    private static BurstPlan BuildBurstPlan(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> actions,
        CombatActionObservation? transform,
        int doom)
    {
        var alreadyTransformed = string.Equals(
                                     state.Player.DefinitionId,
                                     "career_4",
                                     StringComparison.OrdinalIgnoreCase)
                                 || StatusLevel(state.Player, CalamityStatusId) > 0;
        var postTransformMaxHp = Math.Max(1, state.Player.MaxHp);
        var passiveDamage = Math.Max(0, postTransformMaxHp / 50);
        var executableActions = CountExecutableBurstActions(
            actions,
            Math.Max(0, state.CurrentPower));
        var enemyCount = Math.Max(1, state.Enemies.Count(enemy => enemy.Alive));
        var totalPassiveDamage = passiveDamage * executableActions * enemyCount;
        var snapshotStatValue = doom * Math.Min(3, executableActions) * 0.20d;
        var hpClampLoss = transform == null
            ? 0d
            : Feature(transform, "nana:transform-hp-clamp-loss");
        var burstValue = totalPassiveDamage + snapshotStatValue - hpClampLoss * 2d;
        var nextPostThreshold = (passiveDamage + 1) * 50;
        var nextPreTransformThreshold = nextPostThreshold;
        return new BurstPlan
        {
            PostTransformMaxHp = postTransformMaxHp,
            PassiveDamagePerAction = passiveDamage,
            ExecutableActions = executableActions,
            TotalPassiveDamage = totalPassiveDamage,
            SnapshotStatValue = snapshotStatValue,
            HpClampLoss = hpClampLoss,
            BurstValue = burstValue,
            NextPreTransformThreshold = nextPreTransformThreshold,
            ThresholdDistance = Math.Max(
                0,
                nextPreTransformThreshold - state.Player.MaxHp)
        };
    }

    private static void EnrichTransform(
        CombatStateObservation state,
        CombatActionObservation action,
        BurstPlan burst,
        DevourAssessment? bestDevour,
        bool bankForNextTurn,
        bool transformed,
        bool survivalOverride)
    {
        action.Features["roleStrategy:nana.burst-actions"] =
            burst.ExecutableActions;
        action.Features["roleStrategy:nana.early-transform"] =
            !transformed && bestDevour?.Gain > 0d ? 1d : 0d;
        action.Features["roleStrategy:nana.bank-transform"] =
            bankForNextTurn ? 1d : 0d;
        action.Features["nana:post-transform-max-hp"] = burst.PostTransformMaxHp;
        action.Features["nana:post-transform-damage-per-action"] =
            burst.PassiveDamagePerAction;
        action.Features["nana:executable-burst-actions"] =
            burst.ExecutableActions;
        action.Features["nana:transform-total-passive-damage"] =
            burst.TotalPassiveDamage;
        action.Features["nana:transform-snapshot-stat-value"] =
            burst.SnapshotStatValue;
        action.Features["nana:transform-burst-value"] = burst.BurstValue;
        action.Features["nana:next-transform-damage-threshold-max-hp"] =
            burst.NextPreTransformThreshold;
        action.Features["nana:transform-threshold-distance"] =
            burst.ThresholdDistance;

        action.Features[CombatSkillTimingFeatureNames.Active] = 1d;
        action.Features[CombatSkillTimingFeatureNames.ResetsEachBattle] = 1d;
        if (Feature(
                action,
                CombatSkillTimingFeatureNames.CooldownAfterUse) <= 0d)
        {
            action.Features[CombatSkillTimingFeatureNames.CooldownAfterUse] = 2d;
        }

        if (transformed || Feature(action, "nana:repeat-transform") > 0.5d)
        {
            action.Features[CombatSkillTimingFeatureNames.RedundancyCost] = 40d;
            CombatSkillTimingPolicy.Enrich(action);
            action.Features["roleStrategy:nana.transform-ready"] = 0d;
            Prohibit(action, 999d, -20d);
            return;
        }

        var ongoingValue = Math.Max(0d, burst.BurstValue);
        var devourDelayGain = bestDevour?.Gain > 0d
                              && bestDevour.NetValue > 0d
            ? Math.Min(
                10d,
                bestDevour.Gain * 0.45d
                + PersistentGrowthUtility(bestDevour.MaximumHpGain) * 0.30d
                + (bestDevour.ReliableSameTurnBuilder ? 1d : 0d))
            : 0d;
        var nextTurnDelayGain = bankForNextTurn
            ? Math.Min(
                6d,
                Math.Max(1d, burst.PassiveDamagePerAction)
                * Math.Max(1d, StateFeature(
                    state,
                    "nextTurnPowerOnEnd",
                    state.MaxPower) - state.CurrentPower))
            : 0d;
        var turn = Math.Max(1d, StateFeature(state, "turn", 1d));
        var expiryRisk = burst.ExecutableActions <= 0
            ? 0d
            : Math.Min(
                5d,
                0.5d
                + Math.Max(0d, turn - 1d) * 0.35d
                + (survivalOverride ? 1.5d : 0d));
        action.Features[CombatSkillTimingFeatureNames.OngoingEffectValue] =
            ongoingValue;
        action.Features[CombatSkillTimingFeatureNames.DelayGain] =
            Math.Max(devourDelayGain, nextTurnDelayGain);
        action.Features[CombatSkillTimingFeatureNames.ReserveValue] = 0d;
        action.Features[CombatSkillTimingFeatureNames.ExpiryRisk] = expiryRisk;
        action.Features[CombatSkillTimingFeatureNames.OpportunityCost] =
            Math.Max(0d, burst.HpClampLoss * 2d);

        var timing = CombatSkillTimingPolicy.Enrich(action);
        action.Features["roleStrategy:nana.transform-ready"] =
            timing.PositiveOpportunity ? 1d : 0d;
        if (timing.PositiveOpportunity)
        {
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Continuation,
                Math.Min(6d, burst.ExecutableActions));
        }
    }

    private static NightmareAssessment AssessNightmare(
        CombatStateObservation state,
        CombatActionObservation action,
        bool active)
    {
        var result = new NightmareAssessment { Active = active };
        if (!active
            || !action.Legal
            || action.Kind == CombatActionKind.EndTurn
            || action.TargetRuntimeId == state.Player.RuntimeId)
        {
            return result;
        }

        var applications = CollectNegativeApplications(state, action);
        result.EligibleEvents = applications.Sum(item => item.Probability);
        result.ExpectedExtraStacks = result.EligibleEvents
                                     * NightmareDuplicateProbability;
        result.ExpectedDevourThresholdGain = applications.Sum(item =>
            item.Probability
            * NightmareDuplicateProbability
            * DoomContributionDelta(item));
        return result;
    }

    private static List<NegativeApplication> CollectNegativeApplications(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        var result = new List<NegativeApplication>();
        var representedStatuses = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var effect in action.Semantics.TargetEffects.Where(effect =>
                     effect.Kind == CombatSemanticEffectKind.AddStatus
                     && effect.Probability > 0d))
        {
            var target = FindUnit(state, effect.TargetRuntimeId)
                         ?? FindTarget(state, action);
            if (!IsEligibleNegativeApplication(
                    state,
                    action,
                    target,
                    effect.DefinitionId))
            {
                continue;
            }
            var amount = Math.Max(effect.RawAmount, effect.EffectiveAmount);
            result.Add(CreateNegativeApplication(
                target,
                effect.DefinitionId,
                amount,
                effect.Probability));
            representedStatuses.Add(
                (target?.RuntimeId ?? effect.TargetRuntimeId)
                + "|"
                + effect.DefinitionId);
        }

        foreach (var change in action.Semantics.StateChanges.Where(item =>
                     item.Value > 0d
                     && (item.Key.StartsWith(
                             "targetStatus:",
                             StringComparison.OrdinalIgnoreCase)
                         || item.Key.StartsWith(
                             "enemyStatus:",
                             StringComparison.OrdinalIgnoreCase))))
        {
            var statusId = change.Key.Substring(change.Key.IndexOf(':') + 1);
            IEnumerable<CombatUnitObservation?> targets =
                change.Key.StartsWith(
                    "enemyStatus:",
                    StringComparison.OrdinalIgnoreCase)
                && action.Semantics.AffectedEnemyCount > 1
                    ? state.Enemies
                        .Where(enemy => enemy.Alive)
                        .Cast<CombatUnitObservation?>()
                    : new[] { FindTarget(state, action) };
            foreach (var target in targets)
            {
                var signature = (target?.RuntimeId ?? action.TargetRuntimeId)
                                + "|"
                                + statusId;
                if (representedStatuses.Contains(signature)
                    || !IsEligibleNegativeApplication(
                        state,
                        action,
                        target,
                        statusId))
                {
                    continue;
                }
                result.Add(CreateNegativeApplication(
                    target,
                    statusId,
                    change.Value,
                    1d));
                representedStatuses.Add(signature);
            }
        }

        if (result.Count == 0
            && (action.Semantics.Debuff > 0d
                || action.Semantics.DamageOverTime > 0d)
            && action.TargetKind != CombatTargetKind.Self)
        {
            result.Add(CreateNegativeApplication(
                FindTarget(state, action),
                "",
                Math.Max(1d, Math.Max(
                    action.Semantics.Debuff,
                    action.Semantics.DamageOverTime)),
                action.Semantics.RandomOutcome
                    ? ClampUnit(1d - action.Semantics.Uncertainty)
                    : 1d));
        }
        return result;
    }

    private static bool IsEligibleNegativeApplication(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatUnitObservation? target,
        string statusId)
    {
        if (target?.RuntimeId == state.Player.RuntimeId)
        {
            return false;
        }
        var knownStatus = target?.Statuses.FirstOrDefault(status =>
            IdEquals(status.StatusId, statusId));
        return knownStatus != null
               && IsNegativeStatus(knownStatus)
               || action.TargetKind != CombatTargetKind.Self
               && (action.Semantics.Debuff > 0d
                   || action.Semantics.DamageOverTime > 0d);
    }

    private static NegativeApplication CreateNegativeApplication(
        CombatUnitObservation? target,
        string statusId,
        double amount,
        double probability)
    {
        var status = target?.Statuses.FirstOrDefault(item =>
            IdEquals(item.StatusId, statusId));
        return new NegativeApplication
        {
            CurrentLevel = Math.Max(0, status?.Level ?? 0),
            AddedLevels = Math.Max(1, (int)Math.Ceiling(Math.Max(0d, amount))),
            Rarity = Math.Max(1, status?.Rarity ?? 1),
            Probability = ClampUnit(probability)
        };
    }

    private static double DoomContributionDelta(NegativeApplication item)
    {
        var afterBase = item.CurrentLevel + item.AddedLevels;
        return Math.Max(
            0,
            DoomContribution(afterBase + 1, item.Rarity)
            - DoomContribution(afterBase, item.Rarity));
    }

    private static int DoomContribution(int level, int rarity)
    {
        if (level <= 0)
        {
            return 0;
        }
        return Math.Min(
            Math.Max(1, Math.Max(1, rarity) * level / 5),
            10);
    }

    private static void EnrichNightmareAction(
        CombatActionObservation action,
        NightmareAssessment nightmare)
    {
        action.Features["nightmare:active"] = nightmare.Active ? 1d : 0d;
        action.Features["nightmare:eligible-negative-events"] =
            nightmare.EligibleEvents;
        action.Features["nightmare:expected-extra-stacks"] =
            nightmare.ExpectedExtraStacks;
        action.Features["nightmare:expected-devour-threshold-gain"] =
            nightmare.ExpectedDevourThresholdGain;
        if (!nightmare.Active || nightmare.EligibleEvents <= 0d)
        {
            return;
        }
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            Math.Min(
                4d,
                nightmare.ExpectedExtraStacks * 1.5d
                + nightmare.ExpectedDevourThresholdGain * 3d));
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Scaling,
            nightmare.ExpectedDevourThresholdGain * 2d);
    }

    private static void EnrichFinale(
        CombatActionObservation action,
        bool safe)
    {
        action.Features["roleStrategy:nana.finale-line"] = 1d;
        action.Features["roleStrategy:nana.finale-cleanse-ready"] = safe ? 1d : 0d;
        if (!safe)
        {
            SetMax(action, CombatRoleStrategyFeatureNames.Risk, 999d);
            return;
        }
        action.Features[CombatRoleStrategyFeatureNames.SafeContinuationCertified] =
            1d;
        SetMax(action, CombatRoleStrategyFeatureNames.Synergy, 7d);
        SetMax(action, CombatRoleStrategyFeatureNames.Continuation, 5d);
        SetMax(action, CombatRoleStrategyFeatureNames.Scaling, 11d);
    }

    private static void EnrichBleedingAction(
        CombatActionObservation action,
        int bleedPackage,
        int enemyBleeding,
        bool transformed)
    {
        action.Features["roleStrategy:nana.bleeding-line"] = 1d;
        var commitment = Math.Min(5d, bleedPackage * 0.5d);
        var engineValue = Math.Min(6d, enemyBleeding * 0.08d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            commitment + engineValue + (transformed && action.Cost == 0 ? 2d : 0d));
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            action.Cost == 0 ? 1.5d : 0.5d);
    }

    private static void EnrichCalamityAction(
        CombatActionObservation action,
        int passiveDamage,
        int enemyCount)
    {
        var density = action.Cost <= 0 ? 1.5d : action.Cost == 1 ? 1.15d : 0.75d;
        var continuation = Math.Max(
            0d,
            action.Semantics.Draw
            + action.Semantics.EnergyGain
            + action.Semantics.CardGeneration * 0.5d);
        var totalPassiveDamage = passiveDamage * Math.Max(1, enemyCount);
        action.Features["roleStrategy:nana.calamity-action"] = 1d;
        action.Features["roleStrategy:nana.calamity-passive-damage"] =
            totalPassiveDamage;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            Math.Min(12d, totalPassiveDamage * density + continuation));
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            continuation + (action.Cost <= 1 ? 1d : 0d));
    }

    private static void EnrichGrowthBuilder(
        CombatActionObservation action,
        int doom,
        NightmareAssessment nightmare,
        bool priority)
    {
        var nextLayerMaximumHp = Math.Max(1, doom + 1);
        action.Features["roleStrategy:nana.growth-builder"] = 1d;
        action.Features["roleStrategy:nana.priority-builder"] = priority ? 1d : 0d;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            Math.Min(
                8d,
                2d
                + nextLayerMaximumHp * 0.10d
                + nightmare.ExpectedDevourThresholdGain * 3d));
        SetMax(action, CombatRoleStrategyFeatureNames.Continuation, 2.5d);
        SetMax(action, CombatRoleStrategyFeatureNames.Coordination, priority ? 5d : 3d);
    }

    private static void EnrichSurvivalAction(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (action.Kind == CombatActionKind.EndTurn)
        {
            SetMax(action, CombatRoleStrategyFeatureNames.Risk, 20d);
            return;
        }
        var defend = Feature(action, "effectiveDefend", action.Semantics.Defend);
        var heal = Feature(action, "effectiveHeal", action.Semantics.Heal);
        var selfCleanse = action.TargetKind != CombatTargetKind.Enemy
                          ? Math.Max(0d, action.Semantics.Cleanse)
                          : 0d;
        var survivalValue = Math.Max(0d, defend)
                            + Math.Max(0d, heal) * 1.25d
                            + selfCleanse * 0.75d;
        if (survivalValue <= 0d)
        {
            return;
        }
        action.Features["roleStrategy:nana.survival-action"] = 1d;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            6d + Math.Min(10d, survivalValue * 0.5d));
        SetMax(action, CombatRoleStrategyFeatureNames.Continuation, 3d);
        if (survivalValue >= Math.Max(1d, state.ExpectedIncomingDamage))
        {
            action.Features[
                CombatRoleStrategyFeatureNames.SafeContinuationCertified] = 1d;
        }
    }

    private static bool FinaleSafe(
        CombatStateObservation state,
        CombatActionObservation? finale,
        CombatActionObservation? selfDevour,
        int currentPower)
    {
        if (finale == null
            || selfDevour == null
            || !selfDevour.Legal
            || StatusLevel(state.Player, ToxinStatusId) > 0)
        {
            return false;
        }
        var remainingPower = currentPower - Math.Max(0, finale.Cost);
        return remainingPower >= Math.Max(0, selfDevour.Cost)
               && !finale.Semantics.EndsTurn;
    }

    private static double EnemyNegativeFutureValue(
        IEnumerable<CombatStatusObservation> statuses,
        int bleedPackage)
    {
        var value = 0d;
        foreach (var status in statuses)
        {
            var level = Math.Max(0, status.Level);
            var contribution = DoomContribution(level, status.Rarity);
            if (IdEquals(status.StatusId, BleedingStatusId))
            {
                var bleedDamage = level <= 30 ? level : level * 2d;
                value += bleedDamage * Math.Max(1d, bleedPackage * 0.25d) * 0.35d;
            }
            value += Math.Min(12d, level * 0.30d + contribution * 0.75d);
        }
        return value;
    }

    private static bool TargetWillDieThisTurn(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> actions,
        CombatActionObservation devour,
        CombatUnitObservation? target)
    {
        if (target == null)
        {
            return false;
        }
        if (target.CurrentHp <= 5)
        {
            return true;
        }
        return actions.Any(action =>
            !ReferenceEquals(action, devour)
            && action.Legal
            && action.Kind != CombatActionKind.EndTurn
            && action.Cost <= state.CurrentPower
            && (action.TargetRuntimeId == target.RuntimeId
                || action.Semantics.AffectedEnemyCount > 1)
            && CombatActionSemanticMetrics.ImmediateHpDamage(action.Semantics)
               >= target.CurrentHp);
    }

    private static int GrowthTargetDoom(
        bool contextKnown,
        double progress,
        bool finalBoss)
    {
        if (!contextKnown || finalBoss)
        {
            return 0;
        }
        if (progress < 0.35d)
        {
            return 15;
        }
        return progress < 0.72d ? 30 : 43;
    }

    private static double PersistentGrowthUtility(double maximumHpGain)
    {
        return maximumHpGain <= 0d
            ? 0d
            : Math.Min(10d, Math.Log(maximumHpGain + 1d, 2d));
    }

    private static bool HasPlayableDebuffBuilder(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> actions,
        CombatActionObservation devour,
        bool sameTurnOnly,
        bool reliableOnly)
    {
        var target = FindTarget(state, devour);
        if (target == null || target.CurrentHp <= 6)
        {
            return false;
        }
        return actions.Any(candidate =>
        {
            if (!IsDebuffBuilder(candidate)
                || candidate.TargetKind != devour.TargetKind
                || candidate.TargetRuntimeId != devour.TargetRuntimeId
                || candidate.Semantics.EndsTurn
                || reliableOnly && !IsReliableBuilder(candidate))
            {
                return false;
            }
            var immediateDamage =
                CombatActionSemanticMetrics.ImmediateHpDamage(candidate.Semantics);
            if (immediateDamage + 5d >= target.CurrentHp)
            {
                return false;
            }
            return !sameTurnOnly
                   || candidate.Cost + Math.Max(0, devour.Cost)
                   <= state.CurrentPower;
        });
    }

    private static bool IsReliableBuilder(CombatActionObservation action)
    {
        return !action.Semantics.RandomOutcome
               && action.Semantics.Uncertainty <= 0.25d;
    }

    private static bool BuilderPreservesADevourTarget(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> devours,
        CombatActionObservation builder)
    {
        return devours.Any(devour =>
        {
            if (devour.TargetKind != builder.TargetKind
                || devour.TargetRuntimeId != builder.TargetRuntimeId)
            {
                return false;
            }
            var target = FindTarget(state, devour);
            return target != null
                   && CombatActionSemanticMetrics.ImmediateHpDamage(
                       builder.Semantics) + 5d < target.CurrentHp;
        });
    }

    private static bool IsDebuffBuilder(CombatActionObservation action)
    {
        return action.Legal
               && action.Kind != CombatActionKind.EndTurn
               && action.TargetKind != CombatTargetKind.Self
               && (action.Semantics.Debuff > 0d
                   || action.Semantics.DamageOverTime > 0d
                   || action.Semantics.TargetEffects.Any(effect =>
                       effect.Kind == CombatSemanticEffectKind.AddStatus)
                   || action.Semantics.StateChanges.Keys.Any(key =>
                       key.StartsWith(
                           "targetStatus:",
                           StringComparison.OrdinalIgnoreCase)
                       || key.StartsWith(
                           "enemyStatus:",
                           StringComparison.OrdinalIgnoreCase)));
    }

    private static int CountExecutableBurstActions(
        IEnumerable<CombatActionObservation> actions,
        int availablePower)
    {
        var costs = actions
            .Where(action => action.Legal
                             && action.Kind != CombatActionKind.EndTurn
                             && !IdEquals(action.SourceId, "careercard_3")
                             && Feature(action, "visibleFake") <= 0.5d)
            .GroupBy(action => action.RuntimeId > 0
                ? "runtime:" + action.RuntimeId
                : action.Kind == CombatActionKind.UseSkill
                    ? "skill:" + action.SourceId
                    : "candidate:" + action.CandidateId)
            .Select(group => group.Min(action => Math.Max(0, action.Cost)))
            .OrderBy(cost => cost)
            .ToList();
        var remaining = Math.Max(0, availablePower);
        var count = 0;
        foreach (var cost in costs)
        {
            if (cost > remaining)
            {
                continue;
            }
            remaining -= cost;
            count++;
        }
        return count;
    }

    private static bool IsSurvivalOverride(CombatStateObservation state)
    {
        var effectiveHp = Math.Max(0, state.Player.CurrentHp)
                          + Math.Max(0, state.Player.Defend);
        var hpRatio = state.Player.MaxHp <= 0
            ? 0d
            : (double)state.Player.CurrentHp / state.Player.MaxHp;
        return state.ExpectedIncomingDamage >= effectiveHp
               || hpRatio <= 0.25d
               && state.ExpectedIncomingDamage > state.Player.Defend;
    }

    private static bool IsNightmareActive(CombatStateObservation state)
    {
        return StateFeature(state, "blessing:" + NightmareBlessingId, 0d) > 0.5d
               || StateFeature(
                   state,
                   "familiarBlessing:" + NightmareBlessingId,
                   0d) > 0.5d;
    }

    private static bool IsNana(CombatStateObservation state)
    {
        return IdEquals(state.Player?.DefinitionId, "career_2")
               || IdEquals(state.Player?.DefinitionId, "career_4")
               || StateFeature(state, "playerRole:career_2", 0d) > 0.5d
               || StateFeature(state, "playerRole:career_4", 0d) > 0.5d
               || StatusLevel(state.Player, CalamityStatusId) > 0
               || StateFeature(
                   state,
                   "playerStatus:" + CalamityStatusId,
                   0d) > 0.5d;
    }

    private static bool IsBleedingCard(string? cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId)
               && cardId!.StartsWith("blood_", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<CombatStatusObservation> NegativeStatuses(
        CombatUnitObservation? unit)
    {
        return unit?.Statuses?.Where(IsNegativeStatus)
               ?? Enumerable.Empty<CombatStatusObservation>();
    }

    private static bool IsNegativeStatus(CombatStatusObservation status)
    {
        return status != null
               && string.Equals(
                   status.Type,
                   "Negative",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static CombatUnitObservation? FindTarget(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        return FindUnit(state, action.TargetRuntimeId);
    }

    private static CombatUnitObservation? FindUnit(
        CombatStateObservation state,
        int runtimeId)
    {
        if (state.Player.RuntimeId == runtimeId)
        {
            return state.Player;
        }
        return state.Friendlies
            .Concat(state.Enemies)
            .FirstOrDefault(unit => unit.RuntimeId == runtimeId);
    }

    private static int StatusLevel(
        CombatUnitObservation? unit,
        string statusId)
    {
        return unit?.Statuses?
                   .Where(status => IdEquals(status.StatusId, statusId))
                   .Select(status => Math.Max(0, status.Level))
                   .DefaultIfEmpty(0)
                   .Max()
               ?? 0;
    }

    private static double Feature(
        CombatActionObservation action,
        string key,
        double fallback = 0d)
    {
        return action.Features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private static double StateFeature(
        CombatStateObservation state,
        string key,
        double fallback)
    {
        return state.Features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : fallback;
    }

    private static void Prohibit(
        CombatActionObservation action,
        double risk,
        double synergy)
    {
        action.Features[CombatRoleStrategyFeatureNames.StrategicallyProhibited] =
            1d;
        SetMax(action, CombatRoleStrategyFeatureNames.Risk, risk);
        SetMin(action, CombatRoleStrategyFeatureNames.Synergy, synergy);
    }

    private static void SetMax(
        CombatActionObservation action,
        string key,
        double value)
    {
        action.Features[key] = action.Features.TryGetValue(key, out var current)
            ? Math.Max(current, value)
            : value;
    }

    private static void SetMin(
        CombatActionObservation action,
        string key,
        double value)
    {
        action.Features[key] = action.Features.TryGetValue(key, out var current)
            ? Math.Min(current, value)
            : value;
    }

    private static double ClampUnit(double value)
    {
        return Math.Max(0d, Math.Min(1d, value));
    }

    private static bool IdEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DevourAssessment
    {
        public CombatActionObservation Action { get; set; } = new();

        public double Gain { get; set; }

        public double MaximumHpGain { get; set; }

        public int StatusCount { get; set; }

        public int NegativeStacks { get; set; }

        public double NetValue { get; set; }

        public double EnemyFutureValue { get; set; }

        public double CleanseValue { get; set; }

        public bool TargetWillDie { get; set; }

        public bool ConservativeTargetEligible { get; set; }

        public bool ReliableSameTurnBuilder { get; set; }

        public bool RandomSameTurnBuilder { get; set; }

        public bool CrossTurnBuilder { get; set; }

        public bool FriendlyLethal { get; set; }
    }

    private sealed class BurstPlan
    {
        public int PostTransformMaxHp { get; set; }

        public int PassiveDamagePerAction { get; set; }

        public int ExecutableActions { get; set; }

        public int TotalPassiveDamage { get; set; }

        public double SnapshotStatValue { get; set; }

        public double HpClampLoss { get; set; }

        public double BurstValue { get; set; }

        public int NextPreTransformThreshold { get; set; }

        public int ThresholdDistance { get; set; }
    }

    private sealed class NightmareAssessment
    {
        public bool Active { get; set; }

        public double EligibleEvents { get; set; }

        public double ExpectedExtraStacks { get; set; }

        public double ExpectedDevourThresholdGain { get; set; }
    }

    private sealed class NegativeApplication
    {
        public int CurrentLevel { get; set; }

        public int AddedLevels { get; set; }

        public int Rarity { get; set; }

        public double Probability { get; set; }
    }
}
