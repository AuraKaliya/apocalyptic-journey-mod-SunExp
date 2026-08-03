using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine(
        "Expected the bundled campaign and ruleset JSON paths, "
        + "plus an optional integrity sweep campaign count.");
    return 2;
}

try
{
    var integritySweepCampaigns = args.Length == 3
        ? Math.Max(0, int.Parse(args[2]))
        : 64;
    var campaign = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
        File.ReadAllText(args[0]))
        ?? throw new InvalidOperationException("Campaign JSON is empty.");
    var rulesetDocument = JsonConvert.DeserializeObject<CombatRulesetDocument>(
        File.ReadAllText(args[1]))
        ?? throw new InvalidOperationException("Ruleset JSON is empty.");
    var builder = new CombatRulesetBuilder(rulesetDocument.Version);
    foreach (var card in rulesetDocument.Cards) builder.RegisterCard(card);
    foreach (var enemy in rulesetDocument.Enemies) builder.RegisterEnemy(enemy);
    foreach (var status in rulesetDocument.Statuses) builder.RegisterStatus(status);
    var rulesetBuild = builder.Freeze();
    if (!rulesetBuild.Success)
    {
        throw new InvalidOperationException(
            "Ruleset failed to build: "
            + string.Join(" | ", rulesetBuild.Errors.Take(5)));
    }
    var subjectCatalogPath = Path.Combine(
        Path.GetDirectoryName(args[0]) ?? "",
        "witch-game-subjects-v1.catalog.json");
    var subjectCatalog = JsonConvert.DeserializeObject<CombatGameSubjectCatalog>(
                             File.ReadAllText(subjectCatalogPath))
                         ?? throw new InvalidOperationException(
                             "Game subject catalog JSON is empty.");
    subjectCatalog.Normalize();
    var nonCardRewards = campaign.Rewards
        .Where(item => item.Kind != CombatCampaignRewardKind.Card)
        .ToList();
    var authoritative = nonCardRewards.Count(item =>
        item.Fidelity == CombatRuleFidelity.Authoritative);
    var failures = AuraToolsNativeRewardScriptAudit.Validate(campaign);
    failures.AddRange(ValidateSkillTimingCatalog(
        Path.Combine(
            Path.GetDirectoryName(args[0]) ?? "",
            "witch-skill-timing-v1.catalog.json"),
        subjectCatalog));
    failures.AddRange(
        AuraToolsNativeGameScriptAudit.Validate(rulesetBuild.Ruleset));
    var packageValidation = AuraToolsNativeProgramPackageAudit.Validate(
        campaign,
        rulesetBuild.Ruleset);
    failures.AddRange(
        packageValidation.Errors.Select(item => "package: " + item));
    ValidateAuthoritativeRoleProgram(
        "career_2",
        "Partner_10003",
        "careercard_2",
        5,
        CombatSimulationEventKind.TurnStarted,
        4,
        subjectCatalog,
        campaign,
        rulesetBuild.Ruleset,
        failures);
    ValidateAuthoritativeRoleProgram(
        "career_3",
        "Partner_10005",
        "careercard_4",
        12,
        CombatSimulationEventKind.CardDrawn,
        11,
        subjectCatalog,
        campaign,
        rulesetBuild.Ruleset,
        failures);
    failures.AddRange(ValidateAuthoritativeRoleSkillSemantics());
    failures.AddRange(ValidateNanaStatusDerivedMaximumHp(
        subjectCatalog,
        campaign,
        rulesetBuild.Ruleset));
    failures.AddRange(ValidateNightmarePrototypeDuplication(
        campaign,
        rulesetBuild.Ruleset));
    failures.AddRange(ValidateAdelaWholeHandTransform(
        rulesetBuild.Ruleset));
    var dynamicPoolScenario = new CombatScenarioDefinition
    {
        ScenarioId = "dynamic-card-pool-boundary",
        Player = new CombatPlayerSetup
        {
            RoleId = campaign.Player.RoleId,
            SkillCardIds = new List<string>(
                campaign.Player.SkillCardIds)
        },
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
                CardAcquisition = item.CardAcquisition
            }).ToList()
    };
    var dynamicPoolContext = new NativePoolTestContext(
        dynamicPoolScenario,
        rulesetBuild.Ruleset);
    var dynamicPoolGlobals = new NativeRewardScriptGlobals(
        dynamicPoolContext,
        new CombatScenarioRewardRule
        {
            RewardId = "dynamic-pool-test",
            Kind = "Card"
        });
    var dynamicPoolCards = dynamicPoolGlobals
        .GetcardsByRarity(1, 99);
    var dynamicPoolIds = dynamicPoolCards
        .Select(item => item.GetValueOrDefault("Id", ""))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (dynamicPoolCards.Any(item =>
            item.GetValueOrDefault("CreationSource", "")
            != "dynamic-card-pool"))
    {
        failures.Add(
            "dynamic-card-pool: generated card candidate lacks provenance");
    }
    var foreignRoleSkills = campaign.Rewards
        .Where(item =>
            item.Kind == CombatCampaignRewardKind.Card
            && item.CardAcquisition
               == CombatCampaignCardAcquisition.SkillOnly
            && !campaign.Player.SkillCardIds.Contains(
                item.RewardId,
                StringComparer.OrdinalIgnoreCase))
        .Select(item => item.RewardId)
        .ToList();
    if (foreignRoleSkills.Any(dynamicPoolIds.Contains))
    {
        failures.Add(
            "dynamic-card-pool: foreign role SkillOnly card entered default pool");
    }
    var crossRoleGlobals = new NativeRewardScriptGlobals(
        dynamicPoolContext,
        new CombatScenarioRewardRule
        {
            RewardId = "dynamic-pool-opt-in-test",
            Kind = "Card",
            Variables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["AllowCrossRoleSkill"] = "true"
            }
        });
    var crossRolePoolIds = crossRoleGlobals
        .GetcardsByRarity(1, 99)
        .Select(item => item.GetValueOrDefault("Id", ""))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (foreignRoleSkills.Count > 0
        && !foreignRoleSkills.Any(crossRolePoolIds.Contains))
    {
        failures.Add(
            "dynamic-card-pool: explicit cross-role skill opt-in was ignored");
    }

    Console.WriteLine(
        $"Native reward scripts: {nonCardRewards.Count} rewards, "
        + $"{authoritative} authoritative, {failures.Count} package failures, "
        + $"{packageValidation.ReferencedProgramCount} referenced programs, "
        + $"{packageValidation.PrecompiledProgramCount} precompiled programs.");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }
    var runtimeFailures = new List<string>();
    runtimeFailures.AddRange(
        ValidateDivineChoiceActionContract(rulesetBuild.Ruleset));
    var smokeEnemy = rulesetBuild.Ruleset.SnapshotEnemies()
        .OrderBy(item => item.EnemyId, StringComparer.Ordinal)
        .First();
    if (foreignRoleSkills.Count > 0)
    {
        var leakedSkillScenario = CombatScenarioCloner.Clone(
            dynamicPoolScenario);
        leakedSkillScenario.ScenarioId =
            "cross-role-skill-audit";
        leakedSkillScenario.Player.MaxHp = 100;
        leakedSkillScenario.Player.CurrentHp = 100;
        leakedSkillScenario.Player.BaseEnergy = 0;
        leakedSkillScenario.Player.Deck =
            new List<string> { foreignRoleSkills[0] };
        leakedSkillScenario.Enemies =
            new List<CombatEnemySetup>
            {
                new()
                {
                    EnemyId = smokeEnemy.EnemyId,
                    InstanceKey = "cross-role-skill-audit-enemy"
                }
            };
        leakedSkillScenario.InitialDraw = 1;
        leakedSkillScenario.DrawPerTurn = 1;
        leakedSkillScenario.Limits = new CombatSimulationLimits
        {
            MaximumTurns = 1,
            MaximumActions = 5,
            MaximumCommands = 100
        };
        var leakedSkillResult = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(
                leakedSkillScenario,
                rulesetBuild.Ruleset,
                new EndTurnPolicy());
        var leakedSkillIsolated =
            leakedSkillResult.Outcome == CombatSimulationOutcome.Invalid
            && (leakedSkillResult.TerminationReason
                    == CombatTerminationReason.InvalidScenario
                || leakedSkillResult.UnsupportedDefinitions.Any(item =>
                    item.StartsWith(
                        "cross-role-skill-card:",
                        StringComparison.OrdinalIgnoreCase)));
        if (!leakedSkillIsolated)
        {
            failures.Add(
                "dynamic-card-pool: provenance audit did not isolate an unauthorized foreign role skill:"
                + leakedSkillResult.Outcome
                + ":"
                + leakedSkillResult.TerminationReason
                + ":"
                + string.Join(",", leakedSkillResult.UnsupportedDefinitions)
                + ":"
                + string.Join(
                    ",",
                    leakedSkillResult.FinalState.Cards.Select(card =>
                        card.CardId
                        + "/"
                        + card.CreationSource
                        + "/authorized="
                        + card.CreationCrossRoleSkillAuthorized)));
        }
    }
    foreach (var reward in nonCardRewards.Where(item =>
                 !string.IsNullOrWhiteSpace(item.FightScript)))
    {
        var scenario = new CombatScenarioDefinition
        {
            ScenarioId = "reward-smoke:" + reward.RewardId,
            RulesetVersion = rulesetBuild.Ruleset.Version,
            Seed = 772024UL,
            Player = new CombatPlayerSetup
            {
                RoleId = campaign.Player.RoleId,
                SkillCardIds = new List<string>(
                    campaign.Player.SkillCardIds),
                MaxHp = 100,
                CurrentHp = 100,
                BaseEnergy = 99,
                Deck = new List<string>(campaign.Player.Deck),
                Variables = new Dictionary<string, double>
                {
                    ["Strength"] = 40,
                    ["Wisdom"] = 39,
                    ["Perceive"] = 40,
                    ["Lucky"] = 40,
                    ["Money"] = 100,
                    ["EncounterKind"] = 2
                }
            },
            Enemies =
            {
                new CombatEnemySetup
                {
                    EnemyId = smokeEnemy.EnemyId,
                    InstanceKey = "reward-smoke-enemy",
                    HpScale = 50d
                }
            },
            InitialDraw = 10,
            DrawPerTurn = 10,
            HandLimit = 20,
            RequireAuthoritativeRules = false,
            TraceLevel = CombatSimulationTraceLevel.Summary,
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
            EnabledRewardCardPackIds = new List<string>(
                campaign.EnabledRewardCardPackIds),
            Limits = new CombatSimulationLimits
            {
                MaximumTurns = 2,
                MaximumActions = 100,
                MaximumCommands = 10000
            },
            RewardRules =
            {
                new CombatScenarioRewardRule
                {
                    RewardId = reward.RewardId,
                    Kind = reward.Kind.ToString(),
                    Stacks = 1,
                    NativeScriptHash = reward.NativeScriptHash,
                    FightScript = reward.FightScript,
                    Variables = new Dictionary<string, string>(
                        reward.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                }
            }
        };
        var result = new CombatSimulationEngine(
            new AuraToolsNativeRewardExtensionFactory())
            .Run(
                scenario,
                rulesetBuild.Ruleset,
                new SmokePolicy());
        var rewardUnsupported = result.UnsupportedDefinitions
            .Where(item => item.StartsWith(
                "reward-",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (result.TerminationReason == CombatTerminationReason.EngineError
            || rewardUnsupported.Count > 0)
        {
            var diagnostics = result.TerminationReason
                              == CombatTerminationReason.EngineError
                ? result.UnsupportedDefinitions
                : rewardUnsupported;
            runtimeFailures.Add(
                reward.RewardId
                + ": "
                + result.TerminationReason
                + " "
                + string.Join(",", diagnostics));
        }
    }
    runtimeFailures.AddRange(ValidateHardAffixes(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateNativeGameRuntime(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateNativeCombatSemantics(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateIndirectScriptExecution(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateDrawPileSnapshotExecution(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateFullHandGeneratedCardOverflow(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateDeferredEffectSafety(
        campaign,
        rulesetBuild.Ruleset,
        smokeEnemy));
    runtimeFailures.AddRange(ValidateRandomStatusPoolSemantics(
        campaign,
        rulesetBuild.Ruleset));
    runtimeFailures.AddRange(ValidateVisibleFakeCardSemantics(
        campaign,
        rulesetBuild.Ruleset));
    runtimeFailures.AddRange(ValidateKnownIntegritySeeds(
        campaign,
        rulesetBuild.Ruleset));
    runtimeFailures.AddRange(ValidateFinalBossAuthority(
        rulesetBuild.Ruleset));
    runtimeFailures.AddRange(ValidateIntegritySeedSweep(
        campaign,
        rulesetBuild.Ruleset,
        integritySweepCampaigns));
    runtimeFailures.AddRange(ValidateProgressionSemantics(campaign));
    Console.WriteLine(
        $"Native reward runtime smoke: {runtimeFailures.Count} failures.");
    foreach (var failure in runtimeFailures)
    {
        Console.Error.WriteLine(failure);
    }
    foreach (var failure in failures)
    {
        if (!packageValidation.Errors.Contains(
                failure,
                StringComparer.Ordinal))
        {
            Console.Error.WriteLine(failure);
        }
    }

    return failures.Count == 0
           && runtimeFailures.Count == 0
           && authoritative == nonCardRewards.Count
        ? 0
        : 1;
}

catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().FullName);
    Console.Error.WriteLine(ex.Message);
    if (ex.InnerException != null)
    {
        Console.Error.WriteLine(ex.InnerException.GetType().FullName);
        Console.Error.WriteLine(ex.InnerException.Message);
    }
    return 3;
}

static void ValidateAuthoritativeRoleProgram(
    string roleId,
    string partnerId,
    string skillCardId,
    int startingCooldown,
    CombatSimulationEventKind eventKind,
    int expectedCooldown,
    CombatGameSubjectCatalog catalog,
    CombatCampaignDefinition campaignTemplate,
    CombatRuleset ruleset,
    ICollection<string> failures)
{
    var subject = new CombatGameSubjectPreset
    {
        Id = "native-role-test-" + roleId,
        RoleId = roleId,
        PartnerId = partnerId
    };
    catalog.ResolveReferences(subject);
    var campaign = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
                       JsonConvert.SerializeObject(campaignTemplate))
                   ?? new CombatCampaignDefinition();
    CombatGameSubjectPresetRuntime.Apply(subject, campaign);
    var audit = AuraToolsNativeProgramPackageAudit.Validate(campaign, ruleset);
    if (!audit.Success)
    {
        failures.Add(
            "native-role-package:" + roleId + ":" + string.Join("|", audit.Errors));
        return;
    }
    if (campaign.Player.InitialStatuses.Count != 0)
    {
        failures.Add(
            "native-role-initialization:" + roleId
            + ": declarative statuses would duplicate the native role script");
        return;
    }

    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "native-role-event-" + roleId,
        Player = campaign.Player,
        CampaignVariables = campaign.Player.Variables.ToDictionary(
            item => item.Key,
            item => item.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparer.OrdinalIgnoreCase)
    };
    var context = new NativePoolTestContext(scenario, ruleset);
    context.State.PlayerActorId = 1;
    context.State.Actors.Add(new CombatActorState
    {
        ActorId = 1,
        DefinitionId = roleId,
        Kind = CombatSimulationActorKind.Player,
        Hp = campaign.Player.MaxHp,
        MaxHp = campaign.Player.MaxHp
    });
    context.State.Cards.Add(new CombatCardInstanceState
    {
        InstanceId = 1,
        CardId = skillCardId,
        CreationSource = "role-skill",
        CreationSourceId = roleId
    });
    context.State.SkillCards.Add(1);
    context.State.SkillCooldowns[1] = startingCooldown;
    var rule = new CombatScenarioRewardRule
    {
        RewardId = roleId,
        Kind = "Role",
        NativeScriptHash = campaign.Player.RoleNativeScriptHash,
        FightScript = campaign.Player.RoleFightScript
    };
    var globals = new NativeRewardScriptGlobals(context, rule);
    var execution = globals.RunScript(rule, null);
    if (!execution.Success)
    {
        failures.Add(
            "native-role-execution:" + roleId + ":" + execution.Message);
        return;
    }
    context.State.SkillCooldowns[1] = startingCooldown;
    globals.Dispatch(new CombatSimulationEvent
    {
        Kind = eventKind,
        SourceActorId = 1,
        TargetActorId = 1,
        DefinitionId = eventKind == CombatSimulationEventKind.CardDrawn
            ? "nocard_1"
            : roleId
    });
    if (context.State.SkillCooldowns[1] != expectedCooldown)
    {
        failures.Add(
            "native-role-cooldown:" + roleId
            + ": expected=" + expectedCooldown
            + ", actual=" + context.State.SkillCooldowns[1]);
    }
}

static IEnumerable<string> ValidateAuthoritativeRoleSkillSemantics()
{
    var failures = new List<string>();
    AuraToolsAuthoritativeRoleSemantics.Initialize();
    failures.AddRange(
        AuraToolsAuthoritativeRoleSemantics
            .ValidateFrozenTrainingPreparation()
            .Select(error => "frozen-role-preparation:" + error));
    var coverageState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_1",
            Kind = CombatTargetKind.Self,
            CurrentHp = 70,
            MaxHp = 100,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_bleeding",
                    Level = 4,
                    Rarity = 2,
                    Type = "Negative"
                },
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 10,
                    Rarity = 4,
                    Type = "Special"
                }
            }
        },
        CurrentPower = 3,
        MaxPower = 3,
        HandCount = 1,
        HandCards =
        {
            new CombatCardInstanceObservation
            {
                RuntimeId = 10,
                CardId = "fixture-core-card",
                EffectiveCost = 1,
                Retained = true,
                EnhancementCount = 1
            }
        },
        DeckCardIds = { "fixture-core-card", "fixture-sacrifice-card" },
        DeckKnowledge = new CombatDeckKnowledge { DrawPileCount = 2 },
        Features =
        {
            ["handLimit"] = 10d,
            ["drawPileCount"] = 2d,
            [CombatCampaignContextFeatureNames.RemainingBattles] = 8d
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                DefinitionId = "fixture-enemy",
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 120,
                MaxHp = 120,
                Features = { ["actionCount"] = 2d },
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_bleeding",
                        Level = 8,
                        Rarity = 2,
                        Type = "Negative"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_fixture_positive",
                        Level = 3,
                        Rarity = 2,
                        Type = "Positive"
                    }
                }
            }
        },
        Threat = new CombatThreatForecast
        {
            CurrentIntentKnown = true,
            Intents =
            {
                new CombatIntentObservation
                {
                    SourceRuntimeId = 2,
                    Kind = CombatIntentKind.Attack,
                    BlockableDamage = 18d,
                    Probability = 1d,
                    Current = true
                }
            }
        },
        ExpectedIncomingDamage = 18d
    };
    foreach (var pair in new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
             {
                 ["careercard_1"] = 1,
                 ["careercard_2"] = 5,
                 ["careercard_3"] = 2,
                 ["careercard_4"] = 12,
                 ["careercard_5"] = 3,
                 ["careercard_6"] = 3,
                 ["careercard_7"] = 2,
                 ["careercard_8"] = 5,
                 ["careercard_9"] = 2,
                 ["careercard_10"] = 2,
                 ["careercard_11"] = 1,
                 ["careercard_12"] = 2,
                 ["careercard_13"] = 99
             })
    {
        var action = new CombatActionObservation
        {
            CandidateId = "timing-coverage-" + pair.Key,
            SourceId = pair.Key,
            Kind = CombatActionKind.UseSkill,
            TargetRuntimeId = pair.Key == "careercard_2" ? 1 : 2,
            TargetKind = pair.Key == "careercard_2"
                ? CombatTargetKind.Self
                : CombatTargetKind.Enemy
        };
        coverageState.Actions.Clear();
        coverageState.Actions.Add(action);
        CombatAiRegistry.ApplySemantics(coverageState, action);
        CombatAiRegistry.EnrichSkillTimings(coverageState);
        if (action.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
            || action.Features.GetValueOrDefault(
                CombatSkillTimingFeatureNames.Active) != 1d
            || action.Features.GetValueOrDefault(
                CombatSkillTimingFeatureNames.CooldownAfterUse) != pair.Value
            || double.IsNaN(action.Features.GetValueOrDefault(
                CombatSkillTimingFeatureNames.TimingAdvantage)))
        {
            failures.Add("base-skill-timing-coverage:" + pair.Key);
        }
    }
    var lowChoice = new CombatActionObservation
    {
        SourceId = "fixture-low-card",
        Semantics = new CombatActionSemantics { Damage = 1d },
        Features =
        {
            ["choice:cost"] = 0d,
            ["choice:rarity"] = 1d
        }
    };
    var highChoice = new CombatActionObservation
    {
        SourceId = "fixture-high-card",
        Semantics = new CombatActionSemantics
        {
            Damage = 18d,
            Draw = 2d,
            Scaling = 3d
        },
        Features =
        {
            ["choice:cost"] = 3d,
            ["choice:rarity"] = 4d
        }
    };
    foreach (var skillId in new[]
             {
                 "careercard_1",
                 "careercard_9",
                 "careercard_12",
                 "careercard_13"
             })
    {
        var skillAction = new CombatActionObservation
        {
            SourceId = skillId,
            Kind = CombatActionKind.UseSkill
        };
        AuraToolsWitchSkillInteraction.Prepare(coverageState, skillAction);
        var hint = CombatInteractionBroker.ConsumeNextHint(
            new CombatInteractionHint());
        var lowScore = 0d;
        var highScore = 0d;
        var hasLow = hint.ChoiceScorer?.TryScore(
            hint,
            lowChoice,
            out lowScore) == true;
        var hasHigh = hint.ChoiceScorer?.TryScore(
            hint,
            highChoice,
            out highScore) == true;
        var shouldPreferLow = skillId == "careercard_9";
        if (hint.SourceId != skillId
            || hint.ChoiceScorer == null
            || !hasLow
            || !hasHigh
            || (shouldPreferLow ? lowScore <= highScore : highScore <= lowScore))
        {
            failures.Add("base-skill-choice-policy:" + skillId);
        }
    }
    foreach (var test in new[]
             {
                 (Soul: 99, Tier: 1, CardId: "nocard_1", Amount: 16d),
                 (Soul: 100, Tier: 2, CardId: "nocard_2", Amount: 18d),
                 (Soul: 199, Tier: 2, CardId: "nocard_2", Amount: 30d),
                 (Soul: 200, Tier: 3, CardId: "nocard_3", Amount: 33d)
             })
    {
        var state = new CombatStateObservation
        {
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                Kind = CombatTargetKind.Self,
                CurrentHp = 95,
                MaxHp = 95,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_Soul",
                        Level = test.Soul
                    }
                }
            },
            Enemies =
            {
                new CombatUnitObservation
                {
                    RuntimeId = 2,
                    Kind = CombatTargetKind.Enemy,
                    CurrentHp = 1000,
                    MaxHp = 1000
                }
            }
        };
        var action = new CombatActionObservation
        {
            CandidateId = "royal-command-" + test.Soul,
            SourceId = "careercard_4",
            Kind = CombatActionKind.UseSkill
        };
        CombatAiRegistry.ApplySemantics(state, action);
        var transform = action.Semantics.HandTransform;
        if (action.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
            || transform == null
            || transform.TargetTier != test.Tier
            || !string.Equals(
                transform.TargetCardId,
                test.CardId,
                StringComparison.OrdinalIgnoreCase)
            || transform.TargetCardSemantics.Damage != test.Amount
            || transform.TargetCardSemantics.TrueDamage
               != (test.Tier >= 2 ? test.Amount : 0d)
            || action.Semantics.Damage != 0d
            || !transform.TransformAllHandCards
            || !transform.PreserveInstances
            || !transform.ClearsEnhancements
            || !transform.TargetRetained
            || !transform.TargetExhaustsOnUse)
        {
            failures.Add(
                "royal-command-semantics:soul=" + test.Soul);
        }
    }

    var nanaState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 70,
            MaxHp = 115,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 10,
                    Type = "Special"
                },
                new CombatStatusObservation
                {
                    StatusId = "buff_burn",
                    Level = 5,
                    Rarity = 2,
                    Type = "Negative"
                }
            }
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                DefinitionId = "fixture-enemy",
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 50,
                MaxHp = 50,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 10,
                        Rarity = 1,
                        Type = "Negative"
                    }
                }
            }
        }
    };
    var selfDevour = new CombatActionObservation
    {
        CandidateId = "nana-devour-self",
        SourceId = "careercard_2",
        Kind = CombatActionKind.UseSkill,
        TargetRuntimeId = 1,
        TargetKind = CombatTargetKind.Self
    };
    CombatAiRegistry.ApplySemantics(nanaState, selfDevour);
    if (selfDevour.SemanticFidelity != CombatKnowledgeFidelity.Authoritative
        || selfDevour.Semantics.Damage != 0d
        || selfDevour.Semantics.Cleanse != 5d
        || selfDevour.Semantics.Scaling != 2d
        || selfDevour.Semantics.Heal != 12d
        || selfDevour.Semantics.PersistentValue != 12d
        || selfDevour.Semantics.CooldownTurns != 5d
        || selfDevour.Semantics.StateChanges.GetValueOrDefault(
            "player.hp") != 12d
        || selfDevour.Semantics.TargetEffects.SingleOrDefault(effect =>
               effect.Kind == CombatSemanticEffectKind.Heal)
           ?.TargetRuntimeId != 1
        || selfDevour.Features.GetValueOrDefault(
            "nana:projected-doom-gain") != 2d)
    {
        failures.Add("nana-devour-self-semantics");
    }
    var enemyDevour = new CombatActionObservation
    {
        CandidateId = "nana-devour-enemy",
        SourceId = "careercard_2",
        Kind = CombatActionKind.UseSkill,
        TargetRuntimeId = 2,
        TargetKind = CombatTargetKind.Enemy
    };
    CombatAiRegistry.ApplySemantics(nanaState, enemyDevour);
    if (enemyDevour.Semantics.Damage != 5d
        || enemyDevour.Semantics.Heal != 12d
        || enemyDevour.Semantics.Cleanse != 0d
        || enemyDevour.Semantics.Risk != 12d
        || enemyDevour.Semantics.TargetEffects.SingleOrDefault(effect =>
               effect.Kind == CombatSemanticEffectKind.Heal)
           ?.TargetRuntimeId != 1
        || enemyDevour.Features.GetValueOrDefault(
            "nana:enemy-cleanse-cost") != 12d)
    {
        failures.Add("nana-devour-enemy-semantics");
    }
    var growthState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 880,
            MaxHp = 880,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 40,
                    Type = "Special"
                }
            }
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                DefinitionId = "growth-fixture-enemy",
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 100,
                MaxHp = 100,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 1,
                        Rarity = 1,
                        Type = "Negative"
                    }
                }
            }
        },
        CurrentPower = 2,
        MaxPower = 3,
        ExpectedIncomingDamage = 0,
        Features =
        {
            [CombatCampaignContextFeatureNames.ContextKnown] = 1d,
            [CombatCampaignContextFeatureNames.Progress] = 0.25d,
            [CombatCampaignContextFeatureNames.FinalBoss] = 0d
        }
    };
    var growthDevour = new CombatActionObservation
    {
        CandidateId = "nana-growth-devour",
        SourceId = "careercard_2",
        Kind = CombatActionKind.UseSkill,
        TargetRuntimeId = 2,
        TargetKind = CombatTargetKind.Enemy,
        Cost = 0
    };
    var growthBuilder = new CombatActionObservation
    {
        CandidateId = "nana-growth-builder",
        SourceId = "burningcard_2",
        Kind = CombatActionKind.PlayCard,
        RuntimeId = 30,
        TargetRuntimeId = 2,
        TargetKind = CombatTargetKind.Enemy,
        Cost = 1,
        Semantics = new CombatActionSemantics
        {
            Damage = 6d,
            Debuff = 2d
        }
    };
    growthState.Actions.Add(growthDevour);
    growthState.Actions.Add(growthBuilder);
    CombatAiRegistry.ApplySemantics(growthState, growthDevour);
    CombatAiRegistry.EnrichRoleStrategies(growthState);
    if (growthDevour.Features.GetValueOrDefault(
            "nana:projected-doom-gain") != 1d
        || growthDevour.Features.GetValueOrDefault(
            "nana:projected-max-hp-gain") != 41d
        || growthDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d
        || growthBuilder.Features.GetValueOrDefault(
            "roleStrategy:nana.growth-builder") != 1d
        || growthState.Features.GetValueOrDefault(
            "roleStrategy:nana.safe-growth-window") != 1d
        || growthState.Features.GetValueOrDefault(
            "roleStrategy:nana.growth-target-doom") != 15d)
    {
        failures.Add("nana-safe-growth-window-sequencing");
    }
    growthState.Actions.Remove(growthBuilder);
    growthDevour.Features.Clear();
    CombatAiRegistry.ApplySemantics(growthState, growthDevour);
    CombatAiRegistry.EnrichRoleStrategies(growthState);
    if (growthDevour.Features.GetValueOrDefault(
            "nana:conservative-devour-target") != 0d
        || growthDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
    {
        failures.Add("nana-single-kind-enemy-devour-conservative-gate");
    }
    growthState.Enemies[0].Statuses.Add(new CombatStatusObservation
    {
        StatusId = "buff_toxin",
        Level = 1,
        Rarity = 1,
        Type = "Negative"
    });
    growthDevour.Features.Clear();
    CombatAiRegistry.ApplySemantics(growthState, growthDevour);
    CombatAiRegistry.EnrichRoleStrategies(growthState);
    if (growthDevour.Features.GetValueOrDefault(
            "nana:projected-doom-gain") != 2d
        || growthDevour.Features.GetValueOrDefault(
            "nana:devour-event-max-hp-gain") != 42d
        || growthDevour.Features.GetValueOrDefault(
            "roleStrategy:nana.preferred-harvest") != 1d
        || growthDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d
        || growthDevour.Features.GetValueOrDefault(
            "nana:devour-net-value") <= 0d)
    {
        failures.Add("nana-two-kind-enemy-devour-net-value");
    }
    var firstTransform = new CombatActionObservation
    {
        CandidateId = "nana-transform-first",
        SourceId = "careercard_3",
        Kind = CombatActionKind.UseSkill
    };
    CombatAiRegistry.ApplySemantics(nanaState, firstTransform);
    if (firstTransform.SemanticFidelity
        != CombatKnowledgeFidelity.Authoritative
        || firstTransform.Semantics.SelfHpLoss != 0d
        || firstTransform.Semantics.Damage != 2d
        || firstTransform.Semantics.Buff != 20d
        || firstTransform.Semantics.Scaling != 10d
        || firstTransform.Semantics.StateChanges.GetValueOrDefault(
            "playerMaxHp") != 0d
        || firstTransform.Features.GetValueOrDefault(
            "nana:first-transform") != 1d)
    {
        failures.Add("nana-first-transform-semantics");
    }
    nanaState.Player.DefinitionId = "career_4";
    nanaState.Player.CurrentHp = 51;
    nanaState.Player.MaxHp = 95;
    var repeatTransform = new CombatActionObservation
    {
        CandidateId = "nana-transform-repeat",
        SourceId = "careercard_3",
        Kind = CombatActionKind.UseSkill
    };
    CombatAiRegistry.ApplySemantics(nanaState, repeatTransform);
    if (repeatTransform.Semantics.SelfHpLoss != 0d
        || repeatTransform.Semantics.Damage != 1d
        || repeatTransform.Semantics.Buff != 0d
        || repeatTransform.Semantics.StateChanges.GetValueOrDefault(
            "playerMaxHp") != 0d
        || repeatTransform.Features.GetValueOrDefault(
            "nana:repeat-transform") != 1d)
    {
        failures.Add("nana-repeat-transform-semantics");
    }
    nanaState.Actions.Add(repeatTransform);
    CombatAiRegistry.EnrichRoleStrategies(nanaState);
    if (repeatTransform.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
    {
        failures.Add("nana-repeat-transform-strategy-gate");
    }
    nanaState.Player.DefinitionId = "transient-form";
    nanaState.Player.Statuses.Add(new CombatStatusObservation
    {
        StatusId = "SpecialBuff_CalamityIncarnates",
        Level = 1,
        Type = "Special"
    });
    repeatTransform.Features.Clear();
    CombatAiRegistry.ApplySemantics(nanaState, repeatTransform);
    CombatAiRegistry.EnrichRoleStrategies(nanaState);
    if (repeatTransform.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.Active) != 1d
        || repeatTransform.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
    {
        failures.Add("nana-calamity-status-preserves-role-identity");
    }

    var nightmareState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 100,
            MaxHp = 120,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 10,
                    Type = "Special"
                }
            }
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                DefinitionId = "nightmare-fixture-enemy",
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 80,
                MaxHp = 80,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 1,
                        Rarity = 2,
                        Type = "Negative"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_toxin",
                        Level = 1,
                        Rarity = 1,
                        Type = "Negative"
                    }
                }
            }
        },
        CurrentPower = 2,
        MaxPower = 3,
        Features =
        {
            ["blessing:blessing_40"] = 1d
        }
    };
    var nightmareBuilder = new CombatActionObservation
    {
        CandidateId = "nightmare-two-events",
        SourceId = "fixture-two-debuffs",
        Kind = CombatActionKind.PlayCard,
        RuntimeId = 30,
        TargetRuntimeId = 2,
        TargetKind = CombatTargetKind.Enemy,
        Cost = 1,
        Semantics = new CombatActionSemantics
        {
            Debuff = 4d,
            TargetEffects =
            {
                new CombatTargetedSemanticEffect
                {
                    Kind = CombatSemanticEffectKind.AddStatus,
                    TargetRuntimeId = 2,
                    DefinitionId = "buff_burn",
                    RawAmount = 3d,
                    EffectiveAmount = 3d,
                    Probability = 1d
                },
                new CombatTargetedSemanticEffect
                {
                    Kind = CombatSemanticEffectKind.AddStatus,
                    TargetRuntimeId = 2,
                    DefinitionId = "buff_toxin",
                    RawAmount = 1d,
                    EffectiveAmount = 1d,
                    Probability = 1d
                }
            }
        }
    };
    var nightmareDevour = new CombatActionObservation
    {
        CandidateId = "nightmare-devour",
        SourceId = "careercard_2",
        Kind = CombatActionKind.UseSkill,
        TargetRuntimeId = 2,
        TargetKind = CombatTargetKind.Enemy
    };
    nightmareState.Actions.Add(nightmareBuilder);
    nightmareState.Actions.Add(nightmareDevour);
    CombatAiRegistry.ApplySemantics(nightmareState, nightmareDevour);
    CombatAiRegistry.EnrichRoleStrategies(nightmareState);
    if (nightmareBuilder.Features.GetValueOrDefault(
            "nightmare:eligible-negative-events") != 2d
        || Math.Abs(nightmareBuilder.Features.GetValueOrDefault(
            "nightmare:expected-extra-stacks") - 0.4d) > 0.000001d
        || Math.Abs(nightmareBuilder.Features.GetValueOrDefault(
            "nightmare:expected-devour-threshold-gain") - 0.2d) > 0.000001d
        || nightmareBuilder.Features.GetValueOrDefault(
            "roleStrategy:nana.priority-builder") != 1d
        || nightmareDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 1d)
    {
        failures.Add(
            "nana-nightmare-event-and-threshold-strategy:events="
            + nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:eligible-negative-events")
            + ",extra="
            + nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:expected-extra-stacks")
            + ",threshold="
            + nightmareBuilder.Features.GetValueOrDefault(
                "nightmare:expected-devour-threshold-gain")
            + ",priority="
            + nightmareBuilder.Features.GetValueOrDefault(
                "roleStrategy:nana.priority-builder")
            + ",devourProhibited="
            + nightmareDevour.Features.GetValueOrDefault(
                CombatRoleStrategyFeatureNames.StrategicallyProhibited));
    }
    nightmareBuilder.Semantics.RandomOutcome = true;
    nightmareBuilder.Semantics.Uncertainty = 0.5d;
    nightmareBuilder.Features.Clear();
    nightmareDevour.Features.Clear();
    CombatAiRegistry.ApplySemantics(nightmareState, nightmareDevour);
    CombatAiRegistry.EnrichRoleStrategies(nightmareState);
    if (nightmareDevour.Features.GetValueOrDefault(
            "roleStrategy:nana.defer-harvest-random-builder") != 1d
        || nightmareDevour.Features.GetValueOrDefault(
            "roleStrategy:nana.defer-harvest-same-turn") != 0d
        || nightmareDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d)
    {
        failures.Add("nana-random-builder-remains-soft-guidance");
    }

    var burstState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 100,
            MaxHp = 120,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 10,
                    Type = "Special"
                }
            }
        },
        Enemies =
        {
            new CombatUnitObservation
            {
                RuntimeId = 2,
                Kind = CombatTargetKind.Enemy,
                CurrentHp = 100,
                MaxHp = 100
            }
        },
        CurrentPower = 2,
        MaxPower = 3
    };
    var burstTransform = new CombatActionObservation
    {
        CandidateId = "burst-transform",
        SourceId = "careercard_3",
        Kind = CombatActionKind.UseSkill
    };
    burstState.Actions.Add(burstTransform);
    burstState.Actions.Add(new CombatActionObservation
    {
        CandidateId = "burst-card-a",
        SourceId = "fixture-a",
        Kind = CombatActionKind.PlayCard,
        RuntimeId = 31,
        Cost = 1
    });
    burstState.Actions.Add(new CombatActionObservation
    {
        CandidateId = "burst-card-b",
        SourceId = "fixture-b",
        Kind = CombatActionKind.PlayCard,
        RuntimeId = 32,
        Cost = 1
    });
    CombatAiRegistry.ApplySemantics(burstState, burstTransform);
    CombatAiRegistry.EnrichRoleStrategies(burstState);
    if (burstTransform.Features.GetValueOrDefault(
            "nana:post-transform-max-hp") != 120d
        || burstTransform.Features.GetValueOrDefault(
            "nana:post-transform-damage-per-action") != 2d
        || burstTransform.Features.GetValueOrDefault(
            "nana:executable-burst-actions") != 2d
         || burstTransform.Features.GetValueOrDefault(
             "roleStrategy:nana.transform-ready") != 1d
         || burstTransform.Features.GetValueOrDefault(
             CombatSkillTimingFeatureNames.PositiveOpportunity) != 1d
         || burstTransform.Features.GetValueOrDefault(
             CombatSkillTimingFeatureNames.TimingAdvantage) <= 0d
         || burstTransform.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited) != 0d)
    {
        failures.Add("nana-transform-threshold-and-executable-burst");
    }

    burstState.Player.MaxHp = 60;
    burstState.Player.CurrentHp = 60;
    burstTransform.Features.Clear();
    CombatAiRegistry.ApplySemantics(burstState, burstTransform);
    CombatAiRegistry.EnrichRoleStrategies(burstState);
    if (burstTransform.Features.GetValueOrDefault(
            "nana:post-transform-max-hp") != 60d
        || burstTransform.Features.GetValueOrDefault(
            "nana:post-transform-damage-per-action") != 1d
        || burstTransform.Features.GetValueOrDefault(
            "nana:next-transform-damage-threshold-max-hp") != 100d)
    {
        failures.Add("nana-transform-preserves-low-max-hp-threshold");
    }

    var survivalState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 10,
            MaxHp = 100
        },
        CurrentPower = 1,
        MaxPower = 3,
        ExpectedIncomingDamage = 20d
    };
    var survivalAction = new CombatActionObservation
    {
        CandidateId = "nana-survival-block",
        SourceId = "fixture-block",
        Kind = CombatActionKind.PlayCard,
        TargetRuntimeId = 1,
        TargetKind = CombatTargetKind.Self,
        Cost = 1,
        Semantics = new CombatActionSemantics { Defend = 25d }
    };
    survivalState.Actions.Add(survivalAction);
    CombatAiRegistry.EnrichRoleStrategies(survivalState);
    if (survivalState.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.Phase) != 4d
        || survivalState.Features.GetValueOrDefault(
            "roleStrategy:nana.survival-override") != 1d
        || survivalAction.Features.GetValueOrDefault(
            "roleStrategy:nana.survival-action") != 1d
        || survivalAction.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.Synergy) <= 6d)
    {
        failures.Add("nana-survival-override-priority");
    }

    var finaleState = new CombatStateObservation
    {
        Player = new CombatUnitObservation
        {
            RuntimeId = 1,
            DefinitionId = "career_2",
            Kind = CombatTargetKind.Self,
            CurrentHp = 80,
            MaxHp = 120,
            Statuses =
            {
                new CombatStatusObservation
                {
                    StatusId = "buff_DoomPower",
                    Level = 12,
                    Type = "Special"
                },
                new CombatStatusObservation
                {
                    StatusId = "buff_burn",
                    Level = 5,
                    Rarity = 2,
                    Type = "Negative"
                }
            }
        },
        CurrentPower = 3,
        MaxPower = 3,
        HandCount = 3,
        HandCardIds =
        {
            "Crowdfundingcard_43",
            "attack-a",
            "attack-b"
        },
        DeckCardIds =
        {
            "Crowdfundingcard_43",
            "attack-a",
            "attack-b",
            "attack-c",
            "attack-d",
            "attack-e",
            "attack-f"
        },
        Actions =
        {
            new CombatActionObservation
            {
                CandidateId = "nana-finale",
                SourceId = "Crowdfundingcard_43",
                Kind = CombatActionKind.PlayCard,
                RuntimeId = 10,
                Cost = 3
            },
            new CombatActionObservation
            {
                CandidateId = "nana-finale-devour",
                SourceId = "careercard_2",
                Kind = CombatActionKind.UseSkill,
                RuntimeId = 20,
                TargetRuntimeId = 1,
                TargetKind = CombatTargetKind.Self,
                Cost = 0
            },
            new CombatActionObservation
            {
                CandidateId = "nana-finale-transform",
                SourceId = "careercard_3",
                Kind = CombatActionKind.UseSkill,
                RuntimeId = 21,
                Cost = 0
            }
        }
    };
    foreach (var action in finaleState.Actions)
    {
        CombatAiRegistry.ApplySemantics(finaleState, action);
    }
    CombatAiRegistry.EnrichRoleStrategies(finaleState);
    CombatArchetypePolicy.Enrich(finaleState);
    var finaleAction = finaleState.Actions.Single(action =>
        action.SourceId == "Crowdfundingcard_43");
    var finaleLegal = CombatArchetypePolicy.IsLegal(
        finaleState,
        finaleAction,
        out var finaleReason);
    var finaleDevour = finaleState.Actions.Single(action =>
        action.SourceId == "careercard_2");
    var finaleTransform = finaleState.Actions.Single(action =>
        action.SourceId == "careercard_3");
    var finaleTransformLegal = CombatArchetypePolicy.IsLegal(
        finaleState,
        finaleTransform,
        out _);
    if (!finaleLegal
        || finaleAction.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.SafeContinuationCertified) != 1d
        || finaleAction.Semantics.EndOfCycleSelfHpLoss != 0d
        || finaleDevour.Features.GetValueOrDefault(
            CombatRoleStrategyFeatureNames.Synergy) <= 0d
        || finaleTransform.Features.GetValueOrDefault(
            CombatSkillTimingFeatureNames.Active) != 1d
        || finaleTransform.Features.GetValueOrDefault(
            CombatSkillTimingFeatureNames.WaitValue) <= 0d
        || !finaleTransformLegal)
    {
        failures.Add(
            "nana-finale-role-strategy:legal="
            + finaleLegal
            + ", reason="
            + finaleReason);
    }
    var unsafeFinaleState = CombatPlayerObservationBoundary.Normalize(
        finaleState);
    unsafeFinaleState.Actions.RemoveAll(action =>
        action.SourceId == "careercard_2");
    foreach (var action in unsafeFinaleState.Actions)
    {
        action.Features.Remove(
            CombatRoleStrategyFeatureNames.SafeContinuationCertified);
    }
    CombatAiRegistry.EnrichRoleStrategies(unsafeFinaleState);
    CombatArchetypePolicy.Enrich(unsafeFinaleState);
    var unsafeFinale = unsafeFinaleState.Actions.Single(action =>
        action.SourceId == "Crowdfundingcard_43");
    if (CombatArchetypePolicy.IsLegal(
            unsafeFinaleState,
            unsafeFinale,
            out _))
    {
        failures.Add("nana-finale-without-devour-was-legal");
    }
    var nanaCampaignDefaults = new CombatCampaignDefinition
    {
        Player = new CombatPlayerSetup
        {
            RoleId = "career_2",
            FamiliarBlessingIds = { "blessing_40" }
        }
    };
    AuraToolsRoleCampaignStrategy.Apply(nanaCampaignDefaults);
    if (nanaCampaignDefaults.RolePrior.GetValueOrDefault("cycling") <= 0d
        || nanaCampaignDefaults.RolePrior.GetValueOrDefault(
            "nightmare-debuff-events") <= 0d
        || nanaCampaignDefaults.RewardScoreBiases.GetValueOrDefault(
            "Crowdfundingcard_43") <= 0d
        || nanaCampaignDefaults.RewardScoreBiases.GetValueOrDefault(
            "blood_13") <= 0d)
    {
        failures.Add("nana-campaign-strategy-defaults");
    }
    var diagnostics = AuraToolsRoleTrainingDiagnostics.Analyze(new[]
    {
        new CombatEpisode
        {
            EpisodeId = "nana-journey-final",
            JourneyRunId = "nana-journey",
            JourneyBattleIndex = 2,
            FinalPlayerMaxHp = 1200,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                DifficultyId = "normal",
                FinalBossVictory = true,
                TerminalSnapshotKnown = true,
                TerminalBattleIndex = 36,
                TerminalPlayerHp = 140,
                TerminalPlayerMaxHp = 180,
                TerminalDoomPower = 12
            },
            Frames =
            {
                new CombatEpisodeFrame
                {
                    Turn = 2,
                    ActionSequence = 1,
                    ExecutedCandidateId = "devour",
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "devour",
                            SourceId = "careercard_2",
                            Features =
                            {
                                [CombatRoleStrategyFeatureNames.Active] = 1d,
                                ["nana:devour-net-value"] = 5d
                            }
                        }
                    }
                },
                new CombatEpisodeFrame
                {
                    Turn = 3,
                    ActionSequence = 2,
                    ExecutedCandidateId = "transform",
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "transform",
                            SourceId = "careercard_3",
                            Features =
                            {
                                 [CombatRoleStrategyFeatureNames.Active] = 1d,
                                 ["nana:first-transform"] = 1d,
                                 ["roleStrategy:nana.transform-ready"] = 1d,
                                 [CombatSkillTimingFeatureNames.Active] = 1d,
                                 [CombatSkillTimingFeatureNames.PositiveOpportunity] = 1d,
                                 [CombatSkillTimingFeatureNames.TimingAdvantage] = 3d
                            }
                        }
                    }
                },
                new CombatEpisodeFrame
                {
                    Turn = 4,
                    ActionSequence = 3,
                    ExecutedCandidateId = "repeat-transform",
                    StateFeatures =
                    {
                        ["playerRole:career_4"] = 1d
                    },
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "repeat-transform",
                            SourceId = "careercard_3",
                            Features =
                            {
                                 [CombatRoleStrategyFeatureNames.Active] = 1d,
                                 ["nana:repeat-transform"] = 1d,
                                 [CombatSkillTimingFeatureNames.Active] = 1d,
                                 [CombatSkillTimingFeatureNames.BetterToWait] = 1d,
                                 [CombatSkillTimingFeatureNames.TimingAdvantage] = -40d,
                                 [CombatSkillTimingFeatureNames.RedundancyCost] = 40d
                            }
                        }
                    }
                },
                new CombatEpisodeFrame
                {
                    Turn = 4,
                    ActionSequence = 4,
                    StateFeatures =
                    {
                        ["playerRole:career_2"] = 1d
                    }
                }
            }
        },
        new CombatEpisode
        {
            EpisodeId = "nana-journey-earlier",
            JourneyRunId = "nana-journey",
            JourneyBattleIndex = 1,
            FinalPlayerMaxHp = 100,
            Campaign = new CombatCampaignEpisodeMetadata
            {
                DifficultyId = "normal",
                FinalBossVictory = true,
                TerminalSnapshotKnown = true,
                TerminalBattleIndex = 36,
                TerminalPlayerHp = 140,
                TerminalPlayerMaxHp = 180,
                TerminalDoomPower = 12
            }
        },
        new CombatEpisode
        {
            EpisodeId = "generic-skill-expired",
            Frames =
            {
                new CombatEpisodeFrame
                {
                    Turn = 1,
                    ActionSequence = 1,
                    ExecutedCandidateId = "end-turn",
                    Candidates =
                    {
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "generic-skill",
                            SourceId = "generic-skill",
                            Features =
                            {
                                [CombatSkillTimingFeatureNames.Active] = 1d,
                                [CombatSkillTimingFeatureNames.PositiveOpportunity] = 1d,
                                [CombatSkillTimingFeatureNames.TimingAdvantage] = 2d
                            }
                        },
                        new CombatEpisodeCandidate
                        {
                            CandidateId = "end-turn",
                            SourceId = "simulation:end-turn"
                        }
                    }
                }
            }
        }
    }, new[]
    {
        new CombatFoundationCampaignObservation
        {
            SourceStage = "training",
            DifficultyId = "normal",
            FinalBossVictory = true,
            FinalHp = 190,
            FinalMaxHp = 222,
            FinalDoomPower = 17
        }
    });
    if (diagnostics.GetValueOrDefault("final-max-hp.maximum") != 222d
        || diagnostics.GetValueOrDefault("journey-final-doom.mean") != 17d
        || diagnostics.GetValueOrDefault(
            "journey-normal-victory-final-max-hp.mean") != 222d
        || diagnostics.GetValueOrDefault(
            "journey-normal-victory-final-doom.mean") != 17d
        || diagnostics.GetValueOrDefault(
            "nana.devour-transform-link-rate") != 1d
        || diagnostics.GetValueOrDefault("nana.first-transforms") != 1d
        || diagnostics.GetValueOrDefault("nana.repeat-transforms") != 1d
        || diagnostics.GetValueOrDefault(
            "nana.role-strategy-observed-frames") != 4d
        || diagnostics.GetValueOrDefault(
            "nana.role-strategy-non-actionable-frames") != 1d
        || diagnostics.GetValueOrDefault(
            "nana.role-strategy-eligible-frames") != 3d
        || diagnostics.GetValueOrDefault(
            "nana.role-strategy-frame-coverage") != 1d
        || diagnostics.GetValueOrDefault(
            "nana.selected-nonpositive-devours") != 0d
        || diagnostics.GetValueOrDefault(
            "nana.selected-underprepared-transforms") != 0d
        || diagnostics.GetValueOrDefault(
            "skill-timing.evaluated-candidates") != 3d
        || diagnostics.GetValueOrDefault(
            "skill-timing.positive-opportunity-frames") != 2d
        || diagnostics.GetValueOrDefault(
            "skill-timing.expired-positive-skills") != 1d
        || diagnostics.GetValueOrDefault(
            "skill-timing.selected-positive-activations") != 1d
        || diagnostics.GetValueOrDefault(
            "skill-timing.selected-better-to-wait") != 1d
        || diagnostics.GetValueOrDefault(
            "skill-timing.selected-redundant") != 1d
        || diagnostics.GetValueOrDefault(
            "skill-timing.skill.careercard_3.selected-activations") != 2d
        || diagnostics.GetValueOrDefault(
            "skill-timing.skill.careercard_3.selected-positive-activations") != 1d
        || diagnostics.GetValueOrDefault(
            "skill-timing.skill.careercard_3.selected-better-to-wait") != 1d
        || !diagnostics.ContainsKey(
            "skill-timing.skill.careercard_13.evaluated-candidates"))
    {
        failures.Add("nana-training-diagnostics");
    }
    return failures;
}

