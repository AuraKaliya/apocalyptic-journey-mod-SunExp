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
    --policy risk-puct
if ($LASTEXITCODE -ne 0) {
    throw "Aura headless combat simulation contract failed with exit code $LASTEXITCODE."
}
$simulationResult = Get-Content -LiteralPath $simulationOutput -Raw | ConvertFrom-Json
Remove-Item -LiteralPath $simulationOutput -Force
if ($simulationResult.Statistics.CompletedSimulations -ne 4 `
    -or $simulationResult.Statistics.Invalid -ne 0 `
    -or $simulationResult.Statistics.AuthoritativeSimulations -ne 4 `
    -or $simulationResult.Results[0].FinalStateHash -ne "6eb962488d6833d1" `
    -or [string]::IsNullOrWhiteSpace($simulationResult.RulesetHash)) {
    throw "Aura headless combat simulation result contract is invalid."
}

$controllerPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleRuntime.cs"
$presenterPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattlePredictionPresenter.cs"
$interactionPath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatInteractionRuntime.cs"
$runtimePath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatRuntime.cs"
$playerEquivalentPath = Join-Path $root "AuraCombatAiShared\CombatPlayerEquivalent.cs"
$plannerPath = Join-Path $root "AuraCombatAiShared\CombatRiskAwareRootSamplingPuctPlanner.cs"
$worldModelContractsPath = Join-Path $root "AuraCombatAiShared\CombatWorldModelContracts.cs"
$governancePath = Join-Path $root "AuraCombatAiShared\CombatDecisionGovernance.cs"
$transformerTeacherPath = Join-Path $root "AuraCombatAiShared\CombatTransformerTeacher.cs"
$transformerRuntimeResolverPath = Join-Path $root `
    "AuraCombatAiShared\CombatTransformerRuntimeResolver.cs"
$foundationAutoTuningPath = Join-Path $root `
    "AuraCombatAiShared\CombatFoundationAutoTuning.cs"
$transformerTeacherScriptPath = Join-Path $root `
    "tools\transformer-teacher\train_teacher.py"
$packagedTransformerTeacherScriptPath = Join-Path $root `
    "AuraToolsExp\TrainingWorker\TransformerTeacher\train_teacher.py"
$riskStatisticsPath = Join-Path $root "AuraCombatAiShared\CombatSearchRiskStatistics.cs"
$searchBudgetPath = Join-Path $root "AuraCombatAiShared\CombatSearchBudgetPolicy.cs"
$loopSafetyPath = Join-Path $root "AuraCombatAiShared\CombatLoopSafetyAnalyzer.cs"
$forwardModelPath = Join-Path $root "AuraCombatAiShared\CombatForwardModel.cs"
$endTurnSafetyPath = Join-Path $root "AuraCombatAiShared\CombatEndTurnSafety.cs"
$endTurnTransitionPath = Join-Path $root "AuraCombatAiShared\CombatEndTurnTransition.cs"
$searchProjectorPath = Join-Path $root "AuraCombatAiShared\CombatSearchFeatureProjector.cs"
$batchTrainerPath = Join-Path $root "AuraCombatAiShared\CombatPolicyValueBatchTrainer.cs"
$workerContractsPath = Join-Path $root "AuraCombatAiShared\CombatFoundationWorkerContracts.cs"
$checkpointStoragePath = Join-Path $root "AuraCombatAiShared\CombatFoundationCheckpointStorage.cs"
$externalContractsPath = Join-Path $root "AuraCombatAiShared\CombatFoundationExternalContracts.cs"
$registryPath = Join-Path $root "AuraCombatAiShared\CombatAiRegistry.cs"
$guidancePath = Join-Path $root "AuraCombatAiShared\CombatSearchGuidance.cs"
$simulationEnginePath = Join-Path $root "AuraCombatSimulationShared\CombatSimulationEngine.cs"
$simulationModelsPath = Join-Path $root "AuraCombatSimulationShared\CombatSimulationModels.cs"
$actionContractsPath = Join-Path $root "AuraCombatSimulationShared\CombatActionContracts.cs"
$turnTransitionRulesPath = Join-Path $root "AuraCombatSimulationShared\CombatTurnTransitionRules.cs"
$simulationBatchPath = Join-Path $root "AuraCombatSimulationShared\CombatBatchRunner.cs"
$campaignSimulationPath = Join-Path $root "AuraCombatSimulationShared\CombatCampaignSimulation.cs"
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
$foundationTrainingPath = Join-Path $root "AuraCombatAiShared\CombatCampaignFoundationTraining.cs"
$foundationStrategyPath = Join-Path $root "AuraCombatAiShared\CombatFoundationTrainingStrategy.cs"
$foundationCaseLearningPath = Join-Path $root "AuraCombatAiShared\CombatFoundationCaseLearning.cs"
$foundationCaseArchiveProtocolPath = Join-Path $root "AuraCombatAiShared\CombatFoundationCaseArchiveProtocol.cs"
$modelCoveragePath = Join-Path $root "AuraCombatAiShared\CombatModelCoverage.cs"
$contentPackagesPath = Join-Path $root "AuraCombatAiShared\CombatContentPackages.cs"
$modelAdaptersPath = Join-Path $root "AuraCombatAiShared\CombatModelAdapters.cs"
$contentRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsCombatContentRuntime.cs"
$gameValidationProtocolPath = Join-Path $root "AuraCombatAiShared\CombatGameValidation.cs"
$simulationUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSimulationRuntime.cs"
$gameValidationRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleGameValidationRuntime.cs"
$foundationRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleFoundationRuntime.cs"
$foundationWorkerRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsFoundationWorkerRuntime.cs"
$foundationWorkerProjectPath = Join-Path $root "AuraFoundationTrainer.Worker\AuraFoundationTrainer.Worker.csproj"
$foundationWorkerProgramPath = Join-Path $root "AuraFoundationTrainer.Worker\Program.cs"
$nativeRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsNativeRewardSimulationRuntime.cs"
$nativeProgramsPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\Generated\AuraToolsNativePrograms.g.cs"
$modelUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleModelRuntime.cs"
$journeyUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleJourneyRuntime.cs"
$settingsUiRuntimePath = Join-Path $root "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs"
$baseGameKnowledgePath = Join-Path $root "AuraToolsExp\Config\combat-knowledge.base-game.json"
$bundledRulesPath = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-base-evaluation-v2.ruleset.json"
$bundledCampaignV2Path = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-world-simulation-v2.campaign.json"
$authoritativeSeedPath = Join-Path $root "tools\combat-simulation\witch-base-authoritative-seed.json"
$obsoleteRulesPath = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-base-evaluation-v1.ruleset.json"
$obsoleteJourneyPath = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-world-simulation-v1.journey.json"
$campaignGeneratorPath = Join-Path $root "tools\Build-AuraStandardCampaign.ps1"
$frozenTournamentPath = Join-Path $root `
    "tools\Run-AuraFoundationFrozenTournament.ps1"
$controller = Get-Content -LiteralPath $controllerPath -Raw
$presenter = Get-Content -LiteralPath $presenterPath -Raw
$interaction = Get-Content -LiteralPath $interactionPath -Raw
$runtime = Get-Content -LiteralPath $runtimePath -Raw
$playerEquivalent = Get-Content -LiteralPath $playerEquivalentPath -Raw
$planner = Get-Content -LiteralPath $plannerPath -Raw
$worldModelContracts = Get-Content -LiteralPath $worldModelContractsPath -Raw
$governance = Get-Content -LiteralPath $governancePath -Raw
$transformerTeacher = Get-Content -LiteralPath $transformerTeacherPath -Raw
$transformerRuntimeResolver = Get-Content `
    -LiteralPath $transformerRuntimeResolverPath -Raw
