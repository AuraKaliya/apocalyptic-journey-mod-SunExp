param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [int]$PreflightCampaignsPerDifficulty = 1,
    [UInt64]$PreflightSeedStart = 4100000,
    [switch]$PreflightOnly,
    [int]$SearchSimulationBudget = 16,
    [int]$SearchNodeBudget = 256,
    [int]$SearchMaxPly = 2,
    [int]$SearchMinimumSimulations = 8,
    [int]$SearchStabilityWindow = 16,
    [int]$SearchStableChecks = 1,
    [int]$MaximumDegreeOfParallelism = 4,
    [switch]$KeepArtifactsOnFailure
)

$ErrorActionPreference = "Stop"
function Read-FoundationJson([string]$Path) {
    return [System.IO.File]::ReadAllText(
        $Path,
        [System.Text.Encoding]::UTF8) |
        ConvertFrom-Json -Depth 100 -AsHashtable
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$effectivePreflightCampaignsPerDifficulty = [Math]::Max(
    0,
    [Math]::Min(100, $PreflightCampaignsPerDifficulty))
if (-not $SkipPublish) {
    & (Join-Path $repoRoot "tools\Build-AuraFoundationTrainer.ps1") -Configuration $Configuration
}
$worker = Join-Path $repoRoot "AuraToolsExp\TrainingWorker\AuraFoundationTrainer.Worker.exe"
if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    throw "Foundation trainer is missing: $worker"
}

