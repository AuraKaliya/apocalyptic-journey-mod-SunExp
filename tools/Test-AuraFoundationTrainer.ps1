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
    $json = [System.IO.File]::ReadAllText(
        $Path,
        [System.Text.Encoding]::UTF8)
    # Windows PowerShell 5 rejects otherwise valid JSON objects that contain
    # an empty property name. Native simulation state can legitimately expose
    # an empty runtime-variable key, so normalize only that property token in
    # the smoke-test reader.
    return $json.Replace('"":', '"__empty":') | ConvertFrom-Json
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
    $archiveRoot = Join-Path $smokeRoot "foundation-success-cases"
    $profile = [ordered]@{
        Id = "balanced"
        SearchSimulationBudget = $SearchSimulationBudget
        SearchNodeBudget = $SearchNodeBudget
        SearchMaxPly = $SearchMaxPly
        SearchMinimumSimulations = $SearchMinimumSimulations
        SearchStabilityWindow = $SearchStabilityWindow
        SearchStableChecks = $SearchStableChecks
        SearchBudgetMode = "fixed"
    }
    $request = [ordered]@{
        DecisionProfile = "balanced"
        Iterations = 1
        TrainingCampaignsPerIteration = 2
        ArenaCampaignsPerDifficulty = 1
        NormalValidationCampaigns = 5
        AdvancedValidationCampaigns = 5
        CapabilityProbeCampaignsPerDifficulty = 0
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
    $protocolVersion = 8
    $job = [ordered]@{
        SchemaVersion = $protocolVersion
        JobId = "worker-smoke"
        ExpectedRulesetHash = ""
        ResultDirectory = $smokeRoot
        ProgressPath = $progressPath
        ResultPath = $resultPath
        CancellationPath = $cancelPath
        CheckpointPath = $checkpointPath
        CheckpointEpisodesPath = $checkpointEpisodesPath
        SuccessArchiveDirectory = $archiveRoot
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
    if ([int]$progress.SchemaVersion -ne $protocolVersion `
        -or [string]$progress.JobId -ne [string]$job.JobId `
        -or [int]$result.SchemaVersion -ne $protocolVersion `
        -or [string]$result.JobId -ne [string]$job.JobId) {
        throw (
            "Foundation worker artifacts are incompatible with the host protocol: " `
            + "job=$($job.SchemaVersion), progress=$($progress.SchemaVersion), " `
            + "result=$($result.SchemaVersion).")
    }
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
    $checkpoint = $null
    $checkpointSnapshotPath = ""
    if (Test-Path -LiteralPath $checkpointPath -PathType Leaf) {
        $checkpoint = Read-FoundationJson $checkpointPath
        $checkpointSnapshotPath = if (
            $null -ne $checkpoint.EpisodeSnapshot `
            -and -not [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.EpisodeSnapshot.Path)) {
            [string]$checkpoint.EpisodeSnapshot.Path
        }
        else {
            [string]$checkpoint.EpisodesPath
        }
    }
    $checkpointExists = $null -ne $checkpoint `
                        -and -not [string]::IsNullOrWhiteSpace(
                            $checkpointSnapshotPath) `
                        -and (Test-Path -LiteralPath $checkpointSnapshotPath `
                            -PathType Leaf)
    $checkpointSnapshots = @(
        Get-ChildItem -LiteralPath $smokeRoot `
            -Filter "checkpoint-episodes.snapshot-*.jsonl" `
            -File -ErrorAction SilentlyContinue
    )
    if ($PreflightOnly) {
        if ($result.CompletionKind -ne "preflight-passed" `
            -or $result.Resumable `
            -or $checkpointExists `
            -or $checkpointSnapshots.Count -ne 0) {
            throw "Preflight completion must not retain a training checkpoint."
        }
    }
    elseif ($result.Training.AcceptancePassed) {
        if ($result.CompletionKind -ne "training-accepted" `
            -or $checkpointExists `
            -or $checkpointSnapshots.Count -ne 0 `
            -or [string]::IsNullOrWhiteSpace(
                [string]$result.ModelPackagePath) `
            -or -not (Test-Path -LiteralPath (
                [string]$result.ModelPackagePath) -PathType Leaf)) {
            throw "Accepted foundation training must remove resume checkpoints."
        }
        $modelPackage = Read-FoundationJson (
            [string]$result.ModelPackagePath)
        if ([int]$modelPackage.SchemaVersion -ne 1 `
            -or [string]$modelPackage.ArtifactKind `
                -ne "aura.foundation-model-package" `
            -or [string]$modelPackage.CompletionKind `
                -ne "training-accepted" `
            -or [string]$modelPackage.JobId -ne [string]$job.JobId `
            -or [string]$modelPackage.RulesetHash `
                -ne [string]$result.RulesetHash `
            -or $null -eq $modelPackage.Model) {
            throw "Accepted foundation model package is invalid."
        }
    }
    elseif ($result.CompletionKind -ne "training-rejected-resumable" `
        -or (-not $checkpointExists `
             -or -not $result.Resumable `
             -or [string]::IsNullOrWhiteSpace([string]$result.CheckpointPath))) {
        throw "Unaccepted foundation training must retain a resumable checkpoint."
    }
    if (-not $PreflightOnly -and -not $result.Training.AcceptancePassed) {
        if ([int]$checkpoint.SchemaVersion -ne $protocolVersion `
            -or [int]$checkpoint.Resume.SchemaVersion -ne $protocolVersion `
            -or [int]$checkpoint.EpisodeSnapshot.StorageVersion -ne 2 `
            -or [int]$checkpoint.EpisodeSnapshot.EpisodeCount -le 0 `
            -or [int64]$checkpoint.EpisodeSnapshot.Length -ne (
                Get-Item -LiteralPath $checkpointSnapshotPath).Length `
            -or [string]$checkpoint.EpisodeSnapshot.ContentSha256 -ne (
                Get-FileHash -LiteralPath $checkpointSnapshotPath `
                    -Algorithm SHA256).Hash.ToLowerInvariant() `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.RulesetHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.NativeProgramPackageHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.TrainingCampaignHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.ValidationCampaignHash) `
            -or [string]$checkpoint.Resume.Compatibility.TrainingSemanticsVersion `
                -ne "resource-recurrence-monotonic-v1" `
            -or [string]$checkpoint.Resume.Compatibility.SearchPolicyVersion `
                -ne "dynamic-search-v4" `
            -or [string]$checkpoint.Resume.Compatibility.TrainingPolicyVersion `
                -ne "foundation-governance-v9") {
            throw "Foundation checkpoint compatibility manifest is incomplete."
        }
    }

    if (-not $PreflightOnly) {
        $currentObservation = Get-ChildItem -LiteralPath $archiveRoot `
            -Filter "*.json" -File -Recurse |
            Where-Object {
                $_.Directory.Name -eq "o" `
                -and $_.FullName.Contains(
                    [System.IO.Path]::DirectorySeparatorChar `
                    + "v3" `
                    + [System.IO.Path]::DirectorySeparatorChar)
            } |
            Select-Object -First 1
        if ($null -eq $currentObservation) {
            throw "Foundation worker did not write a v3 observation."
        }
        $observation = Read-FoundationJson $currentObservation.FullName
        $archiveLoad = $result.Training.CaseArchiveLoad
        $expectedObservationPath = Join-Path $archiveRoot (
            "v3\" `
            + ([string]$observation.CompatibilityKey).Substring(0, 16) `
            + "\o\" `
            + ([string]$observation.CaseId).Substring(0, 24) `
            + ".json")
        if ([string]$archiveLoad.ProtocolVersion `
                -ne "success-case-archive-worker-v3" `
            -or [string]$archiveLoad.OwnerRuntime -ne ".NET 8 worker" `
            -or [int]$archiveLoad.StorageVersion -ne 3 `
            -or [int]$observation.SchemaVersion -ne 2 `
            -or [string]::IsNullOrWhiteSpace(
                [string]$observation.CompatibilityKey) `
            -or -not (Test-Path -LiteralPath $expectedObservationPath -PathType Leaf)) {
            throw (
                "Foundation archive v3 contract failed: " `
                + ($archiveLoad | ConvertTo-Json -Depth 8 -Compress))
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
        for ($cleanupAttempt = 0; $cleanupAttempt -lt 3; $cleanupAttempt++) {
            try {
                Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force -ErrorAction Stop
                break
            }
            catch {
                Start-Sleep -Milliseconds 100
            }
        }
        if (Test-Path -LiteralPath $resolvedSmokeRoot) {
            try {
                [System.IO.Directory]::Delete(
                    "\\?\" + $resolvedSmokeRoot,
                    $true)
            }
            catch {
                Write-Warning "Foundation smoke artifacts could not be fully removed: $resolvedSmokeRoot"
            }
        }
    }
}
