param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraCombatAiShared.Tests\AuraCombatAiShared.Tests.csproj"
$arguments = @("run", "--project", $project, "-c", "Release")
if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI tests failed with exit code $LASTEXITCODE."
}

$trainerPath = Join-Path $root "tools\train_aura_combat_ai.py"
& python $trainerPath --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI trainer self-test failed with exit code $LASTEXITCODE."
}

$simulationCliProject = Join-Path $root "AuraCombatSimulation.Cli\AuraCombatSimulation.Cli.csproj"
$simulationRules = Join-Path $root "docs\AuraCombatAI\examples\simulation-ruleset.example.json"
$simulationScenario = Join-Path $root "docs\AuraCombatAI\examples\simulation-scenario.example.json"
$simulationOutput = Join-Path ([IO.Path]::GetTempPath()) "aura-combat-simulation-contract.json"
& dotnet run --project $simulationCliProject -c Release -- `
    --ruleset $simulationRules `
    --scenario $simulationScenario `
    --output $simulationOutput `
    --count 4 `
    --parallel 2 `
    --policy chance-puct
if ($LASTEXITCODE -ne 0) {
    throw "Aura headless combat simulation contract failed with exit code $LASTEXITCODE."
}
$simulationResult = Get-Content -LiteralPath $simulationOutput -Raw | ConvertFrom-Json
Remove-Item -LiteralPath $simulationOutput -Force
if ($simulationResult.Statistics.CompletedSimulations -ne 4 `
    -or $simulationResult.Statistics.Invalid -ne 0 `
    -or $simulationResult.Statistics.AuthoritativeSimulations -ne 4 `
    -or $simulationResult.Results[0].FinalStateHash -ne "1deca1bb25d9997c" `
    -or [string]::IsNullOrWhiteSpace($simulationResult.RulesetHash)) {
    throw "Aura headless combat simulation result contract is invalid."
}

$controllerPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleRuntime.cs"
$presenterPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattlePredictionPresenter.cs"
$interactionPath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatInteractionRuntime.cs"
$runtimePath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatRuntime.cs"
$plannerPath = Join-Path $root "AuraCombatAiShared\CombatChancePuctPlanner.cs"
$forwardModelPath = Join-Path $root "AuraCombatAiShared\CombatForwardModel.cs"
$registryPath = Join-Path $root "AuraCombatAiShared\CombatAiRegistry.cs"
$guidancePath = Join-Path $root "AuraCombatAiShared\CombatSearchGuidance.cs"
$simulationEnginePath = Join-Path $root "AuraCombatSimulationShared\CombatSimulationEngine.cs"
$simulationModelsPath = Join-Path $root "AuraCombatSimulationShared\CombatSimulationModels.cs"
$simulationBatchPath = Join-Path $root "AuraCombatSimulationShared\CombatBatchRunner.cs"
$journeySimulationPath = Join-Path $root "AuraCombatSimulationShared\CombatJourneySimulation.cs"
$journeyTrainingPath = Join-Path $root "AuraCombatSimulationShared\CombatJourneyTraining.cs"
$simulationRegistryPath = Join-Path $root "AuraCombatSimulationShared\CombatSimulationRegistry.cs"
$knowledgePath = Join-Path $root "AuraCombatAiShared\CombatKnowledge.cs"
$knowledgeRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsCombatKnowledgeRuntime.cs"
$episodePath = Join-Path $root "AuraCombatAiShared\CombatEpisodeLearning.cs"
$episodeRecorderPath = Join-Path $root "AuraCombatAiShared\CombatEpisodeRecorder.cs"
$liveEpisodeAssemblerPath = Join-Path $root "AuraCombatAiShared\CombatLiveEpisodeAssembler.cs"
$journeyProjectionPath = Join-Path $root "AuraCombatAiShared\CombatJourneyTrainingProjection.cs"
$policyValuePath = Join-Path $root "AuraCombatAiShared\CombatPolicyValueNetwork.cs"
$evolutionPath = Join-Path $root "AuraCombatAiShared\CombatPolicyEvolution.cs"
$simulationUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSimulationRuntime.cs"
$modelUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleModelRuntime.cs"
$journeyUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleJourneyRuntime.cs"
$settingsUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs"
$bundledRulesPath = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-base-evaluation-v1.ruleset.json"
$bundledJourneyPath = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-world-simulation-v1.journey.json"
$controller = Get-Content -LiteralPath $controllerPath -Raw
$presenter = Get-Content -LiteralPath $presenterPath -Raw
$interaction = Get-Content -LiteralPath $interactionPath -Raw
$runtime = Get-Content -LiteralPath $runtimePath -Raw
$planner = Get-Content -LiteralPath $plannerPath -Raw
$forwardModel = Get-Content -LiteralPath $forwardModelPath -Raw
$registry = Get-Content -LiteralPath $registryPath -Raw
$guidance = Get-Content -LiteralPath $guidancePath -Raw
$simulationEngine = Get-Content -LiteralPath $simulationEnginePath -Raw
$simulationModels = Get-Content -LiteralPath $simulationModelsPath -Raw
$simulationBatch = Get-Content -LiteralPath $simulationBatchPath -Raw
$journeySimulation = Get-Content -LiteralPath $journeySimulationPath -Raw
$journeyTraining = Get-Content -LiteralPath $journeyTrainingPath -Raw
$simulationRegistry = Get-Content -LiteralPath $simulationRegistryPath -Raw
$knowledge = Get-Content -LiteralPath $knowledgePath -Raw
$knowledgeRuntime = Get-Content -LiteralPath $knowledgeRuntimePath -Raw
$episode = Get-Content -LiteralPath $episodePath -Raw
$episodeRecorder = Get-Content -LiteralPath $episodeRecorderPath -Raw
$liveEpisodeAssembler = Get-Content -LiteralPath $liveEpisodeAssemblerPath -Raw
$journeyProjection = Get-Content -LiteralPath $journeyProjectionPath -Raw
$policyValue = Get-Content -LiteralPath $policyValuePath -Raw
$evolution = Get-Content -LiteralPath $evolutionPath -Raw
$simulationUiRuntime = Get-Content -LiteralPath $simulationUiRuntimePath -Raw
$modelUiRuntime = Get-Content -LiteralPath $modelUiRuntimePath -Raw
$journeyUiRuntime = Get-Content -LiteralPath $journeyUiRuntimePath -Raw
$settingsUiRuntime = Get-Content -LiteralPath $settingsUiRuntimePath -Raw
$trainer = Get-Content -LiteralPath $trainerPath -Raw

