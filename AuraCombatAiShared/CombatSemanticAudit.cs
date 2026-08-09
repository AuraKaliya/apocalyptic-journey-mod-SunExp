using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

public enum CombatSemanticAuditStatus
{
    ValidMatch,
    ValidMismatch,
    Invalid
}

public sealed class CombatSemanticAuditComparison
{
    public string Kind { get; set; } = "";

    public double Projected { get; set; }

    public double EffectiveProjected { get; set; }

    public double Actual { get; set; }

    public string Classification { get; set; } = "";

    public string Explanation { get; set; } = "";
}

public sealed class CombatSemanticAuditResult
{
    public List<string> AuditedKinds { get; set; } = new();

    public List<string> MismatchKinds { get; set; } = new();

    public List<string> InvalidKinds { get; set; } = new();

    public List<string> ExplainedKinds { get; set; } = new();

    public List<CombatSemanticAuditComparison> Comparisons { get; set; } = new();

    public bool Valid => InvalidKinds.Count == 0;

    public bool Invalid => !Valid;

    public bool Mismatch => Valid && MismatchKinds.Count > 0;

    public bool ExplainedDifference => ExplainedKinds.Count > 0;

    public CombatSemanticAuditStatus Status => Invalid
        ? CombatSemanticAuditStatus.Invalid
        : Mismatch
            ? CombatSemanticAuditStatus.ValidMismatch
            : CombatSemanticAuditStatus.ValidMatch;

    public string Describe(string sourceId)
    {
        var details = Comparisons
            .Where(item =>
                string.Equals(
                    item.Classification,
                    "unexplained",
                    StringComparison.Ordinal)
                || string.Equals(
                    item.Classification,
                    "invalid",
                    StringComparison.Ordinal))
            .Take(4)
            .Select(item =>
                item.Kind
                + ":projected="
                + Format(item.Projected)
                + ",effective="
                + Format(item.EffectiveProjected)
                + ",actual="
                + Format(item.Actual)
                + (string.IsNullOrWhiteSpace(item.Explanation)
                    ? ""
                    : ",reason=" + Sanitize(item.Explanation)));
        return (string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId)
               + "|"
               + string.Join(";", details);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        return (value ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace(";", ",");
    }
}

public sealed class CombatEffectiveActionProjection
{
    public double Damage { get; set; }

    public double DurabilityDamage { get; set; }

    public double IntrinsicDefend { get; set; }

    public double Defend { get; set; }