static IEnumerable<string> ValidateSkillTimingCatalog(
    string path,
    CombatGameSubjectCatalog subjectCatalog)
{
    var failures = new List<string>();
    if (!File.Exists(path))
    {
        failures.Add("skill-timing-catalog: bundled catalog is missing");
        return failures;
    }

    var root = JObject.Parse(File.ReadAllText(path));
    var entries = (root["skills"] as JArray)?.OfType<JObject>().ToList()
                  ?? new List<JObject>();
    var expected = subjectCatalog.Roles
        .SelectMany(role => role.SkillCooldownTurns)
        .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group.First().Value,
            StringComparer.OrdinalIgnoreCase);
    var actualIds = entries
        .Select(entry => (string?)entry["skillId"] ?? "")
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .ToList();
    if ((int?)root["schemaVersion"] != 1
        || (string?)root["gameBuild"] != subjectCatalog.GameBuild
        || entries.Count != 13
        || actualIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 13
        || expected.Count != 13
        || expected.Keys.Any(id => !actualIds.Contains(
            id,
            StringComparer.OrdinalIgnoreCase))
        || entries.Any(entry =>
            string.IsNullOrWhiteSpace((string?)entry["evaluator"])
            || string.IsNullOrWhiteSpace((string?)entry["choicePolicy"])
            || (entry["roleIds"] as JArray)?.Count is not > 0)
        || expected.Any(pair =>
            !AuraToolsWitchSkillTimingProvider.Cooldowns.TryGetValue(
                pair.Key,
                out var cooldown)
            || cooldown != pair.Value))
    {
        failures.Add(
            "skill-timing-catalog: catalog, subject cooldowns, and runtime provider diverged");
    }
    return failures;
}