$requiredControllerAnchors = @(
    "FightUI.ThrowCardScript",
    "FightUI.Burning",
    "CombatActionTransaction",
    "CombatActionTransactionState.HandedOff",
    "CombatActionTransactionState.TimedOut",
    "auto-battle-training-v4.jsonl",
    "CaptureTeacherAction",
    "CaptureTeacherEndTurn",
    "FightUI.onChangeTurnBtn",
    'demonstrator: "human"',
    "[AutoBattle][Training] actor=",
    "PolicyPreselectedCandidateId",
    "policyVisibleToHuman: teacherPolicyVisibleToHuman",
    "UpdateShadowPrediction",
    "ShowPredictionMarkers",
    "AuraToolsAutoBattleModelRuntime.Load",
    "[AutoBattle][ModelShadow]",
    "residualSupport=",
    "AuraSharedJson.SerializeCompact"
)
foreach ($anchor in $requiredControllerAnchors) {
    if (-not $controller.Contains($anchor)) {
        throw "Aura combat AI controller contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "UI/SelectedIcon",
    "AuraToolsResourceCache.Load<GameObject>",
    "raycastTarget = false",
    "blocksRaycasts = false",
    "CombatTargetKind.Enemy",
    "ActionColor",
    "LateUpdate",
    "card.uiElement",
    "SyncEdge",
    "CardBorderThickness = 1.5f",
    "PlaceImmediatelyBehind",
    "ignoreLayout = true"
)) {
    if (-not $presenter.Contains($anchor)) {
        throw "Aura combat AI prediction presenter contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "status.GetBuffs()",
    '"DefendPercent"',
    '"HealMultiplier"',
    '"AttackedPercentDamage"',
    "CombatStatusObservation",
    "ObserveDeck",
    "DrawPileCardIds",
    "FightcardList"
)) {
    if (-not $runtime.Contains($anchor)) {
        throw "Aura combat AI authoritative observation contract is missing: $anchor"
    }
}
if ($runtime.Contains('"PercentDefence"') -or $runtime.Contains('"PercentHeal"')) {
    throw "Aura combat AI still references obsolete runtime multiplier keys."
}

foreach ($anchor in @(
    "CombatKnowledgePackage",
    "CombatKnowledgeCoverageReport",
    "TryDescribeAction",
    "EvaluateCoverage",
    "UnknownDefinitions"
)) {
    if (-not $knowledge.Contains($anchor)) {
        throw "Aura combat knowledge contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "BuildVerifiedBasePackage",
    '"elementscard_1"',
    '"buff_elements"',
    "HasAuthoritativeCoverage",
    "combat-knowledge.base-game.json"
)) {
    if (-not $knowledgeRuntime.Contains($anchor)) {
        throw "AuraTools combat knowledge runtime contract is missing: $anchor"
    }
}
if ($presenter.Contains("Shader.") -or $presenter.Contains("new Material")) {
    throw "The baseline prediction marker must not introduce a shader or per-marker material."
}