$campaignPath = Join-Path $repoRoot "AuraToolsExp\Config\combat-simulation\witch-world-simulation-v2.campaign.json"
$rulesetPath = Join-Path $repoRoot "AuraToolsExp\Config\combat-simulation\witch-base-evaluation-v2.ruleset.json"
$campaign = Get-Content -Raw -Encoding UTF8 -LiteralPath $campaignPath | ConvertFrom-Json
$ruleset = Get-Content -Raw -Encoding UTF8 -LiteralPath $rulesetPath | ConvertFrom-Json
$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("aura-foundation-worker-" + [Guid]::NewGuid().ToString("N"))
$smokeFailed = $false
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
try {
    $jobPath = Join-Path $smokeRoot "job.json"
    $progressPath = Join-Path $smokeRoot "progress.json"
    $resultPath = Join-Path $smokeRoot "result.json"
    $cancelPath = Join-Path $smokeRoot "cancel"
    $checkpointPath = Join-Path $smokeRoot "checkpoint.json"
    $checkpointEpisodesPath = Join-Path $smokeRoot "checkpoint-episodes.jsonl"
    $profile = [ordered]@{
        Id = "balanced"
        SearchSimulationBudget = $SearchSimulationBudget
        SearchNodeBudget = $SearchNodeBudget
        SearchMaxPly = $SearchMaxPly
        SearchMinimumSimulations = $SearchMinimumSimulations
        SearchStabilityWindow = $SearchStabilityWindow
        SearchStableChecks = $SearchStableChecks
        UseChancePuct = $true
    }
    $request = [ordered]@{
        DecisionProfile = "balanced"
        Iterations = 1
        TrainingCampaignsPerIteration = 2
        ArenaCampaignsPerDifficulty = 1
        ValidationCampaignsPerDifficulty = 5
        NormalValidationCampaigns = 5
        AdvancedValidationCampaigns = 5
        PreflightCampaignsPerDifficulty = $PreflightCampaignsPerDifficulty
        PreflightSeedStart = $PreflightSeedStart
        PreflightOnly = [bool]$PreflightOnly
        MaximumDegreeOfParallelism = $MaximumDegreeOfParallelism
        EnableEarlyValidationStop = $true
        TrainingSeedStart = 4100000
        ArenaSeedStart = 4200000
        ValidationSeedStart = 4300000
        Profile = $profile
        Training = [ordered]@{
            Epochs = 2
            HiddenDimensions = 8
            MinimumEpisodes = 2
            BatchSize = 8
            MinimumEpochs = 2
            EarlyStoppingPatience = 2
            ReplayEpisodeLimit = 500
        }
        TrainingCampaign = $campaign
        ValidationCampaign = $campaign
    }
    $job = [ordered]@{
        SchemaVersion = 3
        JobId = "worker-smoke"
        ExpectedRulesetHash = ""
        ResultDirectory = $smokeRoot
        ProgressPath = $progressPath
        ResultPath = $resultPath
        CancellationPath = $cancelPath
        CheckpointPath = $checkpointPath
        CheckpointEpisodesPath = $checkpointEpisodesPath
        Request = $request
        Ruleset = $ruleset
    }
    $job | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $jobPath -Encoding UTF8
    & $worker --job $jobPath
    if ($LASTEXITCODE -ne 0) {
        throw "Foundation trainer smoke process failed: $LASTEXITCODE"
    }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Foundation trainer smoke result is missing."
    }
    $result = Read-FoundationJson $resultPath
    if (-not $result.Success `
        -or $null -eq $result.Training `
        -or -not $result.Training.Preflight.Passed `
        -or $result.Training.InvalidTrainingCampaigns -ne 0 `
        -or @($result.Training.Iterations | Where-Object {
            $_.InvalidChampionCampaigns -ne 0 `
            -or $_.InvalidCandidateCampaigns -ne 0
        }).Count -ne 0) {
        $preflightFailures = $result.Training.Preflight.Failures |
            ConvertTo-Json -Depth 10 -Compress
        throw ("Foundation trainer smoke result failed: {0}; preflight={1}" -f `
            $result.Message, $preflightFailures)
    }
    if ([string]::IsNullOrWhiteSpace($result.RulesetHash)) {
        throw "Foundation trainer did not return a ruleset hash."
    }
    if (-not (Test-Path -LiteralPath $result.EpisodesPath -PathType Leaf)) {
        throw "Foundation trainer episodes artifact is missing."
    }
    $caseAnalysisPath = Join-Path $smokeRoot "foundation-success-analysis-v1.json"
    $caseObservationPath = Join-Path $smokeRoot "foundation-case-observations-v1.jsonl"
    $caseIndexPath = Join-Path $smokeRoot "foundation-success-case-index-v1.jsonl"
    if (-not (Test-Path -LiteralPath $caseAnalysisPath -PathType Leaf) `
        -or -not (Test-Path -LiteralPath $caseObservationPath -PathType Leaf) `
        -or -not (Test-Path -LiteralPath $caseIndexPath -PathType Leaf)) {
        throw "Foundation trainer success-case learning artifacts are missing."
    }
    if ([string]::IsNullOrWhiteSpace(
            [string]$result.Training.SuccessArchiveDirectory)) {
        throw "Foundation trainer did not report its success archive directory."
    }
    if ([int]$result.Training.CaseAnalysis.ArchiveEligibleCases -gt 0 `
        -and ([int]$result.Training.ArchivedSuccessCases `
             + [int]$result.Training.DuplicateSuccessCases) -le 0) {
        throw "Foundation trainer observed eligible successes but did not archive them."
    }
    $episodeCount = @(
        Get-Content -LiteralPath $result.EpisodesPath -Encoding UTF8
    ).Count
    $generatedEpisodes = [int]$result.Training.GeneratedReplayEpisodes
    $persistedEpisodes = [int]$result.Training.PersistedReplayEpisodes
    if ($result.Training.Success -and -not $PreflightOnly) {
        if ($persistedEpisodes -gt $generatedEpisodes `
            -or $persistedEpisodes -le 0 `
            -or $episodeCount -ne $persistedEpisodes) {
            throw (
                "Successful training replay persistence mismatch: " `
                + "generated=$generatedEpisodes, " `
                + "persisted=$persistedEpisodes, file=$episodeCount")
        }
    }
    elseif ($persistedEpisodes -ne 0 -or $episodeCount -ne 0) {
        throw (
            "Failed or preflight-only training must omit full replay data: " `
            + "generated=$generatedEpisodes, " `
            + "persisted=$persistedEpisodes, file=$episodeCount")
    }
    if (-not $PreflightOnly -and $generatedEpisodes -le 0) {
        throw "Foundation trainer smoke did not generate replay episodes."
    }
    if (-not (Test-Path -LiteralPath $progressPath -PathType Leaf)) {
        throw "Foundation trainer progress artifact is missing."
    }
    $progress = Read-FoundationJson $progressPath
    if ($PreflightOnly) {
        if (-not $result.Training.Success `
            -or $result.Training.Preflight.CompletedCampaigns `
                -ne $effectivePreflightCampaignsPerDifficulty * 2 `
            -or $result.Training.CompletedCampaigns -ne 0) {
            throw "Foundation trainer preflight-only result is incomplete."
        }
    }
    elseif ($progress.Telemetry.ModelTotalEpochs -lt 5 `
            -or $progress.Telemetry.PolicyDecisions -le 0 `
            -or $progress.Telemetry.SearchSimulations -le 0) {
            throw "Foundation trainer model/search telemetry is incomplete."
    }
    $checkpointExists = (Test-Path -LiteralPath $checkpointPath) `
                        -and (Test-Path -LiteralPath $checkpointEpisodesPath)
    if ($result.Training.AcceptancePassed -and $checkpointExists) {
        throw "Accepted foundation training must remove resume checkpoints."
    }
    if (-not $result.Training.AcceptancePassed `
        -and (-not $checkpointExists `
             -or -not $result.Resumable `
             -or [string]::IsNullOrWhiteSpace([string]$result.CheckpointPath))) {
        throw "Unaccepted foundation training must retain a resumable checkpoint."
    }
    if (-not $result.Training.AcceptancePassed) {
        $checkpoint = Read-FoundationJson $checkpointPath
        if ([int]$checkpoint.SchemaVersion -ne 3 `
            -or [int]$checkpoint.Resume.SchemaVersion -ne 3 `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.RulesetHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.NativeProgramPackageHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.TrainingCampaignHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.ValidationCampaignHash)) {
            throw "Foundation checkpoint compatibility manifest is incomplete."
        }
    }

    Write-Host ("Aura foundation trainer smoke passed: campaigns={0}/{1}, battles={2}, runtime={3}" -f `
        $result.Training.CompletedCampaigns, `
        $result.Training.RequestedCampaigns, `
        $result.Training.CompletedBattles, `
        $result.Runtime)
}
catch {
    $smokeFailed = $true
    Write-Host ("Aura foundation trainer smoke failed: " + $_.Exception)
    Write-Host $_.ScriptStackTrace
    throw
}
finally {
    $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
    $resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ((-not $KeepArtifactsOnFailure -or -not $smokeFailed) `
        -and $resolvedSmokeRoot.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase) `
        -and (Test-Path -LiteralPath $resolvedSmokeRoot)) {
        Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force
    }
}