static IEnumerable<string> ValidateNanaStatusDerivedMaximumHp(
    CombatGameSubjectCatalog catalog,
    CombatCampaignDefinition campaignTemplate,
    CombatRuleset ruleset)
{
    var failures = new List<string>();
    var subject = new CombatGameSubjectPreset
    {
        Id = "nana-status-derived-maximum-hp",
        RoleId = "career_2",
        PartnerId = "Partner_10003"
    };
    catalog.ResolveReferences(subject);
    var campaign = JsonConvert.DeserializeObject<CombatCampaignDefinition>(
                       JsonConvert.SerializeObject(campaignTemplate))
                   ?? new CombatCampaignDefinition();
    CombatGameSubjectPresetRuntime.Apply(subject, campaign);
    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "nana-status-derived-maximum-hp",
        Player = campaign.Player,
        CampaignVariables = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["DoomPower"] = "1"
        }
    };
    scenario.RewardRules.Add(new CombatScenarioRewardRule
    {
        RewardId = "blessing_40",
        Kind = "Blessing",
        Stacks = 1
    });
    var context = new NativePoolTestContext(scenario, ruleset);
    context.State.PlayerActorId = 1;
    var actor = new CombatActorState
    {
        ActorId = 1,
        DefinitionId = "career_2",
        Kind = CombatSimulationActorKind.Player,
        Hp = 61,
        MaxHp = 61,
        Statuses =
        {
            new CombatStatusState
            {
                StatusId = "buff_DoomPower",
                Stacks = 1,
                SourceActorId = 1
            }
        }
    };
    context.State.Actors.Add(actor);
    var extension = new AuraToolsNativeRewardExtension();
    extension.Initialize(context);
    var initialContributionCorrect = actor.MaxHp == 61;
    actor.Statuses.Single(status => string.Equals(
        status.StatusId,
        "buff_DoomPower",
        StringComparison.OrdinalIgnoreCase)).Stacks = 2;
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.StatusAdded,
        SourceActorId = 1,
        TargetActorId = 1,
        DefinitionId = "buff_DoomPower",
        Amount = 1
    });
    var firstGrowthCorrect = actor.MaxHp == 63 && actor.Hp == 63;
    actor.Statuses.Single(status => string.Equals(
        status.StatusId,
        "buff_DoomPower",
        StringComparison.OrdinalIgnoreCase)).Stacks = 4;
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.StatusAdded,
        SourceActorId = 1,
        TargetActorId = 1,
        DefinitionId = "buff_DoomPower",
        Amount = 2
    });
    var secondGrowthCorrect = actor.MaxHp == 67 && actor.Hp == 67;
    var persistentGrowthCorrect =
        context.PersistentVariableDeltas.GetValueOrDefault("MaxHp") == 6;
    var globals = new NativeRewardScriptGlobals(
        context,
        new CombatScenarioRewardRule
        {
            RewardId = "career_2",
            Kind = "Role",
            NativeScriptHash = campaign.Player.RoleNativeScriptHash,
            FightScript = campaign.Player.RoleFightScript
        });
    globals.SetStatus("Self");
    globals.ChangeCareer("career_4");
    var transformedCorrect = actor.DefinitionId == "career_4"
                             && actor.MaxHp == 67
                             && actor.Hp == 67;
    actor.Statuses.Add(new CombatStatusState
    {
        StatusId = "SpecialBuff_CalamityIncarnates",
        Stacks = 1,
        SourceActorId = 1
    });
    context.State.Actors.Add(new CombatActorState
    {
        ActorId = 2,
        Kind = CombatSimulationActorKind.Enemy,
        Hp = 100,
        MaxHp = 100
    });
    context.State.Actors.Add(new CombatActorState
    {
        ActorId = 3,
        Kind = CombatSimulationActorKind.Enemy,
        Hp = 100,
        MaxHp = 100
    });
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.StatusAdded,
        SourceActorId = 1,
        TargetActorId = 1,
        DefinitionId = "SpecialBuff_CalamityIncarnates",
        Amount = 1
    });
    context.AppliedEffects.Clear();
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.ActionResolved,
        SourceActorId = 1,
        TargetActorId = 2,
        DefinitionId = "fixture-action"
    });
    var calamityDamageCorrect = context.AppliedEffects.Count(item =>
        item.Effect.Kind == CombatSimulationEffectKind.Damage
        && item.Effect.Amount == 1) == 2;
    actor.Hp = 42;
    actor.Statuses.RemoveAll(status => string.Equals(
        status.StatusId,
        "SpecialBuff_CalamityIncarnates",
        StringComparison.OrdinalIgnoreCase));
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.StatusRemoved,
        SourceActorId = 1,
        TargetActorId = 1,
        DefinitionId = "SpecialBuff_CalamityIncarnates",
        Amount = 1
    });
    var restoredCorrect = actor.DefinitionId == "career_2"
                          && actor.MaxHp == 67
                          && actor.Hp == 42;
    var nextScenario = new CombatScenarioDefinition
    {
        ScenarioId = "nana-doom-next-battle",
        Player = scenario.Player,
        CampaignVariables = new Dictionary<string, string>(
            scenario.CampaignVariables,
            StringComparer.OrdinalIgnoreCase)
    };
    var nextContext = new NativePoolTestContext(nextScenario, ruleset);
    nextContext.State.PlayerActorId = 1;
    nextContext.State.Actors.Add(new CombatActorState
    {
        ActorId = 1,
        DefinitionId = "career_2",
        Kind = CombatSimulationActorKind.Player,
        Hp = 42,
        MaxHp = 67,
        Statuses =
        {
            new CombatStatusState
            {
                StatusId = "buff_DoomPower",
                Stacks = 4,
                SourceActorId = 1
            }
        }
    });
    new AuraToolsNativeRewardExtension().Initialize(nextContext);
    var adventurePersistenceCorrect =
        nextScenario.CampaignVariables.GetValueOrDefault("DoomPower") == "4"
        && nextContext.State.Player?.MaxHp == 67
        && nextContext.State.Player?.Hp == 42
        && nextContext.PersistentVariableDeltas.Count == 0;
    if (!initialContributionCorrect
        || !firstGrowthCorrect
        || !secondGrowthCorrect
        || !persistentGrowthCorrect
        || !transformedCorrect
        || !calamityDamageCorrect
        || !restoredCorrect
        || !adventurePersistenceCorrect)
    {
        failures.Add(
            "nana-status-derived-maximum-hp: initial="
            + initialContributionCorrect
            + ", firstGrowth="
            + firstGrowthCorrect
            + ", secondGrowth="
            + secondGrowthCorrect
            + ", persistentGrowth="
            + persistentGrowthCorrect
            + ", transformed="
            + transformedCorrect
            + ", calamityDamage="
            + calamityDamageCorrect
            + ", restored="
            + restoredCorrect
            + ", adventurePersistence="
            + adventurePersistenceCorrect
            + ", hp="
            + actor.Hp
            + ", maxHp="
            + actor.MaxHp);
    }
    if (!ruleset.TryGetCard("careercard_2", out var devour)
        || devour.RequiresEnemyTarget
        || devour.TargetScope != CombatCardTargetScope.AnyActor)
    {
        failures.Add("nana-devour-target-scope-definition");
    }
    else
    {
        var targetState = new CombatBattleState
        {
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            Actors =
            {
                actor,
                new CombatActorState
                {
                    ActorId = 2,
                    Kind = CombatSimulationActorKind.Friendly,
                    Hp = 20,
                    MaxHp = 20
                },
                new CombatActorState
                {
                    ActorId = 3,
                    Kind = CombatSimulationActorKind.Enemy,
                    Hp = 20,
                    MaxHp = 20
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "careercard_2"
                }
            },
            SkillCards = { 1 },
            SkillCooldowns = { [1] = 0 }
        };
        var targetActions = new CombatSimulationEngine()
            .GetInvocablePlayerActions(scenario, ruleset, targetState)
            .Where(action => action.DefinitionId == "careercard_2")
            .ToList();
        var targetIds = targetActions
            .Select(action => action.TargetActorId)
            .OrderBy(id => id)
            .ToArray();
        if (!targetIds.SequenceEqual(new[] { 1, 2, 3 }))
        {
            failures.Add(
                "nana-devour-target-scope-actions:"
                + string.Join(",", targetIds));
        }
        if (!scenario.RewardRules.Any(item => string.Equals(
                item.RewardId,
                "blessing_40",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                item.Kind,
                "Blessing",
                StringComparison.OrdinalIgnoreCase)))
        {
            scenario.RewardRules.Add(new CombatScenarioRewardRule
            {
                RewardId = "blessing_40",
                Kind = "Blessing",
                Stacks = 1
            });
        }
        var observation = PlayerEquivalentSimulationObservationProjector.Project(
            new CombatSimulationPolicyContext
            {
                Scenario = scenario,
                Ruleset = ruleset,
                State = targetState,
                LegalActions = targetActions
            });
        var projectedKinds = observation.Actions
            .OrderBy(action => action.TargetRuntimeId)
            .Select(action => action.TargetKind)
            .ToArray();
        var friendlyFeatures = CombatDecisionEngine.BuildFeatures(
            observation,
            observation.Actions.Single(action =>
                action.TargetKind == CombatTargetKind.Friendly));
        if (!projectedKinds.SequenceEqual(new[]
            {
                CombatTargetKind.Self,
                CombatTargetKind.Friendly,
                CombatTargetKind.Enemy
            })
            || observation.Friendlies.Count != 1
             || observation.Features.GetValueOrDefault(
                 "playerRole:career_2") != 1d
             || observation.Features.GetValueOrDefault(
                 "blessing:blessing_40") != 1d
             || friendlyFeatures.GetValueOrDefault(
                "targetKindFriendly") != 1d)
        {
            failures.Add(
                "nana-devour-target-observation:"
                + string.Join(",", projectedKinds)
                + ":friendlies="
                + observation.Friendlies.Count
                + ":role="
                + observation.Features.GetValueOrDefault(
                    "playerRole:career_2")
                + ":nightmare="
                + observation.Features.GetValueOrDefault(
                    "blessing:blessing_40"));
        }
    }
    var transformState = new CombatBattleState
    {
        Phase = CombatSimulationPhase.PlayerAction,
        PlayerActorId = 1,
        Actors =
        {
            new CombatActorState
            {
                ActorId = 1,
                InstanceKey = "nana-transform-player",
                DefinitionId = "career_2",
                Kind = CombatSimulationActorKind.Player,
                Hp = 61,
                MaxHp = 61,
                Energy = 3,
                BaseEnergy = 3
            },
            new CombatActorState
            {
                ActorId = 2,
                InstanceKey = "nana-transform-enemy",
                DefinitionId = "enemy_1",
                Kind = CombatSimulationActorKind.Enemy,
                Hp = 100,
                MaxHp = 100
            }
        },
        Cards =
        {
            new CombatCardInstanceState
            {
                InstanceId = 1,
                CardId = "careercard_3",
                CreationSource = "role-skill",
                CreationSourceId = "career_2"
            }
        },
        SkillCards = { 1 },
        SkillCooldowns = { [1] = 0 }
    };
    var transformEngine = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory());
    var transformAction = transformEngine.GetInvocablePlayerActions(
            scenario,
            ruleset,
            transformState)
        .Single(action => string.Equals(
            action.DefinitionId,
            "careercard_3",
            StringComparison.OrdinalIgnoreCase));
    var transformResult = transformEngine.ForkAndApplyPlayerAction(
        scenario,
        ruleset,
        transformState,
        transformAction,
        allowPolicyIneligible: true);
    if (!transformResult.Success
        || transformResult.State.SkillCooldowns.GetValueOrDefault(1) != 2
        || !string.Equals(
            transformResult.State.Player?.DefinitionId,
            "career_4",
            StringComparison.OrdinalIgnoreCase))
    {
        failures.Add(
            "nana-native-transform-cooldown: success="
            + transformResult.Success
            + ", cooldown="
            + transformResult.State.SkillCooldowns.GetValueOrDefault(1)
            + ", role="
            + transformResult.State.Player?.DefinitionId);
    }
    return failures;
}