if ($controller.Contains("Math.Max(60f, settings.ActionTimeoutSeconds)")) {
    throw "End-turn actions must use the same root transaction deadline."
}

$requiredInteractionAnchors = @(
    "CombatPromptKind.DiscardCards",
    "CombatPromptKind.BurnCards",
    "CombatPromptSelectionTracker",
    "TryBeginAttempt",
    "TryIssueConfirm",
    "card.selectContainer != null",
    'card.Tags.Contains("Froze")'
)
foreach ($anchor in $requiredInteractionAnchors) {
    if (-not $interaction.Contains($anchor)) {
        throw "Aura combat AI interaction contract is missing: $anchor"
    }
}

if (-not $runtime.Contains("targeted card target is stale or defeated") -or
    -not $runtime.Contains("skill target is stale or defeated")) {
    throw "Aura combat AI stale-target execution guards are missing."
}

foreach ($anchor in @(
    "AddEnemiesAndNativeThreat",
    "ExpectedBlockableDamage",
    "ExpectedUnblockableDamage",
    "ExpectedDamageOverTime",
    "CombatAiRegistry.TryResolveThreat"
)) {
    if (-not $runtime.Contains($anchor)) {
        throw "Aura combat AI threat observation contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatSimulationEngine",
    "ProcessLifecycleEvent",
    "BuildLegalActions",
    "ExecuteEnemyIntent",
    "MaximumTriggerWavesPerAction",
    "UnsupportedRule",
    "CombatBattleStateHasher.Hash"
)) {
    if (-not $simulationEngine.Contains($anchor)) {
        throw "Aura authoritative combat simulation engine contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatScenarioDefinition",
    "CombatBattleState",
    "CombatSimulationEvent",
    "CombatSimulationResult",
    "CombatRuleFidelity",
    "ParentSequence"
)) {
    if (-not $simulationModels.Contains($anchor)) {
        throw "Aura combat simulation model contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "Parallel.For",
    "Wilson",
    "MaximumDegreeOfParallelism",
    "SemanticCoverage",
    "SeedStart"
)) {
    if (-not $simulationBatch.Contains($anchor)) {
        throw "Aura combat batch simulation contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatJourneyTrainingEpisode",
    "CombatJourneyBattleTrainingRecord",
    "CombatJourneyRewardTrainingRecord",
    "reward-value/system-fit/build-tendency"
)) {
    if (-not $journeyTraining.Contains($anchor)) {
        throw "Aura journey training protocol is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatLiveEpisodeAssembler",
    "BattleSessionId",
    "live-world-simulation",
    "ApplyTerminalTargets"
)) {
    if (-not $liveEpisodeAssembler.Contains($anchor)) {
        throw "Aura live episode assembler contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatJourneyTrainingProjection",
    "JourneyRunId",
    "journeyRemainingBattles",
    "battleDiscount"
)) {
    if (-not $journeyProjection.Contains($anchor)) {
        throw "Aura journey return projection contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatJourneyWorldPlanner",
    "CombatJourneyRewardSelector",
    "CombatJourneyCheckpoint",
    "RunPaired",
    "PlanHash",
    "AllowSkipReward",
    "BossPreference"
)) {
    if (-not $journeySimulation.Contains($anchor)) {
        throw "Aura journey simulation contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "journey-episodes-v1.jsonl",
    "CardChoiceUI.Select",
    "GameApp.GameOver",
    "GameExitUI.Start",
    "CaptureTrainingSamples",
    "final-boss-victory"
)) {
    if (-not $journeyUiRuntime.Contains($anchor)) {
        throw "AuraTools live journey capture contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "RegisterProvider",
    "BuildRuleset",
    "SnapshotProviderIds",
    "OwnerModId",
    "ProviderId"
)) {
    if (-not $simulationRegistry.Contains($anchor)) {
        throw "Aura combat simulation registry contract is missing: $anchor"
    }
}

