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
        var doomMaximumHpContribution =
            AuraToolsNanaDoomProgression.MaximumHpContribution(doom);
        var campaignContextKnown = StateFeature(
            state,
            CombatCampaignContextFeatureNames.ContextKnown,
            0d) > 0.5d;
        var campaignProgress = Math.Max(
            0d,
            Math.Min(
                1d,
                StateFeature(
                    state,
                    CombatCampaignContextFeatureNames.Progress,
                    0d)));
        var finalBoss = StateFeature(
                            state,
                            CombatCampaignContextFeatureNames.FinalBoss,
                            0d) > 0.5d;
        var growthTargetDoom = GrowthTargetDoom(
            campaignContextKnown,
            campaignProgress,
            finalBoss);
        var growthGap = Math.Max(0, growthTargetDoom - doom);
        var transformed = string.Equals(
                              state.Player.DefinitionId,
                              "career_4",
                              StringComparison.OrdinalIgnoreCase)
                          || StatusLevel(
                              state.Player,
                              "SpecialBuff_CalamityIncarnates") > 0;
        var devours = actions
            .Where(action => action.Legal
                             && IdEquals(action.SourceId, "careercard_2"))
            .ToList();
        var bleedPackage = state.DeckCardIds.Count(IsBleedingCard);
        var harvestableDevours = devours
            .Where(action => HarvestValue(state, action, bleedPackage) > 0d)
            .ToList();
        var bestHarvestValue = harvestableDevours.Count == 0
            ? 0d
            : harvestableDevours.Max(action => HarvestValue(
                state,
                action,
                bleedPackage));
        var bestDevourGain = harvestableDevours.Count == 0
            ? 0d
            : harvestableDevours.Max(action => Feature(
                action,
                "nana:projected-doom-gain"));
        var bestDevourCount = harvestableDevours.Count == 0
            ? 0d
            : harvestableDevours.Max(action => Feature(
                action,
                "nana:negative-status-count"));
        var selfDevour = devours.FirstOrDefault(action =>
            action.TargetKind == CombatTargetKind.Self
            && action.TargetRuntimeId == state.Player.RuntimeId);
        var transform = actions.FirstOrDefault(action =>
            action.Legal && IdEquals(action.SourceId, "careercard_3"));
        var finale = actions.FirstOrDefault(action =>
            action.Legal && IdEquals(action.SourceId, FinaleCardId));
        var currentPower = Math.Max(0, state.CurrentPower);
        var nextTurnPower = Math.Max(
            state.MaxPower,
            (int)Math.Round(StateFeature(
                state,
                "nextTurnPowerOnEnd",
                state.MaxPower)));
        var burstActions = CountBurstActions(actions, currentPower);
        var safeToBank = state.ExpectedIncomingDamage
                         < state.Player.CurrentHp + state.Player.Defend;
        var growthOpportunityAdventure = campaignContextKnown
                                         && !finalBoss
                                         && campaignProgress < 0.97d;
        var safeGrowthWindow = growthOpportunityAdventure
                               && safeToBank
                               && devours.Any(action =>
                                   HasPlayableDebuffBuilder(
                                       state,
                                       actions,
                                       action,
                                       sameTurnOnly: false));
        var burstReady = transform != null
                         && currentPower >= Math.Max(2, state.MaxPower - 1)
                         && burstActions >= 2;
        var bankForNextTurn = transform != null
                              && !burstReady
                              && nextTurnPower > currentPower
                              && safeToBank;
        var enemyBleeding = state.Enemies.Sum(enemy =>
            StatusLevel(enemy, BleedingStatusId));
        var pigScore = bestDevourGain
                       + bestDevourCount * 1.5d;
        var finaleSafe = FinaleSafe(
            state,
            finale,
            selfDevour,
            currentPower);
        var phase = transformed
            ? 3d
            : bestDevourGain > 0d
                ? 1d
                : transform != null && doom > 0
                    ? 2d
                    : 0d;

        state.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
        state.Features[CombatRoleStrategyFeatureNames.Phase] = phase;
        state.Features["roleStrategy:nana.doom"] = doom;
        state.Features["roleStrategy:nana.doom-max-hp-contribution"] =
            doomMaximumHpContribution;
        state.Features["roleStrategy:nana.campaign-context-known"] =
            campaignContextKnown ? 1d : 0d;
        state.Features["roleStrategy:nana.campaign-progress"] =
            campaignProgress;
        state.Features["roleStrategy:nana.growth-target-doom"] =
            growthTargetDoom;
        state.Features["roleStrategy:nana.growth-gap"] = growthGap;
        state.Features["roleStrategy:nana.safe-growth-window"] =
            safeGrowthWindow ? 1d : 0d;
        state.Features["roleStrategy:nana.best-devour-gain"] =
            bestDevourGain;
        state.Features["roleStrategy:nana.best-devour-status-count"] =
            bestDevourCount;
        state.Features["roleStrategy:nana.burst-actions-now"] = burstActions;
        state.Features["roleStrategy:nana.next-turn-power"] = nextTurnPower;
        state.Features["roleStrategy:nana.bank-for-next-turn"] =
            bankForNextTurn ? 1d : 0d;
        state.Features["roleStrategy:nana.pig-score"] = pigScore;
        state.Features["roleStrategy:nana.bleeding-package"] = bleedPackage;
        state.Features["roleStrategy:nana.enemy-bleeding"] = enemyBleeding;
        state.Features["roleStrategy:nana.finale-safe"] =
            finaleSafe ? 1d : 0d;
        state.Features["roleStrategy:nana.transformed"] =
            transformed ? 1d : 0d;

        var calamityDamage = Math.Max(0, state.Player.MaxHp / 50);
        foreach (var action in actions)
        {
            action.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
            action.Features[CombatRoleStrategyFeatureNames.Phase] = phase;
            action.Features["roleStrategy:nana.doom"] = doom;
            action.Features["roleStrategy:nana.pig-score"] = pigScore;
            action.Features["roleStrategy:nana.bleeding-package"] =
                bleedPackage;

            if (IdEquals(action.SourceId, "careercard_2"))
            {
                EnrichDevour(
                    state,
                    action,
                    actions,
                    bestHarvestValue,
                    bleedPackage,
                    growthOpportunityAdventure,
                    safeToBank);
            }
            else if (IdEquals(action.SourceId, "careercard_3"))
            {
                EnrichTransform(
                    action,
                    bestDevourGain,
                    burstActions,
                    burstReady,
                    bankForNextTurn,
                    transformed,
                    growthGap,
                    growthOpportunityAdventure,
                    safeToBank);
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
                && growthOpportunityAdventure
                && safeToBank
                && BuilderPreservesADevourTarget(state, devours, action))
            {
                EnrichGrowthBuilder(action, doom);
            }

            if (transformed
                && action.Kind != CombatActionKind.EndTurn
                && !IdEquals(action.SourceId, "careercard_3"))
            {
                EnrichCalamityAction(action, calamityDamage);
            }
            if (action.Kind == CombatActionKind.EndTurn && bankForNextTurn)
            {
                SetMax(
                    action,
                    CombatRoleStrategyFeatureNames.Continuation,
                    4d);
                SetMax(
                    action,
                    CombatRoleStrategyFeatureNames.Coordination,
                    2d);
                action.Features["roleStrategy:nana.intent-bank"] = 1d;
            }
        }
        return true;
    }

    private static void EnrichDevour(
        CombatStateObservation state,
        CombatActionObservation action,
        IReadOnlyList<CombatActionObservation> actions,
        double bestHarvestValue,
        int bleedPackage,
        bool growthOpportunityAdventure,
        bool safeToGrow)
    {
        var gain = Feature(action, "nana:projected-doom-gain");
        var projectedMaximumHpGain = Feature(
            action,
            "nana:projected-max-hp-gain");
        var statusCount = Feature(action, "nana:negative-status-count");
        var targetBleeding = StatusLevel(
            FindTarget(state, action),
            BleedingStatusId);
        var expectedBleedDamage = targetBleeding <= 30
            ? targetBleeding
            : targetBleeding * 2d;
        var enemyBleedOpportunity =
            action.TargetKind == CombatTargetKind.Enemy
                ? expectedBleedDamage * Math.Max(1d, bleedPackage * 0.25d)
                : 0d;
        var harvestValue = HarvestValue(state, action, bleedPackage);
        var sameTurnBuilder = HasPlayableDebuffBuilder(
            state,
            actions,
            action,
            sameTurnOnly: true);
        var crossTurnBuilder = !sameTurnBuilder
                               && growthOpportunityAdventure
                               && safeToGrow
                               && HasPlayableDebuffBuilder(
                                   state,
                                   actions,
                                   action,
                                   sameTurnOnly: false);
        var preferredHarvest = harvestValue > 0d
                               && harvestValue + 0.000001d
                               >= bestHarvestValue
                               && !sameTurnBuilder
                               && !crossTurnBuilder;
        action.Features["roleStrategy:nana.harvest"] = gain > 0d ? 1d : 0d;
        action.Features["roleStrategy:nana.harvest-value"] = harvestValue;
        action.Features["roleStrategy:nana.projected-max-hp-gain"] =
            projectedMaximumHpGain;
        action.Features["roleStrategy:nana.preferred-harvest"] =
            preferredHarvest ? 1d : 0d;
        action.Features["roleStrategy:nana.defer-harvest-same-turn"] =
            sameTurnBuilder ? 1d : 0d;
        action.Features["roleStrategy:nana.defer-harvest-cross-turn"] =
            crossTurnBuilder ? 1d : 0d;
        action.Features["roleStrategy:nana.bleed-opportunity-cost"] =
            enemyBleedOpportunity;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Scaling,
            PersistentGrowthUtility(projectedMaximumHpGain)
            + gain * 0.35d
            + statusCount * 0.35d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            preferredHarvest ? 3d : gain > 0d ? 1d : 0d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            preferredHarvest
                ? 3.5d + PersistentGrowthUtility(projectedMaximumHpGain)
                : gain * 0.10d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Risk,
            enemyBleedOpportunity * 0.35d
            + (gain <= 0d ? 4d : 0d)
            + (sameTurnBuilder ? 10d : crossTurnBuilder ? 3d : 0d));
        if (sameTurnBuilder)
        {
            action.Features[
                CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d;
            SetMin(action, CombatRoleStrategyFeatureNames.Synergy, -8d);
        }
        else if (crossTurnBuilder)
        {
            SetMin(action, CombatRoleStrategyFeatureNames.Synergy, -1.5d);
        }
    }

    private static void EnrichTransform(
        CombatActionObservation action,
        double bestDevourGain,
        int burstActions,
        bool burstReady,
        bool bankForNextTurn,
        bool transformed,
        int growthGap,
        bool growthOpportunityAdventure,
        bool safeToGrow)
    {
        action.Features["roleStrategy:nana.burst-actions"] = burstActions;
        action.Features["roleStrategy:nana.early-transform"] =
            !transformed && bestDevourGain > 0d ? 1d : 0d;
        action.Features["roleStrategy:nana.bank-transform"] =
            bankForNextTurn ? 1d : 0d;
        if (transformed)
        {
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                5d);
            return;
        }
        if (bestDevourGain > 0d)
        {
            action.Features[
                CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d;
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                6d + bestDevourGain * 0.75d);
            SetMin(
                action,
                CombatRoleStrategyFeatureNames.Synergy,
                -4d);
            return;
        }
        if (bankForNextTurn)
        {
            action.Features[
                CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d;
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                5d);
            SetMin(
                action,
                CombatRoleStrategyFeatureNames.Synergy,
                -2d);
            return;
        }
        if (growthGap > 0 && growthOpportunityAdventure && safeToGrow)
        {
            action.Features["roleStrategy:nana.transform-before-growth-target"] = 1d;
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                Math.Min(4d, 1d + growthGap * 0.15d));
            SetMin(
                action,
                CombatRoleStrategyFeatureNames.Synergy,
                -Math.Min(3d, growthGap * 0.10d));
        }
        if (burstReady)
        {
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Synergy,
                5d + Math.Min(5d, burstActions));
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Continuation,
                Math.Min(5d, burstActions));
        }
    }

    private static void EnrichFinale(
        CombatActionObservation action,
        bool safe)
    {
        action.Features["roleStrategy:nana.finale-line"] = 1d;
        action.Features["roleStrategy:nana.finale-cleanse-ready"] =
            safe ? 1d : 0d;
        if (!safe)
        {
            SetMax(
                action,
                CombatRoleStrategyFeatureNames.Risk,
                999d);
            return;
        }
        action.Features[
            CombatRoleStrategyFeatureNames.SafeContinuationCertified] = 1d;
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
        int calamityDamage)
    {
        var density = action.Cost <= 0
            ? 1.5d
            : action.Cost == 1
                ? 1.15d
                : 0.75d;
        var continuation = Math.Max(
            0d,
            action.Semantics.Draw
            + action.Semantics.EnergyGain
            + action.Semantics.CardGeneration * 0.5d);
        action.Features["roleStrategy:nana.calamity-action"] = 1d;
        action.Features["roleStrategy:nana.calamity-passive-damage"] =
            calamityDamage;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            Math.Min(12d, calamityDamage * density + continuation));
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            continuation + (action.Cost <= 1 ? 1d : 0d));
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

    private static double HarvestValue(
        CombatStateObservation state,
        CombatActionObservation action,
        int bleedPackage)
    {
        var gain = Feature(action, "nana:projected-doom-gain");
        var maximumHpGain = Feature(action, "nana:projected-max-hp-gain");
        if (gain <= 0d || action.TargetKind != CombatTargetKind.Enemy)
        {
            return gain + PersistentGrowthUtility(maximumHpGain);
        }
        var bleeding = StatusLevel(FindTarget(state, action), BleedingStatusId);
        var expectedBleedDamage = bleeding <= 30
            ? bleeding
            : bleeding * 2d;
        var opportunityCost = expectedBleedDamage
                              * Math.Max(1d, bleedPackage * 0.25d);
        return gain
               + PersistentGrowthUtility(maximumHpGain)
               - opportunityCost * 0.35d;
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
        bool sameTurnOnly)
    {
        var target = FindTarget(state, devour);
        if (target == null || target.CurrentHp <= 6)
        {
            return false;
        }
        return actions.Any(candidate =>
        {
            if (!IsDebuffBuilder(candidate)
                || candidate.TargetKind != CombatTargetKind.Enemy
                || candidate.TargetRuntimeId != devour.TargetRuntimeId
                || candidate.Semantics.EndsTurn)
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

    private static bool BuilderPreservesADevourTarget(
        CombatStateObservation state,
        IReadOnlyList<CombatActionObservation> devours,
        CombatActionObservation builder)
    {
        return devours.Any(devour =>
        {
            if (devour.TargetKind != CombatTargetKind.Enemy
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
               && action.TargetKind == CombatTargetKind.Enemy
               && (action.Semantics.Debuff > 0d
                   || action.Semantics.DamageOverTime > 0d
                   || action.Semantics.StateChanges.Keys.Any(key =>
                       key.StartsWith(
                           "targetStatus:",
                           StringComparison.OrdinalIgnoreCase)
                       || key.StartsWith(
                           "enemyStatus:",
                           StringComparison.OrdinalIgnoreCase)));
    }

    private static void EnrichGrowthBuilder(
        CombatActionObservation action,
        int doom)
    {
        var nextLayerMaximumHp = Math.Max(1, doom + 1);
        action.Features["roleStrategy:nana.growth-builder"] = 1d;
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Synergy,
            Math.Min(6d, 2d + nextLayerMaximumHp * 0.10d));
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Continuation,
            2.5d);
        SetMax(
            action,
            CombatRoleStrategyFeatureNames.Coordination,
            3d);
    }

    private static int CountBurstActions(
        IEnumerable<CombatActionObservation> actions,
        int currentPower)
    {
        return actions
            .Where(action => action.Legal
                             && action.Kind != CombatActionKind.EndTurn
                             && !IdEquals(action.SourceId, "careercard_2")
                             && !IdEquals(action.SourceId, "careercard_3")
                             && action.Cost <= currentPower
                             && !action.Semantics.EndsTurn)
            .GroupBy(action => action.RuntimeId > 0
                ? "runtime:" + action.RuntimeId
                : "source:" + action.SourceId)
            .Count();
    }

    private static bool IsNana(CombatStateObservation state)
    {
        return IdEquals(state.Player?.DefinitionId, "career_2")
               || IdEquals(state.Player?.DefinitionId, "career_4")
               || StateFeature(state, "playerRole:career_2", 0d) > 0.5d
               || StateFeature(state, "playerRole:career_4", 0d) > 0.5d;
    }

    private static bool IsBleedingCard(string? cardId)
    {
        return !string.IsNullOrWhiteSpace(cardId)
               && cardId!.StartsWith(
                   "blood_",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static CombatUnitObservation? FindTarget(
        CombatStateObservation state,
        CombatActionObservation action)
    {
        if (state.Player.RuntimeId == action.TargetRuntimeId)
        {
            return state.Player;
        }
        return state.Friendlies
            .Concat(state.Enemies)
            .FirstOrDefault(unit => unit.RuntimeId == action.TargetRuntimeId);
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
        string key)
    {
        return action.Features.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
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

    private static bool IdEquals(string? left, string? right)
    {
        return string.Equals(
            left,
            right,
            StringComparison.OrdinalIgnoreCase);
    }
}