static IEnumerable<string> ValidateNightmarePrototypeDuplication(
    CombatCampaignDefinition campaign,
    CombatRuleset ruleset)
{
    var failures = new List<string>();
    var blessing = campaign.Rewards.Single(item => string.Equals(
        item.RewardId,
        "blessing_40",
        StringComparison.OrdinalIgnoreCase));
    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "nightmare-prototype-one-layer",
        Player = new CombatPlayerSetup
        {
            RoleId = "career_2"
        },
        RewardRules =
        {
            new CombatScenarioRewardRule
            {
                RewardId = blessing.RewardId,
                Kind = blessing.Kind.ToString(),
                NativeScriptHash = blessing.NativeScriptHash,
                FightScript = blessing.FightScript
            }
        }
    };
    var context = new NativePoolTestContext(scenario, ruleset)
    {
        RandomValue = 99
    };
    context.State.PlayerActorId = 1;
    context.State.Actors.Add(new CombatActorState
    {
        ActorId = 1,
        InstanceKey = "nightmare-player",
        DefinitionId = "career_2",
        Kind = CombatSimulationActorKind.Player,
        Hp = 60,
        MaxHp = 60
    });
    context.State.Actors.Add(new CombatActorState
    {
        ActorId = 2,
        InstanceKey = "nightmare-enemy",
        DefinitionId = "enemy_1",
        Kind = CombatSimulationActorKind.Enemy,
        Hp = 100,
        MaxHp = 100
    });
    var negativeStatus = ruleset.SnapshotStatuses().First(status =>
        status.Tags.Contains("Negative", StringComparer.OrdinalIgnoreCase));
    var extension = new AuraToolsNativeRewardExtension();
    extension.Initialize(context);
    extension.OnEvent(context, new CombatSimulationEvent
    {
        Kind = CombatSimulationEventKind.StatusAdded,
        SourceActorId = 1,
        TargetActorId = 2,
        DefinitionId = negativeStatus.StatusId,
        Amount = 3,
        SourceRewardId = "fixture-debuff-card",
        SourceActionId = 1
    });
    var duplicate = context.AppliedEffects.SingleOrDefault();
    var duplicatedOneLayer = duplicate.Effect != null
                             && duplicate.Effect.Kind
                             == CombatSimulationEffectKind.AddStatus
                             && duplicate.Effect.Amount == 1
                             && string.Equals(
                                 duplicate.Effect.DefinitionId,
                                 negativeStatus.StatusId,
                                 StringComparison.OrdinalIgnoreCase)
                             && string.Equals(
                                 duplicate.SourceEvent?.SourceRewardId,
                                 "blessing_40",
                                 StringComparison.OrdinalIgnoreCase);
    if (duplicate.SourceEvent != null)
    {
        extension.OnEvent(context, new CombatSimulationEvent
        {
            Kind = CombatSimulationEventKind.StatusAdded,
            SourceActorId = 1,
            TargetActorId = 2,
            DefinitionId = negativeStatus.StatusId,
            Amount = 1,
            SourceRewardId = duplicate.SourceEvent.SourceRewardId,
            SourceActionId = 1
        });
    }
    if (!duplicatedOneLayer || context.AppliedEffects.Count != 1)
    {
        failures.Add(
            "nightmare-prototype-one-layer: duplicated="
            + duplicatedOneLayer
            + ", effects="
            + context.AppliedEffects.Count);
    }
    return failures;
}