    public double Heal { get; set; }
}

public static class CombatSemanticAuditor
{
    public static CombatActionSemantics ProjectRealized(
        CombatBattleState before,
        CombatBattleState after,
        IReadOnlyList<CombatSimulationEvent> events,
        CombatSimulationAction action,
        CombatRuleset? ruleset,
        CombatActionSemantics? declared = null)
    {
        before ??= new CombatBattleState();
        after ??= before;
        action ??= new CombatSimulationAction();
        var actionEvents = ScopeActionEvents(
            before,
            events ?? Array.Empty<CombatSimulationEvent>())
            .OrderBy(item => item.Sequence)
            .ToList();
        var result = new CombatActionSemantics();
        result.StateChanges["projection.realized"] = 1d;
        var playerId = before.PlayerActorId;
        var beforePlayer = before.FindActor(playerId);
        var afterPlayer = after.FindActor(playerId);
        var runningPlayerHp = beforePlayer?.Hp ?? 0;
        result.MinimumHpDuringAction = runningPlayerHp;
        var tracedPlayerHpDelta = 0d;

        if (beforePlayer != null && afterPlayer != null)
        {
            var hpDelta = afterPlayer.Hp - beforePlayer.Hp;
            var maximumHpDelta = afterPlayer.MaxHp - beforePlayer.MaxHp;
            result.ObservedNetHpDelta = hpDelta;
            result.StateChanges["player.hp"] = hpDelta;
            result.StateChanges["playerMaxHp"] = maximumHpDelta;
            foreach (var statusId in beforePlayer.Statuses
                         .Select(status => status.StatusId)
                         .Concat(afterPlayer.Statuses.Select(status =>
                             status.StatusId))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var beforeStacks = beforePlayer.Statuses
                    .Where(status => string.Equals(
                        status.StatusId,
                        statusId,
                        StringComparison.OrdinalIgnoreCase))
                    .Sum(status => status.Stacks);
                var afterStacks = afterPlayer.Statuses
                    .Where(status => string.Equals(
                        status.StatusId,
                        statusId,
                        StringComparison.OrdinalIgnoreCase))
                    .Sum(status => status.Stacks);
                if (beforeStacks != afterStacks)
                {
                    result.StateChanges["status:" + statusId] =
                        afterStacks - beforeStacks;
                }
            }
        }

        foreach (var item in actionEvents)
        {
            var attribution = AttributionFor(item, action);
            if (item.Kind == CombatSimulationEventKind.DamageDealt)
            {
                var kind = DamageKind(item);
                var amount = Math.Max(0, item.Amount);
                var effect = EffectFromEvent(item, attribution, kind);
                result.TargetEffects.Add(effect);
                if (IsEnemy(before, item.TargetActorId))
                {
                    if (kind == CombatSemanticEffectKind.Damage)
                    {
                        result.Damage += amount;
                    }
                    else
                    {
                        result.TrueDamage += amount;
                    }
                    if (attribution == CombatSemanticEffectAttribution.DirectAction)
                    {
                        result.DirectDamage += amount;
                    }
                    else
                    {
                        result.ContextDamage += amount;
                    }
                }
                else if (item.TargetActorId == playerId)
                {
                    tracedPlayerHpDelta -= amount;
                    runningPlayerHp -= amount;
                    result.SelfHpLoss += amount;
                    result.Risk += amount;
                    if (attribution == CombatSemanticEffectAttribution.DirectAction)
                    {
                        result.DirectSelfHpLoss += amount;
                    }
                    else
                    {
                        result.ContextSelfHpLoss += amount;
                    }
                    RecordMinimumHp(result, runningPlayerHp);
                }
                continue;
            }

            if (item.TargetActorId == playerId
                && (item.Kind == CombatSimulationEventKind.Healed
                    || IsHpAssignment(item)))
            {
                var delta = item.Amount;
                if (item.CurrentAmount != 0 || item.PreviousAmount != 0)
                {
                    delta = item.CurrentAmount - item.PreviousAmount;
                }
                tracedPlayerHpDelta += delta;
                runningPlayerHp = item.CurrentAmount != 0
                                  || item.PreviousAmount != 0
                    ? item.CurrentAmount
                    : runningPlayerHp + delta;
                if (delta >= 0)
                {
                    result.Heal += delta;
                    if (attribution == CombatSemanticEffectAttribution.DirectAction)
                    {
                        result.DirectHeal += delta;
                    }
                    else
                    {
                        result.ContextHeal += delta;
                    }
                    result.TargetEffects.Add(EffectFromEvent(
                        item,
                        attribution,
                        CombatSemanticEffectKind.Heal,
                        delta));
                }
                else
                {
                    var loss = -delta;
                    result.SelfHpLoss += loss;
                    result.Risk += loss;
                    if (attribution == CombatSemanticEffectAttribution.DirectAction)
                    {
                        result.DirectSelfHpLoss += loss;
                    }
                    else
                    {
                        result.ContextSelfHpLoss += loss;
                    }
                    result.TargetEffects.Add(EffectFromEvent(
                        item,
                        attribution,
                        CombatSemanticEffectKind.DirectHpLoss,
                        loss));
                }
                RecordMinimumHp(result, runningPlayerHp);
                continue;
            }

            if (item.TargetActorId == playerId
                && item.Kind is CombatSimulationEventKind.BlockGained
                    or CombatSimulationEventKind.BlockChanged)
            {
                result.Defend += Math.Max(0, item.Amount);
            }
            if (item.TargetActorId == playerId
                && item.Kind == CombatSimulationEventKind.EnergyChanged)
            {
                if (item.Amount > 0)
                {
                    result.EnergyGain += item.Amount;
                }
                else if (item.Amount < 0)
                {
                    AddStateChange(result, "player.energySpent", -item.Amount);
                }
            }
            if (item.Kind is CombatSimulationEventKind.MaximumHpChanged
                or CombatSimulationEventKind.MaximumEnergyChanged
                or CombatSimulationEventKind.StatusStacksChanged
                or CombatSimulationEventKind.CardCostChanged
                or CombatSimulationEventKind.VariableChanged
                or CombatSimulationEventKind.TurnFlowChanged)
            {
                AddStateChange(
                    result,
                    "fact:"
                    + (string.IsNullOrWhiteSpace(item.StatePath)
                        ? item.Kind + ":" + item.DefinitionId
                        : item.StatePath),
                    item.Amount);
            }
        }

        if (beforePlayer != null && afterPlayer != null)
        {
            var observed = afterPlayer.Hp - beforePlayer.Hp;
            if (Math.Abs(observed - tracedPlayerHpDelta) > 0.000001d)
            {
                var unknownDelta = observed - tracedPlayerHpDelta;
                result.StateChanges["trace.hp.unattributed"] = unknownDelta;
                if (unknownDelta < 0)
                {
                    var loss = -unknownDelta;
                    result.SelfHpLoss += loss;
                    result.ContextSelfHpLoss += loss;
                    result.Risk += loss;
                    result.TargetEffects.Add(new CombatTargetedSemanticEffect
                    {
                        Phase = CombatSemanticEffectPhase.PostAction,
                        Kind = CombatSemanticEffectKind.DirectHpLoss,
                        Attribution = CombatSemanticEffectAttribution.ExternalOrUnknown,
                        TargetRuntimeId = playerId,
                        RawAmount = loss,
                        EffectiveAmount = loss,
                        EffectiveDurabilityAmount = loss,
                        Probability = 1d,
                        BypassesBlock = true,
                        Contextual = true
                    });
                    RecordMinimumHp(result, afterPlayer.Hp);
                }
                else if (unknownDelta > 0)
                {
                    result.Heal += unknownDelta;
                    result.ContextHeal += unknownDelta;
                }
            }
        }

        var beforeCardIds = before.Cards
            .Select(item => item.InstanceId)
            .ToHashSet();
        var createdCardIds = after.Cards
            .Where(item => !beforeCardIds.Contains(item.InstanceId))
            .Select(item => item.InstanceId)
            .ToHashSet();
        result.CardGeneration = createdCardIds.Count;
        result.Draw = actionEvents.Count(item =>
            item.Kind == CombatSimulationEventKind.CardDrawn
            && !createdCardIds.Contains(item.CardInstanceId)
            && (item.TargetActorId == 0 || item.TargetActorId == playerId));

        var statusDeltas = StatusDeltas(
            before,
            action,
            ruleset,
            actionEvents);
        result.Buff = statusDeltas.Buff;
        result.Debuff = statusDeltas.Debuff;
        result.Cleanse = actionEvents
            .Where(item => item.Kind == CombatSimulationEventKind.StatusRemoved
                           && item.TargetActorId == playerId)
            .Sum(item => Math.Max(0, item.Amount));
        result.RandomOutcome = actionEvents.Any(item =>
            item.Kind is CombatSimulationEventKind.RandomResolved
                or CombatSimulationEventKind.DiceChecked);
        result.Uncertainty = result.RandomOutcome ? 1d : 0d;
        result.AffectedEnemyCount = result.TargetEffects
            .Where(item => IsEnemy(before, item.TargetRuntimeId))
            .Select(item => item.TargetRuntimeId)
            .Distinct()
            .Count();
        result.ImmediateHpDamage = result.TargetEffects
            .Where(item => IsEnemy(before, item.TargetRuntimeId)
                           && (item.Kind is CombatSemanticEffectKind.Damage
                               or CombatSemanticEffectKind.TrueDamage
                               or CombatSemanticEffectKind.DirectHpLoss))
            .Sum(item => Math.Max(0d, item.EffectiveAmount));
        result.ImmediateDurabilityDamage = Math.Max(
            0d,
            result.TargetEffects
                .Where(item => IsEnemy(before, item.TargetRuntimeId)
                               && (item.Kind is CombatSemanticEffectKind.Damage
                                   or CombatSemanticEffectKind.TrueDamage
                                   or CombatSemanticEffectKind.DirectHpLoss))
                .Sum(item => item.EffectiveDurabilityAmount));
        PreserveDeferredContract(result, declared);
        return result;
    }

