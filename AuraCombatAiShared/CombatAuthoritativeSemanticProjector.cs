using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public static class CombatAuthoritativeSemanticProjector
{
    public static CombatActionSemantics Project(
        CombatRuleset ruleset,
        CombatBattleState state,
        CombatCardDefinition card,
        CombatSimulationAction action)
    {
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (action == null) throw new ArgumentNullException(nameof(action));

        var semantics = new CombatActionSemantics();
        var projected = state.Clone();
        var sourceActorId = action.ActorId > 0
            ? action.ActorId
            : projected.PlayerActorId;
        var sourceActionId = Math.Max(1L, projected.ActionSequence + 1L);
        var changedStatuses = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var effect in card.Effects)
        {
            var targets = ResolveTargets(
                projected,
                effect.Target,
                sourceActorId,
                action.TargetActorId);
            if (targets.Count == 0)
            {
                continue;
            }
            var legacyAmountRecorded = false;
            foreach (var targetProjection in targets)
            {
                var targetId = targetProjection.TargetActorId;
                if (effect.ConditionExpression != null
                    && CombatSimulationExpressionEvaluator.Evaluate(
                        effect.ConditionExpression,
                        projected,
                        ruleset,
                        sourceActorId,
                        targetId) <= 0d)
                {
                    continue;
                }
                var amount = effect.AmountExpression == null
                    ? effect.Amount
                    : RoundEffectValue(
                        CombatSimulationExpressionEvaluator.Evaluate(
                            effect.AmountExpression,
                            projected,
                            ruleset,
                            sourceActorId,
                            targetId),
                        effect.Rounding);
                var probability =
                    Math.Max(0d, Math.Min(1d, effect.Probability))
                    * targetProjection.Probability;
                if (!legacyAmountRecorded)
                {
                    AddLegacySemantics(
                        semantics,
                        effect,
                        amount
                        * Math.Max(
                            0d,
                            Math.Min(1d, effect.Probability)),
                        projected,
                        targetId);
                    legacyAmountRecorded = true;
                }
                ProjectImmediateEffect(
                    semantics,
                    projected,
                    ruleset,
                    effect,
                    sourceActorId,
                    sourceActionId,
                    targetId,
                    amount,
                    probability,
                    changedStatuses);
            }
            if (effect.Probability < 1d
                || !string.IsNullOrWhiteSpace(effect.RandomChoiceGroup)
                || effect.Target == CombatSimulationTarget.RandomEnemy)
            {
                semantics.RandomOutcome = true;
                semantics.Uncertainty +=
                    1d - Math.Max(0d, Math.Min(1d, effect.Probability));
            }
        }

        ProjectPostActionTriggers(
            semantics,
            projected,
            ruleset,
            sourceActorId,
            sourceActionId,
            action.TargetActorId);
        ProjectDeferredTriggers(
            semantics,
            projected,
            ruleset,
            changedStatuses,
            sourceActionId,
            action.TargetActorId);
        semantics.ImmediateHpDamage =
            CombatActionSemanticMetrics.ImmediateHpDamage(semantics);
        semantics.ImmediateDurabilityDamage = semantics.TargetEffects
            .Where(item =>
                item.Phase == CombatSemanticEffectPhase.Immediate
                && item.Kind is CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss)
            .Sum(item =>
                Math.Max(0d, item.EffectiveDurabilityAmount)
                * Math.Max(0d, Math.Min(1d, item.Probability)));
        semantics.DeferredHpDamage =
            CombatActionSemanticMetrics.DeferredHpDamage(semantics);
        semantics.AffectedEnemyCount = semantics.TargetEffects
            .Where(item =>
                item.Phase == CombatSemanticEffectPhase.Immediate
                && item.Kind is CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss
                && state.FindActor(item.TargetRuntimeId)?.Kind
                == CombatSimulationActorKind.Enemy)
            .Select(item => item.TargetRuntimeId)
            .Distinct()
            .Count();
        return semantics;
    }

    private static void ProjectImmediateEffect(
        CombatActionSemantics semantics,
        CombatBattleState projected,
        CombatRuleset ruleset,
        CombatSimulationEffectDefinition effect,
        int sourceActorId,
        long sourceActionId,
        int targetId,
        int amount,
        double probability,
        ISet<string> changedStatuses)
    {
        var source = projected.FindActor(sourceActorId);
        var target = projected.FindActor(targetId);
        switch (effect.Kind)
        {
            case CombatSimulationEffectKind.Damage:
            case CombatSimulationEffectKind.TrueDamage:
            case CombatSimulationEffectKind.DirectHpLoss:
                if (target == null || !target.Alive)
                {
                    return;
                }
                var damage = CombatDamageResolver.Resolve(
                    source,
                    target,
                    ruleset,
                    effect.Kind,
                    amount,
                    effect.DefinitionId);
                semantics.TargetEffects.Add(new CombatTargetedSemanticEffect
                {
                    Phase = CombatSemanticEffectPhase.Immediate,
                    Kind = ToSemanticDamageKind(effect.Kind),
                    TargetRuntimeId = targetId,
                    DefinitionId = effect.DefinitionId,
                    RawAmount = amount,
                    EffectiveAmount = damage.HpDamage,
                    EffectiveDurabilityAmount = damage.DurabilityDamage,
                    Probability = probability,
                    BypassesBlock = damage.BypassesBlock
                });
                if (probability >= 1d)
                {
                    target.Block = Math.Max(
                        0,
                        target.Block - damage.BlockedAmount);
                    target.Hp = Math.Max(0, target.Hp - damage.HpDamage);
                }
                return;

            case CombatSimulationEffectKind.GainBlock:
                semantics.TargetEffects.Add(new CombatTargetedSemanticEffect
                {
                    Phase = CombatSemanticEffectPhase.Immediate,
                    Kind = CombatSemanticEffectKind.Defend,
                    TargetRuntimeId = targetId,
                    RawAmount = amount,
                    EffectiveAmount = amount,
                    Probability = probability
                });
                return;

            case CombatSimulationEffectKind.Heal:
                semantics.TargetEffects.Add(new CombatTargetedSemanticEffect
                {
                    Phase = CombatSemanticEffectPhase.Immediate,
                    Kind = CombatSemanticEffectKind.Heal,
                    TargetRuntimeId = targetId,
                    RawAmount = amount,
                    EffectiveAmount = target == null
                        ? amount
                        : Math.Min(
                            Math.Max(0, amount),
                            Math.Max(0, target.MaxHp - target.Hp)),
                    Probability = probability
                });
                return;

            case CombatSimulationEffectKind.AddStatus:
                if (target == null
                    || !ruleset.TryGetStatus(
                        effect.DefinitionId,
                        out var statusDefinition))
                {
                    return;
                }
                var existing = target.Statuses.FirstOrDefault(item =>
                    string.Equals(
                        item.StatusId,
                        effect.DefinitionId,
                        StringComparison.OrdinalIgnoreCase));
                var beforeStacks = existing?.Stacks ?? 0;
                var afterStacks = Math.Min(
                    Math.Max(1, statusDefinition.MaximumStacks),
                    beforeStacks + Math.Max(0, amount));
                var gainedStacks = Math.Max(0, afterStacks - beforeStacks);
                semantics.TargetEffects.Add(new CombatTargetedSemanticEffect
                {
                    Phase = CombatSemanticEffectPhase.Immediate,
                    Kind = CombatSemanticEffectKind.AddStatus,
                    TargetRuntimeId = targetId,
                    DefinitionId = effect.DefinitionId,
                    RawAmount = amount,
                    EffectiveAmount = gainedStacks,
                    Probability = probability
                });
                if (target.Kind == CombatSimulationActorKind.Enemy)
                {
                    semantics.Debuff += gainedStacks * probability;
                }
                else
                {
                    semantics.Buff += gainedStacks * probability;
                }
                if (gainedStacks <= 0 || probability < 1d)
                {
                    return;
                }
                if (existing == null)
                {
                    existing = new CombatStatusState
                    {
                        StatusId = effect.DefinitionId,
                        Stacks = afterStacks,
                        SourceActorId = sourceActorId,
                        LastStackGainActionId = sourceActionId,
                        StacksGainedInLastAction = gainedStacks
                    };
                    target.Statuses.Add(existing);
                }
                else
                {
                    existing.Stacks = afterStacks;
                    if (existing.LastStackGainActionId == sourceActionId)
                    {
                        existing.StacksGainedInLastAction += gainedStacks;
                    }
                    else
                    {
                        existing.LastStackGainActionId = sourceActionId;
                        existing.StacksGainedInLastAction = gainedStacks;
                    }
                }
                changedStatuses.Add(StatusKey(targetId, effect.DefinitionId));
                return;
        }
    }

    private static void ProjectPostActionTriggers(
        CombatActionSemantics semantics,
        CombatBattleState projected,
        CombatRuleset ruleset,
        int sourceActorId,
        long sourceActionId,
        int selectedTargetId)
    {
        foreach (var actor in projected.Actors.Where(item => item.Alive))
        {
            foreach (var status in actor.Statuses)
            {
                if (!ruleset.TryGetStatus(status.StatusId, out var definition))
                {
                    continue;
                }
                foreach (var trigger in definition.Triggers.Where(item =>
                             item.EventKind
                             == CombatSimulationEventKind.ActionResolved
                             && OwnerMatchesActionSource(
                                 actor,
                                 sourceActorId,
                                 item.OwnerRelation)))
                {
                    var eligibleStacks = EligibleStacks(
                        status,
                        trigger,
                        sourceActionId);
                    if (eligibleStacks <= 0)
                    {
                        continue;
                    }
                    ProjectTriggerEffects(
                        semantics,
                        projected,
                        ruleset,
                        actor,
                        trigger,
                        eligibleStacks,
                        CombatSemanticEffectPhase.PostAction,
                        sourceActorId,
                        selectedTargetId);
                }
            }
        }
    }

    private static void ProjectDeferredTriggers(
        CombatActionSemantics semantics,
        CombatBattleState projected,
        CombatRuleset ruleset,
        IReadOnlyCollection<string> changedStatuses,
        long sourceActionId,
        int selectedTargetId)
    {
        foreach (var key in changedStatuses)
        {
            var separator = key.IndexOf('|');
            if (separator <= 0
                || !int.TryParse(key.Substring(0, separator), out var actorId))
            {
                continue;
            }
            var statusId = key.Substring(separator + 1);
            var actor = projected.FindActor(actorId);
            var status = actor?.Statuses.FirstOrDefault(item =>
                string.Equals(
                    item.StatusId,
                    statusId,
                    StringComparison.OrdinalIgnoreCase));
            if (actor == null
                || status == null
                || !ruleset.TryGetStatus(statusId, out var definition))
            {
                continue;
            }
            foreach (var trigger in definition.Triggers.Where(item =>
                         item.EventKind
                         is CombatSimulationEventKind.TurnStarted
                             or CombatSimulationEventKind.TurnEnded))
            {
                var eligibleStacks = EligibleStacks(
                    status,
                    trigger,
                    sourceActionId + 1L);
                ProjectTriggerEffects(
                    semantics,
                    projected,
                    ruleset,
                    actor,
                    trigger,
                    eligibleStacks,
                    CombatSemanticEffectPhase.Deferred,
                    actor.ActorId,
                    selectedTargetId);
            }
        }
    }

    private static void ProjectTriggerEffects(
        CombatActionSemantics semantics,
        CombatBattleState projected,
        CombatRuleset ruleset,
        CombatActorState owner,
        CombatStatusTriggerDefinition trigger,
        int eligibleStacks,
        CombatSemanticEffectPhase phase,
        int eventSourceId,
        int eventTargetId)
    {
        foreach (var effect in trigger.Effects)
        {
            var targets = ResolveTriggerTargets(
                projected,
                effect.Target,
                owner.ActorId,
                eventSourceId,
                eventTargetId);
            foreach (var targetId in targets)
            {
                var target = projected.FindActor(targetId);
                var amount = effect.AmountExpression == null
                    ? effect.Amount
                    : RoundEffectValue(
                        CombatSimulationExpressionEvaluator.Evaluate(
                            effect.AmountExpression,
                            projected,
                            ruleset,
                            owner.ActorId,
                            targetId),
                        effect.Rounding);
                if (effect.ScaleWithStatusStacks)
                {
                    amount *= Math.Max(1, eligibleStacks);
                }
                var probability =
                    Math.Max(0d, Math.Min(1d, effect.Probability));
                if (effect.Kind is CombatSimulationEffectKind.Damage
                    or CombatSimulationEffectKind.TrueDamage
                    or CombatSimulationEffectKind.DirectHpLoss)
                {
                    if (target == null)
                    {
                        continue;
                    }
                    var damage = CombatDamageResolver.Resolve(
                        owner,
                        target,
                        ruleset,
                        effect.Kind,
                        amount,
                        effect.DefinitionId);
                    semantics.TargetEffects.Add(
                        new CombatTargetedSemanticEffect
                        {
                            Phase = phase,
                            Kind = ToSemanticDamageKind(effect.Kind),
                            TargetRuntimeId = targetId,
                            DefinitionId = effect.DefinitionId,
                            Trigger = trigger.EventKind.ToString(),
                            RawAmount = amount,
                            EffectiveAmount = damage.HpDamage,
                            EffectiveDurabilityAmount =
                                damage.DurabilityDamage,
                            Probability = probability,
                            BypassesBlock = damage.BypassesBlock,
                            Contextual = phase
                                == CombatSemanticEffectPhase.PostAction
                        });
                }
                else if (effect.Kind == CombatSimulationEffectKind.AddStatus)
                {
                    semantics.TargetEffects.Add(
                        new CombatTargetedSemanticEffect
                        {
                            Phase = phase,
                            Kind = CombatSemanticEffectKind.AddStatus,
                            TargetRuntimeId = targetId,
                            DefinitionId = effect.DefinitionId,
                            Trigger = trigger.EventKind.ToString(),
                            RawAmount = amount,
                            EffectiveAmount = amount,
                            Probability = probability,
                            Contextual = phase
                                == CombatSemanticEffectPhase.PostAction
                        });
                }
            }
        }
    }

    private static void AddLegacySemantics(
        CombatActionSemantics semantics,
        CombatSimulationEffectDefinition effect,
        double expected,
        CombatBattleState state,
        int targetActorId)
    {
        switch (effect.Kind)
        {
            case CombatSimulationEffectKind.Damage:
                semantics.Damage += expected;
                break;
            case CombatSimulationEffectKind.TrueDamage:
                semantics.TrueDamage += expected;
                break;
            case CombatSimulationEffectKind.DirectHpLoss:
                if (effect.Target is CombatSimulationTarget.Self
                    or CombatSimulationTarget.Player)
                {
                    semantics.SelfHpLoss += expected;
                    semantics.Risk += expected;
                }
                else
                {
                    semantics.TrueDamage += expected;
                }
                break;
            case CombatSimulationEffectKind.GainBlock:
                semantics.Defend += expected;
                break;
            case CombatSimulationEffectKind.Heal:
                semantics.Heal += expected;
                break;
            case CombatSimulationEffectKind.Draw:
                semantics.Draw += expected;
                break;
            case CombatSimulationEffectKind.GainEnergy:
                semantics.EnergyGain += expected;
                break;
            case CombatSimulationEffectKind.SetEnergy:
                var energyBefore = state.FindActor(targetActorId)?.Energy ?? 0;
                var energyDelta = expected - energyBefore;
                semantics.EnergyGain += Math.Max(0d, energyDelta);
                if (effect.AmountExpression?.Operation
                    == CombatSimulationValueOperation.SourceMaxEnergy)
                {
                    semantics.RestoreEnergyToMaximum = true;
                }
                else if (TryReadEnergyFloor(
                             effect.AmountExpression,
                             out var energyFloor))
                {
                    semantics.EnergyMinimum = energyFloor;
                }
                else
                {
                    semantics.EnergySetAmount = Math.Max(0d, expected);
                }
                if (targetActorId == state.PlayerActorId)
                {
                    semantics.StateChanges["player.energy"] = energyDelta;
                }
                break;
            case CombatSimulationEffectKind.RetrieveCards:
                semantics.CardRetrievals.Add(new CombatCardRetrievalSemantic
                {
                    SourceZone = ToAiCardZone(effect.SourceZone),
                    DestinationZone = ToAiCardZone(effect.DestinationZone),
                    Amount = Math.Max(0, (int)Math.Round(expected)),
                    RequiredCardTag = effect.RequiredCardTag ?? "",
                    CandidateBranchCount = 3
                });
                semantics.OpensInteraction = true;
                break;
            case CombatSimulationEffectKind.CreateCard:
                semantics.CardGeneration +=
                    Math.Max(0d, effect.Probability);
                break;
            case CombatSimulationEffectKind.ChangeCardCost:
                semantics.CostReduction += Math.Max(0d, -expected);
                break;
            case CombatSimulationEffectKind.SetHp:
                var currentHp = state.Player?.Hp ?? 0;
                var hpDelta = expected - currentHp;
                semantics.StateChanges["player.hp"] = hpDelta;
                if (hpDelta < 0d)
                {
                    semantics.Risk += -hpDelta;
                }
                else
                {
                    semantics.Heal += hpDelta;
                }
                break;
            case CombatSimulationEffectKind.ModifyVariable:
                if (effect.Target is CombatSimulationTarget.Self
                    or CombatSimulationTarget.Player)
                {
                    var key = "player." + effect.DefinitionId;
                    var current = state.Player?.Variables.TryGetValue(
                        effect.DefinitionId,
                        out var value) == true
                        ? value
                        : 0d;
                    var after = Math.Max(
                        effect.MinimumVariableValue,
                        Math.Min(
                            effect.MaximumVariableValue,
                            current + expected));
                    semantics.StateChanges[key] =
                        semantics.StateChanges.TryGetValue(key, out var delta)
                            ? delta + after - current
                            : after - current;
                }
                break;
            case CombatSimulationEffectKind.SummonEnemy:
                semantics.Risk += Math.Max(1d, expected);
                break;
            case CombatSimulationEffectKind.Despawn:
                semantics.TrueDamage += Math.Max(1d, expected);
                break;
            case CombatSimulationEffectKind.RemoveStatus:
                semantics.Cleanse += Math.Max(1d, expected);
                break;
            case CombatSimulationEffectKind.DiscardRandom:
            case CombatSimulationEffectKind.ExhaustRandom:
                semantics.Risk += expected;
                break;
        }
    }

    private static CombatCardZoneKind ToAiCardZone(CombatCardZone zone)
    {
        return zone switch
        {
            CombatCardZone.Hand => CombatCardZoneKind.Hand,
            CombatCardZone.DiscardPile => CombatCardZoneKind.DiscardPile,
            CombatCardZone.ExhaustPile => CombatCardZoneKind.ExhaustPile,
            _ => CombatCardZoneKind.DrawPile
        };
    }

    private static bool TryReadEnergyFloor(
        CombatSimulationValueExpression? expression,
        out double floor)
    {
        floor = 0d;
        if (expression?.Operation != CombatSimulationValueOperation.Maximum
            || expression.Arguments.Count != 2)
        {
            return false;
        }
        var energy = expression.Arguments.FirstOrDefault(argument =>
            argument.Operation == CombatSimulationValueOperation.SourceEnergy);
        var constant = expression.Arguments.FirstOrDefault(argument =>
            argument.Operation == CombatSimulationValueOperation.Constant);
        if (energy == null || constant == null)
        {
            return false;
        }
        floor = Math.Max(0d, constant.Constant);
        return true;
    }

    private static List<TargetProjection> ResolveTargets(
        CombatBattleState state,
        CombatSimulationTarget target,
        int sourceActorId,
        int selectedTargetId)
    {
        switch (target)
        {
            case CombatSimulationTarget.Self:
                return One(sourceActorId);
            case CombatSimulationTarget.Player:
                return One(state.PlayerActorId);
            case CombatSimulationTarget.SelectedEnemy:
                return state.FindActor(selectedTargetId)?.Alive == true
                    ? One(selectedTargetId)
                    : new List<TargetProjection>();
            case CombatSimulationTarget.AllEnemies:
            case CombatSimulationTarget.AllOpponents:
                return state.LivingEnemies
                    .OrderBy(item => item.ActorId)
                    .Select(item => new TargetProjection(item.ActorId, 1d))
                    .ToList();
            case CombatSimulationTarget.RandomEnemy:
                var enemies = state.LivingEnemies
                    .OrderBy(item => item.ActorId)
                    .ToList();
                var probability = enemies.Count == 0
                    ? 0d
                    : 1d / enemies.Count;
                return enemies
                    .Select(item =>
                        new TargetProjection(item.ActorId, probability))
                    .ToList();
            case CombatSimulationTarget.AllAllies:
                var source = state.FindActor(sourceActorId);
                return source == null
                    ? new List<TargetProjection>()
                    : state.Actors
                        .Where(item =>
                            item.Alive && AreAllies(source, item))
                        .OrderBy(item => item.ActorId)
                        .Select(item =>
                            new TargetProjection(item.ActorId, 1d))
                        .ToList();
            case CombatSimulationTarget.AllAlliesExceptSelf:
                var owner = state.FindActor(sourceActorId);
                return owner == null
                    ? new List<TargetProjection>()
                    : state.Actors
                        .Where(item =>
                            item.Alive
                            && item.ActorId != sourceActorId
                            && AreAllies(owner, item))
                        .OrderBy(item => item.ActorId)
                        .Select(item =>
                            new TargetProjection(item.ActorId, 1d))
                        .ToList();
            default:
                return selectedTargetId > 0
                    ? One(selectedTargetId)
                    : One(sourceActorId);
        }
    }

    private static IReadOnlyList<int> ResolveTriggerTargets(
        CombatBattleState state,
        CombatSimulationTarget target,
        int ownerActorId,
        int eventSourceId,
        int eventTargetId)
    {
        if (target == CombatSimulationTarget.EventSource)
        {
            return eventSourceId > 0
                ? new[] { eventSourceId }
                : Array.Empty<int>();
        }
        if (target == CombatSimulationTarget.EventTarget)
        {
            return eventTargetId > 0
                ? new[] { eventTargetId }
                : Array.Empty<int>();
        }
        return ResolveTargets(
                state,
                target,
                ownerActorId,
                eventTargetId)
            .Select(item => item.TargetActorId)
            .ToList();
    }

    private static bool OwnerMatchesActionSource(
        CombatActorState owner,
        int eventSourceId,
        CombatStatusTriggerOwnerRelation relation)
    {
        return relation switch
        {
            CombatStatusTriggerOwnerRelation.EventSource =>
                owner.ActorId == eventSourceId,
            CombatStatusTriggerOwnerRelation.EventTarget => false,
            CombatStatusTriggerOwnerRelation.EventTargetAllyExceptSelf =>
                false,
            _ => true
        };
    }

    private static int EligibleStacks(
        CombatStatusState status,
        CombatStatusTriggerDefinition trigger,
        long sourceActionId)
    {
        if (!trigger.ExcludeStacksAcquiredFromSameAction
            || status.LastStackGainActionId != sourceActionId)
        {
            return Math.Max(0, status.Stacks);
        }
        return Math.Max(
            0,
            status.Stacks
            - Math.Min(status.Stacks, status.StacksGainedInLastAction));
    }

    private static CombatSemanticEffectKind ToSemanticDamageKind(
        CombatSimulationEffectKind kind)
    {
        return kind switch
        {
            CombatSimulationEffectKind.TrueDamage =>
                CombatSemanticEffectKind.TrueDamage,
            CombatSimulationEffectKind.DirectHpLoss =>
                CombatSemanticEffectKind.DirectHpLoss,
            _ => CombatSemanticEffectKind.Damage
        };
    }

    private static bool AreAllies(
        CombatActorState left,
        CombatActorState right)
    {
        return left.Kind == CombatSimulationActorKind.Enemy
               == (right.Kind == CombatSimulationActorKind.Enemy);
    }

    private static List<TargetProjection> One(int actorId)
    {
        return actorId > 0
            ? new List<TargetProjection>
            {
                new(actorId, 1d)
            }
            : new List<TargetProjection>();
    }

    private static string StatusKey(int actorId, string statusId)
    {
        return actorId + "|" + statusId;
    }

    private static int RoundEffectValue(
        double value,
        CombatSimulationValueRounding rounding)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        return rounding switch
        {
            CombatSimulationValueRounding.Truncate => (int)value,
            CombatSimulationValueRounding.Floor => (int)Math.Floor(value),
            CombatSimulationValueRounding.Ceiling => (int)Math.Ceiling(value),
            _ => (int)Math.Round(value)
        };
    }

    private readonly struct TargetProjection
    {
        public TargetProjection(
            int targetActorId,
            double probability)
        {
            TargetActorId = targetActorId;
            Probability = probability;
        }

        public int TargetActorId { get; }

        public double Probability { get; }
    }
}