$foundationAutoTuning = Get-Content -LiteralPath $foundationAutoTuningPath -Raw
$transformerTeacherScript = Get-Content `
    -LiteralPath $transformerTeacherScriptPath -Raw
$packagedTransformerTeacherScript = Get-Content `
    -LiteralPath $packagedTransformerTeacherScriptPath -Raw
$riskStatistics = Get-Content -LiteralPath $riskStatisticsPath -Raw
$searchBudget = Get-Content -LiteralPath $searchBudgetPath -Raw
$loopSafety = Get-Content -LiteralPath $loopSafetyPath -Raw
$forwardModel = Get-Content -LiteralPath $forwardModelPath -Raw
$endTurnSafety = Get-Content -LiteralPath $endTurnSafetyPath -Raw
$endTurnTransition = Get-Content -LiteralPath $endTurnTransitionPath -Raw
$searchProjector = Get-Content -LiteralPath $searchProjectorPath -Raw
$batchTrainer = Get-Content -LiteralPath $batchTrainerPath -Raw
$workerContracts = Get-Content -LiteralPath $workerContractsPath -Raw
$checkpointStorage = Get-Content -LiteralPath $checkpointStoragePath -Raw
$externalContracts = Get-Content -LiteralPath $externalContractsPath -Raw
$registry = Get-Content -LiteralPath $registryPath -Raw
$guidance = Get-Content -LiteralPath $guidancePath -Raw
$simulationEngine = Get-Content -LiteralPath $simulationEnginePath -Raw
$simulationModels = Get-Content -LiteralPath $simulationModelsPath -Raw
$actionContracts = Get-Content -LiteralPath $actionContractsPath -Raw
$turnTransitionRules = Get-Content -LiteralPath $turnTransitionRulesPath -Raw
$simulationBatch = Get-Content -LiteralPath $simulationBatchPath -Raw
$campaignSimulation = Get-Content -LiteralPath $campaignSimulationPath -Raw
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
$foundationTraining = Get-Content -LiteralPath $foundationTrainingPath -Raw
$foundationStrategy = Get-Content -LiteralPath $foundationStrategyPath -Raw
$foundationCaseLearning = Get-Content -LiteralPath $foundationCaseLearningPath -Raw
$foundationCaseArchiveProtocol = Get-Content -LiteralPath $foundationCaseArchiveProtocolPath -Raw
$modelCoverage = Get-Content -LiteralPath $modelCoveragePath -Raw
$contentPackages = Get-Content -LiteralPath $contentPackagesPath -Raw
$modelAdapters = Get-Content -LiteralPath $modelAdaptersPath -Raw
$contentRuntime = Get-Content -LiteralPath $contentRuntimePath -Raw
$gameValidationProtocol = Get-Content -LiteralPath $gameValidationProtocolPath -Raw
$simulationUiRuntime = Get-Content -LiteralPath $simulationUiRuntimePath -Raw
$gameValidationRuntime = Get-Content -LiteralPath $gameValidationRuntimePath -Raw
$foundationRuntime = Get-Content -LiteralPath $foundationRuntimePath -Raw
$foundationWorkerRuntime = Get-Content -LiteralPath $foundationWorkerRuntimePath -Raw
$foundationWorkerProject = Get-Content -LiteralPath $foundationWorkerProjectPath -Raw
$foundationWorkerProgram = Get-Content -LiteralPath $foundationWorkerProgramPath -Raw
$nativeRuntime = Get-Content -LiteralPath $nativeRuntimePath -Raw
$nativePrograms = Get-Content -LiteralPath $nativeProgramsPath -Raw
$modelUiRuntime = Get-Content -LiteralPath $modelUiRuntimePath -Raw
$journeyUiRuntime = Get-Content -LiteralPath $journeyUiRuntimePath -Raw
$settingsUiRuntime = Get-Content -LiteralPath $settingsUiRuntimePath -Raw
$trainer = Get-Content -LiteralPath $trainerPath -Raw
$campaignGenerator = Get-Content -LiteralPath $campaignGeneratorPath -Raw
$frozenTournament = Get-Content -LiteralPath $frozenTournamentPath -Raw