    private static void PreserveDeferredContract(
        CombatActionSemantics realized,
        CombatActionSemantics? declared)
    {
        if (declared == null)
        {
            return;
        }
        realized.OpensInteraction = declared.OpensInteraction
                                      || declared.Interaction != null;
        realized.Interaction = declared.Interaction?.Clone();
        realized.CardRetrievals = declared.CardRetrievals.Select(item =>
            new CombatCardRetrievalSemantic
            {
                SourceZone = item.SourceZone,
                DestinationZone = item.DestinationZone,
                Amount = item.Amount,
                RequiredCardTag = item.RequiredCardTag,
                CandidateBranchCount = item.CandidateBranchCount
            }).ToList();
        realized.HandTransform = declared.HandTransform;
        realized.CooldownTurns = Math.Max(0d, declared.CooldownTurns);
        realized.DeckValue = declared.DeckValue;
        realized.PersistentValue = declared.PersistentValue;
        realized.CostReduction = declared.CostReduction;
        realized.EndsTurn = declared.EndsTurn;
        realized.DamageToBlockSetup = declared.DamageToBlockSetup;
        realized.EnergySetAmount = declared.EnergySetAmount;
        realized.EnergyMinimum = declared.EnergyMinimum;
        realized.RestoreEnergyToMaximum = declared.RestoreEnergyToMaximum;
        realized.Risk = Math.Max(realized.Risk, declared.Risk);
        if (realized.OpensInteraction
            && realized.Interaction?.EffectsComplete == false)
        {
            realized.Uncertainty = Math.Max(realized.Uncertainty, 1.5d);
        }
    }

    private static CombatTargetedSemanticEffect EffectFromEvent(
        CombatSimulationEvent item,
        CombatSemanticEffectAttribution attribution,
        CombatSemanticEffectKind kind,
        double? effectiveOverride = null)
    {
        var effective = Math.Max(0d, effectiveOverride ?? item.Amount);
        var blocked = Math.Max(0d, item.BlockedAmount);
        var durability = item.DurabilityAmount > 0
            ? item.DurabilityAmount
            : effective + blocked;
        return new CombatTargetedSemanticEffect
        {
            Phase = attribution switch
            {
                CombatSemanticEffectAttribution.DirectAction =>
                    CombatSemanticEffectPhase.Immediate,
                CombatSemanticEffectAttribution.PhaseTriggered =>
                    CombatSemanticEffectPhase.Deferred,
                _ => CombatSemanticEffectPhase.PostAction
            },
            Kind = kind,
            Attribution = attribution,
            TargetRuntimeId = item.TargetActorId,
            DefinitionId = item.DefinitionId ?? "",
            Trigger = item.HandlerId ?? "",
            SourceDefinitionId = string.IsNullOrWhiteSpace(item.SourceRewardId)
                ? item.DefinitionId ?? ""
                : item.SourceRewardId,
            SourceActionId = item.SourceActionId,
            Sequence = item.Sequence,
            ParentSequence = item.ParentSequence,
            CausalChainId = item.CausalChainId,
            TriggerWave = Math.Max(0, item.TriggerWave),
            RawAmount = Math.Max(
                0d,
                item.RawAmount == 0 ? effective : item.RawAmount),
            EffectiveAmount = effective,
            EffectiveDurabilityAmount = Math.Max(0d, durability),
            BlockedAmount = blocked,
            Probability = 1d,
            BypassesBlock = kind is CombatSemanticEffectKind.TrueDamage
                or CombatSemanticEffectKind.DirectHpLoss,
            Contextual = attribution
                         != CombatSemanticEffectAttribution.DirectAction
        };
    }

    private static CombatSemanticEffectKind DamageKind(
        CombatSimulationEvent item)
    {
        return string.Equals(
                item.Message,
                CombatSimulationEffectKind.TrueDamage.ToString(),
                StringComparison.OrdinalIgnoreCase)
            ? CombatSemanticEffectKind.TrueDamage
            : string.Equals(
                item.Message,
                CombatSimulationEffectKind.DirectHpLoss.ToString(),
                StringComparison.OrdinalIgnoreCase)
                ? CombatSemanticEffectKind.DirectHpLoss
                : CombatSemanticEffectKind.Damage;
    }

    private static CombatSemanticEffectAttribution AttributionFor(
        CombatSimulationEvent item,
        CombatSimulationAction action)
    {
        if (IsIntrinsic(item, action))
        {
            return CombatSemanticEffectAttribution.DirectAction;
        }
        if (item.Phase != CombatSimulationPhase.PlayerAction
            || item.Kind is CombatSimulationEventKind.TurnStarted
                or CombatSimulationEventKind.TurnEnded)
        {
            return CombatSemanticEffectAttribution.PhaseTriggered;
        }
        if (item.SourceActionId > 0)
        {
            return CombatSemanticEffectAttribution.ActionTriggeredContext;
        }
        return CombatSemanticEffectAttribution.ExternalOrUnknown;
    }

    private static void RecordMinimumHp(
        CombatActionSemantics result,
        double hp)
    {
        result.MinimumHpDuringAction = Math.Min(
            result.MinimumHpDuringAction,
            hp);
        if (hp <= 0d)
        {
            result.LethalBeforeRecovery = true;
        }
    }

    private static void AddStateChange(
        CombatActionSemantics result,
        string key,
        double amount)
    {
        if (string.IsNullOrWhiteSpace(key)
            || double.IsNaN(amount)
            || double.IsInfinity(amount)
            || Math.Abs(amount) <= 0.000001d)
        {
            return;
        }
        result.StateChanges[key] = result.StateChanges.TryGetValue(
            key,
            out var current)
            ? current + amount
            : amount;
    }

    public static CombatEffectiveActionProjection ProjectEffective(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected)
    {
        return ProjectEffective(before, action, projected, null);
    }

    public static CombatEffectiveActionProjection ProjectEffective(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected,
        CombatRuleset? ruleset)
    {
        var realized = projected.StateChanges.TryGetValue(
            "projection.realized",
            out var realizedValue)
            && realizedValue > 0d;
        var targetedDamage = projected.TargetEffects
            .Where(item =>
                (realized
                 || item.Phase == CombatSemanticEffectPhase.Immediate)
                && (item.Kind is CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss)
                && IsEnemy(before, item.TargetRuntimeId))
            .ToList();
        return new CombatEffectiveActionProjection
        {
            Damage = targetedDamage.Count > 0
                ? targetedDamage.Sum(item =>
                    Math.Max(0d, item.EffectiveAmount)
                    * Probability(item))
                : realized
                    ? Math.Max(0d, projected.Damage + projected.TrueDamage)
                    : EffectiveDamage(before, action, projected, ruleset),
            DurabilityDamage = realized
                ? Math.Max(0d, projected.ImmediateDurabilityDamage)
                : targetedDamage.Count > 0
                ? targetedDamage.Sum(item =>
                    Math.Max(0d, item.EffectiveDurabilityAmount)
                    * Probability(item))
                : EffectiveDurabilityDamage(
                    before,
                    action,
                    projected,
                    ruleset),
            IntrinsicDefend = realized
                ? Math.Max(0d, projected.Defend)
                : EffectiveBlock(before.Player, projected.Defend),
            Defend = realized
                ? Math.Max(0d, projected.Defend)
                : NetEffectiveBlock(before.Player, projected.Defend),
            Heal = realized
                ? Math.Max(0d, projected.Heal)
                : EffectiveHeal(before.Player, projected.Heal)
        };
    }

