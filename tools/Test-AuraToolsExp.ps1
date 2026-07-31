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
$foundationRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleFoundationRuntime.cs") -Raw
$foundationWorkerRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsFoundationWorkerRuntime.cs") -Raw
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
$simulationRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSimulationRuntime.cs") -Raw
$gameValidationRuntime = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleGameValidationRuntime.cs") -Raw
$autoBattleSettings = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsAutoBattleSettings.cs") -Raw
$foundationWorkerBuild = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\Build-AuraFoundationTrainer.ps1") -Raw
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
    "一键导出游戏数据集",
    "TryExportBaseGameTables",
    "OpenBaseGameTableExportDirectory"
)) {
    if (-not $settingsRuntime.Contains($anchor)) {
        throw "AuraTools game dataset export UI contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "foundation-training-summary.html",
    "foundation-training-report.md",
    "foundation-training-report.json",
    "BuildFoundationHtml",
    "<!doctype html>",
    "result.TrainingFailureCounts",
    "result.TrainingFailures",
    "fullReplayWritten = writeFullReplay",
    "generatedEpisodes = generatedReplayEpisodes",
    "computationSucceeded = result.Success",
    "generatedOnlyDeckViolations",
    "cardAcquisition = new",
    "bounded-prioritized-diverse-replay",
    "foundation-training-failure-repro-v1.json",
    "if (!writeFullReplay",
    "if (writeFullReplay)",
    "CombatFoundationWorkerJobFactory.Create",
    "ToSharedParameters"
)) {
    if (-not $foundationRuntime.Contains($anchor)) {
        throw "AuraTools readable foundation report contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "activeHandlerChains",
    "activeHandlerIds",
    "CausalChainId",
    "SourceRewardId",
    "generated-only card leaked into reward pool"
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
    "AuraToolsFoundationWorkerRuntime.Run",
    "MaximumCompletedBattleDepth",
    "SearchSimulationsPerSecond",
    "EstimatedRemainingSeconds",
    "CheckpointPath",
    "CheckpointEpisodesPath"
)) {
    if (-not $foundationRuntime.Contains($anchor)) {
        throw "AuraTools external foundation integration contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "training.GeneratedReplayEpisodes = Math.Max(",
    "training.PersistedReplayEpisodes = training.Success",
    "Array.Empty<CombatEpisode>()",
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
    "TrainingWorker",
    "ExpectedRulesetHash",
    "CancellationPath",
    "CreateNoWindow",
    "BeginOutputReadLine",
    "LaunchControlCenter",
    "ExternalTrainingActive",
    "AuraFoundationTrainer.ControlCenter.exe"
)) {
    if (-not $foundationWorkerRuntime.Contains($anchor)) {
        throw "AuraTools foundation worker process contract is missing: $anchor"
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
        "loadedSchemaVersion < 3") `
        -or -not $foundationControllerRuntime.Contains(
            "loadedSchemaVersion < 4") `
        -or -not $foundationControllerRuntime.Contains(
            "loadedSchemaVersion < 5") `
        -or -not $foundationControllerRuntime.Contains(
            "loadedSchemaVersion < 6") `
        -or -not $foundationControllerRuntime.Contains(
            "AdditionalIterationsOnResume") `
        -or -not $foundationControllerRuntime.Contains(
            "MinimumAdvancedDefeatReplayShare") `
        -or -not $foundationControllerModels.Contains(
            "SchemaVersion { get; set; } = 6")) {
    throw "Foundation controller resumable-training settings migration is missing."
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
if (-not (Test-Path -LiteralPath $gameSubjectCatalogPath -PathType Leaf)) {
    throw "Foundation controller game-subject catalog is missing."
}
$gameSubjectCatalog = Get-Content -Raw -Encoding UTF8 `
    -LiteralPath $gameSubjectCatalogPath | ConvertFrom-Json
if ($gameSubjectCatalog.schemaVersion -ne 1 `
        -or @($gameSubjectCatalog.roles).Count -lt 9 `
        -or @($gameSubjectCatalog.familiars).Count -lt 5 `
        -or @($gameSubjectCatalog.cardPacks).Count -ne 18 `
        -or @($gameSubjectCatalog.cardPacks | Where-Object {
            $_.id -eq "cardpack_13"
        }).Count -ne 0) {
    throw "Foundation controller game-subject catalog is incomplete or unsafe."
}
if ($foundationWorkerRuntime.Contains("--mod-root") -or
    $foundationWorkerRuntime.Contains("--data-root") -or
    -not $foundationControllerModels.Contains("[JsonIgnore]")) {
    throw "Foundation controller must derive runtime roots from its EXE instead of persisted absolute paths."
}
foreach ($anchor in @(
    '[JsonProperty("executionMode")]',
    '"external"',
    '"partitioned-v3"',
    "Environment.ProcessorCount",
    "ModelEpochs",
    "ModelBatchSize",
    "EnableFrameStratification",
    "ModelMaximumFrameStratumWeight",
    "EnableCounterfactualHardEncounters",
    "MaximumConsecutiveRejectedIterations",
    "CapabilityProbeCampaignsPerDifficulty",
    "TuningNormalCampaigns",
    "TuningAdvancedCampaigns",
    "ModelEarlyStoppingPatience",
    "ModelReplayEpisodeLimit"
)) {
    if (-not $autoBattleSettings.Contains($anchor)) {
        throw "AuraTools foundation worker settings contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "TryStageExternalFoundationPackage",
    "TryPromoteExternalValidationModel",
    "EvaluationModelId",
    "CreateGameParametersSection(content)",
    "CreateAutoBattleModelManagementSection",
    "CreateAutoBattleModelApplicationRows",
    "AuraToolsLocalSectionRefreshView",
    "AuraToolsScrollRestoreDriver",
    "CreateVerticalStack",
    "CreateCompactFoldout",
    "AutoBattle.ValidationAndDiagnostics",
    "LayoutRebuilder.ForceRebuildLayoutImmediate",
    "AutoBattleExternalFoundationValidationActions",
    "AutoBattleFoundationTrainingActions",
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
    "PortableFoundationMeetsActivationGate"
)) {
    if (-not $modelRuntime.Contains($anchor)) {
        throw "AuraTools external foundation staging contract is missing: $anchor"
    }
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
    "Request-WorkerCancellation",
    "CancellationPath",
    "AuraFoundationTrainer.Publish",
    "Copy-PublishedFileWithRetry"
)) {
    if (-not $foundationWorkerBuild.Contains($anchor)) {
        throw "Aura foundation worker packaging contract is missing: $anchor"
    }
}
if (-not $runtimeProject.Contains("StopRunningFoundationTrainer") `
    -or $foundationWorkerBuild.Contains(".Kill(")) {
    throw (
        "Aura foundation worker deployment must expose graceful shutdown " +
        "without force-killing an active training process.")
}

Write-Host "AuraToolsExp runtime compilation and retained-UI gates passed."