$requiredControllerAnchors = @(
    "FightUI.ThrowCardScript",
    "FightUI.Burning",
    "CombatActionTransaction",
    "CombatActionTransactionState.HandedOff",
    "CombatActionTransactionState.TimedOut",
    "auto-battle-training-v7.jsonl",
    "TryCapturePlayerObservation",
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
    "CombatFoundationTrainingSubject",
    "CombatFoundationDeclaredCoverage",
    "CombatModelCoverageAssessment",
    "CreateDeclaredCoverage",
    "RuntimeExtraCardPackIds",
    "RoleSkillFallbackRequired",
    "CoverageAwareCombatPolicyValueModel",
    "CombatActionKind.UseSkill",
    "CombatActionKind.PlayCard"
)) {
    if (-not $modelCoverage.Contains($anchor)) {
        throw "Aura portable foundation model coverage contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "status.GetBuffs()",
    "CombatStatusObservation",
    "ObserveDeck",
    "CombatDeckKnowledge",
    "KnownDeckCardIds",
    "CombatPlayerObservationBoundary.Normalize",
    "AddBoundAction",
    "TryResolvePresentation"
)) {
    if (-not $runtime.Contains($anchor)) {
        throw "Aura combat AI authoritative observation contract is missing: $anchor"
    }
}
if ($runtime.Contains("DrawPileCardIds") -or
    $runtime.Contains("result.Features[pair.Key]")) {
    throw "Aura combat AI runtime still exposes hidden deck order or unregistered runtime variables."
}
if ($runtime.Contains('"PercentDefence"') -or $runtime.Contains('"PercentHeal"')) {
    throw "Aura combat AI still references obsolete runtime multiplier keys."
}

foreach ($anchor in @(
    "PlayerCombatObservation",
    "CombatBeliefTracker",
    "CombatRootDeterminizer",
    "CombatExecutionContext",
    "CombatDecisionExecutionBindingProtocol",
    "TryBindToObservation",
    "selected candidate has no current execution token",
    "CombatPublicFeatureRegistry",
    "CombatPublicObservationHasher",
    "NormalizeSemantics"
)) {
    if (-not $playerEquivalent.Contains($anchor)) {
        throw "Aura player-equivalent observation boundary is missing: $anchor"
    }
}
$baseGameKnowledge = Get-Content -LiteralPath $baseGameKnowledgePath -Raw |
    ConvertFrom-Json
