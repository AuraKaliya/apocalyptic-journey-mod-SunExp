using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;

sealed class ReentrantDiscardExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new ReentrantDiscardExtension();
    }
}

sealed class TestResurrectionExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new TestResurrectionExtension();
    }
}

sealed class TestLateEscapeExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    public ICombatSimulationRuntimeExtension Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        return new TestLateEscapeExtension();
    }
}

sealed class TestLateEscapeExtension : ICombatSimulationRuntimeExtension
{
    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (sourceEvent.Kind != CombatSimulationEventKind.BattleEnded
            || context.State.Outcome != CombatSimulationOutcome.Defeat)
        {
            return;
        }
        var player = context.State.Player;
        if (player == null)
        {
            return;
        }
        player.Hp = Math.Min(player.MaxHp, 5);
        context.Terminate(
            CombatSimulationOutcome.Victory,
            CombatTerminationReason.Victory);
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class TestResurrectionExtension : ICombatSimulationRuntimeExtension
{
    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (sourceEvent.Kind != CombatSimulationEventKind.ActorDefeated)
        {
            return;
        }
        var target = context.State.FindActor(sourceEvent.TargetActorId);
        if (target?.Kind == CombatSimulationActorKind.Player)
        {
            target.Hp = Math.Min(target.MaxHp, 5);
        }
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class ReentrantDiscardExtension : ICombatSimulationRuntimeExtension
{
    private bool moved;

    public void Initialize(ICombatSimulationRuntimeContext context)
    {
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (moved
            || sourceEvent.Kind != CombatSimulationEventKind.CardDiscarded
            || context.State.Hand.Count == 0)
        {
            return;
        }
        var instanceId = context.State.Hand[0];
        context.State.Hand.RemoveAt(0);
        context.State.DrawPile.Add(instanceId);
        moved = true;
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
    }
}

sealed class EndTurnSimulationPolicy : ICombatSimulationPolicy
{
    public static readonly EndTurnSimulationPolicy Instance = new();

    public string PolicyId => "tests:end-turn";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class PlayCardOnceThenEndPolicy : ICombatSimulationPolicy
{
    private readonly string cardId;
    private bool played;

    public PlayCardOnceThenEndPolicy(string cardId)
    {
        this.cardId = cardId;
    }

    public string PolicyId => "tests:play-once-then-end";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (!played)
        {
            var selected = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    cardId,
                    StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                played = true;
                return selected;
            }
        }
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class PreserveSurplusThenFinishPolicy : ICombatSimulationPolicy
{
    private bool charged;

    public string PolicyId => "tests:preserve-surplus-then-finish";

    public int SecondTurnEnergy { get; private set; } = -1;

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (context.State.Turn <= 1 && !charged)
        {
            var charge = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    "charge-surplus",
                    StringComparison.OrdinalIgnoreCase));
            if (charge != null)
            {
                charged = true;
                return charge;
            }
        }
        if (context.State.Turn >= 2)
        {
            SecondTurnEnergy = context.State.Player?.Energy ?? -1;
            var finish = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    "finish-after-charge",
                    StringComparison.OrdinalIgnoreCase));
            if (finish != null)
            {
                return finish;
            }
        }
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class PlayCardsInOrderThenEndPolicy : ICombatSimulationPolicy
{
    private readonly Queue<string> cardIds;

    public PlayCardsInOrderThenEndPolicy(params string[] cardIds)
    {
        this.cardIds = new Queue<string>(cardIds);
    }

    public string PolicyId => "tests:play-in-order-then-end";

    public CombatSimulationAction? SelectAction(
        CombatSimulationPolicyContext context)
    {
        if (cardIds.Count > 0)
        {
            var selected = context.LegalActions.FirstOrDefault(item =>
                item.Kind == CombatSimulationActionKind.PlayCard
                && string.Equals(
                    item.DefinitionId,
                    cardIds.Peek(),
                    StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                cardIds.Dequeue();
                return selected;
            }
        }
        return context.LegalActions.FirstOrDefault(item =>
            item.Kind == CombatSimulationActionKind.EndTurn);
    }
}

sealed class RejectCandidateRule : ICombatPreflightRule
{
    private readonly string candidateId;

    public RejectCandidateRule(string candidateId)
    {
        this.candidateId = candidateId;
    }

    public bool IsLegal(
        CombatStateObservation state,
        CombatActionObservation action,
        out string reason)
    {
        var legal = action.CandidateId != candidateId;
        reason = legal ? "" : "test rejection";
        return legal;
    }
}

sealed class FixedRulesetProvider : ICombatRulesetProvider
{
    public void RegisterDefinitions(CombatRulesetBuilder builder)
    {
        var result = BuildDefinitions(builder);
        _ = result;
    }

    private static CombatRulesetBuilder BuildDefinitions(CombatRulesetBuilder builder)
    {
        builder.RegisterStatus(new CombatStatusDefinition
        {
            OwnerModId = "Tests",
            StatusId = "training",
            DecayAtRoundEnd = false
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "strike",
            Cost = 1,
            RequiresEnemyTarget = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Damage,
                    Target = CombatSimulationTarget.SelectedEnemy,
                    Amount = 6
                }
            }
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "guard",
            Cost = 1,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.GainBlock,
                    Target = CombatSimulationTarget.Self,
                    Amount = 5
                }
            }
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = "insight",
            Cost = 0,
            Exhaust = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.Draw,
                    Target = CombatSimulationTarget.Self,
                    Amount = 1
                }
            }
        });
        builder.RegisterEnemy(new CombatEnemyDefinition
        {
            OwnerModId = "Tests",
            EnemyId = "dummy",
            MaxHp = 18,
            Intents =
            {
                new CombatEnemyIntentDefinition
                {
                    IntentId = "hit",
                    Weight = 1,
                    Effects =
                    {
                        new CombatSimulationEffectDefinition
                        {
                            Kind = CombatSimulationEffectKind.Damage,
                            Target = CombatSimulationTarget.Player,
                            Amount = 4
                        }
                    }
                }
            }
        });
        return builder;
    }
}