static IEnumerable<string> ValidateAdelaWholeHandTransform(
    CombatRuleset ruleset)
{
    var failures = new List<string>();
    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "adela-whole-hand-transform",
        Player = new CombatPlayerSetup
        {
            RoleId = "career_3",
            SkillCardIds = { "careercard_4" },
            SkillCooldownTurns = { ["careercard_4"] = 12 }
        },
        CampaignVariables = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["Soul"] = "100"
        },
        HandLimit = 10
    };
    var state = new CombatBattleState
    {
        Turn = 1,
        Phase = CombatSimulationPhase.PlayerAction,
        PlayerActorId = 1,
        NextCardInstanceId = 4,
        Actors =
        {
            new CombatActorState
            {
                ActorId = 1,
                DefinitionId = "career_3",
                Kind = CombatSimulationActorKind.Player,
                Hp = 95,
                MaxHp = 95,
                Energy = 3
            }
        },
        Cards =
        {
            new CombatCardInstanceState
            {
                InstanceId = 1,
                CardId = "careercard_4",
                CreationSource = "role-skill",
                CreationSourceId = "career_3"
            },
            new CombatCardInstanceState
            {
                InstanceId = 2,
                CardId = "card_1",
                CreationSource = "fixture",
                EnchantmentIds = { "fixture-enhancement" },
                Variables = { ["fixture"] = "old" }
            },
            new CombatCardInstanceState
            {
                InstanceId = 3,
                CardId = "card_2",
                CreationSource = "fixture"
            }
        },
        SkillCards = { 1 },
        SkillCooldowns = { [1] = 0 },
        Hand = { 2, 3 }
    };
    var engine = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory());
    var skill = engine.GetInvocablePlayerActions(scenario, ruleset, state)
        .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
    var applied = engine.ForkAndApplyPlayerAction(
        scenario,
        ruleset,
        state,
        skill,
        allowPolicyIneligible: true);
    var transformed = applied.State.Hand
        .Select(applied.State.FindCard)
        .Where(item => item != null)
        .Select(item => item!)
        .ToList();
    if (!applied.Success
        || transformed.Count != 2
        || transformed.Any(item => !string.Equals(
            item.CardId,
            "nocard_2",
            StringComparison.OrdinalIgnoreCase))
        || transformed.Any(item => item.EnchantmentIds.Count != 0)
        || transformed.Any(item => !item.Tags.Contains(
            "Burnout",
            StringComparer.OrdinalIgnoreCase))
        || transformed.Any(item => !item.Tags.Contains(
            "Retain",
            StringComparer.OrdinalIgnoreCase))
        || applied.State.SkillCooldowns.GetValueOrDefault(1) != 12
        || scenario.CampaignVariables.GetValueOrDefault("Soul") != "100")
    {
        failures.Add(
            "adela-whole-hand-transform:success="
            + applied.Success
            + ", hand="
            + string.Join(",", transformed.Select(item => item.CardId))
            + ", cooldown="
            + applied.State.SkillCooldowns.GetValueOrDefault(1));
    }
    return failures;
}