foreach ($expectedBossEncounter in @(
    @{ Level = "level_0"; Enemy = "enemy_10027" },
    @{ Level = "level_10046"; Enemy = "enemy_10048" },
    @{ Level = "level_10048"; Enemy = "enemy_10055" },
    @{ Level = "level_10051"; Enemy = "enemy_10058" }
)) {
    $actualEncounter = $baseGameKnowledge.encounters |
        Where-Object { $_.encounterId -eq $expectedBossEncounter.Level } |
        Select-Object -First 1
    if ($null -eq $actualEncounter `
        -or $actualEncounter.enemyIds -notcontains $expectedBossEncounter.Enemy) {
        throw "Bundled final-boss encounter authority mismatch: $($expectedBossEncounter.Level) -> $($expectedBossEncounter.Enemy)"
    }
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
    "HasPlayerEquivalentReadiness",
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
    "end-turn recapture failed",
    "CombatEndTurnSafety.AssessObservation",
    "end-turn state changed"
)) {
    if (-not $runtime.Contains($anchor)) {
        throw "Aura combat AI live end-turn preflight is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatEndTurnVerdict",
    "CombatEndTurnDecisionTrace",
    "EndTurnDominated",
    "EndTurnAvoidableLethal",
    "CombatCycleOpportunityClassification.Certified"
)) {
    if (-not $endTurnSafety.Contains($anchor) `
        -and -not $endTurnTransition.Contains($anchor)) {
        throw "Aura combat AI end-turn counterfactual contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "NextTurnPower",
    "EnergyCarryOpportunityCost"
)) {
    if (-not $turnTransitionRules.Contains($anchor)) {
        throw "Aura combat turn-transition rule is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatSimulationEngine",
    "ProcessLifecycleEvent",
    "BuildLegalActions",
    "ExecuteEnemyIntent",
    "MaximumTriggerWavesPerAction",
    "UnsupportedRule",
    "CombatBattleStateHasher.Hash",
    "duplicate-reward-rule:",
    "CompleteResultAfterFailure",
    "ResurrectionEscapeOverride",
    "TryOverridePhysicalDefeat",
    "HpLossThisAction",
    "DamageFilterMultiplier",
    "RecentEvents",
    "BuildInvocableActions",
    "RecordNoEffectAction",
    "InteractiveActionContractFailures"
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
    "ParentSequence",
    "CausalChainId",
    "HandlerId",
    "SourceRewardId",
    "SourceActionId",
    "CombatActionApplicationOutcome",
    "NoEffectActionAttemptsThisTurn",
    "ICombatSimulationBorrowedStatePolicy",
    "ICombatSimulationPolicyMetricsProvider"
)) {
    if (-not $simulationModels.Contains($anchor)) {
        throw "Aura combat simulation model contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "action-contract-v2",
    "CombatActionContractDefinition",
    "GameInvocable",
    "PolicyEligible",
    "AppliedPostconditionsSatisfied",
    "suppressed after a no-effect attempt this turn"
)) {
    if (-not $actionContracts.Contains($anchor)) {
        throw "Aura combat action contract is missing: $anchor"
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
    "CombatCampaignCardAcquisition",
    "CombatCampaignCardAcquisitionPolicy",
    "CardRewardEncounterKinds",
    "TargetDeckSizeMaximum",
    "CombatCampaignBuildPlan",
    "SynergySources",
    "DeckDilutionPenalty",
    "SkipScore",
    "CombatCampaignAttributeThresholdRewardDefinition",
    "CombatCampaignAttributeThresholdRewardReconciler",
    "AttributeThresholdRewards",
    "RemovedCardIds",
    "AdjustDeckAtLayerEnd",
    "ReserveCards",
    "CardRemovalScore"
)) {
    if (-not $campaignSimulation.Contains($anchor)) {
        throw "Aura campaign progression-policy contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "MaximumDegreeOfParallelism",
    "Parallel.For",
    "Interlocked.Increment",
    "EnableEarlyValidationStop",
    "EarlyStopReason",
    "CombatCampaignFoundationTelemetry",
    "PeakConcurrentCampaigns",
    "ObservedWorkerThreads",
    "GC.CollectionCount",
    "RunMonitored",
    "MaximumCompletedBattleDepth",
    "MaximumActiveBattleDepth",
    "SearchSimulationsPerSecond",
    "EstimatedRemainingSeconds",
    "TrainingFailureCounts",
    "TrainingFailures",
    "AddIntegrityFailure",
    "TrainingReplayDroppedDuplicates",
    "TrainingReplayTargetNormalShare",
    "TrainingReplayQuotaShortfalls",
    "EnableHardSeedCurriculum",
    "HardSeedReplayShare",
    "HardSeedTrainingVictories",
    "ApplyHardEncounterTargets",
    "CombatFoundationTerminalCreditProtocol",
    "CombatFoundationCounterfactualProtocol",
    "ClassifyCounterfactual",
    "HardSeedCounterfactualImprovements",
    "DiscardedCounterfactualEpisodes",
    "CombatFoundationStagnationProtocol",
    "MaximumConsecutiveRejectedIterations",
    "StoppedForStagnation",
    "ShouldStopForStagnation",
    "ShouldRunCounterfactualHardEncounter",
    "HardSeedCounterfactualVictories",
    "RunCapabilityProbe",
    "champion-teacher-hard",
    "RewardResidualTraining",
    "RuleTerminalOverrides",
    "CertifiedLoops",
    "FakeLoops",
    "no-meaningful-gain",
    "ArenaConfirmationPairs",
    "TerminalConsistencyViolations",
    "FeatureLeakageViolations",
    "Math.Min(1024",
    "AutoTuneCampaignKey",
    "BuildAutoTuneParallelismCandidates",
    "RetainValidationRunDetails",
    "CompactValidationRun(campaign)"
)) {
    if (-not $foundationTraining.Contains($anchor)) {
        throw "Aura foundation CPU training contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatFoundationHardSeedCurriculum",
    "TerminalScenarioId",
    "WorldSeed",
    "OutcomeClass",
    "FailureCluster",
    "FailureEncounterCheckpoint",
    "RoutedBuildLimitedCampaigns",
    "RoutedProvisionalBuildLimitedCampaigns",
    "AdvancedShare = 0.35d"
)) {
    if (-not $foundationStrategy.Contains($anchor)) {
        throw "Aura hard-seed curriculum contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatFoundationExpertReplaySelection",
    "SelectExpertReplay",
    "maximumEpisodesPerRun",
    "QuotaShortfalls",
    "TrainRewardResiduals",
    "maximumAbsoluteResidual",
    "SelectedRewardIds",
    "RelicResiduals",
    "BlessingResiduals",
    "CombatFoundationCaseArchiveLoadDiagnostics"
)) {
    if (-not $foundationCaseLearning.Contains($anchor)) {
        throw "Aura foundation case-learning contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "RewardScoreResiduals",
    "RewardScoreResidualMaximumAbsolute",
    "LearnedResidual",
    "RewardScoreBiases",
    "RewardScoreBiasMaximumAbsolute",
    "ConfiguredBias",
    "PickWeightedUnused",
    "RunMonitoredSegment",
    "RunMonitoredWithEncounterStarts"
)) {
    if (-not $campaignSimulation.Contains($anchor)) {
        throw "Aura campaign optimization contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "ModelTrainingProgress",
    "CombatCampaignFoundationResumeState",
    "PublishCheckpoint",
    "CombatPolicyValueTrainingSession",
    "ReplayEpisodeLimit"
)) {
    if (-not $foundationTraining.Contains($anchor)) {
        throw "Aura resumable foundation training contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "Parallel.For",
    "ApplyBatch",
    "Shuffle",
    "EarlyStoppingPatience",
    "CombatPolicyValueTrainingResumeState",
    "Checkpoint",
    "validationValueMae",
    "optimizerAdamW",
    "gradientClipCount",
    "testCompositeLoss",
    "stateFeatureCollisionRate",
    "CombatPolicyValueFrameStratificationProtocol",
    "BuildStratifiedOrder",
    "SampleWeight",
    "selectedModel.Metrics",
    "candidateEpoch",
    "maximumFrameWeight"
)) {
    if (-not $batchTrainer.Contains($anchor)) {
        throw "Aura deterministic minibatch trainer contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "AuraToolsNativeRewardExtensionFactory",
    "AuraToolsNativeProgramPackageAudit.Validate",
    "BeginReadinessRefresh",
    "TryGetCachedFoundationPackage",
    "foundation.ExecutionMode",
    "AuraToolsFoundationWorkerRuntime.Run",
    "CombatFoundationWorkerJobFactory.Create",
    "ToSharedParameters",
    "result.TrainingFailures",
    "EnableCounterfactualHardEncounters",
    "EnableFrameStratification",
    "CombatFoundationCaseArchiveProtocol"
)) {
    if (-not $foundationRuntime.Contains($anchor)) {
        throw "AuraTools authoritative foundation runtime contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatFoundationTrainingParameters",
    "CombatFoundationWorkerJobFactory",
    "PreflightSeedStart = parameters.TrainingSeedStart",
    "ExpertReplayEpisodeLimit",
    "archive loading deferred to worker",
    "CombatFoundationModelPackageProtocol",
    "foundation-model-package-v3.json",
    "training-accepted",
    "CombatPolicyValueNetworkValidator.TryValidate"
)) {
    if (-not $externalContracts.Contains($anchor)) {
        throw "Aura external foundation training contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatFoundationWorkerProtocol",
    "public const int SchemaVersion = 11",
    "CheckpointFileName",
    "CheckpointEpisodesFileName",
    "TryValidateJob",
    "TryValidateProgress",
    "TryValidateResult",
    "CombatFoundationWorkerJob",
    "CombatFoundationWorkerProgress",
    "CombatFoundationWorkerResult",
    "CombatFoundationWorkerCheckpoint",
    "CheckpointEpisodesPath",
    "ModelPackagePath",
    "Resumable",
    "CompletionKind"
)) {
    if (-not $workerContracts.Contains($anchor)) {
        throw "Aura foundation worker protocol is missing: $anchor"
    }
}
foreach ($anchor in @(
    "TrainingWorker",
    "CancellationPath",
    "ExpectedRulesetHash",
    "CreateNoWindow",
    "Kill",
    "CombatFoundationWorkerProtocol.TryValidateProgress",
    "progressDiagnostic",
    "[Worker][Progress]"
)) {
    if (-not $foundationWorkerRuntime.Contains($anchor)) {
        throw "AuraTools external foundation worker runtime is missing: $anchor"
    }
}