    public static CombatSemanticAuditResult Audit(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events,
        CombatActionSemantics? projected,
        CombatSimulationAction action)
    {
        return Audit(before, before, events, projected, action, null);
    }

    public static CombatSemanticAuditResult Audit(
        CombatBattleState before,
        CombatBattleState after,
        IReadOnlyList<CombatSimulationEvent> events,
        CombatActionSemantics? projected,
        CombatSimulationAction action,
        CombatRuleset? ruleset)
    {
        var result = new CombatSemanticAuditResult();
        if (projected == null)
        {
            AddInvalid(
                result,
                "projection",
                "no projected semantics were available");
            return result;
        }

        before ??= new CombatBattleState();
        after ??= before;
        action ??= new CombatSimulationAction();
        var actionEvents = ScopeActionEvents(
            before,
            events ?? Array.Empty<CombatSimulationEvent>());
        if (!actionEvents.Any(IsSemanticTraceEvidence))
        {
            AddInvalid(
                result,
                "action-trace",
                "no action-scoped semantic events were captured");
            return result;
        }
        var intrinsicEvents = actionEvents
            .Where(item => IsIntrinsic(item, action))
            .ToList();
        var contextualEvents = actionEvents
            .Where(item => !IsIntrinsic(item, action))
            .ToList();
        var realizedProjection = projected.StateChanges.TryGetValue(
            "projection.realized",
            out var realizedMarker)
            && realizedMarker > 0d;
        var comparisonEvents = realizedProjection
            ? actionEvents
            : intrinsicEvents;
        var playerId = before.PlayerActorId;
        var target = before.FindActor(action.TargetActorId);

        var actualDamageByTarget = comparisonEvents
            .Where(item => item.Kind == CombatSimulationEventKind.DamageDealt
                           && IsEnemy(before, item.TargetActorId))
            .GroupBy(item => item.TargetActorId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => Math.Max(0, item.Amount)));
        var actualDamage = actualDamageByTarget.Values.Sum();
        var observedDurabilityDamage = before.LivingEnemies.Sum(item =>
                                           Math.Max(0, item.Hp)
                                           + Math.Max(0, item.Block))
                                       - after.LivingEnemies.Sum(item =>
                                           Math.Max(0, item.Hp)
                                           + Math.Max(0, item.Block));
        var damageTraceComplete = observedDurabilityDamage <= 0
                                  || actionEvents.Any(item =>
                                      item.Kind
                                      == CombatSimulationEventKind.DamageDealt);
        var effectiveProjection = ProjectEffective(
            before,
            action,
            projected,
            ruleset);
        var effectiveDamage = effectiveProjection.Damage;
        var targetedDamage = projected.TargetEffects
            .Where(item =>
                (realizedProjection
                 || item.Phase == CombatSemanticEffectPhase.Immediate)
                && (item.Kind is CombatSemanticEffectKind.Damage
                    or CombatSemanticEffectKind.TrueDamage
                    or CombatSemanticEffectKind.DirectHpLoss)
                && IsEnemy(before, item.TargetRuntimeId))
            .ToList();
        IEnumerable<int> auditedDamageTargets = targetedDamage.Count == 0
            ? Array.Empty<int>()
            : targetedDamage
                .Select(item => item.TargetRuntimeId)
                .Concat(actualDamageByTarget.Keys)
                .Distinct()
                .OrderBy(item => item);
        foreach (var targetId in auditedDamageTargets)
        {
            var targetProjection = targetedDamage
                .Where(item => item.TargetRuntimeId == targetId)
                .ToList();
            Compare(
                result,
                "damage:target:" + targetId,
                targetProjection.Sum(item =>
                    Math.Max(0d, item.RawAmount)
                    * Probability(item)),
                targetProjection.Sum(item =>
                    Math.Max(0d, item.EffectiveAmount)
                    * Probability(item)),
                actualDamageByTarget.TryGetValue(
                    targetId,
                    out var targetDamage)
                    ? targetDamage
                    : 0d,
                "",
                damageTraceComplete);
        }
        var projectedDamage = targetedDamage.Count > 0
            ? targetedDamage.Sum(item =>
                Math.Max(0d, item.RawAmount)
                * Probability(item))
            : projected.Damage + projected.TrueDamage;
        Compare(
            result,
            "damage",
            projectedDamage,
            effectiveDamage,
            actualDamage,
            ExplainDamage(before, action, projected, effectiveDamage),
            damageTraceComplete);

        var actualBlock = comparisonEvents
            .Where(item =>
                item.TargetActorId == playerId
                && (item.Kind == CombatSimulationEventKind.BlockGained
                    || item.Kind == CombatSimulationEventKind.BlockChanged))
            .Sum(item => Math.Max(0, item.Amount));
        var effectiveBlock = effectiveProjection.IntrinsicDefend;
        var observedBlockGain = Math.Max(
            0,
            (after.Player?.Block ?? 0) - (before.Player?.Block ?? 0));
        var blockTraceComplete = observedBlockGain <= 0
                                 || actionEvents.Any(item =>
                                     item.Kind
                                     is CombatSimulationEventKind.BlockGained
                                     or CombatSimulationEventKind.BlockChanged);
        Compare(
            result,
            "defend",
            projected.Defend,
            effectiveBlock,
            actualBlock,
            Different(projected.Defend, effectiveBlock)
                ? "attribute-or-status-modified"
                : "",
            blockTraceComplete);
        if (Different(
                effectiveProjection.IntrinsicDefend,
                effectiveProjection.Defend))
        {
            AddExplained(
                result,
                "defend-net-value",
                "post-action-status-nullified");
        }

        var actualHeal = comparisonEvents
            .Where(item => item.TargetActorId == playerId
                           && (item.Kind == CombatSimulationEventKind.Healed
                               || IsHpAssignment(item)))
            .Sum(item => Math.Max(0, item.Amount));
        var effectiveHeal = effectiveProjection.Heal;
        var observedHeal = Math.Max(
            0,
            (after.Player?.Hp ?? 0) - (before.Player?.Hp ?? 0));
        var healTraceComplete = observedHeal <= 0
                                || actionEvents.Any(item =>
                                    item.Kind == CombatSimulationEventKind.Healed
                                    || IsHpAssignment(item));
        Compare(
            result,
            "heal",
            projected.Heal,
            effectiveHeal,
            actualHeal,
            Different(projected.Heal, effectiveHeal)
                ? "missing-hp-or-heal-modifier"
                : "",
            healTraceComplete);

