using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

sealed class SmokePolicy : ICombatSimulationPolicy
{
    public string PolicyId => "native-reward-smoke";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        return context.LegalActions.FirstOrDefault(item =>
                   item.Kind == CombatSimulationActionKind.PlayCard)
               ?? context.LegalActions.FirstOrDefault(item =>
                   item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class EndFirstTurnThenPlayPolicy : ICombatSimulationPolicy
{
    public string PolicyId => "end-first-turn-then-play";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (context.State.Turn <= 1)
        {
            return context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.EndTurn);
        }
        return context.LegalActions.FirstOrDefault(item =>
                   item.Kind == CombatSimulationActionKind.PlayCard)
               ?? context.LegalActions.FirstOrDefault(item =>
                   item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class EndTurnPolicy : ICombatSimulationPolicy
{
    public string PolicyId => "end-turn";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class NativePoolTestContext :
    ICombatSimulationRuntimeContext,
    ICombatPersistentProgressionContext
{
    public NativePoolTestContext(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        Scenario = scenario;
        Ruleset = ruleset;
    }

    public CombatScenarioDefinition Scenario { get; }

    public CombatRuleset Ruleset { get; }

    public CombatBattleState State { get; } = new();

    public Dictionary<string, int> PersistentVariableDeltas { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<(CombatSimulationEffectDefinition Effect,
        CombatSimulationEvent? SourceEvent)> AppliedEffects { get; } = new();

    public int RandomValue { get; set; }

    public void ApplyEffects(
        IEnumerable<CombatSimulationEffectDefinition> effects,
        int sourceActorId,
        int selectedTargetId,
        CombatSimulationEvent? sourceEvent = null)
    {
        AppliedEffects.AddRange(effects.Select(effect =>
            (effect, sourceEvent?.Clone())));
    }

    public int NextRandomInt(string streamId, int exclusiveMaximum)
    {
        return Math.Max(0, Math.Min(exclusiveMaximum - 1, RandomValue));
    }

    public void AddUnsupported(string definitionId)
    {
    }

    public void RecordRewardMutation(
        string operation,
        string kind,
        string rewardId)
    {
    }

    public void RecordPersistentVariableDelta(string variableId, int amount)
    {
        PersistentVariableDeltas[variableId] =
            PersistentVariableDeltas.TryGetValue(variableId, out var current)
                ? current + amount
                : amount;
    }

    public void Terminate(
        CombatSimulationOutcome outcome,
        CombatTerminationReason reason)
    {
    }
}
