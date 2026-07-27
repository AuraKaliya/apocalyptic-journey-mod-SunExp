using System;
using System.Collections.Generic;
using System.Linq;
using AuraDecision.Shared;

namespace AuraCombatAi.Shared;

public sealed class CombatPlanResult
{
    public bool HasAction { get; set; }

    public CombatActionObservation? Action { get; set; }

    public double Score { get; set; }

    public List<CombatPlanStep> Steps { get; set; } = new();

    public string Summary { get; set; } = "";
}

public sealed class CombatTurnPlanner
{
    private readonly IDecisionResidualModel residualModel;

    public CombatTurnPlanner(IDecisionResidualModel? residualModel = null)
    {
        this.residualModel = residualModel ?? NullDecisionResidualModel.Instance;
    }

    public CombatPlanResult Choose(
        CombatStateObservation state,
        IReadOnlyList<CombatCandidateEvaluation> candidates,
        CombatDecisionProfile profile)
    {
        var legal = candidates
            .Where(candidate => candidate.Legal
                                && candidate.Action != null
                                && candidate.Action.Kind != CombatActionKind.EndTurn)
            .ToList();
        if (legal.Count == 0)
        {
            return new CombatPlanResult { Summary = "no legal plan action" };
        }

        var width = Math.Max(1, Math.Min(24, profile.BeamWidth));
        var depth = Math.Max(1, Math.Min(12, profile.MaxPlanDepth));
        var root = new PlanNode
        {
            State = CloneState(state)
        };
        var beam = new List<PlanNode> { root };
        var completed = new List<PlanNode> { root };

        for (var stepIndex = 0; stepIndex < depth; stepIndex++)
        {
            var expanded = new List<PlanNode>();
            for (var nodeIndex = 0; nodeIndex < beam.Count; nodeIndex++)
            {
                var node = beam[nodeIndex];
                for (var candidateIndex = 0; candidateIndex < legal.Count; candidateIndex++)
                {
                    var candidate = legal[candidateIndex];
                    var action = candidate.Action;
                    if (WasUsed(node, action)
                        || !TargetAlive(node.State, action.TargetRuntimeId))
                    {
                        continue;
                    }

                    var effectiveCost = Math.Max(0, action.Cost - node.CostReduction);
                    if (effectiveCost > node.State.CurrentPower)
                    {
                        continue;
                    }

                    var simulatedAction = CloneAction(action, effectiveCost);
                    var stepScore = Score(node.State, simulatedAction, profile);
                    stepScore += OrderingBonus(node, simulatedAction);
                    var child = CloneNode(node);
                    child.State.CurrentPower -= effectiveCost;
                    child.State.HandCount = Math.Max(
                        0,
                        child.State.HandCount - (action.Kind == CombatActionKind.PlayCard ? 1 : 0));
                    child.CostReduction = Math.Max(
                        0,
                        child.CostReduction - (action.Kind == CombatActionKind.PlayCard ? effectiveCost : 0));
                    child.CostReduction += Math.Max(0, (int)Math.Round(action.Semantics.CostReduction));
                    child.SetupMagnitude += Math.Max(0d, action.Semantics.Buff)
                                            + Math.Max(0d, action.Semantics.Debuff)
                                            + Math.Max(0d, action.Semantics.Scaling);
                    MarkUsed(child, action);
                    Apply(child.State, simulatedAction);
                    var discounted = stepScore * Math.Pow(0.985d, stepIndex);
                    child.Score += discounted;
                    child.Steps.Add(new CombatPlanStep
                    {
                        CandidateId = action.CandidateId,
                        SourceId = action.SourceId,
                        DisplayName = action.DisplayName,
                        StepScore = stepScore,
                        CumulativeScore = child.Score,
                        RemainingPower = child.State.CurrentPower
                    });
                    expanded.Add(child);
                }
            }

            if (expanded.Count == 0)
            {
                break;
            }

            beam = expanded
                .OrderByDescending(node => node.Score)
                .ThenByDescending(FirstActionOrder)
                .Take(width)
                .ToList();
            completed.AddRange(beam);
        }

        var best = completed
            .Where(node => node.Steps.Count > 0)
            .OrderByDescending(node => node.Score)
            .ThenByDescending(FirstActionOrder)
            .FirstOrDefault();
        if (best == null)
        {
            return new CombatPlanResult { Summary = "no expandable plan" };
        }

        var first = legal.First(candidate =>
            string.Equals(candidate.Action.CandidateId, best.Steps[0].CandidateId, StringComparison.Ordinal));
        foreach (var candidate in candidates)
        {
            var matchingPlan = completed
                .Where(node => node.Steps.Count > 0
                               && string.Equals(
                                   node.Steps[0].CandidateId,
                                   candidate.Action.CandidateId,
                                   StringComparison.Ordinal))
                .OrderByDescending(node => node.Score)
                .FirstOrDefault();
            candidate.PlanScore = matchingPlan?.Score ?? 0d;
        }

        return new CombatPlanResult
        {
            HasAction = true,
            Action = first.Action,
            Score = best.Score,
            Steps = best.Steps,
            Summary = BuildSummary(state, best)
        };
    }

