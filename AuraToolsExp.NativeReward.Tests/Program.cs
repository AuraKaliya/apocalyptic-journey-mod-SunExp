using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static NativeRewardTestSuite;


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
    Console.Error.WriteLine(ex);
    return 3;
}