static IEnumerable<string> ValidateDivineChoiceActionContract(
    CombatRuleset ruleset)
{
    var failures = new List<string>();
    if (!ruleset.TryGetCard("careercard_1", out var divineChoice)
        || divineChoice.RequiresEnemyTarget
        || divineChoice.ActionContract == null
        || divineChoice.ActionContract.Version
           != CombatActionContractProtocol.Version
        || divineChoice.VerificationSource
           != "Decompiler:v1.0.23816797")
    {
        failures.Add(
            "divine-choice-contract: bundled semantic contract is missing or invalid");
        return failures;
    }

    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "divine-choice-contract",
        HandLimit = 1,
        Player = new CombatPlayerSetup
        {
            RoleId = "career_1",
            SkillCardIds = { "careercard_1" },
            SkillCooldownTurns = { ["careercard_1"] = 1 }
        }
    };
    CombatBattleState State(bool drawCard, bool fullHand)
    {
        var state = new CombatBattleState
        {
            Turn = 1,
            Phase = CombatSimulationPhase.PlayerAction,
            PlayerActorId = 1,
            Actors =
            {
                new CombatActorState
                {
                    ActorId = 1,
                    Kind = CombatSimulationActorKind.Player,
                    Hp = 20,
                    MaxHp = 20,
                    Energy = 0
                }
            },
            Cards =
            {
                new CombatCardInstanceState
                {
                    InstanceId = 1,
                    CardId = "careercard_1",
                    CreationSource = "role-skill",
                    CreationSourceId = "career_1"
                }
            },
            SkillCards = { 1 },
            SkillCooldowns = { [1] = 0 },
            NextCardInstanceId = 2
        };
        if (drawCard)
        {
            state.Cards.Add(new CombatCardInstanceState
            {
                InstanceId = 2,
                CardId = "card_1",
                CreationSource = "fixture"
            });
            state.DrawPile.Add(2);
            state.NextCardInstanceId = 3;
        }
        if (fullHand)
        {
            state.Cards.Add(new CombatCardInstanceState
            {
                InstanceId = 3,
                CardId = "card_1",
                CreationSource = "fixture"
            });
            state.Hand.Add(3);
            state.NextCardInstanceId = 4;
        }
        return state;
    }

    var engine = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory());
    var emptyDraw = State(drawCard: false, fullHand: false);
    var emptyDrawSkill = engine.GetInvocablePlayerActions(
            scenario,
            ruleset,
            emptyDraw)
        .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
    var forcedEmptyDraw = engine.ForkAndApplyPlayerAction(
        scenario,
        ruleset,
        emptyDraw,
        emptyDrawSkill,
        allowPolicyIneligible: true);
    if (emptyDrawSkill.PolicyEligible
        || emptyDrawSkill.ExpectedOutcome
           != CombatActionApplicationOutcome.NoEffect
        || !forcedEmptyDraw.Success
        || forcedEmptyDraw.Outcome
           != CombatActionApplicationOutcome.NoEffect
        || forcedEmptyDraw.State.DrawPile.Count != 0
        || forcedEmptyDraw.State.Hand.Count != 0
        || forcedEmptyDraw.State.SkillCooldowns[1] != 0)
    {
        failures.Add(
            "divine-choice-contract: empty draw pile must remain a no-effect, no-cooldown invocation");
    }

    var fullHand = State(drawCard: true, fullHand: true);
    var fullHandSkill = engine.GetInvocablePlayerActions(
            scenario,
            ruleset,
            fullHand)
        .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
    if (fullHandSkill.PolicyEligible
        || fullHandSkill.ExpectedOutcome
           != CombatActionApplicationOutcome.NoEffect)
    {
        failures.Add(
            "divine-choice-contract: full hand must be excluded from policy candidates");
    }

    var applicable = State(drawCard: true, fullHand: false);
    var applicableSkill = engine.GetLegalPlayerActions(
            scenario,
            ruleset,
            applicable)
        .Single(item => item.Kind == CombatSimulationActionKind.UseSkill);
    var applied = engine.ForkAndApplyPlayerAction(
        scenario,
        ruleset,
        applicable,
        applicableSkill);
    if (!applied.Success
        || applied.Outcome != CombatActionApplicationOutcome.Applied
        || !applied.State.Hand.SequenceEqual(new[] { 2 })
        || applied.State.DrawPile.Count != 0
        || applied.State.SkillCooldowns[1] != 1)
    {
        failures.Add(
            "divine-choice-contract: successful selection must move one draw-pile card to hand and start cooldown");
    }

    Console.WriteLine("Divine Choice action contract checks: 3 cases.");
    return failures;
}