$forbiddenCurrentTreeMarkers = @(
    ("Combat" + "TurnPlanner"),
    ("UseChance" + "Puct"),
    ("Beam" + "Width"),
    ("EnableDynamic" + "SearchBudget"),
    ("chance-" + "puct"),
    ("aura.combat-ai.sample." + "v5"),
    ("aura.combat-ai.episode." + "v2"),
    ("aura.combat-ai.episode." + "v3"),
    ("partitioned-" + "v2"),
    ("hashed-" + "v1"),
    ("Create" + "Legacy"),
    ("foundation-training-checkpoint-" + "v3"),
    ("foundation-training-checkpoint-" + "v4"),
    ("foundation-training-checkpoint-" + "v5"),
    ("foundation-training-checkpoint-" + "v7"),
    ("foundation-training-episodes-" + "v2"),
    ("foundation-training-episodes-" + "v3"),
    ("live-combat-episodes-" + "v2"),
    ("live-combat-episodes-" + "v3"),
    ("success-case-archive-worker-" + "v2"),
    ("auto-battle-training-" + "v5"),
    ("UseTrained" + "Model"),
    ("ValidationCampaignsPer" + "Difficulty")
)
foreach ($marker in $forbiddenCurrentTreeMarkers) {
    $matches = & git -C $root grep -n -I -- $marker -- `
        "*.cs" "*.py" "*.ps1" "*.json" "*.md" 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "Aura combat AI current tree still contains removed marker '$marker': $matches"
    }
}

$avoidedRelicIds = @(
    "relic_5",
    "relic_28",
    "relic_52",
    "CrowdFundingRelic_24",
    "CrowdFundingRelic_43",
    "relic_38",
    "CrowdFundingRelic_12",
    "CrowdFundingRelic_13"
)
$campaignV2Json = Get-Content `
    -LiteralPath $bundledCampaignV2Path `
    -Raw `
    -Encoding UTF8
$campaignV2 = $campaignV2Json | ConvertFrom-Json
$expectedOriginThresholdRewards = @{
    Strength = @("blessing_101", "blessing_105", "blessing_109", "blessing_113")
    Lucky = @("blessing_102", "blessing_106", "blessing_110", "blessing_114")
    Perceive = @("blessing_104", "blessing_108", "blessing_112", "blessing_116")
    Wisdom = @("blessing_103", "blessing_107", "blessing_111", "blessing_115")
}
if ($campaignV2.campaignVersion -ne "3.0.0" `
    -or @($campaignV2.attributeThresholdRewards).Count -ne 16) {
    throw "Bundled campaign origin threshold protocol is incomplete."
}
foreach ($attributeId in $expectedOriginThresholdRewards.Keys) {
    $actualIds = @($campaignV2.attributeThresholdRewards |
        Where-Object { $_.attributeId -eq $attributeId } |
        Sort-Object threshold |
        ForEach-Object { $_.rewardId })
    if (($actualIds -join ",") -ne `
        ($expectedOriginThresholdRewards[$attributeId] -join ",")) {
        throw "Bundled campaign origin threshold mapping is invalid: $attributeId"
    }
}
foreach ($anchor in @(
    '$attributeThresholdRewards = @(',
    'attributeThresholdRewards = @($attributeThresholdRewards)',
    'campaignVersion = "3.0.0"',
    'New-StatusTrigger "rotten-action" "ActionResolved"',
    'witch-base-authoritative-seed.json',
    'if ($id -eq "ritualcard_8")'
)) {
    if (-not $campaignGenerator.Contains($anchor)) {
        throw "Campaign generator origin threshold contract is missing: $anchor"
    }
}
if ($campaignGenerator.Contains('-notmatch "(?i)skill|技能"')) {
    throw "Campaign generator must not exclude skill cards from reward and ruleset generation."
}
foreach ($relicId in $avoidedRelicIds) {
    $escapedRelicId = [regex]::Escape($relicId)
    $rewardPattern = '"rewardId"\s*:\s*"' + $escapedRelicId `
        + '"[\s\S]{0,180}?"offerWeight"\s*:\s*0\.05(?:0+)?'
    $biasPattern = '"' + $escapedRelicId `
        + '"\s*:\s*-4(?:\.0+)?'
    if (-not [regex]::IsMatch($campaignV2Json, $rewardPattern) `
        -or -not [regex]::IsMatch($campaignV2Json, $biasPattern)) {
        throw "Avoided relic campaign weight/bias is invalid: $relicId"
    }
    if (-not $campaignGenerator.Contains('"' + $relicId + '" = -4.0')) {
        throw "Campaign generator is missing avoided relic policy: $relicId"
    }
}
foreach ($anchor in @(
    'Version = "success-case-archive-worker-v4"',
    "StorageVersion = 4",
    "CompatibilityKeyLength = 16",
    "EntryKeyLength = 24",
    "CompressedJsonExtension",
    "MaximumExpertCasesPerCompatibility",
    "MaximumObservationsPerCompatibility",
    "CompactIdentifier"
)) {
    if (-not $foundationCaseArchiveProtocol.Contains($anchor)) {
        throw "Aura foundation compact case archive protocol is missing: $anchor"
    }
}
foreach ($anchor in @(
    "WriteEpisodeSnapshot",
    "WriteAtomicText",
    "WriteAtomicStream",
    "ReadAndValidateJsonLines",
    "FileShare.ReadWrite | FileShare.Delete",
    "File.Replace",
    "MaximumFileAttempts",
    "CleanupArtifacts",
    "DeleteCheckpointArtifacts"
)) {
    if (-not $checkpointStorage.Contains($anchor)) {
        throw "Aura foundation transactional checkpoint storage is missing: $anchor"
    }
}
foreach ($anchor in @(
    "PrepareCaseArchive",
    "LoadSuccessCasePaths",
    "LoadObservationPaths",
    "ResolveSuccessCasePath",
    "ResolveObservationPath",
    "WriteAtomicCompressed",
    "ArchiveWriteBudget.TryReserve",
    "PersistBuildLimitedSeeds",
    "ApplyRewardResiduals",
    "AcquireTrainingLease",
    "CombatFoundationModelPackageProtocol.Create",
    "ModelPackagePath",
    "ReplayIdentity",
    "EpisodeSnapshot = nextSnapshot",
    "checkpointWriteFailures++",
    "memory-tier-",
    "AutoTuneCampaignKey",
    "TryGetResumableCheckpoint",
    "WriteAtomicJson",
    "training.ValidationRuns.Clear()"
)) {
    if (-not $foundationWorkerProgram.Contains($anchor)) {
        throw "Aura foundation worker-owned case archive contract is missing: $anchor"
    }
}
if (-not [regex]::IsMatch(
        $campaignV2Json,
        '"rewardScoreBiasMaximumAbsolute"\s*:\s*8(?:\.0+)?')) {
    throw "Campaign reward score bias bound is invalid."
}
foreach ($anchor in @(
    "net8.0",
    "ServerGarbageCollection",
    "ConcurrentGarbageCollection",
    "TieredPGO",
    "AuraToolsNativePrograms.g.cs"
)) {
    if (-not $foundationWorkerProject.Contains($anchor)) {
        throw "Aura foundation worker build contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "NativeRewardProgramRegistry",
    "AuraToolsNativeProgramPackageAudit",
    "NativeDefinitionPresence",
    "AddAndGetBuff",
    '"DamageFilter."',
    "Cast<NativeRewardDataConfig>"
)) {
    if (-not $nativeRuntime.Contains($anchor)) {
        throw "AuraTools precompiled native program contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "TryRunPrecompiledProgram",
    "ContainsPrecompiledProgram",
    "PrecompiledProgramProtocol"
)) {
    if (-not $nativePrograms.Contains($anchor)) {
        throw "AuraTools generated native program contract is missing: $anchor"
    }
}
foreach ($forbidden in @(
    "CSharpScript",
    "CSharpCompilation",
    "Microsoft.CodeAnalysis",
    "NativeRewardScriptCompiler",
    "AuraToolsNativeRewardRuntimeWarmup"
)) {
    if ($nativeRuntime.Contains($forbidden)) {
        throw "AuraTools runtime must not contain dynamic compilation: $forbidden"
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
    "CampaignCompletedBattles",
    "OutcomeClass",
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
    -or -not (Test-Path -LiteralPath $bundledCampaignV2Path -PathType Leaf) `
    -or -not (Test-Path -LiteralPath $authoritativeSeedPath -PathType Leaf) `
    -or (Test-Path -LiteralPath $obsoleteRulesPath) `
    -or (Test-Path -LiteralPath $obsoleteJourneyPath)) {
    throw "Bundled standard evaluation package is incomplete."
}
$bundledRules = Get-Content -LiteralPath $bundledRulesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$bundledCampaign = Get-Content -LiteralPath $bundledCampaignV2Path -Raw -Encoding UTF8 | ConvertFrom-Json
$authoritativeSeed = Get-Content -LiteralPath $authoritativeSeedPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($bundledRules.version -ne "witch-base-evaluation-v2" `
    -or $bundledCampaign.rulesetVersion -ne $bundledRules.version `
    -or $bundledCampaign.player.roleId -ne "career_1" `
    -or $bundledCampaign.PSObject.Properties.Name -contains "retainBlockBetweenTurns" `
    -or $authoritativeSeed.seedId -ne "witch-base-authoritative-seed" `
    -or (Get-Content -LiteralPath $bundledRulesPath -Raw -Encoding UTF8).Contains("Terrias")) {
    throw "Bundled standard evaluation package does not satisfy the base-game-only contract."
}
$divineChoice = @($bundledRules.cards | Where-Object {
    $_.cardId -eq "careercard_1"
}) | Select-Object -First 1
if ($null -eq $divineChoice `
    -or $divineChoice.requiresEnemyTarget `
    -or $divineChoice.verificationSource -ne "Decompiler:v1.0.23816797" `
    -or $divineChoice.actionContract.version -ne "action-contract-v2" `
    -or @($divineChoice.actionContract.preconditions).Count -ne 2 `
    -or $divineChoice.actionContract.preconditionFailureOutcome -ne "NoEffect" `
    -or $divineChoice.actionContract.policyEligibleOnPreconditionFailure) {
    throw "Bundled Divine Choice action contract is missing or unsafe."
}
$nanaDevour = @($bundledRules.cards | Where-Object {
    $_.cardId -eq "careercard_2"
}) | Select-Object -First 1
if ($null -eq $nanaDevour `
    -or $nanaDevour.requiresEnemyTarget `
    -or [int]$nanaDevour.targetScope -ne 7) {
    throw "Bundled Nana devour target scope must include self, friendly and enemy actors."
}

foreach ($anchor in @(
    "CombatRiskAwareRootSamplingPuctPlanner",
    "SnapshotSimulationRules",
    "TranspositionHits",
    "DeathRiskLimit",
    "CombatBeliefTracker.FromObservation",
    "CombatPublicObservationHasher.Seed",
    "RequiresFreshObservation",
    "BuildPrincipalVariation",
    "RootLeadIsStable",
    "CombatLoopSafetyAnalyzer",
    "BuildLoopSummary",
    "SearchModelEvaluationBudget",
    "StoppedByModelBudget",
    "PruneActorCandidates"
)) {
    if (-not $planner.Contains($anchor)) {
        throw "Aura combat AI risk-aware root-sampling PUCT contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatSearchRiskStatistics",
    "EffectiveLowerTailMean",
    "TailRiskPenalty",
    "UncertaintyPenalty",
    "StandardError",
    "RiskPreference"
)) {
    if (-not $riskStatistics.Contains($anchor)) {
        throw "Aura combat AI risk statistics contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "CombatSearchBudgetPolicy",
    "SearchSimulationBudget",
    "SearchNodeBudget",
    "SearchMaxPly",
    "SearchModelEvaluationBudget",
    "EnableActorCandidatePruning",
    "damage-cap-or-limit",
    "loop-or-fake-loop"
)) {
    if (-not $searchBudget.Contains($anchor)) {
        throw "Aura combat AI dynamic search budget contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-world-model.observation.v1",
    "CombatObjectTokenKind",
    "CombatTypedActionEnvelope",
    "CombatTransitionEnvelope",
    "CardInstanceBound",
    "SkillLifecycleBound",
    "CombatWorldModelCoverageManifest",
    "CombatCampaignWorldModelTokenizer",
    "CombatWorldModelTokenEncoding"
)) {
    if (-not $worldModelContracts.Contains($anchor)) {
        throw "Aura combat Transformer object protocol is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatDecisionGovernance",
    "CombatGovernanceDecision",
    "SelectSafeFallback",
    "StoppedByModelBudget",
    "ModelEvaluations",
    "ModelCacheHits"
)) {
    if (-not $governance.Contains($anchor)) {
        throw "Aura combat decision governance contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-transformer-world-model.v2",
    "AutomaticExecutable",
    "HiddenDimensions { get; set; } = 384",
    "Layers { get; set; } = 6",
    "AttentionHeads { get; set; } = 8",
    "FeedForwardDimensions { get; set; } = 1536",
    "EnableWarmStart { get; set; } = true",
    "MaximumFrames { get; set; } = 10000",
    "CpuRefreshInterval { get; set; } = 4",
    "CpuEpochs { get; set; } = 4",
    "CpuIncrementalEpochs { get; set; } = 1",
    "CpuFinalEpochs { get; set; } = 4",
    "EnableFixedAnchorValidation { get; set; } = true",
    "CombatTransformerTeacherProgress",
    "ValidationDynamicsMse",
    "ValidationOutcomeMae",
    "WorldModelQualityGatePassed"
)) {
    if (-not $transformerTeacher.Contains($anchor)) {
        throw "Aura combat Transformer world-model teacher contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "aura.transformer-runtime-probe.v1",
    "AURA_TRANSFORMER_PYTHON",
    "AuraTF",
    "CudaAvailable",
    "ResolutionSource"
)) {
    if (-not $transformerRuntimeResolver.Contains($anchor)) {
        throw "Aura Transformer runtime discovery contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "foundation-auto-tune-v4",
    "foundation-auto-tune-v4.json"
)) {
    if (-not $foundationAutoTuning.Contains($anchor)) {
        throw "Aura foundation auto-tune protocol is missing: $anchor"
    }
}
foreach ($anchor in @(
    "--micro-batch-size",
    "LengthBucketBatchSampler",
    "tensorize_rows",
    "runtime-auto-tune-v2",
    "fixed-anchor",
    "maximum-head-regression",
    "--resume-model",
    "--training-enabled",
    "AURA_TEACHER_PROGRESS",
    "working_set_bytes",
    '"_sampling_repeats"',
    '"TrainingFramesPerSecond"',
    "torch.autocast"
)) {
    if (-not $transformerTeacherScript.Contains($anchor)) {
        throw "Aura Transformer teacher execution plan is missing: $anchor"
    }
}
if ($transformerTeacherScript -cne $packagedTransformerTeacherScript) {
    throw "Packaged Transformer teacher script differs from its source copy."
}
foreach ($anchor in @(
    "CombatLoopClassification",
    "CertifiedLethal",
    "SustainableControl",
    "damageLimitActive",
    "escalationPressure",
    "LoopMinimumHpReserveRatio"
)) {
    if (-not $loopSafety.Contains($anchor)) {
        throw "Aura combat loop safety contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "CombatSimulationState",
    "CombatActionModel",
    "CombatActionOutcome",
    "reductionSpent",
    "HandLimit",
    "public ulong Hash()",
    "public ulong CycleHash()",
    "SelfHpLoss",
    "EndOfCycleSelfHpLoss",
    "ApplyDamage",
    "CloneForTransition"
)) {
    if (-not $forwardModel.Contains($anchor)) {
        throw "Aura combat AI forward model contract is missing: $anchor"
    }
}
if (-not $searchProjector.Contains("ProjectLeafInto")) {
    throw "Aura combat AI leaf projection must support reusable feature buffers."
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
    "aura.combat-ai.episode.v5",
    "public const int FeatureSchemaVersion = 26",
    "content-set-quantile-q-role-quota-risk-aux-fixed-anchor-promotion-v20",
    "frame-strata-v7-strategy-quota-risk-aux",
    "CombatCampaignEpisodeMetadata",
    "TerminalSnapshotKnown",
    "TerminalDoomPower",
    "LongTermReturn",
    "SearchVisits",
    "CombatObservationEnvelope Observation",
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
    "SearchDeathRisk",
    "CombatWorldModelTokenizer.Build"
)) {
    if (-not $episodeRecorder.Contains($anchor)) {
        throw "Aura combat episode recorder contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.transformer-adapter.v2",
    "CombatTransformerAdapterComposition",
    "CombatTransformerLoRAMerger",
    "BuildMergeCacheKey",
    "preference LoRA may target only actor modules",
    "TransformerAdapter"
)) {
    if (-not ($modelAdapters.Contains($anchor) -or $contentPackages.Contains($anchor))) {
        throw "Aura combat Transformer LoRA v2 contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-policy-value.mlp.v2",
    "ICombatPolicyValueModel",
    "EvaluateBatch",
    "ExpectedReturn",
    "DeathProbability",
    "ActionReturnQuantiles",
    "ActionQuantileWeights"
)) {
    if (-not $policyValue.Contains($anchor)) {
        throw "Aura combat policy-value network contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.content-package.v1",
    "CombatTransitionAuditAnalyzer",
    "CombatContentTrainingEpisodeProtocol",
    "FoundationTrainingReady",
    "ContentSetHash",
    "escapes package root",
    "lowercase SHA-256"
)) {
    if (-not $contentPackages.Contains($anchor)) {
        throw "Aura combat content-package contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "AuraSharedResourceProtocol.QueryCatalog",
    "AuraSharedResourceProtocol.ResolvePath",
    "AuraSharedParticipantKinds.Content",
    "AuraSharedResourceKinds.Directory",
    "SnapshotPolicyAdapters",
    "TryLoadAuthoritativeTrainingEpisodes",
    "LiveDatasetDirectory"
)) {
    if (-not $contentRuntime.Contains($anchor)) {
        throw "AuraTools content discovery contract is missing: $anchor"
    }
}
foreach ($forbidden in @(
    "ModsDirectory",
    "ModsDataDirectory",
    "Directory.EnumerateDirectories"
)) {
    if ($contentRuntime.Contains($forbidden)) {
        throw "AuraTools content discovery must not scan private MOD directories: $forbidden"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.adapter.v1",
    "content-low-rank",
    "personal-residual",
    "玩家适配器不得修改动作 Q",
    "AdaptedCombatPolicyValueModel"
)) {
    if (-not $modelAdapters.Contains($anchor)) {
        throw "Aura combat model-adapter contract is missing: $anchor"
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
    "BuildCompatibilityKey",
    "BuildReceiptHash",
    "ValidateRequest",
    "ValidateReport",
    "ModelArtifactHash",
    "NativePackageHash"
)) {
    if (-not $gameValidationProtocol.Contains($anchor)) {
        throw "Aura game-host validation protocol is missing: $anchor"
    }
}
foreach ($anchor in @(
    "FinalBossCases",
    "level_0",
    "enemy_10027",
    "level_10046",
    "enemy_10048",
    "level_10048",
    "enemy_10055",
    "level_10051",
    "enemy_10058",
    "ReadyToInit",
    "IsFake = true",
    "RestoreRole",
    "HideFightPresentation",
    "CanPromote",
    "WriteRawJsonAtomic"
)) {
    if (-not $gameValidationRuntime.Contains($anchor)) {
        throw "Aura game-host validation runtime is missing: $anchor"
    }
}

foreach ($anchor in @(
    "AutoBattleEvolutionView",
    "QueueRun",
    "QueueEvolution",
    "AuraToolsAutoBattleSimulationResultView",
    "AuraToolsAutoBattleWorkLockView",
    "InputField.ContentType.IntegerNumber",
    "EnsureContentBuilt",
    "CPU 并行线程",
    "CampaignsPerSecond",
    "FormatDuration(status.EstimatedRemainingSeconds)",
    "status.ProgressDiagnostic",
    "AuraToolsAutoBattleGameValidationStatusView",
    "隐藏战斗画面"
)) {
    if (-not $settingsUiRuntime.Contains($anchor)) {
        throw "AuraTools auto-battle interaction contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.sample.v7",
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

foreach ($anchor in @(
    "foundation-frozen-tournament-v1",
    "CampaignsPerDifficulty",
    "CompatibilityKey",
    "ConclusivePairWins",
    "AutomaticallyActivatesModel",
    "ProvisionalWinner"
)) {
    if (-not $frozenTournament.Contains($anchor)) {
        throw "Aura frozen multi-model tournament contract is missing: $anchor"
    }
}

Write-Host "Aura combat AI source contracts passed."
& (Join-Path $root "tools\Test-AuraCombatKnowledge.ps1")
& (Join-Path $root "tools\Test-AuraFoundationArchiveMaintenance.ps1")
$global:LASTEXITCODE = 0