        var actualSelfHpLoss = comparisonEvents
            .Where(item => item.TargetActorId == playerId)
            .Sum(item => item.Kind == CombatSimulationEventKind.DamageDealt
                ? Math.Max(0, item.Amount)
                : IsHpAssignment(item)
                    ? Math.Max(0, -HpAssignmentDelta(item))
                    : 0d);
        var projectedSelfHpLoss = !realizedProjection
                                  && (projected.DirectSelfHpLoss > 0d
                                      || projected.ContextSelfHpLoss > 0d)
            ? projected.DirectSelfHpLoss
            : projected.SelfHpLoss;
        Compare(
            result,
            "self-hp-loss",
            projectedSelfHpLoss,
            projectedSelfHpLoss,
            actualSelfHpLoss,
            "",
            true);

        var tracedHpDelta = actionEvents
            .Where(item => item.TargetActorId == playerId)
            .Sum(item => item.Kind == CombatSimulationEventKind.DamageDealt
                ? -Math.Max(0, item.Amount)
                : item.Kind == CombatSimulationEventKind.Healed
                  || IsHpAssignment(item)
                    ? HpAssignmentDelta(item)
                    : 0d);
        var observedHpDelta = (after.Player?.Hp ?? 0)
                              - (before.Player?.Hp ?? 0);
        if (Different(tracedHpDelta, observedHpDelta))
        {
            AddInvalid(
                result,
                "hp-conservation",
                "observed HP change is not fully represented by causal facts",
                tracedHpDelta,
                tracedHpDelta,
                observedHpDelta);
        }

        var projectedMaximumHp = projected.StateChanges.TryGetValue(
            "playerMaxHp",
            out var maximumHpProjection)
            ? maximumHpProjection
            : 0d;
        var actualMaximumHp = (after.Player?.MaxHp ?? 0)
                              - (before.Player?.MaxHp ?? 0);
        Compare(
            result,
            "maximum-hp",
            projectedMaximumHp,
            projectedMaximumHp,
            actualMaximumHp,
            "");

        var beforeCardIds = before.Cards
            .Select(item => item.InstanceId)
            .ToHashSet();
        var createdCardIds = after.Cards
            .Where(item => !beforeCardIds.Contains(item.InstanceId))
            .Select(item => item.InstanceId)
            .ToHashSet();
        var actualDraw = comparisonEvents.Count(item =>
            item.Kind == CombatSimulationEventKind.CardDrawn
            && !createdCardIds.Contains(item.CardInstanceId)
            && (item.TargetActorId == 0 || item.TargetActorId == playerId));
        var consumedCard = action.Kind == CombatSimulationActionKind.PlayCard
                           && before.Hand.Contains(action.CardInstanceId)
                           && !after.Hand.Contains(action.CardInstanceId)
            ? 1
            : 0;
        var observedDraw = Math.Max(
            0,
            after.Hand.Count - before.Hand.Count + consumedCard);
        var drawTraceComplete = observedDraw <= 0
                                || actionEvents.Any(item =>
                                    item.Kind
                                    == CombatSimulationEventKind.CardDrawn);
        Compare(
            result,
            "draw",
            projected.Draw,
            projected.Draw,
            actualDraw,
            projected.Draw > actualDraw ? "draw-cap-or-empty-pile" : "",
            drawTraceComplete);

        var actualEnergy = comparisonEvents
            .Where(item => item.Kind == CombatSimulationEventKind.EnergyChanged
                           && item.TargetActorId == playerId)
            .Sum(item => Math.Max(0, item.Amount));
        var observedEnergyGain = Math.Max(
            0,
            (after.Player?.Energy ?? 0)
            - (before.Player?.Energy ?? 0)
            + Math.Max(0, action.Cost));
        var energyTraceComplete = observedEnergyGain <= 0
                                  || actionEvents.Any(item =>
                                      item.Kind
                                      == CombatSimulationEventKind.EnergyChanged);
        Compare(
            result,
            "energy-gain",
            projected.EnergyGain,
            projected.EnergyGain,
            actualEnergy,
            "",
            energyTraceComplete);

        var actualGenerated = createdCardIds.Count;
        var generationTraceComplete = createdCardIds.Count == 0
                                      || createdCardIds.All(instanceId =>
                                          actionEvents.Any(item =>
                                              item.CardInstanceId == instanceId
                                              && item.Kind is
                                                  CombatSimulationEventKind.CardCreated
                                                  or CombatSimulationEventKind.CardDrawn
                                                  or CombatSimulationEventKind.CardDiscarded
                                                  or CombatSimulationEventKind.CardExhausted));
        Compare(
            result,
            "card-generation",
            projected.CardGeneration,
            projected.CardGeneration,
            actualGenerated,
            "",
            generationTraceComplete);

