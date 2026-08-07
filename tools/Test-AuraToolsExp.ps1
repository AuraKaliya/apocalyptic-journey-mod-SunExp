param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp-Dev.Tests\AuraToolsExp-Dev.Tests.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraToolsExp test project is missing: $project"
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp tests failed."
}

$runtimeProject = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj") -Raw
$nativeRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsNativeRewardSimulationRuntime.cs") -Raw
$nativeCompiler = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\AuraNativeProgramCompiler\Program.cs") -Raw
$startupRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleRuntime.cs") -Raw
$settingsRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs") -Raw
$uiSnapshotRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleUiSnapshotRuntime.cs") -Raw
$knowledgeRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsCombatKnowledgeRuntime.cs") -Raw
$foundationControllerRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraFoundationTrainer.ControlCenter\MainWindow.cs") -Raw
$foundationControllerModels = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraFoundationTrainer.ControlCenter\ControllerModels.cs") -Raw
$foundationWorkerProgram = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraFoundationTrainer.Worker\Program.cs") -Raw
$foundationCheckpointStorage = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraCombatAiShared\CombatFoundationCheckpointStorage.cs") -Raw
$modelRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleModelRuntime.cs") -Raw
$bundledModelRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsBundledFoundationModelRuntime.cs") -Raw
$simulationRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSimulationRuntime.cs") -Raw
$gameValidationRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleGameValidationRuntime.cs") -Raw
$autoBattleSettings = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsAutoBattleSettings.cs") -Raw
$foundationWorkerBuild = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\Build-AuraFoundationTrainer.ps1") -Raw
$transformerSetup = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\Setup-AuraTransformerTeacher.ps1") -Raw
$transformerInstaller = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\Install-AuraPyTorch.cmd") -Raw
$generatedProgramsPath = Join-Path $repoRoot (
    "AuraToolsExp-Dev\Features\AutoBattle\Generated\AuraToolsNativePrograms.g.cs")
$manifestPath = Join-Path $repoRoot (
    "AuraToolsExp\Config\combat-programs.base-game.manifest.json")
$gameSubjectCatalogPath = Join-Path $repoRoot (
    "AuraToolsExp\Config\combat-simulation\witch-game-subjects-v1.catalog.json")

foreach ($forbidden in @(
    "Microsoft.CodeAnalysis",
    "CSharpScript",
    "CSharpCompilation",
    "ScriptOptions",
    "NativeRewardScriptCompiler",
    "AuraToolsNativeRewardRuntimeWarmup"
)) {
    if ($runtimeProject.Contains($forbidden) -or $nativeRuntime.Contains($forbidden)) {
        throw "AuraToolsExp release runtime contains forbidden dynamic compilation: $forbidden"
    }
}
if ($startupRuntime.Contains("BeginReadinessWarmup")) {
    throw "AuraToolsExp startup must not prewarm or compile combat scripts."
}
$generatedProgramsMissing = -not (
    Test-Path -LiteralPath $generatedProgramsPath -PathType Leaf)
$manifestMissing = -not (
    Test-Path -LiteralPath $manifestPath -PathType Leaf)