if (-not (Test-Path -LiteralPath $bundledRulesPath -PathType Leaf) `
    -or -not (Test-Path -LiteralPath $bundledJourneyPath -PathType Leaf)) {
    throw "Bundled standard evaluation package is incomplete."
}
$bundledRules = Get-Content -LiteralPath $bundledRulesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$bundledJourney = Get-Content -LiteralPath $bundledJourneyPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($bundledRules.version -ne "witch-base-evaluation-v1" `
    -or $bundledJourney.rulesetVersion -ne $bundledRules.version `
    -or $bundledJourney.player.roleId -ne "career_1" `
    -or $bundledJourney.stages[-1].encounterPool[0] -ne "enemy_10022" `
    -or (Get-Content -LiteralPath $bundledRulesPath -Raw -Encoding UTF8).Contains("Terrias")) {
    throw "Bundled standard evaluation package does not satisfy the base-game-only contract."
}

foreach ($anchor in @(
    "CombatChancePuctPlanner",
    "SearchSimulationBudget",
    "SearchNodeBudget",
    "SearchMaxPly",
    "SnapshotSimulationRules",
    "TranspositionHits",
    "DeathRiskLimit",
    "TailRiskPenalty",
    "BuildPrincipalVariation"
)) {
    if (-not $planner.Contains($anchor)) {
        throw "Aura combat AI Chance-PUCT planner contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatSimulationState",
    "CombatActionModel",
    "CombatActionOutcome",
    "reductionSpent",
    "HandLimit",
    "public ulong Hash()",
    "ApplyDamage"
)) {
    if (-not $forwardModel.Contains($anchor)) {
        throw "Aura combat AI forward model contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "RegisterEffectResolver",
    "TryResolveEffects",
    "RegisterSimulationRule",
    "EvaluateSimulation"
)) {
    if (-not $registry.Contains($anchor)) {
        throw "Aura combat AI extension registry contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-search.gbdt.v1",
    "BoundedTreeCombatSearchGuidanceModel",
    "CombatSearchGuidanceTrainer",
    "PolicyLogit",
    "LeafValue",
    "DeathRisk"
)) {
    if (-not $guidance.Contains($anchor)) {
        throw "Aura combat AI search guidance contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.episode.v1",
    "LongTermReturn",
    "SearchVisits",
    "validationValueMae",
    "PolicyTargets",
    "CancellationToken cancellationToken"
)) {
    if (-not $episode.Contains($anchor)) {
        throw "Aura combat episode learning contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "SemanticCoverage >= 1d",
    "Math.Pow(0.99d",
    "CombatEpisodeFrame",
    "SearchDeathRisk"
)) {
    if (-not $episodeRecorder.Contains($anchor)) {
        throw "Aura combat episode recorder contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-policy-value.mlp.v1",
    "ICombatPolicyValueModel",
    "EvaluateBatch",
    "ExpectedReturn",
    "DeathProbability"
)) {
    if (-not $policyValue.Contains($anchor)) {
        throw "Aura combat policy-value network contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatPolicyEvolutionRunner",
    "TrainingEpisodesPerIteration",
    "ArenaEpisodesPerIteration",
    "MaximumWinRateRegression",
    "Promoted"
)) {
    if (-not $evolution.Contains($anchor)) {
        throw "Aura combat policy evolution contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "QueueEvolution",
    "RunJourneyEvaluation",
    "*.journey.json",
    "checkpoints",
    "episodes-v1.jsonl",
    "evolution-summary.json",
    "WritePolicyValueCandidate",
    "ResolveEvolutionScenarios",
    "AutoBattleSimulationOperation",
    "GetResultPresentation",
    "LatestEvolutionPath"
)) {
    if (-not $simulationUiRuntime.Contains($anchor)) {
        throw "AuraTools policy evolution runtime contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "QueueImportCandidate",
    "AutoBattleCandidateBundle",
    "CaptureTrainingSnapshot",
    "CandidateMeetsValidationGate",
    "QueueRollbackChampion",
    "CancelTraining",
    "AnyTrainingBusy",
    "CancellationTokenSource.CreateLinkedTokenSource",
    "AutoBattleTrainingStage.Cancelling"
)) {
    if (-not $modelUiRuntime.Contains($anchor)) {
        throw "AuraTools model task response contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "AutoBattleEvolutionView",
    "QueueRun",
    "QueueEvolution",
    "AuraToolsAutoBattleSimulationResultView",
    "AuraToolsAutoBattleWorkLockView",
    "InputField.ContentType.IntegerNumber"
)) {
    if (-not $settingsUiRuntime.Contains($anchor)) {
        throw "AuraTools auto-battle interaction contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.sample.v3",
    "aura.combat-ai.sample.v4",
    "aura.combat-ai.selection.v1",
    "aura.combat-ai.training-report.v1",
    "aura.decision-residual.linear.v1",
    "HumanPolicyDisagreementCount",
    "PolicyVisibleToHuman",
    "HumanPolicyVisibleCount",
    '"UsedAsPreferenceLabels": False',
    "MaximumCorrection",
    "ApplicabilityProtocolVersion",
    "CategoryObservationCounts"
)) {
    if (-not $trainer.Contains($anchor)) {
        throw "Aura combat AI trainer contract is missing: $anchor"
    }
}

if ($trainer.Contains("positive, negative = other, chosen")) {
    throw "Policy failures must not invent counterfactual preference labels."
}

Write-Host "Aura combat AI source contracts passed."
& (Join-Path $root "tools\Test-AuraCombatKnowledge.ps1")