sealed class FixedThreatProvider : ICombatThreatProvider
{
    private readonly CombatThreatForecast forecast;

    public FixedThreatProvider(CombatThreatForecast forecast)
    {
        this.forecast = forecast;
    }

    public bool TryForecast(
        CombatStateObservation state,
        out CombatThreatForecast result)
    {
        result = forecast;
        return true;
    }
}

sealed class FrozenPreparationSemanticProvider : ICombatSemanticProvider
{
    private readonly string sourceId;

    public FrozenPreparationSemanticProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionSemantics semantics)
    {
        semantics = new CombatActionSemantics { Buff = 17d };
        return string.Equals(
            action.SourceId,
            sourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}

sealed class CountingSemanticProvider : ICombatSemanticProvider
{
    private readonly string sourceId;
    private int callCount;

    public CountingSemanticProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public int CallCount => Volatile.Read(ref callCount);

    public bool TryDescribe(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionSemantics semantics)
    {
        Interlocked.Increment(ref callCount);
        semantics = new CombatActionSemantics { Buff = 19d };
        return string.Equals(
            action.SourceId,
            sourceId,
            StringComparison.OrdinalIgnoreCase);
    }
}

sealed class FrozenPreparationRoleStrategyProvider :
    ICombatRoleStrategyProvider
{
    public bool TryEnrich(CombatStateObservation state)
    {
        foreach (var action in state.Actions)
        {
            action.Features[CombatRoleStrategyFeatureNames.Active] = 1d;
        }
        return true;
    }
}

sealed class ProhibitSourceRoleStrategyProvider : ICombatRoleStrategyProvider
{
    private readonly string sourceId;

    public ProhibitSourceRoleStrategyProvider(string sourceId)
    {
        this.sourceId = sourceId;
    }

    public bool TryEnrich(CombatStateObservation state)
    {
        state.Features["roleStrategy:test.prepared-state"] = 1d;
        foreach (var action in state.Actions.Where(action => string.Equals(
                     action.SourceId,
                     sourceId,
                     StringComparison.OrdinalIgnoreCase)))
        {
            action.Features[
                CombatRoleStrategyFeatureNames.StrategicallyProhibited] = 1d;
        }
        return true;
    }
}

sealed class FixedScenarioProvider : ICombatScenarioProvider
{
    private readonly ulong seed;

    public FixedScenarioProvider(ulong seed)
    {
        this.seed = seed;
    }

    public IEnumerable<CombatScenarioDefinition> GetScenarios()
    {
        yield return new CombatScenarioDefinition
        {
            ScenarioId = "registered-headless",
            RulesetVersion = "registry-v1",
            Seed = seed,
            Player = new CombatPlayerSetup
            {
                RoleId = "tests",
                MaxHp = 20,
                CurrentHp = 20,
                Deck = new List<string> { "Tests:strike" }
            },
            Enemies =
            {
                new CombatEnemySetup { EnemyId = "Tests:dummy" }
            }
        };
    }
}

sealed class FixedEffectResolver : ICombatEffectResolver
{
    private readonly string candidateId;

    public FixedEffectResolver(string candidateId)
    {
        this.candidateId = candidateId;
    }

    public bool TryResolve(
        CombatStateObservation state,
        CombatActionObservation action,
        out CombatActionModel model)
    {
        model = new CombatActionModel();
        if (action.CandidateId != candidateId)
        {
            return false;
        }
        model.ModelId = "test-chance";
        model.Outcomes = new List<CombatActionOutcome>
        {
            new CombatActionOutcome
            {
                OutcomeId = "low",
                Probability = 2d,
                Effects =
                {
                    new CombatEffectOperation
                    {
                        Kind = CombatEffectKind.Damage,
                        TargetRuntimeId = action.TargetRuntimeId,
                        Magnitude = 2d
                    }
                }
            },
            new CombatActionOutcome
            {
                OutcomeId = "high",
                Probability = 2d,
                Effects =
                {
                    new CombatEffectOperation
                    {
                        Kind = CombatEffectKind.Damage,
                        TargetRuntimeId = action.TargetRuntimeId,
                        Magnitude = 6d
                    }
                }
            }
        };
        return true;
    }
}

sealed class RecordingPolicyValueModel : ICombatPolicyValueModel
{
    public string ModelId => "recording-policy-value";

    public CombatPolicyValueInput? LastInput { get; private set; }

    public CombatPolicyValuePrediction Evaluate(CombatPolicyValueInput input)
    {
        LastInput = input;
        var result = new CombatPolicyValuePrediction
        {
            ExpectedReturn = 0.75d
        };
        foreach (var candidate in input.Candidates)
        {
            result.PolicyLogits[candidate.CandidateId] = 2d;
        }
        return result;
    }

    public IReadOnlyList<CombatPolicyValuePrediction> EvaluateBatch(
        IReadOnlyList<CombatPolicyValueInput> inputs)
    {
        return inputs.Select(Evaluate).ToList();
    }
}

sealed class FixedSkillTimingProvider : ICombatSkillTimingProvider
{
    public bool TryEnrich(CombatStateObservation state)
    {
        var action = state.Actions.FirstOrDefault(item =>
            item.Kind == CombatActionKind.UseSkill
            && item.SourceId == "skill_test");
        if (action == null)
        {
            return false;
        }
        action.Features[CombatSkillTimingFeatureNames.Active] = 1d;
        action.Features[CombatSkillTimingFeatureNames.OngoingEffectValue] = 2d;
        CombatSkillTimingPolicy.Enrich(action);
        return true;
    }
}