if ($generatedProgramsMissing -or $manifestMissing) {
    throw "AuraToolsExp precompiled native program artifacts are missing."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifestInvalid = $manifest.runtimeProtocol -ne "aura.native-programs.precompiled.v1" `
    -or $manifest.programCount -lt 400 `
    -or [string]::IsNullOrWhiteSpace($manifest.programSetSha256)
if ($manifestInvalid) {
    throw "AuraToolsExp precompiled native program manifest is invalid."
}
$uiReadsScenarioFiles = $settingsRuntime.Contains(
    "AuraToolsAutoBattleSimulationRuntime.AvailableScenarioIds().ToList()")
$uiReadsResults = $settingsRuntime.Contains(
    "AuraToolsAutoBattleSimulationRuntime.GetResultPresentation(")
$uiReadsModels = $settingsRuntime.Contains(
    ".SnapshotModelLibrary(autoBattle.Profile)")
if ($uiReadsScenarioFiles -or $uiReadsResults -or $uiReadsModels) {
    throw "AuraTools settings UI must read auto-battle data through its immutable snapshot."
}
$showPanelMatch = [regex]::Match(
    $settingsRuntime,
    "(?s)private static void ShowAuraPanel\(\).*?\n    \}")
$openRebuildsUnconditionally = $showPanelMatch.Value -match `
    "SetAsLastSibling\(\);\s*RebuildPanel\(activePanel\.transform\);"
if (-not $showPanelMatch.Success -or $openRebuildsUnconditionally) {
    throw "Opening the AuraTools panel must reuse the retained UI tree."
}
foreach ($anchor in @(
    "AuraSharedBackgroundWorkScheduler.Queue",
    "AutoBattle.UiSnapshot",
    "ApplyOnMainThread"
)) {
    if (-not $uiSnapshotRuntime.Contains($anchor)) {
        throw "AuraTools UI snapshot background-loading contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "BeginBundledPackageLoad",
    "KnowledgePackageLoad",
    "AuraSharedBackgroundWorkKind.Io",
    "TryExportBaseGameTables",
    "OpenBaseGameTableExportDirectory",
    "witch-tables-"
)) {
    if (-not $knowledgeRuntime.Contains($anchor)) {
        throw "AuraTools knowledge package background-loading contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "AutoBattleDatasetExportRow",
    "TryExportBaseGameTables",
    "OpenBaseGameTableExportDirectory"
)) {
    if (-not $settingsRuntime.Contains($anchor)) {
        throw "AuraTools game dataset export UI contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "activeHandlerChains",
    "activeHandlerIds",
    "CausalChainId",
    "SourceRewardId",
    "generated-only card leaked into reward pool",
    "[ThreadStatic]",
    "threadGlobals"
)) {
    if (-not $nativeRuntime.Contains($anchor)) {
        throw "AuraTools causal-chain/card-acquisition contract is missing: $anchor"
    }
}
$generatedPrograms = Get-Content -LiteralPath $generatedProgramsPath -Raw
if (-not $nativeRuntime.Contains("UseAndBurnDrawPileSnapshot") `
    -or -not $nativeCompiler.Contains("UseAndBurnDrawPileSnapshot") `
    -or -not $generatedPrograms.Contains("UseAndBurnDrawPileSnapshot();") `
    -or $generatedPrograms.Contains("DeckCard[i]")) {
    throw "AuraTools Supernova snapshot normalization contract is missing."
}
if (-not $nativeRuntime.Contains("ApplyCopiedProgramDefaults") `
    -or -not $nativeRuntime.Contains("InvokeFirst") `
    -or -not $nativeRuntime.Contains("InvokeLast") `
    -or $generatedPrograms.Contains("effectList.First().action()") `
    -or $generatedPrograms.Contains("effectList.Last().action()")) {
    throw "AuraTools copied-program defaults or deferred-effect safety contract is missing."
}
if (-not $nativeRuntime.Contains(
        "NativeRewardProgramRegistry.TryRun(`r`n                        previousRelicRule,`r`n                        globals)") `
    -and -not $nativeRuntime.Contains(
        "NativeRewardProgramRegistry.TryRun(`n                        previousRelicRule,`n                        globals)")) {
    throw "AuraTools copied relic must execute inside its own script globals."
}
foreach ($anchor in @(
    "training.GeneratedReplayEpisodes = Math.Max(",
    "training.PersistedReplayEpisodes = training.Replay.Count",
    "WriteEpisodes(episodesPath, training.Replay)",
    "RoleStrategyGatePassed = roleStrategyGatePassed",
    "ResumedFromCheckpoint = resumedFromCheckpoint",
    "AcquireTrainingLease",
    "CombatFoundationModelPackageProtocol.Create",
    "ModelPackagePath",
    "EpisodeSnapshot = nextSnapshot",
    "ReplayIdentity",
    "TryGetResumableCheckpoint"
)) {
    if (-not $foundationWorkerProgram.Contains($anchor)) {
        throw "Aura foundation worker failed-replay contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "WriteEpisodeSnapshot",
    "WriteAtomicText",
    "ReadAndValidateJsonLines",
    "FileShare.ReadWrite | FileShare.Delete",
    "File.Replace",
    "CleanupArtifacts"
)) {
    if (-not $foundationCheckpointStorage.Contains($anchor)) {
        throw "Aura foundation checkpoint durability contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "Environment.ProcessPath",
    "ExecutableDirectory",
    "DiscoverModRoot",
    "settings.ModRoot = modRoot",
    "settings.DataRoot = dataRoot",
    "FormatLoss",
    "CheckpointWriteFailures",
    "ReadAllTextShared",
    "recentResultStatus",
    "completionNotificationArmed",
    "tabs.SelectedIndex = ProgressTabIndex",
    "TryShowCompletionNotification",
    "FlashTaskbar",
    "PlayCompletionSound",
    "ResultSummary",
    "RoleStrategyGatePassed",
    "ResumedFromCheckpoint",
    "CheckpointSerializationAutoScaled",
    "CheckpointWritesEnqueued = Convert.ToInt64",
    'SetToggle(',
    '"RequireCapabilityProbeBaselineGain"'
)) {
    if (-not $foundationControllerRuntime.Contains($anchor)) {
        throw "Foundation controller relative-path contract is missing: $anchor"
    }
}
if ($foundationControllerRuntime -match
        '(?s)Set\(\s*"RequireCapabilityProbeBaselineGain"') {
    throw "Foundation controller must restore the capability baseline gate through its checkbox."
}
$resultPollingIndex = $foundationControllerRuntime.IndexOf(
    "TryGetFileIdentity(")
$progressPollingIndex = $foundationControllerRuntime.IndexOf(
    "if (File.Exists(job.ProgressPath))")
if ($resultPollingIndex -lt 0 `
        -or $progressPollingIndex -lt 0 `
        -or $resultPollingIndex -gt $progressPollingIndex) {
    throw "Foundation controller must prioritize final results over stale progress snapshots."
}
if (-not $foundationControllerRuntime.Contains(
        "settings.SchemaVersion != ControllerSettings.CurrentSchemaVersion") `
        -or -not $foundationControllerRuntime.Contains(
            "ApplyIndependentTrainerExecutionContract") `
        -or -not $foundationControllerModels.Contains(
            "CurrentSchemaVersion = 16") `
        -or -not $foundationControllerRuntime.Contains(
            "parameters.EnableMemoryCapacityParallelism = true") `
        -or -not $foundationControllerRuntime.Contains(
            "parameters.ReuseAutoTuneCache = false")) {
    throw "Foundation controller memory-capacity parallelism settings contract is missing."
}
foreach ($removedAnchor in @(
    "AddExecutionProfileSelect(panel)",
    'AddToggle(panel, "ReuseAutoTuneCache"',
    "AddAutoTuneObjectiveSelect(panel)",
    '"AutoTuneSampleCampaigns"',
    '"AutoTuneThroughputTolerance"'
)) {
    if ($foundationControllerRuntime.Contains($removedAnchor)) {
        throw "Foundation controller still exposes obsolete auto-tune UI: $removedAnchor"
    }
}
foreach ($anchor in @(
    "AdditionalIterationsOnResume = 2",
    "TrainingCampaignsPerIteration = 96",
    "ArenaCampaignsPerDifficulty = 16",
    "ArenaConfirmationCampaignsPerDifficulty = 48",
    "NormalValidationCampaigns = 100",
    "AdvancedValidationCampaigns = 200",
    "CapabilityProbeCampaignsPerDifficulty = 64",
    "PreflightCampaignsPerDifficulty = 16",
    "CombatFoundationExecutionProfileNames.Auto",
    "CombatFoundationExecutionProfileNames.DirectInference",
    "ReuseAutoTuneCache = false",
    "SuccessExpertReplayShare = 0.10d",
    "ModelGradientShardCount = 0",
    "ModelMaximumUnsafeEndTurnFrameShare = 0.20d",
    "ModelLearningRate = 0.004d",
    "ModelL2 = 0.002d",
    "ModelStateDimensions = 1024",
    "ModelActionDimensions = 1024",
    "ModelHiddenDimensions = 512",
    "MaximumStateFeatureCollisionRate = 0.20d",
    "MaximumActionFeatureCollisionRate = 0.06d",
    "TransformerTeacherEpochs = 12",
    "TransformerTeacherMinimumFrames = 1024",
    "TransformerTeacherMaximumFrames = 10000",
    "TransformerTeacherCpuEpochs = 4",
    "TransformerTeacherCpuIncrementalEpochs = 1",
    "TransformerTeacherEnableWarmStart = true",
    "TransformerTeacherCpuRefreshInterval = 4",
    "TransformerTeacherIncrementalEpochs = 4",
    "TransformerTeacherFinalEpochs = 12",
    "TransformerTeacherCpuInteropThreads = 0",
    "TransformerTeacherMicroBatchSize = 0",
    "TransformerTeacherDataLoaderWorkers = 2",
    "TransformerTeacherPrefetchBatches = 2",
    "TransformerDistillationWeight = 0.35d"
)) {
    if (-not $foundationControllerModels.Contains($anchor)) {
        throw "Foundation controller development preset is missing: $anchor"
    }
}
foreach ($anchor in @(
    "AuraToolsBundledFoundationModelRuntime.Initialize(modConfig)",
    "RegisterBundledFoundationPackages",
    "CombatPolicyValueArtifactProtocol.TryValidatePayload",
    "ModelVersion",
    "FoundationSourcePackageSha256",
    "FoundationDistributionOrigin",
    "SameFoundationRelease",
    "ModelBundleFileName"
)) {
    if (-not $startupRuntime.Contains($anchor) `
            -and -not $modelRuntime.Contains($anchor) `
            -and -not $bundledModelRuntime.Contains($anchor)) {
        throw "AuraTools bundled foundation-model registration contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    'LoadResidentModels',
    'UnloadResidentModels',
    'AutoBattle.ModelResidency',
    'modelConfigurationKey',
    'FightStarting = _ => ResetForBattle()'
)) {
    if (-not $modelRuntime.Contains($anchor) `
            -and -not $startupRuntime.Contains($anchor)) {
        throw "AuraTools resident model lifecycle contract is missing: $anchor"
    }
}
if ($startupRuntime.Contains('FightStarted = _ => ResetForBattle()')) {
    throw "AuraTools must not reset or reload the model twice for the same battle start."
}
foreach ($anchor in @(
    'SchemaVersion { get; set; } = 5',
    'DisplayNameMode',
    'GeneratedDisplayName',
    'TryRestoreGeneratedLibraryModelName',
    'ApplyFoundationDisplayName',
    'IsLegacyGeneratedFoundationName',
    'ShouldPreserveBundledRegistration',
    'DisplayNameMode = "user"',
    'DisplayNameMode = "generated"',
    'SameIds('
)) {
    if (-not $modelRuntime.Contains($anchor)) {
        throw "AuraTools foundation-model naming/provenance migration contract is missing: $anchor"
    }
}
if (-not $settingsRuntime.Contains("TryRestoreGeneratedLibraryModelName") `
        -or -not $bundledModelRuntime.Contains("BuildCanonicalDisplayName")) {
    throw "AuraTools model UI and bundled/manual import paths must share canonical automatic naming."
}
foreach ($anchor in @(
    'SearchOption.TopDirectoryOnly',
    'MaximumPackageBytes',
    'new UTF8Encoding(false, true)',
    'CombatFoundationModelPackageProtocol.TryValidate',
    'ModelVersionPattern',
    'witch-game-subjects-v1.catalog.json'
)) {
    if (-not $bundledModelRuntime.Contains($anchor) `
            -and -not $settingsRuntime.Contains($anchor)) {
        throw "AuraTools bundled model scanner/UI contract is missing: $anchor"
    }
}
$bundledModelFiles = @(Get-ChildItem -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\ModResource\Model") -Filter "*.json" -File)
foreach ($bundledModelFile in $bundledModelFiles) {
    $bundledModel = Get-Content -Raw -Encoding UTF8 -LiteralPath $bundledModelFile.FullName | ConvertFrom-Json
    $modelMetadata = if ($null -ne $bundledModel.Model) {
        $bundledModel.Model
    } else {
        $bundledModel.ModelArtifact
    }
    $expectedVersion = switch ([int]$bundledModel.SchemaVersion) {
        3 { "3.0.0" }
        4 { "4.0.0" }
        5 { "5.0.0" }
        default { "" }
    }
    if ([string]::IsNullOrWhiteSpace($expectedVersion) `
            -or $bundledModel.ArtifactKind -ne "aura.foundation-model-package" `
            -or $bundledModel.ModelVersion -ne $expectedVersion `
            -or $modelMetadata.ModelProtocol -ne "aura.combat-policy-value.mlp.v2" `
            -or $modelMetadata.ProtocolVersion -ne 2 `
            -or $modelMetadata.FeatureSchemaVersion -ne 26 `
            -or $bundledModel.Compatibility.FeatureSchemaVersion -ne 26) {
        throw "Bundled foundation model is incompatible with the current trainer protocol: $($bundledModelFile.Name)"
    }
}
foreach ($anchor in @(
    "AuraSharedRoot(dataRoot)",
    '"FoundationTrainer"',
    "LegacySessionPath",
    "TrainingResultsRoot(settings.DataRoot)"
)) {
    if (-not $foundationControllerRuntime.Contains($anchor)) {
        throw "Foundation controller AuraShared path migration contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "BuildGameSubjectSection",
    "CombatGameSubjectPreset",
    "CombatGameSubjectPresetRuntime.Apply",
    "witch-game-subjects-v1.catalog.json",
    "PullGameSubjectFromUi",
    "GameParameterHash"
)) {
    if (-not $foundationControllerRuntime.Contains($anchor) `
            -and -not $foundationControllerModels.Contains($anchor)) {
        throw "Foundation controller game-subject persistence is missing: $anchor"
    }
}
foreach ($anchor in @(
    "AuraSharedPaths.OwnerSystemDataDirectory",
    '"FoundationModels"',
    "EnsureModelLibraryMigrated",
    '"model-library"'
)) {
    if (-not $modelRuntime.Contains($anchor)) {
        throw "AuraTools model-library owner-data migration contract is missing: $anchor"
    }
}
if (-not (Test-Path -LiteralPath $gameSubjectCatalogPath -PathType Leaf)) {
    throw "Foundation controller game-subject catalog is missing."
}
$gameSubjectCatalog = Get-Content -Raw -Encoding UTF8 `
    -LiteralPath $gameSubjectCatalogPath | ConvertFrom-Json
if ($gameSubjectCatalog.schemaVersion -ne 1 `
        -or @($gameSubjectCatalog.roles).Count -lt 13 `
        -or @($gameSubjectCatalog.familiars).Count -lt 5 `
        -or @($gameSubjectCatalog.cardPacks).Count -ne 19 `
        -or @($gameSubjectCatalog.cardPacks | Where-Object {
            $_.id -eq "cardpack_13"
        }).Count -ne 0) {
    throw "Foundation controller game-subject catalog is incomplete or unsafe."
}
if (-not $foundationControllerModels.Contains("[JsonIgnore]")) {
    throw "Foundation controller must derive runtime roots from its EXE instead of persisted absolute paths."
}
foreach ($anchor in @(
    "TryStageExternalFoundationPackage",
    "TryPromoteExternalValidationModel",
    "EvaluationModelId",
    "CreateGameParametersSection(content)",
    "CreateAutoBattleModelManagementSection",
    "CreateAutoBattleModelApplicationRows",
    "CreateAutoBattlePlayerAdaptationSection",
    "CreateAutoBattleAdvancedDiagnosticsSection",
    "AutoBattle.PlayerResidualParameters",
    "SnapshotPolicyAdapters",
    "AuraToolsLocalSectionRefreshView",
    "AuraToolsScrollRestoreDriver",
    "CreateVerticalStack",
    "CreateCompactFoldout",
    "AutoBattle.AdvancedAndDiagnostics",
    "LayoutRebuilder.ForceRebuildLayoutImmediate",
    "AutoBattleExternalFoundationValidationActions",
    "AutoBattleGameValidationActions"
)) {
    if (-not $settingsRuntime.Contains($anchor)) {
        throw "AuraTools external foundation validation UI contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "TryStageExternalFoundationPackage",
    "ExternalValidationMeetsGate",
    "TryPromoteExternalValidationModel",
    "ClearExternalValidationModel",
    "CombatFoundationModelPackageProtocol.TryValidate",
    "ResolvePackageCoverage",
    "CoverageAwareCombatPolicyValueModel",
    "FoundationArtifactValidated",
    "PortableFoundationMeetsActivationGate",
    "NormalizeAcceptance",
    "FoundationAcceptanceKind",
    "ValidFoundationAcceptance"
)) {
    if (-not $modelRuntime.Contains($anchor)) {
        throw "AuraTools external foundation staging contract is missing: $anchor"
    }
}
foreach ($removedAnchor in @(
    "AutoBattleFoundationTrainingActions",
    "AuraToolsAutoBattleFoundationRuntime",
    "AutoBattleFoundationTrainingSettings",
    'JsonProperty("foundationTraining")',
    "AutoBattleFoundationCpuProfileRow",
    "ApplyFoundationCpuProfile",
    "LaunchControlCenter"
)) {
    if ($settingsRuntime.Contains($removedAnchor) `
            -or $autoBattleSettings.Contains($removedAnchor) `
            -or $modelRuntime.Contains($removedAnchor)) {
        throw "AuraTools still exposes removed in-game foundation training: $removedAnchor"
    }
}
if (Test-Path -LiteralPath (Join-Path $repoRoot (
        "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleFoundationRuntime.cs"))) {
    throw "AuraTools in-game foundation training runtime must be removed."
}
if (Test-Path -LiteralPath (Join-Path $repoRoot (
        "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsFoundationWorkerRuntime.cs"))) {
    throw "AuraTools in-game foundation worker launcher must be removed."
}
if ($settingsRuntime.Contains("NextAutoBattleModelMode")) {
    throw "AuraTools battle strategy laboratory must use the compact explicit model-application flow."
}
$snapshotChangedMatch = [regex]::Match(
    $settingsRuntime,
    "(?s)private static void OnAutoBattleUiSnapshotChanged\(\).*?\n    \}")
if (-not $snapshotChangedMatch.Success -or
    $snapshotChangedMatch.Value.Contains("RebuildPanel(")) {
    throw "Auto-battle snapshot updates must not rebuild the complete settings viewport."
}
foreach ($anchor in @(
    "TrySetModelApplicationMode",
    "SnapshotModelApplicationStatus",
    "CombatDecisionExecutionBindingProtocol.TryBindToObservation",
    "[AutoBattle][ActionRebind]",
    "policyPrior="
)) {
    if (-not $startupRuntime.Contains($anchor)) {
        throw "AuraTools live model application contract is missing: $anchor"
    }
}
if ($modelRuntime.Contains("TryValidateExternalPackageCompatibility")) {
    throw "AuraTools external model import must not reject a valid package because the current game subject differs."
}
if (-not $simulationRuntime.Contains("EvaluationModelId") `
        -or -not $simulationRuntime.Contains("ExternalValidationMeetsGate") `
        -or -not $gameValidationRuntime.Contains("EvaluationModelId") `
        -or -not $gameValidationRuntime.Contains("IsStartEnvironmentReady")) {
    throw "AuraTools external foundation dual-validation routing contract is missing."
}
foreach ($anchor in @(
    "ReadResultSummaryStreaming",
    "DeserializeFileStreaming<CombatFoundationWorkerJob>",
    "presentedResultLastWriteUtc",
    "IdleRefreshInterval",
    "FileOptions.SequentialScan"
)) {
    if (-not $foundationControllerRuntime.Contains($anchor)) {
        throw "Aura foundation controller low-overhead polling contract is missing: $anchor"
    }
}
if ($foundationControllerRuntime.Contains(
        "ReadAllTextShared(`r`n                    job.ResultPath)")) {
    throw "Aura foundation controller must not repeatedly materialize the full worker result."
}
foreach ($anchor in @(
    "PublishSingleFile=true",
    "EnableCompressionInSingleFile=true",
    "win-x64",
    "TrainingWorker",
    "AuraFoundationTrainer.ControlCenter",
    "controlCenterProject",
    "StopRunningTrainer",
    "Get-NormalizedTrainerPath",
    "Request-WorkerCancellation",
    "CancellationPath",
    "Regex]::Unescape",
    ".publish-staging",
    "Copy-PublishedFileWithRetry",
    "Install-AuraPyTorch.cmd"
)) {
    if (-not $foundationWorkerBuild.Contains($anchor)) {
        throw "Aura foundation worker packaging contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    '[ValidateSet("auto", "cpu", "cuda")]',
    'Python.Python.3.11',
    'AURA_TRANSFORMER_PYTHON',
    'Test-NvidiaGpu',
    'NvidiaCudaVersion',
    'whl/cu126',
    'whl/cu130',
    'Falling back to CPU',
    'torch.cuda.is_available()',
    'Transformer teacher self-test'
)) {
    if (-not $transformerSetup.Contains($anchor)) {
        throw "Aura PyTorch setup contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    'Setup-AuraTransformerTeacher.ps1',
    'ExecutionPolicy Bypass',
    'AURA_INSTALL_NO_PAUSE',
    'exit /b %INSTALL_EXIT_CODE%'
)) {
    if (-not $transformerInstaller.Contains($anchor)) {
        throw "Aura PyTorch one-click installer is missing: $anchor"
    }
}
if (-not $runtimeProject.Contains("StopRunningFoundationTrainer") `
    -or $foundationWorkerBuild.Contains(".Kill(")) {
    throw (
        "Aura foundation worker deployment must expose graceful shutdown " +
        "without force-killing an active training process.")
}

Write-Host "AuraToolsExp runtime compilation and retained-UI gates passed."