    private double Score(
        CombatStateObservation state,
        CombatActionObservation action,
        CombatDecisionProfile profile)
    {
        var utility = CombatDecisionEngine.BuildUtility(state, action, profile);
        var features = CombatDecisionEngine.BuildFeatures(state, action, utility, profile);
        var graph = DecisionGraphEvaluator.Evaluate(profile.Graph, features);
        if (graph.Rejected)
        {
            return -1000d;
        }

        utility.Add(graph.UtilityDelta);
        var residual = CombatDecisionEngine.EvaluateResidual(residualModel, features);
        return profile.Weights.Score(utility) + residual.AppliedCorrection;
    }

    private static double OrderingBonus(PlanNode node, CombatActionObservation action)
    {
        var semantics = action.Semantics;
        var bonus = 0d;
        var damage = Math.Max(0d, semantics.Damage)
                     + Math.Max(0d, semantics.TrueDamage)
                     + Math.Max(0d, semantics.DamageOverTime);
        if (damage > 0d && node.SetupMagnitude > 0d)
        {
            bonus += Math.Min(4d, node.SetupMagnitude * Math.Min(20d, damage) * 0.025d);
        }
        if (action.Cost == 0
            && (semantics.Draw > 0d
                || semantics.EnergyGain > 0d
                || semantics.Buff > 0d
                || semantics.Debuff > 0d
                || semantics.CostReduction > 0d))
        {
            bonus += 0.2d;
        }

        return bonus;
    }

    private static void Apply(CombatStateObservation state, CombatActionObservation action)
    {
        var semantics = action.Semantics;
        state.Player.Defend += Math.Max(0, (int)Math.Round(semantics.Defend));
        state.Player.CurrentHp = Math.Min(
            state.Player.MaxHp,
            state.Player.CurrentHp + Math.Max(0, (int)Math.Round(semantics.Heal)));
        var energyGain = Math.Max(0, (int)Math.Round(semantics.EnergyGain));
        var energyCap = state.MaxPower > 0
            ? Math.Max(state.MaxPower, state.CurrentPower)
            : state.CurrentPower + energyGain;
        state.CurrentPower = Math.Min(energyCap, state.CurrentPower + energyGain);
        if (action.TargetRuntimeId != 0)
        {
            var target = state.Enemies.FirstOrDefault(enemy => enemy.RuntimeId == action.TargetRuntimeId);
            if (target != null)
            {
                ApplyDamage(target, semantics);
            }
        }
        else if (semantics.Damage > 0d || semantics.TrueDamage > 0d || semantics.DamageOverTime > 0d)
        {
            for (var i = 0; i < state.Enemies.Count; i++)
            {
                ApplyDamage(state.Enemies[i], semantics);
            }
        }
    }

    private static void ApplyDamage(CombatUnitObservation target, CombatActionSemantics semantics)
    {
        var normal = Math.Max(0, (int)Math.Round(semantics.Damage * Math.Max(1d, semantics.HitCount)));
        var absorbed = Math.Min(target.Defend, normal);
        target.Defend -= absorbed;
        target.CurrentHp = Math.Max(
            0,
            target.CurrentHp
            - Math.Max(0, normal - absorbed)
            - Math.Max(0, (int)Math.Round(semantics.TrueDamage + semantics.DamageOverTime)));
    }

    private static bool TargetAlive(CombatStateObservation state, int runtimeId)
    {
        return runtimeId == 0
               || state.Enemies.Any(enemy => enemy.RuntimeId == runtimeId && enemy.CurrentHp > 0)
               || state.Player.RuntimeId == runtimeId;
    }

    private static bool WasUsed(PlanNode node, CombatActionObservation action)
    {
        return action.RuntimeId != 0
            ? node.UsedRuntimeIds.Contains(action.RuntimeId)
            : node.UsedCandidateIds.Contains(action.CandidateId);
    }

