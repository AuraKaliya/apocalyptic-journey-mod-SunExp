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
$foundationWorkerProgram = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraFoundationTrainer.Worker\Program.cs") -Raw
$autoBattleSettings = Get-Content -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsAutoBattleSettings.cs") -Raw
$foundationWorkerBuild = Get-Content -LiteralPath (
    Join-Path $repoRoot "tools\Build-AuraFoundationTrainer.ps1") -Raw
$generatedProgramsPath = Join-Path $repoRoot (
    "AuraToolsExp-Dev\Features\AutoBattle\Generated\AuraToolsNativePrograms.g.cs")
$manifestPath = Join-Path $repoRoot (
    "AuraToolsExp\Config\combat-programs.base-game.manifest.json")

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
    "AuraSharedBackgroundWorkKind.Io"
)) {
    if (-not $knowledgeRuntime.Contains($anchor)) {
        throw "AuraTools knowledge package background-loading contract is missing: $anchor"
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
    "PreflightSeedStart = foundation.TrainingSeedStart"
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
    "Array.Empty<CombatEpisode>()"
)) {
    if (-not $foundationWorkerProgram.Contains($anchor)) {
        throw "Aura foundation worker failed-replay contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "TrainingWorker",
    "ExpectedRulesetHash",
    "CancellationPath",
    "CreateNoWindow",
    "BeginOutputReadLine"
)) {
    if (-not $foundationWorkerRuntime.Contains($anchor)) {
        throw "AuraTools foundation worker process contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    '[JsonProperty("executionMode")]',
    '"external"',
    "sourceSchemaVersion < 18",
    "Environment.ProcessorCount",
    "ModelEpochs",
    "ModelBatchSize",
    "ModelEarlyStoppingPatience",
    "ModelReplayEpisodeLimit"
)) {
    if (-not $autoBattleSettings.Contains($anchor)) {
        throw "AuraTools foundation worker settings contract is missing: $anchor"
    }
}
foreach ($anchor in @(
    "PublishSingleFile=true",
    "EnableCompressionInSingleFile=true",
    "win-x64",
    "TrainingWorker"
)) {
    if (-not $foundationWorkerBuild.Contains($anchor)) {
        throw "Aura foundation worker packaging contract is missing: $anchor"
    }
}

Write-Host "AuraToolsExp runtime compilation and retained-UI gates passed."