        var (actualBuff, actualDebuff, hasExactStatusProjection) =
            StatusDeltas(
                before,
                action,
                ruleset,
                comparisonEvents);
        if (!hasExactStatusProjection)
        {
            actualBuff = comparisonEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && item.TargetActorId == playerId)
                ? Math.Max(1d, projected.Buff)
                : 0d;
            actualDebuff = comparisonEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.StatusAdded
                && IsEnemy(before, item.TargetActorId))
                ? Math.Max(1d, projected.Debuff)
                : 0d;
        }
        Compare(
            result,
            "buff",
            projected.Buff,
            projected.Buff,
            actualBuff,
            projected.Buff > actualBuff ? "status-cap-or-no-op" : "");
        Compare(
            result,
            "debuff",
            projected.Debuff,
            projected.Debuff,
            actualDebuff,
            projected.Debuff > actualDebuff ? "status-cap-or-no-op" : "");

        if (comparisonEvents.Any(item =>
                item.Kind == CombatSimulationEventKind.ActorSummoned))
        {
            result.AuditedKinds.Add("summon");
            result.MismatchKinds.Add("summon-unrepresented");
            result.Comparisons.Add(new CombatSemanticAuditComparison
            {
                Kind = "summon",
                Actual = 1d,
                Classification = "unexplained",
                Explanation = "summon semantics are not represented"
            });
        }
        var actualRandom = comparisonEvents.Any(item =>
            item.Kind == CombatSimulationEventKind.RandomResolved
            || item.Kind == CombatSimulationEventKind.DiceChecked);
        if (projected.RandomOutcome || actualRandom)
        {
            result.AuditedKinds.Add("random-outcome");
            if (projected.RandomOutcome != actualRandom)
            {
                result.MismatchKinds.Add("random-outcome");
                result.Comparisons.Add(new CombatSemanticAuditComparison
                {
                    Kind = "random-outcome",
                    Projected = projected.RandomOutcome ? 1d : 0d,
                    EffectiveProjected = projected.RandomOutcome ? 1d : 0d,
                    Actual = actualRandom ? 1d : 0d,
                    Classification = "unexplained",
                    Explanation = "intrinsic random event presence differs"
                });
            }
        }

        foreach (var kind in ContextualKinds(before, contextualEvents))
        {
            AddExplained(result, kind, "trigger-side-effect");
        }
        return result;
    }

    private static IReadOnlyList<CombatSimulationEvent> ScopeActionEvents(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events)
    {
        var sourceActionId = Math.Max(1L, before.ActionSequence + 1L);
        var attributed = events
            .Where(item => item.SourceActionId == sourceActionId)
            .ToList();
        return attributed.Count > 0
            ? attributed
            : events.Where(item => item.SourceActionId == 0).ToList();
    }

    private static bool IsIntrinsic(
        CombatSimulationEvent item,
        CombatSimulationAction action)
    {
        if (item.Kind == CombatSimulationEventKind.RandomResolved
            && item.CardInstanceId == 0
            && string.IsNullOrWhiteSpace(item.DefinitionId)
            && string.IsNullOrWhiteSpace(item.HandlerId)
            && string.IsNullOrWhiteSpace(item.SourceRewardId))
        {
            // Unscoped random draws are environmental evidence. Treating a
            // blank trace as intrinsic made unrelated buffs and blessings
            // appear to be randomness owned by the selected card.
            return false;
        }
        var actionReward = !string.IsNullOrWhiteSpace(item.SourceRewardId)
                           && string.Equals(
                               item.SourceRewardId,
                               action.DefinitionId,
                               StringComparison.OrdinalIgnoreCase);
        if ((!string.IsNullOrWhiteSpace(item.HandlerId)
             || !string.IsNullOrWhiteSpace(item.SourceRewardId))
            && !actionReward)
        {
            return false;
        }
        return item.CardInstanceId == 0
               || action.CardInstanceId == 0
               || item.CardInstanceId == action.CardInstanceId;
    }

    private static bool IsSemanticTraceEvidence(CombatSimulationEvent item)
    {
        return item.Kind is not CombatSimulationEventKind.BattleStarted
            and not CombatSimulationEventKind.BattleEnded
            and not CombatSimulationEventKind.TurnStarted
            and not CombatSimulationEventKind.TurnEnded;
    }

    private static bool IsHpAssignment(CombatSimulationEvent item)
    {
        return item.Kind == CombatSimulationEventKind.VariableChanged
               && (string.Equals(
                       item.DefinitionId,
                       "Hp",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       item.Message,
                       "Hp",
                       StringComparison.OrdinalIgnoreCase));
    }

    private static double HpAssignmentDelta(CombatSimulationEvent item)
    {
        return item.CurrentAmount != 0 || item.PreviousAmount != 0
            ? item.CurrentAmount - item.PreviousAmount
            : item.Amount;
    }

    private static double EffectiveDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected,
        CombatRuleset? ruleset)
    {
        if (projected.Damage <= 0d && projected.TrueDamage <= 0d)
        {
            return 0d;
        }
        var source = before.FindActor(action.ActorId) ?? before.Player;
        var target = before.FindActor(action.TargetActorId);
        if (target == null)
        {
            return Math.Max(0d, projected.Damage + projected.TrueDamage);
        }
        var hp = Math.Max(0, target.Hp);
        var normalResolution = ruleset == null
            ? null
            : CombatDamageResolver.Resolve(
                source,
                target,
                ruleset,
                CombatSimulationEffectKind.Damage,
                WitchRounded(projected.Damage));
        var normal = normalResolution?.IncomingAmount
                     ?? ModifiedDamage(
                         source,
                         target,
                         projected.Damage,
                         applyStrength:
                             source?.Kind
                             == CombatSimulationActorKind.Player,
                         damageType: "Normal");
        var blocked = normalResolution?.BlockedAmount
                      ?? Math.Min(Math.Max(0, target.Block), normal);
        var hpDamage = normalResolution?.HpDamage
                       ?? Math.Min(hp, Math.Max(0, normal - blocked));
        hp -= hpDamage;
        var trueResolution = ruleset == null
            ? null
            : CombatDamageResolver.Resolve(
                source,
                target,
                ruleset,
                CombatSimulationEffectKind.TrueDamage,
                WitchRounded(projected.TrueDamage));
        var trueDamage = trueResolution?.HpDamage
                         ?? ModifiedDamage(
                             source,
                             target,
                             projected.TrueDamage,
                             applyStrength: false,
                             damageType: "True");
        var totalHpDamage =
            hpDamage + Math.Min(hp, Math.Max(0, trueDamage));
        return ApplyHpLossLimit(target, totalHpDamage);
    }

    private static double EffectiveDurabilityDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected,
        CombatRuleset? ruleset)
    {
        if (projected.Damage <= 0d && projected.TrueDamage <= 0d)
        {
            return 0d;
        }
        var source = before.FindActor(action.ActorId) ?? before.Player;
        var target = before.FindActor(action.TargetActorId);
        if (target == null)
        {
            return Math.Max(0d, projected.Damage + projected.TrueDamage);
        }
        var durability = Math.Max(0, target.Hp) + Math.Max(0, target.Block);
        var normalResolution = ruleset == null
            ? null
            : CombatDamageResolver.Resolve(
                source,
                target,
                ruleset,
                CombatSimulationEffectKind.Damage,
                WitchRounded(projected.Damage));
        var normal = normalResolution?.IncomingAmount
                     ?? ModifiedDamage(
                         source,
                         target,
                         projected.Damage,
                         applyStrength:
                             source?.Kind
                             == CombatSimulationActorKind.Player,
                         damageType: "Normal");
        var blockDamage = normalResolution?.BlockedAmount
                          ?? Math.Min(
                              Math.Max(0, target.Block),
                              Math.Max(0, normal));
        var normalHpDamage = normalResolution?.HpDamage
                             ?? Math.Min(
                                 Math.Max(0, target.Hp),
                                 Math.Max(0, normal - blockDamage));
        var hpAfterNormal = Math.Max(
            0,
            target.Hp - normalHpDamage);
        var trueResolution = ruleset == null
            ? null
            : CombatDamageResolver.Resolve(
                source,
                target,
                ruleset,
                CombatSimulationEffectKind.TrueDamage,
                WitchRounded(projected.TrueDamage));
        var trueDamage = trueResolution?.HpDamage
                         ?? ModifiedDamage(
                             source,
                             target,
                             projected.TrueDamage,
                             applyStrength: false,
                             damageType: "True");
        var hpDamage = normalHpDamage
                       + Math.Min(
                           hpAfterNormal,
                           Math.Max(0, trueDamage));
        return Math.Min(
            durability,
            blockDamage + ApplyHpLossLimit(target, hpDamage));
    }

    private static int ModifiedDamage(
        CombatActorState? source,
        CombatActorState target,
        double amount,
        bool applyStrength,
        string damageType)
    {
        var projected = string.Equals(
                damageType,
                "True",
                StringComparison.OrdinalIgnoreCase)
            ? CombatDynamicDamageProjection.ResolveTrue(
                amount,
                Variable(source, CombatDynamicDamageProjection.TruePercentDamage, 1d))
            : CombatDynamicDamageProjection.ResolveNormal(
                amount,
                Variable(source, CombatDynamicDamageProjection.PercentDamage, 1d),
                Variable(source, CombatDynamicDamageProjection.DefaultDamage, 0d),
                Variable(source, CombatDynamicDamageProjection.Strength, 0d),
                Variable(target, CombatDynamicDamageProjection.AttackedPercentDamage, 1d),
                Variable(target, CombatDynamicDamageProjection.AttackedDefaultDamage, 0d),
                applyStrength);
        var typedMultiplier = Math.Max(
            0d,
            Variable(
                target,
                "DamageTakenMultiplier." + damageType,
                1d));
        var filterReduction = Math.Max(
            0d,
            Variable(
                target,
                "DamageFilter." + damageType,
                0d));
        return Math.Max(
            0,
            (int)(projected
                  * typedMultiplier
                  * Math.Max(0d, 1d - filterReduction / 100d)));
    }

    private static double EffectiveBlock(
        CombatActorState? player,
        double amount)
    {
        if (player == null || amount <= 0d)
        {
            return Math.Max(0d, amount);
        }
        return Math.Max(
            0,
            WitchRounded(
                amount
                * Variable(player, "DefendPercent", 1d)
                * (1d + Math.Max(0d, Variable(player, "Perceive", 0d)) * 0.04d)));
    }

    private static double NetEffectiveBlock(
        CombatActorState? player,
        double amount)
    {
        var intrinsic = EffectiveBlock(player, amount);
        return StatusStacks(player, "buff_rotten") > 0
            ? 0d
            : intrinsic;
    }

    private static double ApplyHpLossLimit(
        CombatActorState target,
        double requested)
    {
        requested = Math.Max(0d, requested);
        if (!target.Variables.TryGetValue(
                "MaxChangeHp",
                out var maximumChangeRatio))
        {
            return requested;
        }
        var maximumLoss = Math.Floor(
            target.MaxHp
            * Math.Max(0d, Math.Min(1d, maximumChangeRatio)));
        var alreadyLost = Math.Max(
            0d,
            Variable(target, "HpLossThisAction", 0d));
        return Math.Min(
            requested,
            Math.Max(0d, maximumLoss - alreadyLost));
    }

    private static double EffectiveHeal(
        CombatActorState? player,
        double amount)
    {
        if (player == null || amount <= 0d)
        {
            return Math.Max(0d, amount);
        }
        var modified = Math.Max(
            0,
            (int)Math.Round(
                amount * Variable(player, "HealMultiplier", 1d)));
        return Math.Min(modified, Math.Max(0, player.MaxHp - player.Hp));
    }

    private static (
        double Buff,
        double Debuff,
        bool Exact) StatusDeltas(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatRuleset? ruleset,
        IReadOnlyList<CombatSimulationEvent> intrinsicEvents)
    {
        if (ruleset == null
            || !ruleset.TryGetCard(action.DefinitionId, out var card))
        {
            return (0d, 0d, false);
        }
        var buff = 0d;
        var debuff = 0d;
        var exact = false;
        var targets = card.Effects
            .Where(item => item.Kind == CombatSimulationEffectKind.AddStatus)
            .SelectMany(effect => TargetActorIds(before, action, effect.Target)
                .Select(targetId => new
                {
                    TargetId = targetId,
                    StatusId = effect.DefinitionId
                }))
            .Concat(intrinsicEvents
                .Where(item => item.Kind == CombatSimulationEventKind.StatusAdded)
                .Select(item => new
                {
                    TargetId = item.TargetActorId,
                    StatusId = item.DefinitionId
                }))
            .Distinct()
            .ToList();
        foreach (var target in targets)
        {
            exact = true;
            var current = StatusStacks(
                before.FindActor(target.TargetId),
                target.StatusId);
            var maximum = ruleset.TryGetStatus(
                target.StatusId,
                out var statusDefinition)
                ? Math.Max(1, statusDefinition.MaximumStacks)
                : int.MaxValue;
            var delta = 0;
            foreach (var item in intrinsicEvents.Where(item =>
                         item.Kind == CombatSimulationEventKind.StatusAdded
                         && item.TargetActorId == target.TargetId
                         && string.Equals(
                             item.DefinitionId,
                             target.StatusId,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var next = Math.Min(
                    maximum,
                    current + Math.Max(1, item.Amount));
                delta += Math.Max(0, next - current);
                current = next;
            }
            if (before.FindActor(target.TargetId)?.Kind
                == CombatSimulationActorKind.Enemy)
            {
                debuff += delta;
            }
            else
            {
                buff += delta;
            }
        }
        return (buff, debuff, exact);
    }

    private static IEnumerable<int> TargetActorIds(
        CombatBattleState state,
        CombatSimulationAction action,
        CombatSimulationTarget target)
    {
        return target switch
        {
            CombatSimulationTarget.Self
                or CombatSimulationTarget.Player =>
                new[] { state.PlayerActorId },
            CombatSimulationTarget.AllEnemies =>
                state.LivingEnemies.Select(item => item.ActorId),
            CombatSimulationTarget.AllAllies =>
                state.Actors
                    .Where(item => item.Alive
                                   && item.Kind
                                   != CombatSimulationActorKind.Enemy)
                    .Select(item => item.ActorId),
            _ => new[] { action.TargetActorId }
        };
    }

    private static int StatusStacks(
        CombatActorState? actor,
        string statusId)
    {
        return actor?.Statuses.FirstOrDefault(item => string.Equals(
            item.StatusId,
            statusId,
            StringComparison.OrdinalIgnoreCase))?.Stacks ?? 0;
    }

    private static IEnumerable<string> ContextualKinds(
        CombatBattleState before,
        IReadOnlyList<CombatSimulationEvent> events)
    {
        var playerId = before.PlayerActorId;
        if (events.Any(item => item.Kind == CombatSimulationEventKind.DamageDealt
                              && IsEnemy(before, item.TargetActorId)))
        {
            yield return "damage";
        }
        if (events.Any(item =>
                item.TargetActorId == playerId
                && (item.Kind == CombatSimulationEventKind.BlockGained
                    || item.Kind == CombatSimulationEventKind.BlockChanged)))
        {
            yield return "defend";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.Healed
                              && item.TargetActorId == playerId))
        {
            yield return "heal";
        }
        if (events.Any(item => item.TargetActorId == playerId
                              && (item.Kind == CombatSimulationEventKind.DamageDealt
                                  || (IsHpAssignment(item)
                                      && HpAssignmentDelta(item) < 0))))
        {
            yield return "self-hp-loss";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.Healed
                              && IsEnemy(before, item.TargetActorId)))
        {
            yield return "enemy-heal";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.StatusAdded
                              && item.TargetActorId == playerId))
        {
            yield return "buff";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.StatusAdded
                              && IsEnemy(before, item.TargetActorId)))
        {
            yield return "debuff";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.CardDrawn))
        {
            yield return "draw";
        }
        if (events.Any(item => item.Kind is CombatSimulationEventKind.CardCreated
            or CombatSimulationEventKind.CardDiscarded
            or CombatSimulationEventKind.CardExhausted
            or CombatSimulationEventKind.CardCostChanged
            or CombatSimulationEventKind.CardTagChanged))
        {
            yield return "card-lifecycle";
        }
        if (events.Any(item => item.Kind is CombatSimulationEventKind.StatusRemoved
            or CombatSimulationEventKind.StatusStacksChanged))
        {
            yield return "status-lifecycle";
        }
        if (events.Any(item => item.Kind is CombatSimulationEventKind.EnergyChanged
            or CombatSimulationEventKind.MaximumEnergyChanged))
        {
            yield return "energy";
        }
        if (events.Any(item => item.Kind is CombatSimulationEventKind.MaximumHpChanged
            or CombatSimulationEventKind.VariableChanged))
        {
            yield return "state-change";
        }
        if (events.Any(item => item.Kind is CombatSimulationEventKind.ActorDefeated
            or CombatSimulationEventKind.ActorResurrected
            or CombatSimulationEventKind.ActorSummoned))
        {
            yield return "actor-lifecycle";
        }
        if (events.Any(item => item.Kind == CombatSimulationEventKind.TurnFlowChanged))
        {
            yield return "turn-flow";
        }
    }

    private static string ExplainDamage(
        CombatBattleState before,
        CombatSimulationAction action,
        CombatActionSemantics projected,
        double effective)
    {
        var raw = projected.Damage + projected.TrueDamage;
        if (!Different(raw, effective))
        {
            return "";
        }
        var target = before.FindActor(action.TargetActorId);
        if (target?.Block > 0 && projected.Damage > 0d)
        {
            return "absorbed-by-block";
        }
        if (target != null && target.Hp < raw)
        {
            return "overkill-clamped";
        }
        return "attribute-or-status-modified";
    }

    private static void Compare(
        CombatSemanticAuditResult result,
        string kind,
        double projected,
        double effectiveProjected,
        double actual,
        string explanation,
        bool traceComplete = true)
    {
        if (Math.Abs(projected) <= 0.000001d
            && Math.Abs(effectiveProjected) <= 0.000001d
            && Math.Abs(actual) <= 0.000001d)
        {
            return;
        }
        if (!traceComplete)
        {
            AddInvalid(
                result,
                kind,
                "state transition occurred without an attributed event",
                projected,
                effectiveProjected,
                actual);
            return;
        }
        result.AuditedKinds.Add(kind);
        if (Different(projected, effectiveProjected))
        {
            AddExplained(
                result,
                kind,
                string.IsNullOrWhiteSpace(explanation)
                    ? "context-adjusted"
                    : explanation);
        }
        var tolerance = Math.Max(
            2d,
            Math.Max(
                Math.Abs(effectiveProjected),
                Math.Abs(actual)) * 0.25d);
        if (Math.Abs(effectiveProjected - actual) <= tolerance)
        {
            result.Comparisons.Add(new CombatSemanticAuditComparison
            {
                Kind = kind,
                Projected = projected,
                EffectiveProjected = effectiveProjected,
                Actual = actual,
                Classification = Different(projected, effectiveProjected)
                    ? "explained"
                    : "matched",
                Explanation = explanation
            });
            return;
        }
        result.MismatchKinds.Add(kind);
        result.Comparisons.Add(new CombatSemanticAuditComparison
        {
            Kind = kind,
            Projected = projected,
            EffectiveProjected = effectiveProjected,
            Actual = actual,
            Classification = "unexplained",
            Explanation = explanation
        });
    }

    private static void AddInvalid(
        CombatSemanticAuditResult result,
        string kind,
        string explanation,
        double projected = 0d,
        double effectiveProjected = 0d,
        double actual = 0d)
    {
        if (!result.InvalidKinds.Contains(
                kind,
                StringComparer.OrdinalIgnoreCase))
        {
            result.InvalidKinds.Add(kind);
        }
        if (result.Comparisons.Any(item =>
                string.Equals(
                    item.Kind,
                    kind,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Classification,
                    "invalid",
                    StringComparison.Ordinal)))
        {
            return;
        }
        result.Comparisons.Add(new CombatSemanticAuditComparison
        {
            Kind = kind,
            Projected = projected,
            EffectiveProjected = effectiveProjected,
            Actual = actual,
            Classification = "invalid",
            Explanation = explanation
        });
    }

    private static void AddExplained(
        CombatSemanticAuditResult result,
        string kind,
        string explanation)
    {
        if (!result.ExplainedKinds.Contains(
                kind,
                StringComparer.OrdinalIgnoreCase))
        {
            result.ExplainedKinds.Add(kind);
        }
        if (result.Comparisons.Any(item =>
                string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    item.Classification,
                    "explained",
                    StringComparison.Ordinal)))
        {
            return;
        }
        result.Comparisons.Add(new CombatSemanticAuditComparison
        {
            Kind = kind,
            Classification = "explained",
            Explanation = explanation
        });
    }

    private static bool Different(double left, double right)
    {
        return Math.Abs(left - right) > 0.000001d;
    }

    private static bool IsEnemy(CombatBattleState state, int actorId)
    {
        return state.FindActor(actorId)?.Kind
               == CombatSimulationActorKind.Enemy;
    }

    private static double Variable(
        CombatActorState? actor,
        string key,
        double fallback)
    {
        return actor?.Variables.TryGetValue(key, out var value) == true
            ? value
            : fallback;
    }

    private static double Probability(
        CombatTargetedSemanticEffect effect)
    {
        return Math.Max(0d, Math.Min(1d, effect.Probability));
    }

    private static int WitchRounded(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }
        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }
        var ceiling = Math.Ceiling(value);
        return (int)(ceiling - value <= 0.01d
            ? ceiling
            : Math.Floor(value));
    }
}
