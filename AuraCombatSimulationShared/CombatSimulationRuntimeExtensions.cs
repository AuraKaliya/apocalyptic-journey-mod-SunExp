using System;
using System.Collections.Generic;

namespace AuraCombatSimulation.Shared;

/// <summary>
/// Optional, consumer-owned combat semantics that execute through the shared
/// deterministic engine without introducing a dependency from Aura.Shared to
/// a content or tool mod.
/// </summary>
public interface ICombatSimulationRuntimeExtensionFactory
{
    ICombatSimulationRuntimeExtension? Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset);
}

public interface ICombatSimulationRuntimeExtension
{
    void Initialize(ICombatSimulationRuntimeContext context);

    void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent);

    void Complete(ICombatSimulationRuntimeContext context);
}

public interface ICombatSimulationDecisionRuntimeExtension
{
    void BeforePolicyDecision(ICombatSimulationRuntimeContext context);
}

public interface ICombatSimulationRuntimeContext
{
    CombatScenarioDefinition Scenario { get; }

    CombatRuleset Ruleset { get; }

    CombatBattleState State { get; }

    void ApplyEffects(
        IEnumerable<CombatSimulationEffectDefinition> effects,
        int sourceActorId,
        int selectedTargetId,
        CombatSimulationEvent? sourceEvent = null);

    int NextRandomInt(string streamId, int exclusiveMaximum);

    void AddUnsupported(string definitionId);

    void RecordRewardMutation(
        string operation,
        string kind,
        string rewardId);

    void Terminate(
        CombatSimulationOutcome outcome,
        CombatTerminationReason reason);
}

/// <summary>
/// Optional campaign-progression sink exposed by the shared simulation engine.
/// Runtime extensions use it to report authoritative player deltas without
/// teaching the shared engine any content-specific meaning.
/// </summary>
public interface ICombatPersistentProgressionContext
{
    void RecordPersistentVariableDelta(string variableId, int amount);
}

public sealed class CombatScenarioRewardCatalogEntry
{
    public string RewardId { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Tier { get; set; }

    public bool Negative { get; set; }

    public string RewardCardPackId { get; set; } = "";

    public CombatCampaignCardAcquisition CardAcquisition { get; set; } =
        CombatCampaignCardAcquisition.RewardPool;

    public string NativeScriptHash { get; set; } = "";

    public string FightScript { get; set; } = "";

    public Dictionary<string, string> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatScenarioRewardCatalogEntry Clone()
    {
        return new CombatScenarioRewardCatalogEntry
        {
            RewardId = RewardId,
            Kind = Kind,
            Tier = Tier,
            Negative = Negative,
            RewardCardPackId = RewardCardPackId,
            CardAcquisition = CardAcquisition,
            NativeScriptHash = NativeScriptHash,
            FightScript = FightScript,
            Variables = new Dictionary<string, string>(
                Variables,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}

public sealed class CombatSimulationRewardMutation
{
    public string Operation { get; set; } = "";

    public string Kind { get; set; } = "";

    public string RewardId { get; set; } = "";
}

public sealed class CombatScenarioRewardRule
{
    public string RewardId { get; set; } = "";

    public string Kind { get; set; } = "";

    public int Stacks { get; set; } = 1;

    public string NativeScriptHash { get; set; } = "";

    public string FightScript { get; set; } = "";

    public Dictionary<string, string> Variables { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public CombatScenarioRewardRule Clone()
    {
        return new CombatScenarioRewardRule
        {
            RewardId = RewardId,
            Kind = Kind,
            Stacks = Stacks,
            NativeScriptHash = NativeScriptHash,
            FightScript = FightScript,
            Variables = new Dictionary<string, string>(
                Variables,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