static IEnumerable<string> ValidateFinalBossAuthority(CombatRuleset ruleset)
{
    var failures = new List<string>();
    if (!ruleset.TryGetEnemy("enemy_10007", out var lostWitch)
        || lostWitch.Intents.Any(item =>
            item.IntentId.StartsWith("enemycard_HJE_", StringComparison.Ordinal)))
    {
        failures.Add(
            "final-boss-authority: HJE dynamic intents leaked into enemy_10007");
    }
    var requiredHjeIntents = new[]
    {
        "enemycard_HJE_Judgment",
        "enemycard_HJE_Dawn",
        "enemycard_HJE_HolyMachine"
    };
    if (!ruleset.TryGetEnemy("enemy_10055", out var hje)
        || requiredHjeIntents.Any(expected =>
            hje.Intents.All(item =>
                !string.Equals(
                    item.IntentId,
                    expected,
                    StringComparison.Ordinal))))
    {
        failures.Add(
            "final-boss-authority: enemy_10055 is missing one or more dynamic fate intents");
    }
    if (!ruleset.TryGetStatus(
            "SpecialBuff_CAR_Deadline",
            out var deadline)
        || !deadline.Metadata.TryGetValue(
            "NativeApplyScript",
            out var deadlineScript)
        || deadlineScript.IndexOf(
            "Damage(Object[0].MaxHp.ToString(),\"True\")",
            StringComparison.Ordinal) < 0
        || deadlineScript.IndexOf(
            "ChangeHp((-Object[0].MaxHp)",
            StringComparison.Ordinal) >= 0)
    {
        failures.Add(
            "final-boss-authority: Caroline deadline must execute max-HP true damage, not direct HP loss");
    }
    Console.WriteLine("Final boss authority checks: 3 cases.");
    return failures;
}

