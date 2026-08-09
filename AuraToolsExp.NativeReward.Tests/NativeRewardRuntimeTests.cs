using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class NativeRewardTestSuite
{
    internal static IEnumerable<string> ValidateNativeGameRuntime(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        var failures = new List<string>();
        var nativeCards = ruleset.SnapshotCards()
            .Where(card => card.Metadata.GetValueOrDefault(
                "NativeExecution",
                "") == "Script")
            .ToList();
        foreach (var card in nativeCards)
        {
            var scenario = NewNativeScenario(
                campaign,
                ruleset,
                enemy,
                "native-card:" + card.CardId,
                new List<string> { card.CardId });
            var result = new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory())
                .Run(scenario, ruleset, new SmokePolicy());
            AddNativeRuntimeFailure(
                failures,
                "card",
                card.CardId,
                result);
        }
        var nativeStatuses = ruleset.SnapshotStatuses()
            .Where(status => status.Metadata.GetValueOrDefault(
                "NativeExecution",
                "") == "Script")
            .ToList();
        foreach (var status in nativeStatuses)
        {
            var scenario = NewNativeScenario(
                campaign,
                ruleset,
                enemy,
                "native-status:" + status.StatusId,
                new List<string> { campaign.Player.Deck[0] });
            var initial = new CombatInitialStatus
            {
                StatusId = status.StatusId,
                Stacks = 3
            };
            if (status.StatusId.StartsWith(
                    "SpecialBuff_",
                    StringComparison.OrdinalIgnoreCase))
            {
                scenario.Enemies[0].InitialStatuses.Add(initial);
            }
            else
            {
                scenario.Player.InitialStatuses.Add(initial);
            }
            var result = new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory())
                .Run(scenario, ruleset, new EndTurnPolicy());
            AddNativeRuntimeFailure(
                failures,
                "status",
                status.StatusId,
                result);
        }
        Console.WriteLine(
            $"Native game runtime smoke: {nativeCards.Count} cards, "
            + $"{nativeStatuses.Count} statuses.");
        return failures;
    }

    internal static IEnumerable<string> ValidateNativeCombatSemantics(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        var failures = new List<string>();
        CombatSimulationResult Run(
            string id,
            List<string> deck,
            Action<CombatScenarioDefinition> configure,
            ICombatSimulationPolicy policy)
        {
            var scenario = NewNativeScenario(
                campaign,
                ruleset,
                enemy,
                "native-semantics:" + id,
                deck);
            scenario.TraceLevel = CombatSimulationTraceLevel.Full;
            scenario.Limits.MaximumTurns = 1;
            configure(scenario);
            return new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory())
                .Run(scenario, ruleset, policy);
        }

        var fast = Run(
            "persistent-draw-modifier",
            Enumerable.Repeat("blood_1", 8).ToList(),
            scenario => scenario.Player.InitialStatuses.Add(
                new CombatInitialStatus
                {
                    StatusId = "buff_fast",
                    Stacks = 2
                }),
            new EndTurnPolicy());
        var fastPlayer = fast.FinalState.Player;
        if (fast.Metrics.CardsDrawn != 3
            || fastPlayer?.Variables.GetValueOrDefault(
                "DrawPerTurnModifier",
                0d) != 2d)
        {
            failures.Add(
                "native-semantics:buff_fast:expected-3-draws-and-modifier-2:"
                + fast.Metrics.CardsDrawn
                + ":"
                + fastPlayer?.Variables.GetValueOrDefault(
                    "DrawPerTurnModifier",
                    0d));
        }

        var blood = Run(
            "blood-cleanse",
            new List<string> { "blood_3" },
            scenario =>
            {
                scenario.Player.CurrentHp = 9000;
                scenario.Player.InitialStatuses.Add(
                    new CombatInitialStatus
                    {
                        StatusId = "buff_bleeding",
                        Stacks = 3
                    });
                scenario.Enemies[0].InitialStatuses.Add(
                    new CombatInitialStatus
                    {
                        StatusId = "buff_bleeding",
                        Stacks = 4
                    });
            },
            new SmokePolicy());
        var bloodPlayerBlock = blood.Events
            .Where(item =>
                item.Kind == CombatSimulationEventKind.BlockGained
                && item.TargetActorId == blood.FinalState.PlayerActorId
                && string.Equals(
                    item.DefinitionId,
                    "blood_3",
                    StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Amount);
        if (bloodPlayerBlock != 36
            || blood.Metrics.Healing != 14
            || blood.FinalState.Actors.Any(actor => actor.Statuses.Any(status =>
                string.Equals(
                    status.StatusId,
                    "buff_bleeding",
                    StringComparison.OrdinalIgnoreCase))))
        {
            failures.Add(
                "native-semantics:blood_3:expected-cleanse-heal-14-and-scaled-block-36:"
                + blood.Metrics.Healing
                + ":"
                + bloodPlayerBlock);
        }

        var comboPayload = Run(
            "action-card-payload",
            new List<string> { "combo_1" },
            scenario =>
            {
                scenario.Player.CurrentHp = 9000;
                scenario.Player.InitialStatuses.Add(
                    new CombatInitialStatus
                    {
                        StatusId = "buff_RegenerationPrayer",
                        Stacks = 2
                    });
            },
            new SmokePolicy());
        if (comboPayload.Metrics.Healing != 4)
        {
            failures.Add(
                "native-semantics:action-card-payload:expected-healing-4:"
                + comboPayload.Metrics.Healing);
        }

        var timeLock = Run(
            "time-lock-deferred-use",
            new List<string> { "timekeeper_9" },
            _ => { },
            new SmokePolicy());
        var timeLockDamage = timeLock.Events.Where(item =>
                item.Kind == CombatSimulationEventKind.DamageDealt
                && string.Equals(
                    item.DefinitionId,
                    "timekeeper_9",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var timeLockEnemies = timeLock.FinalState.Actors
            .Where(actor => actor.Kind == CombatSimulationActorKind.Enemy)
            .Select(actor => actor.ActorId)
            .ToHashSet();
        var timeLockStatus = timeLock.FinalState.Player?.Statuses
            .FirstOrDefault(status => string.Equals(
                status.StatusId,
                "buff_timelock",
                StringComparison.OrdinalIgnoreCase));
        if (timeLockDamage.Count != 6
            || timeLockDamage.Sum(item => item.Amount) != 114
            || timeLock.Metrics.DamageDealt != 114
            || timeLockDamage.Any(item => !timeLockEnemies.Contains(
                item.TargetActorId))
            || timeLockStatus?.Stacks != 0)
        {
            failures.Add(
                "native-semantics:timekeeper_9:expected-six-scaled-enemy-hits-and-zero-stack:"
                + timeLockDamage.Sum(item => item.Amount)
                + ":"
                + string.Join(
                    ",",
                    timeLock.Events.Select(item =>
                        item.Kind
                        + "/"
                        + item.DefinitionId
                        + "/"
                        + item.Amount)));
        }

        var crowdfunding = Run(
            "crowdfunding-deck-projection",
            new List<string>
            {
                "Crowdfundingcard_7",
                "blood_1",
                "blood_1"
            },
            scenario => scenario.InitialDraw = 3,
            new SmokePolicy());
        if (crowdfunding.Outcome == CombatSimulationOutcome.Invalid
            || crowdfunding.UnsupportedDefinitions.Any(item =>
                item.Contains(
                    "Crowdfundingcard_7",
                    StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(
                "native-semantics:Crowdfundingcard_7:deck-projection:"
                + crowdfunding.TerminationReason
                + ":"
                + string.Join(",", crowdfunding.UnsupportedDefinitions));
        }

        var crowdfundingRelic = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "CrowdFundingRelic_17",
                StringComparison.OrdinalIgnoreCase));
        var crowdfundingFeedback = Run(
            "crowdfunding-relic-causal-chain",
            new List<string>
            {
                "universalcard_11",
                "universalcard_11"
            },
            scenario =>
            {
                scenario.InitialDraw = 2;
                scenario.Limits.MaximumCommandsPerAction = 100;
                scenario.RewardRules.Add(new CombatScenarioRewardRule
                {
                    RewardId = crowdfundingRelic.RewardId,
                    Kind = crowdfundingRelic.Kind.ToString(),
                    Stacks = 1,
                    NativeScriptHash = crowdfundingRelic.NativeScriptHash,
                    FightScript = crowdfundingRelic.FightScript,
                    Variables = new Dictionary<string, string>(
                        crowdfundingRelic.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                });
            },
            new SmokePolicy());
        var causalDamage = crowdfundingFeedback.Events.Where(item =>
                item.Kind == CombatSimulationEventKind.DamageDealt
                && string.Equals(
                    item.SourceRewardId,
                    "CrowdFundingRelic_17",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var causalDamageAmounts = causalDamage
            .OrderBy(item => item.Sequence)
            .Select(item => item.Amount)
            .ToList();
        var increasingDamage =
            causalDamageAmounts.Count >= 2
            && causalDamageAmounts.SequenceEqual(
                Enumerable.Range(1, causalDamageAmounts.Count));
        if (crowdfundingFeedback.TerminationReason
                is CombatTerminationReason.MaximumCommands
                or CombatTerminationReason.TriggerLoop
            || !increasingDamage
            || causalDamage.Any(item =>
                item.SourceActorId
                   != crowdfundingFeedback.FinalState.PlayerActorId
                || crowdfundingFeedback.FinalState.FindActor(
                       item.TargetActorId)?.Kind
                   != CombatSimulationActorKind.Enemy)
            || causalDamage.Any(item =>
                item.CausalChainId <= 0
                || string.IsNullOrWhiteSpace(item.HandlerId)
                || item.SourceActionId <= 0))
        {
            failures.Add(
                "native-semantics:CrowdFundingRelic_17:"
                + "hurt-count-increasing-true-damage:"
                + crowdfundingFeedback.TerminationReason
                + ":"
                + crowdfundingFeedback.FailureDiagnostics.PendingCommand
                + ":"
                + string.Join(
                    ",",
                    causalDamage.Select(item =>
                        item.Amount
                        + "/"
                        + item.SourceActorId
                        + ">"
                        + item.TargetActorId
                        + "/chain="
                        + item.CausalChainId
                        + "/handler="
                        + item.HandlerId
                        + "/action="
                        + item.SourceActionId)));
        }

        var timekeeperDraw = Run(
            "timekeeper-safe-draw",
            new List<string> { "timekeeper_3", "blood_1" },
            scenario => scenario.InitialDraw = 2,
            new SmokePolicy());
        if (timekeeperDraw.Outcome == CombatSimulationOutcome.Invalid
            || timekeeperDraw.UnsupportedDefinitions.Any(item =>
                item.Contains(
                    "timekeeper_3",
                    StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(
                "native-semantics:timekeeper_3:safe-draw:"
                + timekeeperDraw.TerminationReason
                 + ":"
                 + string.Join(",", timekeeperDraw.UnsupportedDefinitions));
        }

        var commonCardRelic = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "relic_51",
                StringComparison.OrdinalIgnoreCase));
        var commonCardCount = Run(
            "relic-common-card-count",
            new List<string> { "blood_1" },
            scenario =>
            {
                scenario.InitialDraw = 1;
                scenario.RewardRules.Add(new CombatScenarioRewardRule
                {
                    RewardId = commonCardRelic.RewardId,
                    Kind = commonCardRelic.Kind.ToString(),
                    Stacks = 1,
                    NativeScriptHash = commonCardRelic.NativeScriptHash,
                    FightScript = commonCardRelic.FightScript,
                    Variables = new Dictionary<string, string>(
                        commonCardRelic.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                });
            },
            new EndTurnPolicy());
        if (commonCardCount.Outcome == CombatSimulationOutcome.Invalid
            || commonCardCount.TerminationReason
                == CombatTerminationReason.EngineError)
        {
            failures.Add(
                "native-semantics:relic_51:missing-init-script-default:"
                + commonCardCount.TerminationReason
                + ":"
                + string.Join(",", commonCardCount.UnsupportedDefinitions));
        }

        var powerPayload = Run(
            "power-payload-actor-id",
            new List<string> { "blood_1" },
            scenario =>
            {
                scenario.InitialDraw = 1;
                scenario.Enemies[0].InitialStatuses.Add(
                    new CombatInitialStatus
                    {
                        StatusId = "SpecialBuff_EndlessDesire",
                        Stacks = 1
                    });
            },
            new SmokePolicy());
        AddNativeRuntimeFailure(
            failures,
            "power-payload",
            "SpecialBuff_EndlessDesire",
            powerPayload);

        var maximumHpRelic = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "relic_25",
                StringComparison.OrdinalIgnoreCase));
        var maximumHpFacts = Run(
            "maximum-hp-factual-trace",
            new List<string> { "blood_1", "blood_1", "blood_1" },
            scenario => scenario.RewardRules.Add(
                new CombatScenarioRewardRule
                {
                    RewardId = maximumHpRelic.RewardId,
                    Kind = maximumHpRelic.Kind.ToString(),
                    Stacks = 1,
                    NativeScriptHash = maximumHpRelic.NativeScriptHash,
                    FightScript = maximumHpRelic.FightScript,
                    Variables = new Dictionary<string, string>(
                        maximumHpRelic.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                }),
            new EndTurnPolicy());
        var maximumHpFact = maximumHpFacts.Events.FirstOrDefault(item =>
            item.Kind == CombatSimulationEventKind.MaximumHpChanged
            && string.Equals(
                item.SourceRewardId,
                "relic_25",
                StringComparison.OrdinalIgnoreCase));
        if (maximumHpFact == null
            || maximumHpFact.Amount != 1
            || maximumHpFact.RawAmount != 1
            || maximumHpFact.CurrentAmount
               - maximumHpFact.PreviousAmount != 1
            || maximumHpFact.CausalChainId <= 0)
        {
            failures.Add(
                "native-semantics:relic_25:maximum-hp-fact-missing-or-incomplete");
        }

        Console.WriteLine("Native combat semantic checks: 10 cases.");
        return failures;
    }

    internal static CombatScenarioDefinition NewNativeScenario(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy,
        string scenarioId,
        List<string> deck)
    {
        return new CombatScenarioDefinition
        {
            ScenarioId = scenarioId,
            RulesetVersion = ruleset.Version,
            Seed = 772026UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 10000,
                CurrentHp = 10000,
                BaseEnergy = 99,
                Deck = deck,
                Variables = new Dictionary<string, double>
                {
                    ["Difficulty"] = 1,
                    ["Strength"] = 40,
                    ["Wisdom"] = 39,
                    ["Perceive"] = 40,
                    ["Lucky"] = 40,
                    ["Money"] = 1000,
                    ["TagDiff"] = 0,
                    ["EncounterKind"] = (int)CombatCampaignEncounterKind.Normal
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "native-game-smoke-enemy",
                    HpScale = 100d
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 1,
            HandLimit = 20,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Summary,
            EnabledRewardCardPackIds = new List<string>(
                campaign.EnabledRewardCardPackIds),
            RewardCatalog = campaign.Rewards.Select(item =>
                new CombatScenarioRewardCatalogEntry
                {
                    RewardId = item.RewardId,
                    Kind = item.Kind.ToString(),
                    Tier = item.Tier,
                    Negative = item.Negative,
                    RewardCardPackId = item.RewardCardPackId,
                    CardAcquisition = item.CardAcquisition,
                    NativeScriptHash = item.NativeScriptHash,
                    FightScript = item.FightScript,
                    Variables = new Dictionary<string, string>(
                        item.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                }).ToList(),
            CampaignVariables = new Dictionary<string, string>
            {
                ["DoomPower"] = "0",
                ["SevenCursePower"] = "0"
            },
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 2,
                MaximumActions = 30,
                MaximumCommands = 10000
            }
        };
    }

    internal static void AddNativeRuntimeFailure(
        ICollection<string> failures,
        string kind,
        string definitionId,
        CombatSimulationResult result)
    {
        var diagnostics = result.UnsupportedDefinitions
            .Where(item =>
                item.StartsWith("native-", StringComparison.OrdinalIgnoreCase)
                || item.StartsWith("engine-error:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (result.TerminationReason == CombatTerminationReason.EngineError
            || diagnostics.Count > 0)
        {
            failures.Add(
                "native-"
                + kind
                + ":"
                + definitionId
                + ":"
                + result.TerminationReason
                + ":"
                + string.Join(",", diagnostics));
        }
    }

    internal static IEnumerable<string> ValidateIndirectScriptExecution(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        var failures = new List<string>();
        var source = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "relic_77",
                StringComparison.OrdinalIgnoreCase));
        var copier = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "CrowdFundingRelic_63",
                StringComparison.OrdinalIgnoreCase));
        CombatScenarioRewardRule Rule(CombatCampaignRewardDefinition reward)
        {
            return new CombatScenarioRewardRule
            {
                RewardId = reward.RewardId,
                Kind = reward.Kind.ToString(),
                Stacks = 1,
                NativeScriptHash = reward.NativeScriptHash,
                FightScript = reward.FightScript,
                Variables = new Dictionary<string, string>(
                    reward.InitialVariables,
                    StringComparer.OrdinalIgnoreCase)
            };
        }
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "indirect-reward-script-smoke",
            RulesetVersion = ruleset.Version,
            Seed = 772025UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 0,
                Deck = new List<string> { campaign.Player.Deck[0] },
                Variables = new Dictionary<string, double>
                {
                    ["Difficulty"] = 1,
                    ["EncounterKind"] = (int)CombatCampaignEncounterKind.Normal
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "copy-smoke-enemy",
                    HpScale = 10d
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 1,
            HandLimit = 10,
            RequireAuthoritativeRules = false,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 10,
                MaximumCommands = 10000
            },
            RewardRules =
            {
                Rule(source),
                Rule(copier)
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, ruleset, new EndTurnPolicy());
        if (result.Metrics.BlockGained != 24)
        {
            failures.Add(
                "reward-runtime:CrowdFundingRelic_63:expected-24-block-gained:"
                + result.Metrics.BlockGained);
        }

        var statefulSource = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "relic_58",
                StringComparison.OrdinalIgnoreCase));
        scenario.ScenarioId = "indirect-stateful-reward-script-smoke";
        scenario.Seed++;
        scenario.RewardRules.Clear();
        scenario.RewardRules.Add(Rule(statefulSource));
        scenario.RewardRules.Add(Rule(copier));
        var statefulResult = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, ruleset, new EndTurnPolicy());
        AddNativeRuntimeFailure(
            failures,
            "copied-reward",
            statefulSource.RewardId,
            statefulResult);

        var initializedSource = campaign.Rewards.First(item =>
            string.Equals(
                item.RewardId,
                "CrowdFundingRelic_4",
                StringComparison.OrdinalIgnoreCase));
        scenario.ScenarioId = "indirect-initialized-reward-script-smoke";
        scenario.Seed++;
        scenario.RewardRules.Clear();
        scenario.RewardRules.Add(Rule(initializedSource));
        scenario.RewardRules.Add(Rule(copier));
        var initializedResult = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, ruleset, new EndTurnPolicy());
        AddNativeRuntimeFailure(
            failures,
            "copied-reward-defaults",
            initializedSource.RewardId,
            initializedResult);
        if (!initializedResult.RewardVariables.TryGetValue(
                copier.RewardId,
                out var copiedVariables)
            || copiedVariables.GetValueOrDefault("ThisCount", "") != "1")
        {
            failures.Add(
                "native-copied-reward-defaults:"
                + initializedSource.RewardId
                + ":expected-ThisCount-1");
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateDrawPileSnapshotExecution(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        const string supernovaId = "Crowdfundingcard_23";
        var failures = new List<string>();
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "supernova-draw-pile-snapshot-smoke",
            RulesetVersion = ruleset.Version,
            Seed = 772027UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 99,
                Deck = new List<string>
                {
                    supernovaId,
                    supernovaId,
                    supernovaId
                },
                Variables = new Dictionary<string, double>
                {
                    ["Difficulty"] = 1,
                    ["EncounterKind"] = (int)CombatCampaignEncounterKind.Normal
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "supernova-smoke-enemy",
                    HpScale = 100d
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 1,
            HandLimit = 10,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 10,
                MaximumCommands = 10000
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, ruleset, new SmokePolicy());
        AddNativeRuntimeFailure(
            failures,
            "card-combination",
            supernovaId,
            result);
        if (result.FinalState.ExhaustPile.Count < 2)
        {
            failures.Add(
                "native-card-combination:"
                + supernovaId
                + ":expected-at-least-2-exhausted:"
                + result.FinalState.ExhaustPile.Count);
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateFullHandGeneratedCardOverflow(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        const string generatedCardId = "Crowdfundingcard_28";
        const string generatorCardId = "test_full_hand_generator";
        const string fillerCardId = "test_full_hand_filler";
        var failures = new List<string>();
        var builder = new CombatRulesetBuilder(
            ruleset.Version + ".full-hand-overflow-test");
        foreach (var card in ruleset.SnapshotCards())
        {
            builder.RegisterCard(card);
        }
        foreach (var status in ruleset.SnapshotStatuses())
        {
            builder.RegisterStatus(status);
        }
        foreach (var registeredEnemy in ruleset.SnapshotEnemies())
        {
            builder.RegisterEnemy(registeredEnemy);
        }
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = generatorCardId,
            Cost = 0,
            Exhaust = true,
            Effects =
            {
                new CombatSimulationEffectDefinition
                {
                    Kind = CombatSimulationEffectKind.CreateCard,
                    Target = CombatSimulationTarget.Self,
                    DefinitionId = generatedCardId,
                    Amount = 1,
                    DestinationZone = CombatCardZone.Hand
                }
            }
        });
        builder.RegisterCard(new CombatCardDefinition
        {
            OwnerModId = "Tests",
            CardId = fillerCardId,
            Cost = 99,
            Tags = { "Unusable" }
        });
        var augmented = builder.Freeze();
        if (!augmented.Success)
        {
            failures.Add(
                "native-full-hand-overflow:ruleset:"
                + string.Join(",", augmented.Errors.Take(3)));
            return failures;
        }

        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "full-hand-generated-card-overflow",
            RulesetVersion = augmented.Ruleset.Version,
            Seed = 772029UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 99,
                Deck = new List<string>
                {
                    generatorCardId,
                    fillerCardId
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "full-hand-overflow-enemy",
                    HpScale = 100d
                }
            },
            InitialDraw = 2,
            DrawPerTurn = 0,
            HandLimit = 2,
            MovePlayedCardAfterResolution = true,
            RequireAuthoritativeRules = false,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 10,
                MaximumCommands = 100,
                MaximumCommandsPerAction = 25
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(scenario, augmented.Ruleset, new SmokePolicy());
        AddNativeRuntimeFailure(
            failures,
            "full-hand-overflow",
            generatedCardId,
            result);
        var created = result.Events
            .Where(item =>
                item.Kind == CombatSimulationEventKind.CardCreated
                && string.Equals(
                    item.DefinitionId,
                    generatedCardId,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (created.Count != 1
            || result.FinalState.DrawPile.LastOrDefault()
               != created[0].CardInstanceId
            || result.Events.Any(item =>
                item.Kind == CombatSimulationEventKind.CardDiscarded
                && item.CardInstanceId == created[0].CardInstanceId))
        {
            failures.Add(
                "native-full-hand-overflow:"
                + generatedCardId
                + ":expected-single-card-on-draw-pile-top");
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateDeferredEffectSafety(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        CombatEnemyDefinition enemy)
    {
        const string echoCardId = "timekeeper_13";
        const string timeLockStatusId = "buff_timelock";
        var failures = new List<string>();
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "timekeeper-empty-deferred-effect-smoke",
            RulesetVersion = ruleset.Version,
            Seed = 772028UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 99,
                Deck = new List<string> { echoCardId },
                InitialStatuses =
                {
                    new CombatInitialStatus
                    {
                        StatusId = timeLockStatusId,
                        Stacks = 1
                    }
                },
                Variables = new Dictionary<string, double>
                {
                    ["Difficulty"] = 1,
                    ["EncounterKind"] = (int)CombatCampaignEncounterKind.Normal
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemy.EnemyId,
                    InstanceKey = "timekeeper-smoke-enemy",
                    HpScale = 100d
                }
            },
            InitialDraw = 1,
            DrawPerTurn = 1,
            HandLimit = 10,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 2,
                MaximumActions = 10,
                MaximumCommands = 10000
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(
                scenario,
                ruleset,
                new EndFirstTurnThenPlayPolicy());
        AddNativeRuntimeFailure(
            failures,
            "empty-deferred-effect",
            echoCardId,
            result);
        var timeLock = result.FinalState.Player?.Statuses.FirstOrDefault(item =>
            string.Equals(
                item.StatusId,
                timeLockStatusId,
                StringComparison.OrdinalIgnoreCase));
        if (timeLock == null || timeLock.Stacks != 0)
        {
            failures.Add(
                "native-status-lifecycle:"
                + timeLockStatusId
                + ":expected-retained-zero-stack-status");
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateKnownIntegritySeeds(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var seeds = new[]
        {
            // 2026-07-26 reproduced premature defeat while a resurrection
            // source restored the player after the lethal event.
            (Difficulty: "normal", Seed: 2_000_002UL),
            (Difficulty: "normal", Seed: 2261918009116763129UL),
            (Difficulty: "normal", Seed: 2152195624958294618UL),
            (Difficulty: "normal", Seed: 2152195624958294662UL),
            (Difficulty: "normal", Seed: 1792150460818600981UL),
            (Difficulty: "advanced", Seed: 4216546253471716545UL),
            (Difficulty: "normal", Seed: 1249823491188953225UL),
            (Difficulty: "normal", Seed: 2166170238168703541UL),
            (Difficulty: "normal", Seed: 4601385609574227197UL),
            (Difficulty: "normal", Seed: 1862278965995231554UL),
            (Difficulty: "normal", Seed: 1802918007689908913UL),
            (Difficulty: "normal", Seed: 773428UL),
            (Difficulty: "normal", Seed: 773810UL),
            (Difficulty: "advanced", Seed: 772183UL),
            (Difficulty: "normal", Seed: 772256UL),
            (Difficulty: "normal", Seed: 772352UL),
            (Difficulty: "normal", Seed: 772546UL),
            (Difficulty: "normal", Seed: 772678UL),
            (Difficulty: "advanced", Seed: 773057UL),
            (Difficulty: "normal", Seed: 773164UL),
            (Difficulty: "normal", Seed: 773604UL),
            // 2026-07-26 arena failure: CrowdFundingRelic_17 recursively
            // re-entered its own Hurt handler at level_10020.
            (Difficulty: "normal", Seed: 3707272656116217686UL),
            // 2026-07-30 self-play failure: careercard_9 created a blessing
            // config without metadata/runtime context at level_10024.
            (Difficulty: "advanced", Seed: 2031138444152085570UL),
            // 2026-07-30 self-play failures: core CreateRandomCard bypassed the
            // role-aware dynamic pool and generated foreign career skills.
            (Difficulty: "advanced", Seed: 1251924352389057161UL),
            (Difficulty: "advanced", Seed: 1251924352389057199UL),
            (Difficulty: "normal", Seed: 1251924352389057202UL)
        };
        var runner = new CombatCampaignRunner(
            new CombatSimulationEngine(
                new AuraToolsNativeRewardExtensionFactory()));
        var policyFactory = IntegrityPolicyFactory();
        foreach (var entry in seeds)
        {
            var plan = CombatCampaignWorldPlanner.Build(
                campaign,
                entry.Difficulty,
                entry.Seed);
            var result = runner.Run(
                campaign,
                plan,
                ruleset,
                policyFactory);
            var allowedRoleSkills = new HashSet<string>(
                campaign.Player.SkillCardIds,
                StringComparer.OrdinalIgnoreCase);
            var skillOnlyCards = new HashSet<string>(
                campaign.Rewards
                    .Where(item =>
                        item.Kind == CombatCampaignRewardKind.Card
                        && item.CardAcquisition
                           == CombatCampaignCardAcquisition.SkillOnly)
                    .Select(item => item.RewardId),
                StringComparer.OrdinalIgnoreCase);
            var leakedRoleSkills = result.Battles
                .SelectMany(item => item.FinalState.Cards)
                .Where(item =>
                    skillOnlyCards.Contains(item.CardId)
                    && !allowedRoleSkills.Contains(item.CardId))
                .Select(item =>
                    item.CardId
                    + "@"
                    + item.CreationSource
                    + ":"
                    + item.CreationSourceId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (leakedRoleSkills.Count > 0)
            {
                failures.Add(
                    "known-integrity-seed:"
                    + entry.Difficulty
                    + ":"
                    + entry.Seed
                    + ":cross-role-skill-leak:"
                    + string.Join(",", leakedRoleSkills));
            }
            if (!result.Invalid)
            {
                continue;
            }

            var diagnostics = result.Battles
                .Where(item =>
                    item.Outcome == CombatSimulationOutcome.Invalid
                    || item.TerminationReason
                    == CombatTerminationReason.EngineError)
                .SelectMany(item => item.UnsupportedDefinitions
                    .Concat(new[]
                    {
                        "termination:" + item.TerminationReason,
                        "scope:" + item.FailureDiagnostics.LimitScope,
                        "action:" + item.FailureDiagnostics.ActionDefinitionId,
                        "count:"
                        + item.FailureDiagnostics.TotalCommandCount
                        + "/"
                        + item.FailureDiagnostics.ActionCommandCount,
                        "pending:" + item.FailureDiagnostics.PendingCommand
                    })
                    .Concat(item.FailureDiagnostics.StateSummary))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            failures.Add(
                "known-integrity-seed:"
                + entry.Difficulty
                + ":"
                + entry.Seed
                + ":completed-"
                + result.CompletedBattles
                + ":"
                + string.Join(",", diagnostics));
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateRandomStatusPoolSemantics(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory()).Run(
            BuildNativeEnemyScenario(
                campaign,
                ruleset,
                "enemy_10040",
                "random-status-pool"),
            ruleset,
            new EndTurnPolicy());
        var enemy = result.FinalState.Actors.FirstOrDefault(actor =>
            actor.Kind == CombatSimulationActorKind.Enemy);
        var generated = enemy?.Statuses
            .Where(status => !string.Equals(
                status.StatusId,
                "SpecialBuff_WitchCultists",
                StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<CombatStatusState>();
        if (generated.Count == 0)
        {
            failures.Add("random-status-pool:no-generated-status");
            return failures;
        }
        foreach (var status in generated)
        {
            if (!ruleset.TryGetStatus(status.StatusId, out var definition))
            {
                failures.Add("random-status-pool:missing:" + status.StatusId);
                continue;
            }
            var type = definition.Metadata.GetValueOrDefault("Type", "");
            if (!string.Equals(type, "正面", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "负面", StringComparison.OrdinalIgnoreCase)
                && !definition.Tags.Contains(
                    "Positive",
                    StringComparer.OrdinalIgnoreCase)
                && !definition.Tags.Contains(
                    "Negative",
                    StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    "random-status-pool:escaped:"
                    + status.StatusId
                    + ":"
                    + type);
            }
        }
        return failures;
    }

    internal static IEnumerable<string> ValidateVisibleFakeCardSemantics(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var failures = new List<string>();
        var scenario = BuildNativeEnemyScenario(
            campaign,
            ruleset,
            "enemy_10060",
            "visible-fake-card");
        var engine = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory());
        var result = engine.Run(scenario, ruleset, new EndTurnPolicy());
        var fakeCards = result.FinalState.Cards
            .Where(card => card.IsVisibleFake)
            .ToList();
        if (fakeCards.Count == 0)
        {
            failures.Add("visible-fake-card:not-created");
            return failures;
        }
        foreach (var fake in fakeCards)
        {
            if (!string.Equals(
                    fake.CardId,
                    "cursecard_15",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(fake.ApparentCardId)
                || !fake.EnchantmentIds.Contains(
                    "enchtag_16",
                    StringComparer.OrdinalIgnoreCase)
                || !fake.Tags.Contains(
                    "VisibleFake",
                    StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    "visible-fake-card:invalid-instance:"
                    + fake.InstanceId
                    + ":"
                    + fake.CardId
                    + ":"
                    + fake.ApparentCardId);
            }
        }

        var decisionState = result.FinalState.Clone();
        decisionState.Outcome = CombatSimulationOutcome.None;
        decisionState.TerminationReason = CombatTerminationReason.None;
        decisionState.Phase = CombatSimulationPhase.PlayerAction;
        var player = decisionState.Player;
        if (player == null)
        {
            failures.Add("visible-fake-card:missing-player");
            return failures;
        }
        player.Hp = Math.Max(1, player.Hp);
        player.Energy = 99;
        decisionState.DrawPile.Clear();
        decisionState.DiscardPile.Clear();
        decisionState.ExhaustPile.Clear();
        decisionState.Hand.Clear();
        var fakeCard = fakeCards[0];
        var safeCard = decisionState.Cards.FirstOrDefault(card =>
            !card.IsVisibleFake
            && ruleset.TryGetCard(card.CardId, out _));
        if (safeCard == null)
        {
            failures.Add("visible-fake-card:missing-safe-card");
            return failures;
        }
        decisionState.Hand.Add(fakeCard.InstanceId);
        decisionState.Hand.Add(safeCard.InstanceId);
        foreach (var card in decisionState.Cards.Where(card =>
                     card.InstanceId != fakeCard.InstanceId
                     && card.InstanceId != safeCard.InstanceId))
        {
            decisionState.DrawPile.Add(card.InstanceId);
        }
        var legal = engine.GetLegalPlayerActions(
            scenario,
            ruleset,
            decisionState);
        var selected = new CombatDecisionSimulationPolicy(
            IntegrityProfile()).SelectAction(
            new CombatSimulationPolicyContext
            {
                Scenario = scenario,
                Ruleset = ruleset,
                State = decisionState,
                LegalActions = legal
            });
        if (selected?.CardInstanceId == fakeCard.InstanceId)
        {
            failures.Add("visible-fake-card:ai-selected-over-safe-card");
        }
        return failures;
    }

    internal static CombatScenarioDefinition BuildNativeEnemyScenario(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        string enemyId,
        string scenarioId)
    {
        return new CombatScenarioDefinition
        {
            ScenarioId = scenarioId,
            RulesetVersion = ruleset.Version,
            Seed = 772026UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                MaxHp = 1000,
                CurrentHp = 1000,
                BaseEnergy = 9,
                Deck = new List<string>(campaign.Player.Deck),
                Variables = new Dictionary<string, double>
                {
                    ["TempLucky"] = 0d
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = enemyId,
                    InstanceKey = scenarioId + ":enemy"
                }
            },
            InitialDraw = 5,
            DrawPerTurn = 5,
            HandLimit = 20,
            RequireAuthoritativeRules = true,
            TraceLevel = CombatSimulationTraceLevel.Full,
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 1,
                MaximumActions = 20,
                MaximumCommands = 10000,
                MaximumCommandsPerAction = 2000
            }
        };
    }

    internal static IEnumerable<string> ValidateIntegritySeedSweep(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset,
        int totalCampaigns)
    {
        var failures = new List<string>();
        var remaining = Math.Max(0, totalCampaigns);
        var seedStart = 772100UL;
        var completed = 0;
        while (remaining > 0)
        {
            var perDifficulty = Math.Min(
                100,
                Math.Max(1, (remaining + 1) / 2));
            var result = new CombatCampaignFoundationTrainer(
                new CombatCampaignRunner(
                    new CombatSimulationEngine(
                        new AuraToolsNativeRewardExtensionFactory()))).Run(
                new CombatCampaignFoundationTrainingRequest
                {
                    DecisionProfile = "native-integrity-sweep",
                    PreflightCampaignsPerDifficulty = perDifficulty,
                    PreflightSeedStart = seedStart,
                    PreflightOnly = true,
                    MaximumDegreeOfParallelism = Math.Max(
                        1,
                        Math.Min(24, Environment.ProcessorCount)),
                    Profile = IntegrityProfile(),
                    TrainingCampaign = campaign,
                    ValidationCampaign = campaign
                },
                ruleset);
            foreach (var failure in result.Preflight.Failures)
            {
                failures.Add(
                    "integrity-sweep:"
                    + failure.DifficultyId
                    + ":"
                    + failure.WorldSeed
                    + ":completed-"
                    + failure.CompletedBattles
                    + ":"
                    + string.Join(",", failure.Reasons));
            }
            var batchCampaigns = perDifficulty * 2;
            completed += batchCampaigns;
            remaining -= batchCampaigns;
            seedStart += (ulong)batchCampaigns;
        }
        Console.WriteLine(
            "Native integrity seed sweep: "
            + completed
            + " campaigns, "
            + failures.Count
            + " failures.");
        return failures;
    }

    internal static CombatDecisionSimulationPolicyFactory IntegrityPolicyFactory()
    {
        return new CombatDecisionSimulationPolicyFactory(IntegrityProfile());
    }

    internal static CombatDecisionProfile IntegrityProfile()
    {
        return new CombatDecisionProfile
        {
            Id = "native-integrity-sweep",
            SearchSimulationBudget = 16,
            SearchNodeBudget = 256,
            SearchMaxPly = 2,
            SearchMinimumSimulations = 8,
            SearchStabilityWindow = 16,
            SearchStableChecks = 1,
            SearchBudgetMode = "fixed"
        };
    }

}