    private static void MarkUsed(PlanNode node, CombatActionObservation action)
    {
        if (action.RuntimeId != 0)
        {
            node.UsedRuntimeIds.Add(action.RuntimeId);
        }
        else
        {
            node.UsedCandidateIds.Add(action.CandidateId);
        }
    }

    private static int FirstActionOrder(PlanNode node)
    {
        if (node.Steps.Count == 0)
        {
            return 0;
        }

        return node.Steps[0].RemainingPower;
    }

    private static string BuildSummary(CombatStateObservation state, PlanNode node)
    {
        var threat = state.Threat ?? new CombatThreatForecast();
        return "threat(blockable="
               + threat.ExpectedBlockableDamage.ToString("0.0")
               + ", unblockable="
               + threat.ExpectedUnblockableDamage.ToString("0.0")
               + ", dot="
               + threat.ExpectedDamageOverTime.ToString("0.0")
               + ", known="
               + threat.CurrentIntentKnown
               + "); plan="
               + string.Join(" -> ", node.Steps.Select(step => step.DisplayName))
               + "; score="
               + node.Score.ToString("0.00");
    }

    private static PlanNode CloneNode(PlanNode source)
    {
        return new PlanNode
        {
            State = CloneState(source.State),
            UsedRuntimeIds = new HashSet<int>(source.UsedRuntimeIds),
            UsedCandidateIds = new HashSet<string>(source.UsedCandidateIds, StringComparer.Ordinal),
            Steps = new List<CombatPlanStep>(source.Steps),
            Score = source.Score,
            CostReduction = source.CostReduction,
            SetupMagnitude = source.SetupMagnitude
        };
    }

    private static CombatStateObservation CloneState(CombatStateObservation source)
    {
        return new CombatStateObservation
        {
            InformationBoundaryVersion = source.InformationBoundaryVersion,
            ObservationId = source.ObservationId,
            BattleSessionId = source.BattleSessionId,
            Sequence = source.Sequence,
            Player = CloneUnit(source.Player),
            Friendlies = source.Friendlies.Select(CloneUnit).ToList(),
            Enemies = source.Enemies.Select(CloneUnit).ToList(),
            CurrentPower = source.CurrentPower,
            MaxPower = source.MaxPower,
            HandCount = source.HandCount,
            HandCardIds = new List<string>(source.HandCardIds),
            RetainedHandCardIds = new List<string>(source.RetainedHandCardIds),
            DeckCardIds = new List<string>(source.DeckCardIds),
            DiscardPileCardIds = new List<string>(source.DiscardPileCardIds),
            ExhaustPileCardIds = new List<string>(source.ExhaustPileCardIds),
            DeckKnowledge = source.DeckKnowledge,
            ExpectedIncomingDamage = source.ExpectedIncomingDamage,
            Threat = source.Threat,
            Features = new Dictionary<string, double>(source.Features, StringComparer.OrdinalIgnoreCase),
            IsPlayerActionWindow = source.IsPlayerActionWindow,
            UiBusy = source.UiBusy,
            Fingerprint = source.Fingerprint
        };
    }

    private static CombatUnitObservation CloneUnit(CombatUnitObservation source)
    {
        return new CombatUnitObservation
        {
            RuntimeId = source.RuntimeId,
            Name = source.Name,
            Kind = source.Kind,
            CurrentHp = source.CurrentHp,
            MaxHp = source.MaxHp,
            Defend = source.Defend,
            Attack = source.Attack,
            Features = new Dictionary<string, double>(source.Features, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static CombatActionObservation CloneAction(CombatActionObservation source, int cost)
    {
        return new CombatActionObservation
        {
            ObservationId = source.ObservationId,
            ActionToken = source.ActionToken,
            CandidateId = source.CandidateId,
            SourceId = source.SourceId,
            DisplayName = source.DisplayName,
            Kind = source.Kind,
            RuntimeId = source.RuntimeId,
            TargetRuntimeId = source.TargetRuntimeId,
            TargetKind = source.TargetKind,
            Cost = cost,
            Legal = source.Legal,
            RejectionReason = source.RejectionReason,
            Semantics = source.Semantics,
            Features = new Dictionary<string, double>(source.Features, StringComparer.OrdinalIgnoreCase)
        };
    }

    private sealed class PlanNode
    {
        public CombatStateObservation State { get; set; } = new();

        public HashSet<int> UsedRuntimeIds { get; set; } = new();

        public HashSet<string> UsedCandidateIds { get; set; } = new(StringComparer.Ordinal);

        public List<CombatPlanStep> Steps { get; set; } = new();

        public double Score { get; set; }

        public int CostReduction { get; set; }

        public double SetupMagnitude { get; set; }
    }
}