static IEnumerable<string> ValidateHardAffixes(
    CombatCampaignDefinition campaign,
    CombatRuleset ruleset,
    CombatEnemyDefinition enemy)
{
    var failures = new List<string>();
    var advanced = campaign.Difficulties.FirstOrDefault(item =>
        string.Equals(
            item.DifficultyId,
            "advanced",
            StringComparison.OrdinalIgnoreCase));
    var expectedAffixes = new[] { "Hard_5", "Hard_7", "Hard_8", "Hard_13" };
    foreach (var affixId in expectedAffixes)
    {
        if (advanced?.HardAffixes.Any(item =>
                item.Implemented
                && string.Equals(
                    item.AffixId,
                    affixId,
                    StringComparison.OrdinalIgnoreCase)) != true)
        {
            failures.Add("affix-definition:" + affixId);
        }
    }
    foreach (var statusId in new[]
             {
                 "buff_elements",
                 "buff_elementalBody",
                 "SpecialBuff_Restrain",
                 "SpecialBuff_Irritable",
                 "SpecialBuff_Hysteresis"
             })
    {
        var status = ruleset.SnapshotStatuses().FirstOrDefault(item =>
            string.Equals(
                item.StatusId,
                statusId,
                StringComparison.OrdinalIgnoreCase));
        if (status?.Fidelity != CombatRuleFidelity.Authoritative)
        {
            failures.Add("affix-status:" + statusId);
        }
    }
    var scenario = new CombatScenarioDefinition
    {
        ScenarioId = "hard-affix-smoke",
        RulesetVersion = ruleset.Version,
        Seed = 772025UL,
        Player = new CombatPlayerSetup
        {
            RoleId = campaign.Player.RoleId,
            SkillCardIds = new List<string>(
                campaign.Player.SkillCardIds),
            MaxHp = 10000,
            CurrentHp = 10000,
            BaseEnergy = 0,
            Deck = new List<string> { campaign.Player.Deck[0] },
            Variables = new Dictionary<string, double>
            {
                ["Difficulty"] = 5,
                ["EncounterKind"] = (int)CombatCampaignEncounterKind.Elite
            }
        },
        Enemies =
        {
            new CombatEnemySetup
            {
                EnemyId = enemy.EnemyId,
                InstanceKey = "hard-low",
                HpScale = 1d
            },
            new CombatEnemySetup
            {
                EnemyId = enemy.EnemyId,
                InstanceKey = "hard-high",
                HpScale = 2d
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
        }
    };
    var result = new CombatSimulationEngine(
        new AuraToolsNativeRewardExtensionFactory())
        .Run(scenario, ruleset, new EndTurnPolicy());
    var enemies = result.FinalState.Actors
        .Where(item => item.Kind == CombatSimulationActorKind.Enemy)
        .ToList();
    if (enemies.Count != 2
        || enemies.Any(item => item.Statuses.FirstOrDefault(status =>
               status.StatusId == "buff_elements")?.Stacks != 4)
        || enemies.Any(item => item.Statuses.All(status =>
               status.StatusId != "buff_elementalBody")))
    {
        failures.Add("affix-runtime:Hard_7/Hard_13");
    }
    var traits = new HashSet<string>(
        new[]
        {
            "SpecialBuff_Restrain",
            "SpecialBuff_Irritable",
            "SpecialBuff_Hysteresis"
        },
        StringComparer.OrdinalIgnoreCase);
    var traitOwners = enemies.Where(item =>
            item.Statuses.Any(status => traits.Contains(status.StatusId)))
        .ToList();
    if (traitOwners.Count != 1
        || !string.Equals(
            traitOwners[0].InstanceKey,
            "hard-high",
            StringComparison.Ordinal))
    {
        failures.Add("affix-runtime:Hard_8");
    }
    return failures;
}

static IEnumerable<string> ValidateProgressionSemantics(
    CombatCampaignDefinition campaign)
{
    var failures = new List<string>();
    CombatCampaignState NewState()
    {
        var state = new CombatCampaignState
        {
            WorldSeed = 772025UL,
            MaxHp = campaign.Player.MaxHp,
            CurrentHp = campaign.Player.CurrentHp,
            Money = campaign.InitialMoney,
            Deck = new List<string>(campaign.Player.Deck),
            CurrentGameLevel = 1
        };
        foreach (var attribute in campaign.AttributeIds)
        {
            state.Attributes[attribute] = 0;
            state.LayerBaseAttributes[attribute] = 0;
            state.PermanentAttributeBonuses[attribute] = 0;
            state.AttributeUpperBounds[attribute] = 100;
        }
        return state;
    }

    CombatCampaignRewardDecision Acquire(
        CombatCampaignState state,
        string relicId,
        int index)
    {
        return CombatCampaignRewardSelector.Apply(
            campaign,
            new CombatCampaignPlannedEncounter
            {
                Index = index,
                EncounterId = "progression-" + index,
                RewardOffer = new CombatCampaignRewardOffer
                {
                    RelicId = relicId
                }
            },
            state);
    }

    var replacementState = NewState();
    var replacement = Acquire(
        replacementState,
        "CrowdFundingRelic_29",
        1);
    if (string.IsNullOrWhiteSpace(replacement.Relic.ResolvedId)
        || string.Equals(
            replacement.Relic.ResolvedId,
            "CrowdFundingRelic_29",
            StringComparison.OrdinalIgnoreCase)
        || !replacementState.Relics.Contains(
            replacement.Relic.ResolvedId,
            StringComparer.OrdinalIgnoreCase))
    {
        failures.Add("progression:random-relic-replacement");
    }

    var oneTimeState = NewState();
    Acquire(oneTimeState, "CrowdFundingRelic_12", 2);
    var cardCount = oneTimeState.Deck.Count(item =>
        string.Equals(
            item,
            "Crowdfundingcard_13",
            StringComparison.OrdinalIgnoreCase));
    var blessingCount = oneTimeState.Blessings.Count(item =>
        string.Equals(
            item,
            "CrowdfundingBlessing_8",
            StringComparison.OrdinalIgnoreCase));
    oneTimeState.Relics.RemoveAll(item => string.Equals(
        item,
        "CrowdFundingRelic_12",
        StringComparison.OrdinalIgnoreCase));
    Acquire(oneTimeState, "CrowdFundingRelic_12", 3);
    if (oneTimeState.SpecialVariables.GetValueOrDefault(
            "CrowdFundingRelic12First",
            "") != "1"
        || oneTimeState.Deck.Count(item => string.Equals(
            item,
            "Crowdfundingcard_13",
            StringComparison.OrdinalIgnoreCase)) != cardCount
        || oneTimeState.Blessings.Count(item => string.Equals(
            item,
            "CrowdfundingBlessing_8",
            StringComparison.OrdinalIgnoreCase)) != blessingCount)
    {
        failures.Add("progression:one-time-own-script");
    }

    var removalState = NewState();
    removalState.Deck.AddRange(new[]
    {
        "card_1", "card_2", "card_3", "card_4"
    });
    var beforeRemoval = removalState.Deck.Count;
    Acquire(removalState, "CrowdFundingRelic_60", 4);
    if (removalState.Deck.Count != beforeRemoval - 4)
    {
        failures.Add("progression:random-card-removal");
    }
    return failures;
}

static IEnumerable<string> ValidateNativeGameRuntime(
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

static IEnumerable<string> ValidateNativeCombatSemantics(
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

    Console.WriteLine("Native combat semantic checks: 9 cases.");
    return failures;
}

static CombatScenarioDefinition NewNativeScenario(
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

static void AddNativeRuntimeFailure(
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

static IEnumerable<string> ValidateIndirectScriptExecution(
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

static IEnumerable<string> ValidateDrawPileSnapshotExecution(
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

static IEnumerable<string> ValidateFullHandGeneratedCardOverflow(
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

static IEnumerable<string> ValidateDeferredEffectSafety(
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

static IEnumerable<string> ValidateKnownIntegritySeeds(
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

static IEnumerable<string> ValidateRandomStatusPoolSemantics(
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

static IEnumerable<string> ValidateVisibleFakeCardSemantics(
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

static CombatScenarioDefinition BuildNativeEnemyScenario(
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

static IEnumerable<string> ValidateIntegritySeedSweep(
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

static CombatDecisionSimulationPolicyFactory IntegrityPolicyFactory()
{
    return new CombatDecisionSimulationPolicyFactory(IntegrityProfile());
}

static CombatDecisionProfile IntegrityProfile()
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
