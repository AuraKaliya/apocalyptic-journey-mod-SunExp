param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [int]$PreflightCampaignsPerDifficulty = 1,
    [UInt64]$PreflightSeedStart = 4100000,
    [UInt64]$RunSeed = [UInt64]13786276755866915639,
    [switch]$PreflightOnly,
    [int]$SearchSimulationBudget = 16,
    [int]$SearchNodeBudget = 256,
    [int]$SearchMaxPly = 2,
    [int]$SearchMinimumSimulations = 8,
    [int]$SearchStabilityWindow = 16,
    [int]$SearchStableChecks = 1,
    [int]$MaximumDegreeOfParallelism = 8,
    [ValidateSet("auto", "cpu-16", "cpu-32", "custom")]
    [string]$ParallelismProfile = "auto",
    [ValidateSet("direct", "sharded-batch")]
    [string]$InferenceExecutionMode = "sharded-batch",
    [int]$InferenceParallelism = 0,
    [bool]$ReuseAutoTuneCache = $true,
    [int]$AutoTuneSampleCampaigns = 32,
    [double]$AutoTuneThroughputTolerance = 0.02,
    [ValidateSet("balanced-efficiency", "maximum-throughput")]
    [string]$AutoTuneObjective = "maximum-throughput",
    [ValidateSet("disabled", "auto", "cpu", "cuda")]
    [string]$TransformerTeacherBackend = "disabled",
    [ValidateRange(1, 100)]
    [int]$TransformerTeacherEpochs = 2,
    [ValidateRange(64, 100000)]
    [int]$TransformerTeacherMinimumFrames = 64,
    [switch]$ExpectAutoTuneCacheHit,
    [string]$SuccessArchiveDirectory = "",
    [ValidateRange(1, 20)]
    [int]$Iterations = 1,
    [int]$TrainingCampaignsPerIteration = 2,
    [int]$NormalValidationCampaigns = 5,
    [int]$AdvancedValidationCampaigns = 5,
    [switch]$KeepArtifactsOnFailure,
    [string]$WorkerPath = ""
)

$ErrorActionPreference = "Stop"
function Read-FoundationJson([string]$Path) {
    $input = [System.IO.File]::OpenRead($Path)
    try {
        $first = $input.ReadByte()
        $second = $input.ReadByte()
        $input.Position = 0
        if ($first -eq 0x1f -and $second -eq 0x8b) {
            $payload = [System.IO.Compression.GZipStream]::new(
                $input,
                [System.IO.Compression.CompressionMode]::Decompress,
                $true)
        }
        else {
            $payload = $input
        }
        try {
            $reader = [System.IO.StreamReader]::new(
                $payload,
                [System.Text.Encoding]::UTF8,
                $true,
                65536,
                $true)
            try {
                $json = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            if ($payload -ne $input) {
                $payload.Dispose()
            }
        }
    }
    finally {
        $input.Dispose()
    }
    # Windows PowerShell 5 rejects otherwise valid JSON objects that contain
    # an empty property name. Native simulation state can legitimately expose
    # an empty runtime-variable key, so normalize only that property token in
    # the smoke-test reader.
    return $json.Replace('"":', '"__empty":') | ConvertFrom-Json
}
function Assert-TransformerTeacherTransitions {
    param(
        [object[]]$Reports,
        [bool]$RequireResumeModelFiles = $true,
        [string]$Context = "Transformer teacher"
    )

    if ($Reports.Count -lt 1) {
        throw "$Context has no iteration reports."
    }
    $firstReport = $Reports[0]
    if ([bool]$firstReport.WarmStarted `
        -or -not [bool]$firstReport.AnchorCreated `
        -or [int]$firstReport.TeacherGeneration `
            -ne [int][bool]$firstReport.UpdateAccepted `
        -or [bool]$firstReport.TeacherSourceGatePassed `
            -ne [bool]$firstReport.UpdateAccepted `
        -or -not [string]::IsNullOrWhiteSpace(
            [string]$firstReport.ResumeModelPath) `
        -or [string]$firstReport.RefreshReason -ne "cold-start") {
        throw (
            "$Context first cold iteration contract failed: " `
            + ($firstReport | ConvertTo-Json -Depth 8 -Compress))
    }

    $anchorReport = $firstReport
    $stableReport = $null
    $reportIndex = 0
    foreach ($report in $Reports) {
        $reportIndex++
        $acceptedUpdate = [bool]$report.UpdateAccepted
        if ($acceptedUpdate `
            -and -not [bool]$report.HeadRegressionGatePassed) {
            throw (
                "$Context accepted an update that failed head regression: " `
                + ($report | ConvertTo-Json -Depth 8 -Compress))
        }
        if ($reportIndex -gt 1 `
            -and ([bool]$report.AnchorCreated `
                -or [int]$report.AnchorValidationFrames `
                    -ne [int]$anchorReport.AnchorValidationFrames `
                -or [string]$report.AnchorPath `
                    -ne [string]$anchorReport.AnchorPath)) {
            throw (
                "$Context changed the fixed anchor after the first iteration: " `
                + ($report | ConvertTo-Json -Depth 8 -Compress))
        }
        $policyApplied = [bool]$report.PolicyTeacherApplied
        $worldApplied = [bool]$report.WorldTeacherApplied
        $appliedQualityValid =
            [bool]$report.TeacherSourceGatePassed `
            -and [bool]$report.PolicyQualityGatePassed `
            -and [bool]$report.AnchorCoverageGatePassed `
            -and (-not $acceptedUpdate `
                -or [bool]$report.HeadRegressionGatePassed)
        if ([bool]$report.Applied -ne $policyApplied `
            -or ($policyApplied -and -not $appliedQualityValid) `
            -or ($worldApplied `
                -and (-not $policyApplied `
                    -or -not [bool]$report.WorldModelQualityGatePassed))) {
            throw (
                "$Context violated the independent Policy/World teacher gates: " `
                + ($report | ConvertTo-Json -Depth 8 -Compress))
        }

        if ($null -eq $stableReport) {
            if ([bool]$report.WarmStarted `
                -or -not [string]::IsNullOrWhiteSpace(
                    [string]$report.ResumeModelPath) `
                -or [string]$report.RefreshReason -ne "cold-start" `
                -or [int]$report.TeacherGeneration `
                    -ne [int]$acceptedUpdate `
                -or [bool]$report.TeacherSourceGatePassed `
                    -ne $acceptedUpdate) {
                throw (
                    "$Context cold retry contract failed: " `
                    + ($report | ConvertTo-Json -Depth 8 -Compress))
            }
            if ([bool]$report.Applied) {
                if (-not $acceptedUpdate `
                    -or [int]$report.TeacherGeneration -ne 1) {
                    throw (
                        "$Context first stable model contract failed: " `
                        + ($report | ConvertTo-Json -Depth 8 -Compress))
                }
                $stableReport = $report
            }
            continue
        }

        $expectedGeneration = [int]$stableReport.TeacherGeneration `
            + [int]$acceptedUpdate
        $resumeModelValid = -not $RequireResumeModelFiles `
            -or (Test-Path -LiteralPath ([string]$report.ResumeModelPath) `
                -PathType Leaf)
        if (-not [bool]$report.WarmStarted `
            -or [bool]$report.AnchorCreated `
            -or [string]::IsNullOrWhiteSpace(
                [string]$report.ResumeModelPath) `
            -or -not $resumeModelValid `
            -or [int]$report.TeacherGeneration -ne $expectedGeneration `
            -or [int]$report.RequiredAnchorValidationFrames `
                -ne [int]$stableReport.RequiredAnchorValidationFrames `
            -or [int]$report.AnchorValidationFrames `
                -ne [int]$stableReport.AnchorValidationFrames `
            -or [string]$report.AnchorPath -ne [string]$stableReport.AnchorPath) {
            throw (
                "$Context sequential continuation contract failed: prior=" `
                + ($stableReport | ConvertTo-Json -Depth 8 -Compress) `
                + ", current=" `
                + ($report | ConvertTo-Json -Depth 8 -Compress))
        }
        if ([bool]$report.Applied) {
            $stableReport = $report
        }
    }
    return $stableReport
}
function Assert-TransformerTeacherTransitionFixture {
    $stable = [pscustomobject]@{
        Iteration = 1
        Applied = $true
        PolicyTeacherApplied = $true
        WorldTeacherApplied = $true
        UpdateAccepted = $true
        TeacherGeneration = 1
        WarmStarted = $false
        AnchorCreated = $true
        ResumeModelPath = ""
        RefreshReason = "cold-start"
        QualityGatePassed = $true
        TeacherSourceGatePassed = $true
        PolicyQualityGatePassed = $true
        WorldModelQualityGatePassed = $true
        HeadRegressionGatePassed = $true
        AnchorCoverageGatePassed = $true
        RequiredAnchorValidationFrames = 64
        AnchorValidationFrames = 64
        AnchorPath = "fixture-anchor.jsonl"
    }
    $rejectedWarmUpdate = $stable.PSObject.Copy()
    $rejectedWarmUpdate.Iteration = 2
    $rejectedWarmUpdate.Applied = $false
    $rejectedWarmUpdate.PolicyTeacherApplied = $false
    $rejectedWarmUpdate.WorldTeacherApplied = $false
    $rejectedWarmUpdate.UpdateAccepted = $true
    $rejectedWarmUpdate.TeacherGeneration = 2
    $rejectedWarmUpdate.WarmStarted = $true
    $rejectedWarmUpdate.AnchorCreated = $false
    $rejectedWarmUpdate.ResumeModelPath = "fixture-model.pt"
    $rejectedWarmUpdate.RefreshReason = "pending-backlog"
    $rejectedWarmUpdate.QualityGatePassed = $false
    $rejectedWarmUpdate.PolicyQualityGatePassed = $false
    $rejectedWarmUpdate.HeadRegressionGatePassed = $true
    $nextWarmFromStable = $stable.PSObject.Copy()
    $nextWarmFromStable.Iteration = 3
    $nextWarmFromStable.UpdateAccepted = $false
    $nextWarmFromStable.TeacherGeneration = 1
    $nextWarmFromStable.WarmStarted = $true
    $nextWarmFromStable.AnchorCreated = $false
    $nextWarmFromStable.ResumeModelPath = "fixture-model.pt"
    $nextWarmFromStable.RefreshReason = "pending-backlog"
    $nextWarmFromStable.HeadRegressionGatePassed = $false
    $finalRejectedWarmUpdate = $stable.PSObject.Copy()
    $finalRejectedWarmUpdate.Iteration = 4
    $finalRejectedWarmUpdate.Applied = $false
    $finalRejectedWarmUpdate.PolicyTeacherApplied = $false
    $finalRejectedWarmUpdate.WorldTeacherApplied = $false
    $finalRejectedWarmUpdate.UpdateAccepted = $true
    $finalRejectedWarmUpdate.TeacherGeneration = 2
    $finalRejectedWarmUpdate.WarmStarted = $true
    $finalRejectedWarmUpdate.AnchorCreated = $false
    $finalRejectedWarmUpdate.ResumeModelPath = "fixture-model.pt"
    $finalRejectedWarmUpdate.RefreshReason = "pending-backlog"
    $finalRejectedWarmUpdate.QualityGatePassed = $false
    $finalRejectedWarmUpdate.PolicyQualityGatePassed = $false
    $finalRejectedWarmUpdate.HeadRegressionGatePassed = $true
    $fixtureStable = Assert-TransformerTeacherTransitions `
        -Reports @(
            $stable,
            $rejectedWarmUpdate,
            $nextWarmFromStable,
            $finalRejectedWarmUpdate) `
        -RequireResumeModelFiles $false `
        -Context "Transformer teacher synthetic transition fixture"
    if ($null -eq $fixtureStable `
        -or [int]$fixtureStable.Iteration -ne 3 `
        -or [int]$fixtureStable.TeacherGeneration -ne 1) {
        throw "Transformer teacher synthetic transition fixture lost the last stable teacher after a final rejected warm update."
    }
    $policyOnly = $stable.PSObject.Copy()
    $policyOnly.QualityGatePassed = $false
    $policyOnly.WorldModelQualityGatePassed = $false
    $policyOnly.WorldTeacherApplied = $false
    $policyOnlyStable = Assert-TransformerTeacherTransitions `
        -Reports @($policyOnly) `
        -RequireResumeModelFiles $false `
        -Context "Transformer policy-only teacher fixture"
    if ($null -eq $policyOnlyStable `
        -or -not [bool]$policyOnlyStable.PolicyTeacherApplied `
        -or [bool]$policyOnlyStable.WorldTeacherApplied) {
        throw "Transformer policy-only teacher fixture incorrectly coupled the world quality gate back into distillation."
    }
}
Assert-TransformerTeacherTransitionFixture
function Get-FoundationSha256([string]$Path) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = $algorithm.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}
function ConvertTo-FoundationSignedRandomSeed([UInt64]$Seed) {
    $lowBits = [Int64]($Seed % [UInt64]4294967296)
    if ($lowBits -gt [Int64][Int32]::MaxValue) {
        return [int]($lowBits - [Int64]4294967296)
    }
    return [int]$lowBits
}
function Read-FoundationSnapshotFirstRecord([string]$Path) {
    $input = [System.IO.File]::OpenRead($Path)
    try {
        $header = [System.IO.BinaryReader]::new(
            $input,
            [System.Text.Encoding]::UTF8,
            $true)
        try {
            $magic = [System.Text.Encoding]::ASCII.GetString(
                $header.ReadBytes(8))
            $version = $header.ReadInt32()
            $headerSize = $header.ReadInt32()
            $recordCount = $header.ReadInt32()
            $compression = $header.ReadInt32()
            if ($magic -ne "AURAFES5" `
                -or $version -ne 5 `
                -or $headerSize -ne 72 `
                -or $recordCount -le 0 `
                -or $compression -ne 1) {
                throw "Invalid foundation episode snapshot header."
            }
            $input.Position = $headerSize
            $gzip = [System.IO.Compression.GZipStream]::new(
                $input,
                [System.IO.Compression.CompressionMode]::Decompress,
                $true)
            try {
                $records = [System.IO.BinaryReader]::new(
                    $gzip,
                    [System.Text.Encoding]::UTF8,
                    $true)
                try {
                    $length = $records.ReadInt32()
                    [void]$records.ReadUInt32()
                    if ($length -lt 0 -or $length -gt 268435456) {
                        throw "Invalid foundation episode snapshot record length."
                    }
                    return [System.Text.Encoding]::UTF8.GetString(
                        $records.ReadBytes($length))
                }
                finally { $records.Dispose() }
            }
            finally { $gzip.Dispose() }
        }
        finally { $header.Dispose() }
    }
    finally { $input.Dispose() }
}
function Get-FoundationPersistedEpisodeCount([string]$Path) {
    $input = [System.IO.File]::OpenRead($Path)
    try {
        if ($input.Length -ge 20) {
            $reader = [System.IO.BinaryReader]::new(
                $input,
                [System.Text.Encoding]::UTF8,
                $true)
            try {
                $magic = [System.Text.Encoding]::ASCII.GetString(
                    $reader.ReadBytes(8))
                if ($magic -eq "AURAFES5") {
                    $version = $reader.ReadInt32()
                    $headerSize = $reader.ReadInt32()
                    $recordCount = $reader.ReadInt32()
                    if ($version -ne 5 -or $headerSize -ne 72 `
                        -or $recordCount -lt 0) {
                        throw "Invalid foundation episode snapshot header."
                    }
                    return $recordCount
                }
            }
            finally { $reader.Dispose() }
        }
    }
    finally { $input.Dispose() }
    return @(Get-Content -LiteralPath $Path -Encoding UTF8).Count
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$expectedTransformerRandomSeed = if ($RunSeed -eq 0) {
    1701
}
else {
    ConvertTo-FoundationSignedRandomSeed $RunSeed
}
$effectivePreflightCampaignsPerDifficulty = [Math]::Max(
    0,
    [Math]::Min(100, $PreflightCampaignsPerDifficulty))
$integrityRegressionSeedCount = 4
$effectiveNormalValidationCampaigns = [Math]::Max(
    1,
    [Math]::Min(1000, $NormalValidationCampaigns))
$effectiveAdvancedValidationCampaigns = [Math]::Max(
    1,
    [Math]::Min(1000, $AdvancedValidationCampaigns))
$minimumTrainingCampaigns = if ($TransformerTeacherBackend -eq "disabled") {
    2
} else {
    40
}
$effectiveTrainingCampaignsPerIteration = [Math]::Max(
    $minimumTrainingCampaigns,
    [Math]::Min(1000, $TrainingCampaignsPerIteration))
if ($ExpectAutoTuneCacheHit -and -not $ReuseAutoTuneCache) {
    throw "ExpectAutoTuneCacheHit conflicts with ReuseAutoTuneCache=false."
}
if (-not $SkipPublish) {
    & (Join-Path $repoRoot "tools\Build-AuraFoundationTrainer.ps1") -Configuration $Configuration
}
$worker = if ([string]::IsNullOrWhiteSpace($WorkerPath)) {
    Join-Path $repoRoot "AuraToolsExp\TrainingWorker\AuraFoundationTrainer.Worker.exe"
} else {
    [System.IO.Path]::GetFullPath($WorkerPath)
}
if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    throw "Foundation trainer is missing: $worker"
}
$sourceTeacher = Join-Path `
    $repoRoot `
    "tools\transformer-teacher\train_teacher.py"
$deployedTeacher = Join-Path `
    (Split-Path -Parent $worker) `
    "TransformerTeacher\train_teacher.py"
if (-not (Test-Path -LiteralPath $sourceTeacher -PathType Leaf)) {
    throw "Transformer teacher source is missing: $sourceTeacher"
}
if ([string]::IsNullOrWhiteSpace($WorkerPath) `
    -and -not (Test-Path -LiteralPath $deployedTeacher -PathType Leaf)) {
    throw "Published Transformer teacher is missing: $deployedTeacher"
}
if ((Test-Path -LiteralPath $deployedTeacher -PathType Leaf) `
    -and (Get-FoundationSha256 $sourceTeacher) `
        -ne (Get-FoundationSha256 $deployedTeacher)) {
    throw (
        "Transformer teacher source/deployment SHA256 mismatch: " `
        + "source=$sourceTeacher, deployed=$deployedTeacher")
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
    $checkpointEpisodesPath = Join-Path $smokeRoot "checkpoint-episodes.afes"
    $archiveRoot = if ([string]::IsNullOrWhiteSpace(
            $SuccessArchiveDirectory)) {
        Join-Path $smokeRoot "foundation-success-cases"
    }
    else {
        [System.IO.Path]::GetFullPath($SuccessArchiveDirectory)
    }
    $autoTuneCachePath = Join-Path $archiveRoot "foundation-auto-tune-v12.json"
    $autoTuneCacheExistedBefore = Test-Path -LiteralPath `
        $autoTuneCachePath -PathType Leaf
    $autoTuneCacheHashBefore = if ($autoTuneCacheExistedBefore) {
        (Get-FileHash -LiteralPath $autoTuneCachePath -Algorithm SHA256).Hash
    }
    else {
        ""
    }
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
        ContentSetHash = "68281d76979876df4d64a7a03a9675c4eca4bbd43999f32719924ab959900b36"
        OwnerModSetHash = "e912a92c1baf87aa6bd444e99f042782ae879590443232d42b88ff1ee6eb7b85"
        RunSeed = $RunSeed
        DecisionProfile = "balanced"
        Iterations = $Iterations
        TrainingCampaignsPerIteration = $effectiveTrainingCampaignsPerIteration
        ArenaCampaignsPerDifficulty = 1
        NormalValidationCampaigns = $effectiveNormalValidationCampaigns
        AdvancedValidationCampaigns = $effectiveAdvancedValidationCampaigns
        CapabilityProbeCampaignsPerDifficulty = 0
        TuningNormalCampaigns = 2
        TuningAdvancedCampaigns = 2
        TuningScreeningNormalCampaigns = 1
        TuningScreeningAdvancedCampaigns = 1
        TuningFinalistCount = 1
        PreflightCampaignsPerDifficulty = $PreflightCampaignsPerDifficulty
        PreflightSeedStart = $PreflightSeedStart
        PreflightOnly = [bool]$PreflightOnly
        MaximumDegreeOfParallelism = $MaximumDegreeOfParallelism
        ParallelismProfile = $ParallelismProfile
        InferenceExecutionMode = $InferenceExecutionMode
        InferenceParallelism = $InferenceParallelism
        ReuseAutoTuneCache = $ReuseAutoTuneCache
        AutoTuneSampleCampaigns = $AutoTuneSampleCampaigns
        AutoTuneThroughputTolerance = $AutoTuneThroughputTolerance
        AutoTuneObjective = $AutoTuneObjective
        EnableEarlyValidationStop = $true
        TrainingSeedStart = 4100000
        ArenaSeedStart = 4200000
        ValidationSeedStart = 4300000
        Profile = $profile
        TransformerTeacher = [ordered]@{
            Backend = $TransformerTeacherBackend
            PythonExecutable = "auto"
            Epochs = $TransformerTeacherEpochs
            BatchSize = 64
            StateDimensions = 128
            ActionDimensions = 128
            HiddenDimensions = 64
            Layers = 2
            AttentionHeads = 4
            HistoryLength = 12
            MinimumFrames = $TransformerTeacherMinimumFrames
            MaximumFrames = 10000
            EnableWarmStart = $true
            CpuRefreshInterval = 4
            CpuEpochs = $TransformerTeacherEpochs
            CpuIncrementalEpochs = [Math]::Min(1, $TransformerTeacherEpochs)
            CpuFinalEpochs = $TransformerTeacherEpochs
            IncrementalEpochs = [Math]::Min(4, $TransformerTeacherEpochs)
            FinalEpochs = $TransformerTeacherEpochs
            EnableAdaptiveRefresh = $true
            AdaptiveRefreshDriftThreshold = 0.15
            EnableFixedAnchorValidation = $true
            MaximumHeadRegression = 0.05
            CpuThreads = 0
            CpuInteropThreads = 0
            MicroBatchSize = 0
            DataLoaderWorkers = 0
            PrefetchBatches = 2
            EnablePinnedMemory = $true
            EnableMixedPrecision = $true
            DistillationWeight = 0.35
            RandomSeed = $expectedTransformerRandomSeed
        }
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
    $protocolVersion = 17
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
    $serializedJob = Get-Content -Raw -Encoding UTF8 `
        -LiteralPath $jobPath | ConvertFrom-Json
    if ($null -eq $serializedJob.Request.PSObject.Properties[
            "ReuseAutoTuneCache"] `
        -or [bool]$serializedJob.Request.ReuseAutoTuneCache `
            -ne [bool]$ReuseAutoTuneCache) {
        throw "Foundation smoke did not preserve the explicit auto-tune cache policy."
    }
    if ([UInt64]$serializedJob.Request.RunSeed -ne $RunSeed `
        -or [int]$serializedJob.Request.TransformerTeacher.RandomSeed `
            -ne $expectedTransformerRandomSeed) {
        throw (
            "Foundation smoke did not preserve the full RunSeed/signed " `
            + "Transformer seed contract: run=" `
            + "$($serializedJob.Request.RunSeed)/$RunSeed, transformer=" `
            + "$($serializedJob.Request.TransformerTeacher.RandomSeed)/" `
            + "$expectedTransformerRandomSeed.")
    }
    & $worker --job $jobPath
    if ($LASTEXITCODE -ne 0) {
        throw "Foundation trainer smoke process failed: $LASTEXITCODE"
    }
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Foundation trainer smoke result is missing."
    }
    $result = Read-FoundationJson $resultPath
    if (-not $ReuseAutoTuneCache) {
        if ([bool]$result.Training.AutoTune.CacheHit) {
            throw "Disabled auto-tune cache reuse still reported a cache hit."
        }
        $autoTuneCacheExistsAfter = Test-Path -LiteralPath `
            $autoTuneCachePath -PathType Leaf
        if (-not $autoTuneCacheExistedBefore -and $autoTuneCacheExistsAfter) {
            throw "Disabled auto-tune cache reuse still created a cache file."
        }
        if ($autoTuneCacheExistedBefore) {
            if (-not $autoTuneCacheExistsAfter) {
                throw "Disabled auto-tune cache reuse removed the existing cache file."
            }
            $autoTuneCacheHashAfter = (Get-FileHash -LiteralPath `
                $autoTuneCachePath -Algorithm SHA256).Hash
            if ($autoTuneCacheHashAfter -ne $autoTuneCacheHashBefore) {
                throw "Disabled auto-tune cache reuse modified the existing cache file."
            }
        }
    }
    $semanticRejected = -not [bool]$result.Training.SemanticGatePassed
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
    if ([UInt64]$result.Training.RunSeed -ne $RunSeed `
        -or [int]$result.Training.ModelRandomSeed -lt 0) {
        throw (
            "Foundation training did not retain the full RunSeed/nonnegative " `
            + "model seed: run=$($result.Training.RunSeed)/$RunSeed, " `
            + "model=$($result.Training.ModelRandomSeed).")
    }
    if (-not $PreflightOnly -and $semanticRejected) {
        throw (
            "Foundation trainer semantic admission failed: " `
            + "selectedInvalid=" `
            + [int]$result.Training.SemanticAudit.SelectedInvalidActions `
            + ", selectedUnexplained=" `
            + [int]$result.Training.SemanticAudit.SelectedUnexplainedMismatchActions)
    }
    if (-not $PreflightOnly `
        -and $TransformerTeacherBackend -ne "disabled") {
        $teacherReports = @($result.Training.TransformerTeacherReports)
        if ($teacherReports.Count -lt 1) {
            throw "Transformer teacher did not publish an iteration report."
        }
        # Iteration isolation publishes the final child result. Validate the
        # last available teacher report so this assertion remains correct if
        # the supervisor later preserves earlier child diagnostics as well.
        $teacherReport = $teacherReports[-1]
        if (-not [bool]$teacherReport.Requested) {
            throw "Transformer teacher final iteration was not requested."
        }
        $requiredAnchorFrames =
            [int]$teacherReport.RequiredAnchorValidationFrames
        $incrementalTeacherEpochs = if (
            $TransformerTeacherBackend -eq "cpu") {
            [Math]::Min(1, $TransformerTeacherEpochs)
        }
        else { [Math]::Min(4, $TransformerTeacherEpochs) }
        $expectedFinalTeacherEpochs = if (
            [bool]$teacherReport.WarmStarted `
            -and [string]$teacherReport.RefreshReason -ne "final-refresh") {
            $incrementalTeacherEpochs
        }
        else { $TransformerTeacherEpochs }
        if ([int]$teacherReport.FrameCount `
            -lt $TransformerTeacherMinimumFrames `
            -or -not [bool]$teacherReport.Success `
            -or ([bool]$teacherReport.Applied `
                -and (-not [bool]$teacherReport.PolicyTeacherApplied `
                    -or -not [bool]$teacherReport.TeacherSourceGatePassed `
                    -or -not [bool]$teacherReport.PolicyQualityGatePassed `
                    -or -not [bool]$teacherReport.AnchorCoverageGatePassed)) `
            -or ([bool]$teacherReport.WorldTeacherApplied `
                -and (-not [bool]$teacherReport.PolicyTeacherApplied `
                    -or -not [bool]$teacherReport.WorldModelQualityGatePassed)) `
            -or ([bool]$teacherReport.Applied `
                -ne [bool]$teacherReport.PolicyTeacherApplied) `
            -or -not [bool]$teacherReport.TrainingRefreshed `
            -or [int]$teacherReport.RequestedEpochs `
                -ne $expectedFinalTeacherEpochs `
            -or [int]$teacherReport.AnnotatedFrames `
                -lt $TransformerTeacherMinimumFrames `
            -or $requiredAnchorFrames -lt 1 `
            -or [int]$teacherReport.AnchorValidationFrames `
                -lt $requiredAnchorFrames `
            -or [int]$teacherReport.TeacherGeneration -lt 1 `
            -or ([bool]$teacherReport.UpdateAccepted `
                -and -not [bool]$teacherReport.HeadRegressionGatePassed) `
            -or -not [bool]$teacherReport.AnchorCoverageGatePassed `
            -or [int]$teacherReport.IncrementalPendingFrames -lt 0 `
            -or [int]$teacherReport.IncrementalDeferredFrames -lt 0 `
            -or -not (Test-Path -LiteralPath `
                ([string]$teacherReport.ModelPath) -PathType Leaf) `
            -or -not (Test-Path -LiteralPath `
                ([string]$teacherReport.AnchorPath) -PathType Leaf)) {
            throw (
                "Transformer teacher annotation failed: " `
                + ($teacherReport | ConvertTo-Json -Depth 8 -Compress))
        }
        if ($Iterations -le 1) {
            if (-not [bool]$teacherReport.Applied `
                -or [bool]$teacherReport.WarmStarted `
                -or -not [bool]$teacherReport.UpdateAccepted `
                -or -not [bool]$teacherReport.AnchorCreated `
                -or [string]$teacherReport.RefreshReason -ne "cold-start") {
                throw (
                    "Transformer teacher cold-start contract failed: " `
                    + ($teacherReport | ConvertTo-Json -Depth 8 -Compress))
            }
        }
        else {
            $iterationTeacherReports = @()
            for ($teacherIteration = 1; `
                 $teacherIteration -le $Iterations; `
                 $teacherIteration++) {
                $iterationTeacherReportPath = Join-Path `
                    $smokeRoot `
                    ("transformer-teacher\iteration-{0:D2}\world-model-report-v2.json" `
                        -f $teacherIteration)
                if (-not (Test-Path -LiteralPath `
                        $iterationTeacherReportPath -PathType Leaf)) {
                    throw (
                        "Transformer teacher lost isolated iteration report: " `
                        + $teacherIteration)
                }
                $iterationTeacherReport =
                    Read-FoundationJson $iterationTeacherReportPath
                $expectedIterationEpochs = if (
                    [bool]$iterationTeacherReport.WarmStarted `
                    -and [string]$iterationTeacherReport.RefreshReason `
                        -ne "final-refresh") {
                    $incrementalTeacherEpochs
                }
                else { $TransformerTeacherEpochs }
                if ([int]$iterationTeacherReport.Iteration `
                        -ne $teacherIteration `
                    -or -not [bool]$iterationTeacherReport.Requested `
                    -or -not [bool]$iterationTeacherReport.Success `
                    -or -not [bool]$iterationTeacherReport.TrainingRefreshed `
                    -or [int]$iterationTeacherReport.RequestedEpochs `
                        -ne $expectedIterationEpochs) {
                    throw (
                        "Transformer teacher iteration report is invalid: " `
                        + ($iterationTeacherReport | ConvertTo-Json `
                            -Depth 8 -Compress))
                }
                $iterationTeacherReports += $iterationTeacherReport
            }
            $stableTeacherReport = Assert-TransformerTeacherTransitions `
                -Reports $iterationTeacherReports `
                -RequireResumeModelFiles $true `
                -Context "Transformer teacher smoke"
            if ($null -eq $stableTeacherReport) {
                throw "Transformer teacher smoke never established an applied stable teacher."
            }
            if ([string]$teacherReport.RefreshReason `
                    -in @("pending-backlog", "accelerator-incremental") `
                -and (-not [bool]$teacherReport.IncrementalTrainingSelection `
                    -or [int]$teacherReport.IncrementalNewFrames -lt 1 `
                    -or [int]$teacherReport.IncrementalTrainingFrames `
                        -ne ([int]$teacherReport.IncrementalNewFrames `
                            + [int]$teacherReport.IncrementalReplayFrames))) {
                throw (
                    "Transformer teacher incremental selection contract failed: " `
                    + ($teacherReport | ConvertTo-Json -Depth 8 -Compress))
            }
        }
        if ([string]$teacherReport.DatasetStorageMode -eq "resident" `
            -and [int]$teacherReport.EffectiveDataLoaderWorkers -ne 0) {
            throw "Resident Transformer corpus spawned duplicating DataLoader workers."
        }
        $teacherThroughput = [double]$teacherReport.TrainingFramesPerSecond
        $annotationThroughput =
            [double]$teacherReport.AnnotationFramesPerSecond
        $teacherPreparation = [double]$teacherReport.DataPreparationSeconds
        $teacherCalibration = [double]$teacherReport.RuntimeCalibrationSeconds
        if (-not [System.IO.Path]::IsPathRooted(
                [string]$teacherReport.ResolvedPythonExecutable) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$teacherReport.RuntimeResolutionSource) `
            -or [string]::IsNullOrWhiteSpace([string]$teacherReport.PythonVersion) `
            -or [string]::IsNullOrWhiteSpace([string]$teacherReport.TorchVersion) `
            -or [string]::IsNullOrWhiteSpace([string]$teacherReport.NumpyVersion) `
            -or [int]$teacherReport.EffectiveCpuThreads -lt 1 `
            -or [int]$teacherReport.EffectiveCpuInteropThreads -lt 1 `
            -or [int]$teacherReport.EffectiveBatchSize -lt 1 `
            -or [int]$teacherReport.EffectiveMicroBatchSize -lt 1 `
            -or [int]$teacherReport.EffectiveMicroBatchSize `
                -gt [int]$teacherReport.EffectiveBatchSize `
            -or [string]::IsNullOrWhiteSpace(
                [string]$teacherReport.NumericPrecision) `
            -or [double]::IsNaN($teacherThroughput) `
            -or [double]::IsInfinity($teacherThroughput) `
            -or $teacherThroughput -le 0 `
            -or [double]::IsNaN($annotationThroughput) `
            -or [double]::IsInfinity($annotationThroughput) `
            -or $annotationThroughput -le 0 `
            -or [double]$teacherReport.ProcessCpuSeconds -le 0 `
            -or [long]$teacherReport.PeakWorkingSetBytes -le 0 `
            -or [double]$teacherReport.DataLoadingSeconds -lt 0 `
            -or [double]$teacherReport.TrainingSeconds -le 0 `
            -or [double]$teacherReport.EvaluationSeconds -le 0 `
            -or [double]$teacherReport.AnnotationSeconds -le 0 `
            -or [double]$teacherReport.SavingSeconds -lt 0 `
            -or @($teacherReport.StageSeconds.PSObject.Properties).Count -lt 6 `
            -or [double]::IsNaN($teacherPreparation) `
            -or [double]::IsInfinity($teacherPreparation) `
            -or $teacherPreparation -lt 0 `
            -or [double]::IsNaN($teacherCalibration) `
            -or [double]::IsInfinity($teacherCalibration) `
            -or $teacherCalibration -lt 0 `
            -or (-not [bool]$teacherReport.RuntimeAutoTuned `
                -and -not [bool]$teacherReport.RuntimeAutoTuneCacheHit)) {
            throw (
                "Transformer teacher runtime plan is incomplete: " `
                + ($teacherReport | ConvertTo-Json -Depth 8 -Compress))
        }
        if ($TransformerTeacherBackend -eq "cuda" `
            -and [string]$teacherReport.EffectiveBackend -ne "cuda") {
            throw "Transformer teacher silently downgraded an explicit CUDA request."
        }
    }
    if (-not $PreflightOnly -and $null -ne $result.Training.Champion) {
        $championMetricNames = @(
            $result.Training.Champion.Metrics.PSObject.Properties.Name)
        foreach ($requiredMetric in @(
                "episodeCount",
                "optimizerAdamW",
                "stateFeatureCollisionRate",
                "validationCompositeLoss",
                "testCompositeLoss",
                "candidateEpoch")) {
            if ($championMetricNames -notcontains $requiredMetric) {
                throw "Champion lost full model metric: $requiredMetric"
            }
        }
        if ($championMetricNames.Count -lt 40) {
            throw (
                "Champion metric manifest is unexpectedly truncated: " `
                + $championMetricNames.Count)
        }
    }
    if (@($result.Training.ValidationRuns).Count -ne 0) {
        throw "Foundation worker result retained process-local validation run details."
    }
    if ([string]::IsNullOrWhiteSpace($result.RulesetHash)) {
        throw "Foundation trainer did not return a ruleset hash."
    }
    if (-not $PreflightOnly -and $Iterations -gt 1) {
        $completedIterations = @($result.Training.Iterations)
        $warehouseRoot = [string]$result.Training.ReplayWarehousePath
        $warehouseIndex = if ([string]::IsNullOrWhiteSpace($warehouseRoot)) {
            ""
        }
        else {
            Join-Path $warehouseRoot "replay-index-v2.jsonl"
        }
        $segmentFiles = @(Get-ChildItem -LiteralPath `
            (Join-Path $smokeRoot ".iteration-processes") `
            -File -ErrorAction SilentlyContinue)
        $iterationProcessIds = @($completedIterations | ForEach-Object {
            [int]$_.WorkerProcessId
        } | Sort-Object -Unique)
        if ($completedIterations.Count -lt $Iterations `
            -or -not [bool]$result.Training.IterationProcessIsolationEnabled `
            -or $iterationProcessIds.Count -lt $Iterations `
            -or -not (Test-Path -LiteralPath $warehouseIndex -PathType Leaf) `
            -or $segmentFiles.Count -ne 0 `
            -or [int]$completedIterations[1].ReplayLoadedHistoricalEpisodes -lt 1 `
            -or -not [bool]$result.ResumeRequested `
            -or -not [bool]$result.ResumedFromCheckpoint `
            -or [string]$result.RequestedStartMode `
                -ne "iteration-boundary-resume" `
            -or [string]$result.EffectiveStartMode -ne "checkpoint-exact") {
            throw (
                "Iteration process isolation or Replay warehouse failed: " `
                + "iterations=$($completedIterations.Count)/$Iterations, " `
                + "processes=$($iterationProcessIds -join ','), " `
                + "warehouse=$(Test-Path -LiteralPath $warehouseIndex), " `
                + "segmentFiles=$($segmentFiles.Count), " `
                + "historical=$([int]$completedIterations[1].ReplayLoadedHistoricalEpisodes), " `
                + "start=$($result.RequestedStartMode)/" `
                + "$($result.EffectiveStartMode), " `
                + "resumed=$($result.ResumedFromCheckpoint)")
        }
    }
    if (-not (Test-Path -LiteralPath $result.EpisodesPath -PathType Leaf)) {
        throw "Foundation trainer episodes artifact is missing."
    }
    if (-not (Test-Path -LiteralPath $result.TrainingMetricsPath -PathType Leaf) `
        -or -not (Test-Path -LiteralPath $result.TrainingAnalysisPath -PathType Leaf)) {
        throw "Foundation trainer independent metric artifacts are missing."
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
    $episodeCount = Get-FoundationPersistedEpisodeCount `
        ([string]$result.EpisodesPath)
    $generatedEpisodes = [int]$result.Training.GeneratedReplayEpisodes
    $persistedEpisodes = [int]$result.Training.PersistedReplayEpisodes
    $hasContinuableWorkingModel = $null -ne $result.Training.WorkingChampion
    if (($result.Training.Success -or $hasContinuableWorkingModel) `
        -and -not $PreflightOnly) {
        if ($persistedEpisodes -gt $generatedEpisodes `
            -or $persistedEpisodes -le 0 `
            -or $episodeCount -ne $persistedEpisodes) {
            throw (
                "Accepted or Working training replay persistence mismatch: " `
                + "generated=$generatedEpisodes, " `
                + "persisted=$persistedEpisodes, file=$episodeCount")
        }
        if ($generatedEpisodes -gt $persistedEpisodes) {
            $warehouseAccepted = (
                [int]$result.Training.ReplayArchivedEpisodes `
                + [int]$result.Training.ReplayArchiveDuplicates)
            if (-not [string]::IsNullOrWhiteSpace(
                    [string]$result.Training.ReplayWarehouseError) `
                -or $warehouseAccepted -lt $generatedEpisodes `
                -or -not (Test-Path -LiteralPath (
                        [string]$result.Training.ReplayWarehousePath) `
                    -PathType Container)) {
                throw (
                    "Bounded hot replay did not preserve full cold history: " `
                    + "generated=$generatedEpisodes, hot=$persistedEpisodes, " `
                    + "warehouseAccepted=$warehouseAccepted, " `
                    + "error=$($result.Training.ReplayWarehouseError)")
            }
        }
    }
    elseif ($persistedEpisodes -ne 0 -or $episodeCount -ne 0) {
        throw (
            "Non-continuable or preflight-only training must omit full replay data: " `
            + "generated=$generatedEpisodes, " `
            + "persisted=$persistedEpisodes, file=$episodeCount")
    }
    if (-not $PreflightOnly -and $generatedEpisodes -le 0) {
        throw "Foundation trainer smoke did not generate replay episodes."
    }
    if (-not $PreflightOnly) {
        $checkpointCatalogPath = Join-Path `
            (Split-Path -Parent $checkpointPath) `
            "foundation-checkpoint-catalog-v1.json"
        $checkpointCatalogExists = Test-Path `
            -LiteralPath $checkpointCatalogPath -PathType Leaf
        $checkpointCatalogRequired = [bool]$result.Resumable `
            -or [string]$result.CompletionKind -eq "iteration-boundary"
        if ([int]$result.CheckpointWriteFailures -ne 0 `
            -or -not [string]::IsNullOrWhiteSpace(
                [string]$result.CheckpointWarning) `
            -or ($checkpointCatalogRequired `
                -and -not $checkpointCatalogExists)) {
            throw (
                "Foundation checkpoint storage reported a silent failure: " `
                + "failures=$($result.CheckpointWriteFailures), " `
                + "warning=$($result.CheckpointWarning), " `
                + "catalog=$checkpointCatalogPath")
        }
        $checkpointSeconds = [double]$result.CheckpointSerializationSeconds
        $checkpointEnqueued = [int64]$result.CheckpointWritesEnqueued
        $checkpointExecuted = [int64]$result.CheckpointWritesExecuted
        $checkpointCoalesced = [int64]$result.CheckpointWritesCoalesced
        if ([int]$result.EffectiveCheckpointSerializationParallelism -lt 1 `
            -or [double]::IsNaN($checkpointSeconds) `
            -or [double]::IsInfinity($checkpointSeconds) `
            -or $checkpointSeconds -lt 0 `
            -or $checkpointEnqueued -lt 1 `
            -or $checkpointExecuted -lt 1 `
            -or $checkpointExecuted -gt $checkpointEnqueued `
            -or $checkpointCoalesced -lt 0 `
            -or $checkpointCoalesced -gt $checkpointEnqueued) {
            throw (
                "Foundation checkpoint execution telemetry is incomplete: " `
                + ($result | Select-Object `
                    EffectiveCheckpointSerializationParallelism, `
                    CheckpointSerializationAutoScaled, `
                    CheckpointSerializationSeconds, `
                    CheckpointWritesEnqueued, `
                    CheckpointWritesExecuted, `
                    CheckpointWritesCoalesced | `
                    ConvertTo-Json -Compress))
        }
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
    if (@($progress.Telemetry.ModelEpochHistory).Count -ne 0) {
        throw "Frequent progress payload retained the growing Epoch history."
    }
    if ([int]$result.Training.ExecutedCampaigns -lt `
            [int]$result.Training.CompletedCampaigns `
        -or [int]$result.Training.ExecutedCampaigns -le 0 `
        -or [int]$progress.Telemetry.ExecutedCampaigns -lt `
            [int]$progress.Telemetry.CompletedCampaigns `
        -or [int]$progress.Telemetry.RunExecutedCampaigns -lt `
            [int]$progress.Telemetry.RunCompletedCampaigns `
        -or [int]$progress.Telemetry.CurrentPhaseCompletedBattles -lt 0) {
        throw (
            "Foundation phase/formal campaign telemetry is inconsistent: " `
            + ($progress.Telemetry | Select-Object `
                CompletedCampaigns, ExecutedCampaigns, `
                RunCompletedCampaigns, RunExecutedCampaigns, `
                CurrentPhaseCompletedCampaigns, `
                CurrentPhaseRequestedCampaigns, `
                CurrentPhaseCompletedBattles | ConvertTo-Json -Compress))
    }
    if ($PreflightOnly) {
        if (-not $result.Training.Success `
            -or $result.Training.Preflight.CompletedCampaigns `
                -ne ($effectivePreflightCampaignsPerDifficulty * 2 `
                     + $integrityRegressionSeedCount) `
            -or $result.Training.CompletedCampaigns -ne 0) {
            throw "Foundation trainer preflight-only result is incomplete."
        }
    }
    elseif (-not $semanticRejected `
            -and ($progress.Telemetry.ModelTotalEpochs -lt 5 `
            -or $progress.Telemetry.PolicyDecisions -le 0 `
            -or $progress.Telemetry.SearchSimulations -le 0)) {
            throw "Foundation trainer model/search telemetry is incomplete."
    }
    if (-not $PreflightOnly -and -not $semanticRejected) {
        $epochHistory = @($result.Training.ModelEpochHistory)
        $metricRecords = @(
            Get-Content -LiteralPath $result.TrainingMetricsPath -Encoding UTF8
        )
        $trainingAnalysis = Read-FoundationJson $result.TrainingAnalysisPath
        $phaseHotspots = @($trainingAnalysis.PerformanceHotspots | Where-Object {
            [string]$_.Scope -eq "phase"
        })
        if ([double]$result.RoleStrategyMetrics."journey-terminal-snapshots" -le 0 `
            -or [double]$result.RoleStrategyMetrics."journey-final-max-hp.mean" -le 0 `
            -or [double]$trainingAnalysis.RoleStrategyMetrics."journey-terminal-snapshots" -le 0) {
            throw "Foundation terminal campaign snapshots are missing from training diagnostics."
        }
        if ($epochHistory.Count -lt 2 `
            -or $metricRecords.Count -lt 2 `
            -or [int]$trainingAnalysis.EpochCount -lt 1 `
            -or @($trainingAnalysis.Points).Count -lt 1 `
            -or [string]$trainingAnalysis.PerformanceProbeVersion `
                -ne "foundation-performance-probe-v1" `
            -or [int]$trainingAnalysis.LogicalProcessors -lt 1 `
            -or @($trainingAnalysis.EnabledPerformanceProbes).Count -lt 8 `
            -or $phaseHotspots.Count -lt 3 `
            -or @($phaseHotspots | Where-Object {
                [string]$_.Name -eq "self-play" `
                -and [double]$_.ElapsedSeconds -gt 0 `
                -and [int]$_.PeakConcurrentWork -gt 0 `
                -and [int]$_.ObservedWorkerThreads -gt 0
            }).Count -ne 1 `
            -or ($result.Training.Success -and @($phaseHotspots | Where-Object {
                    [string]$_.Name -eq "validation" `
                    -and [double]$_.ElapsedSeconds -gt 0 `
                    -and [int]$_.PeakConcurrentWork -gt 0
                }).Count -ne 1) `
            -or [double]$result.Training.ModelTrainingLoss -le 0 `
            -or [double]$result.Training.ModelValidationLoss -le 0 `
            -or @($epochHistory | Where-Object {
                -not $_.Calibrated `
                -and ([double]$_.Training.CompositeLoss -le 0 `
                     -or [double]$_.Validation.CompositeLoss -le 0)
            }).Count -gt 0) {
            throw (
                "Foundation trainer loss diagnostics are incomplete: " `
                + "history=$($epochHistory.Count), " `
                + "training=$($result.Training.ModelTrainingLoss), " `
                + "validation=$($result.Training.ModelValidationLoss).")
        }
    }
    if (-not $semanticRejected) {
        $expectedParallelism = switch ($ParallelismProfile) {
            "cpu-16" { [Math]::Min(16, [Environment]::ProcessorCount) }
            "cpu-32" { [Math]::Min(32, [Environment]::ProcessorCount) }
            "auto" {
                if ($null -eq $result.Training.AutoTune `
                    -or [int]$result.Training.AutoTune.SelectedParallelism -le 0) {
                    throw "Auto profile did not report measured parallelism."
                }
                [int]$result.Training.AutoTune.SelectedParallelism
            }
            default {
                [Math]::Max(
                    1,
                    [Math]::Min(
                        [Environment]::ProcessorCount,
                        $MaximumDegreeOfParallelism))
            }
        }
        $availableValidationWork = if ($PreflightOnly) {
            $effectivePreflightCampaignsPerDifficulty * 2 `
            + $integrityRegressionSeedCount
        }
        else {
            [Math]::Max(
                $effectiveNormalValidationCampaigns,
                $effectiveAdvancedValidationCampaigns)
        }
        $expectedValidationPeak = [Math]::Min(
            $expectedParallelism,
            $(if ($PreflightOnly) {
                [Math]::Min(3, $availableValidationWork)
            }
            else {
                $availableValidationWork
            }))
        $expectedInferenceMode = if ($ParallelismProfile -eq "auto") {
            if ([bool]$result.Training.AutoTune.InferenceCalibrated) {
                [string]$result.Training.AutoTune.SelectedInferenceMode
            }
            else {
                "direct"
            }
        }
        else {
            $InferenceExecutionMode
        }
        if ([int]$result.Training.EffectiveParallelism `
                -ne $expectedParallelism `
            -or [int]$result.Training.PeakConcurrentCampaigns `
                -lt $expectedValidationPeak `
            -or [string]$result.Training.InferenceExecutionMode `
                -ne $expectedInferenceMode) {
            throw (
                "Foundation worker did not sustain configured parallelism: " `
                + "effective=$($result.Training.EffectiveParallelism)/" `
                + "$expectedParallelism, peak=" `
                + "$($result.Training.PeakConcurrentCampaigns)/" `
                + "$expectedValidationPeak, inference=" `
                + "$($result.Training.InferenceExecutionMode)/" `
                + "$expectedInferenceMode.")
        }
        if ($ParallelismProfile -eq "auto") {
            $measurements = @($result.Training.AutoTune.Measurements)
            $capacityDecision = $result.Training.ParallelismDecision
            if ([string]$result.Training.AutoTune.Version `
                    -ne "foundation-auto-tune-v12-signed-microbenchmark" `
                -or [string]$result.Training.AutoTune.Objective `
                    -ne $AutoTuneObjective `
                -or $null -eq $capacityDecision `
                -or [string]$capacityDecision.ProtocolVersion `
                    -ne "foundation-parallelism-v4-phase-aware-128m-reserve" `
                -or [int]$capacityDecision.SelectedParallelism `
                    -ne [int]$result.Training.EffectiveParallelism `
                -or [int64]$capacityDecision.PredictedPerLaneBytes -le 0 `
                -or [int64]$capacityDecision.MemoryReserveBytes `
                    -ne [int64](128 * 1024 * 1024) `
                -or [string]::IsNullOrWhiteSpace(
                    [string]$capacityDecision.Reason) `
                -or [string]::IsNullOrWhiteSpace(
                    [string]$result.Training.AutoTune.CacheKey)) {
                throw "Auto profile did not retain memory-capacity evidence."
            }
            if ($Iterations -le 1 `
                -and -not $ExpectAutoTuneCacheHit `
                -and [bool]$result.Training.AutoTune.CacheHit) {
                throw "Memory-capacity parallelism unexpectedly reused a throughput cache."
            }
            if ($Iterations -gt 1 `
                -and $ReuseAutoTuneCache `
                -and -not [bool]$result.Training.AutoTune.CacheHit) {
                throw "Iteration child did not reuse the first round's stable auto-tune plan."
            }
            if ($ExpectAutoTuneCacheHit `
                -and -not [bool]$result.Training.AutoTune.CacheHit) {
                throw "Foundation smoke expected a stable auto-tune cache hit."
            }
            $actualCampaignPoints = @($measurements | Where-Object {
                    ([string]$_.MeasurementKind).StartsWith(
                        "campaign",
                        [System.StringComparison]::Ordinal)
                } | ForEach-Object { [int]$_.Parallelism } `
                  | Sort-Object -Unique)
            $calibrationMaximum = [Math]::Max(
                1,
                [Math]::Min(
                    64,
                    [Math]::Min(
                        [Environment]::ProcessorCount,
                        $MaximumDegreeOfParallelism)))
            $expectedCampaignPoints = @(
                [Math]::Ceiling($calibrationMaximum * 0.50),
                $calibrationMaximum
            ) | ForEach-Object { [int]$_ } | Sort-Object -Unique
            $disabledMultiProcessCacheReuse = -not $PreflightOnly `
                -and $Iterations -gt 1 `
                -and -not $ReuseAutoTuneCache
            if ($disabledMultiProcessCacheReuse `
                -and $actualCampaignPoints.Count -ne 0) {
                throw (
                    "Disabled auto-tune cache reuse leaked campaign probes " `
                    + "from an earlier iteration process: " `
                    + "actual=$($actualCampaignPoints -join ',').")
            }
            if (-not $disabledMultiProcessCacheReuse `
                -and -not [bool]$result.Training.AutoTune.CacheHit `
                -and ([string]$result.Training.AutoTune.CampaignCalibrationKind `
                        -eq "short-two-candidate-v1") `
                -and ($actualCampaignPoints -join ",") `
                    -ne ($expectedCampaignPoints -join ",")) {
                throw (
                    "Adaptive parallelism campaign probes are incomplete: " `
                    + "actual=$($actualCampaignPoints -join ','), " `
                    + "expected=$($expectedCampaignPoints -join ',').")
            }
            if (-not $disabledMultiProcessCacheReuse `
                -and -not [bool]$result.Training.AutoTune.CacheHit `
                -and ([string]::IsNullOrWhiteSpace(
                        [string]$result.Training.AutoTune.CacheMissReason) `
                    -or [string]::IsNullOrWhiteSpace(
                        [string]$result.Training.AutoTune.CampaignCalibrationKind) `
                    -or $actualCampaignPoints.Count -gt 2 `
                    -or -not ($actualCampaignPoints -contains $calibrationMaximum))) {
                throw (
                    "Short auto-tune diagnostics are incomplete: " `
                    + ($result.Training.AutoTune `
                        | ConvertTo-Json -Depth 8 -Compress))
            }
            if (-not $PreflightOnly `
                -and ($Iterations -le 1 `
                    -or $disabledMultiProcessCacheReuse)) {
                $inferenceMeasurements = @($measurements | Where-Object {
                    ([string]$_.MeasurementKind).StartsWith(
                        "inference-microbenchmark",
                        [System.StringComparison]::Ordinal)
                })
                if (-not [bool]$result.Training.AutoTune.InferenceCalibrated `
                    -or [string]$result.Training.AutoTune.InferenceCalibrationKind `
                        -ne "inference-microbenchmark-v1" `
                    -or [int]$result.Training.AutoTune.InferenceCalibrationSamples `
                        -lt 64 `
                    -or $inferenceMeasurements.Count -lt 2 `
                    -or @($inferenceMeasurements | Where-Object {
                        [int]$_.Campaigns -ne 0 `
                        -or [int64]$_.InferenceRequests -le 0 `
                        -or [double]$_.UsefulWorkPerSecond -le 0
                    }).Count -gt 0) {
                    throw "Auto profile did not retain bounded inference microbenchmark evidence."
                }
            }
        }
        elseif ([bool]$result.Training.AutoTune.CacheHit `
                -or @($result.Training.AutoTune.Measurements).Count -ne 0) {
            throw "Fixed parallelism unexpectedly used auto-tune measurements or cache."
        }
        if (-not $PreflightOnly `
            -and [int64]$result.Training.EpisodeCompactStateVectors -le 0) {
            throw "Foundation worker did not record compact episode state vectors."
        }
        if (-not $PreflightOnly `
            -and [string]$TransformerTeacherBackend -eq "disabled" `
            -and ([int64]$result.Training.WorldModelObservationsBuilt -ne 0 `
                 -or [int64]$result.Training.WorldModelObservationsSkipped `
                    -ne [int64]$result.Training.EpisodeCompactStateVectors)) {
            throw "Disabled Transformer teacher still built WorldModel observations."
        }
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
    if (-not $PreflightOnly `
        -and ([string]::IsNullOrWhiteSpace(
                [string]$result.ArtifactBundleDirectory) `
             -or -not (Test-Path -LiteralPath (
                [string]$result.ArtifactManifestPath) -PathType Leaf) `
             -or -not (Test-Path -LiteralPath (
                [string]$result.CapabilityReportPath) -PathType Leaf) `
             -or -not (Test-Path -LiteralPath (
                [string]$result.SimulationDatabasePath) -PathType Leaf) `
             -or -not (Test-Path -LiteralPath (
                [string]$result.ModelNodeGraphPath) -PathType Leaf))) {
        throw "Training did not publish the required model/report/process artifact bundle."
    }
    if ($checkpointExists `
        -and ([UInt64]$checkpoint.Resume.RunSeed -ne $RunSeed `
            -or [int]$checkpoint.Resume.ModelRandomSeed `
                -ne [int]$result.Training.ModelRandomSeed)) {
        throw (
            "Foundation checkpoint changed the seed plan across the " `
            + "iteration boundary: run=$($checkpoint.Resume.RunSeed)/" `
            + "$RunSeed, model=$($checkpoint.Resume.ModelRandomSeed)/" `
            + "$($result.Training.ModelRandomSeed).")
    }
    $checkpointSnapshots = @(
        Get-ChildItem -LiteralPath $smokeRoot `
            -Filter "checkpoint-episodes.snapshot-*.afes" `
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
    elseif ($semanticRejected) {
        if ($result.CompletionKind -ne "training-rejected" `
            -or $result.Resumable `
            -or $checkpointExists `
            -or $checkpointSnapshots.Count -ne 0 `
            -or [bool]$result.ModelAccepted `
            -or [bool]$result.TrainingSucceeded `
            -or -not [bool]$result.WorkerCompleted `
            -or [int]$result.Training.SemanticRejectedCampaigns -le 0 `
            -or [int]$result.Training.DiscardedSemanticEpisodes -le 0 `
            -or [int]$result.Training.PersistedReplayEpisodes -ne 0 `
            -or -not [string]::IsNullOrWhiteSpace(
                [string]$result.ModelPackagePath)) {
            throw "Semantic admission rejection did not produce a clean non-resumable worker result."
        }
    }
    elseif ($result.Training.AcceptancePassed) {
        if ($result.CompletionKind -ne "training-accepted" `
            -or $checkpointExists `
            -or $checkpointSnapshots.Count -ne 0 `
            -or $checkpointCatalogExists `
            -or [string]::IsNullOrWhiteSpace(
                [string]$result.ModelPackagePath) `
            -or -not (Test-Path -LiteralPath (
                [string]$result.ModelPackagePath) -PathType Leaf)) {
            throw "Accepted foundation training must remove resume checkpoints."
        }
        $modelPackage = Read-FoundationJson (
            [string]$result.ModelPackagePath)
        if ([int]$modelPackage.SchemaVersion -ne 5 `
            -or [string]$modelPackage.ArtifactKind `
                -ne "aura.foundation-model-package" `
             -or [string]$modelPackage.ModelVersion -ne "5.0.0" `
             -or [string]$modelPackage.DeploymentTier -ne "formal" `
             -or [string]$modelPackage.QualityCertification -ne "passed" `
             -or -not [bool]$modelPackage.SameModelEvidenceBound `
             -or [string]$modelPackage.CapabilityStatus -ne "pass" `
             -or [string]$modelPackage.CompletionKind `
                -ne "training-accepted" `
            -or [string]$modelPackage.JobId -ne [string]$job.JobId `
            -or [string]$modelPackage.RulesetHash `
                -ne [string]$result.RulesetHash `
            -or [string]$modelPackage.ContentSetHash `
                -ne [string]$request.ContentSetHash `
            -or [string]$modelPackage.OwnerModSetHash `
                -ne [string]$request.OwnerModSetHash `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.RoleId) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.PartnerId) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.GameParameterPresetId) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.GameParameterHash) `
            -or @($modelPackage.EnabledRewardCardPackIds |
                Group-Object | Where-Object Count -gt 1).Count -ne 0 `
            -or $null -eq $modelPackage.Acceptance `
            -or -not [bool]$modelPackage.Acceptance.FormalIsolationPassed `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.Acceptance.Classification) `
            -or $null -ne $modelPackage.Model `
            -or $null -eq $modelPackage.ModelArtifact `
            -or [string]::IsNullOrWhiteSpace(
                [string]$modelPackage.ModelArtifact.WeightsFile) `
            -or -not (Test-Path -LiteralPath (Join-Path (
                    Split-Path -Parent ([string]$result.ModelPackagePath)) (
                    [string]$modelPackage.ModelArtifact.WeightsFile)) -PathType Leaf)) {
            throw (
                "Accepted foundation model package is invalid: " `
                + "schema=$($modelPackage.SchemaVersion), " `
                + "kind=$($modelPackage.ArtifactKind), " `
                + "completion=$($modelPackage.CompletionKind), " `
                + "job=$($modelPackage.JobId)/$($job.JobId), " `
                + "ruleset=$($modelPackage.RulesetHash)/$($result.RulesetHash), " `
                + "artifactPresent=$($null -ne $modelPackage.ModelArtifact), " `
                + "path=$($result.ModelPackagePath)")
        }
    }
    elseif ([bool]$result.Training.ExperimentalEligibilityPassed) {
        $expectedExperimentalCompletion = if ([bool]$result.Resumable) {
            "training-experimental-resumable"
        }
        else {
            "training-experimental"
        }
        if ([string]$result.CompletionKind `
                -ne $expectedExperimentalCompletion `
            -or [bool]$result.ModelAccepted `
            -or -not [bool]$result.BusinessModelIncluded `
            -or [string]::IsNullOrWhiteSpace(
                [string]$result.ModelPackagePath) `
            -or -not (Test-Path -LiteralPath (
                [string]$result.ModelPackagePath) -PathType Leaf) `
            -or ([bool]$result.Resumable -and -not $checkpointExists)) {
            throw "Experimental foundation training did not preserve its loadable package and recovery contract."
        }
        $modelPackage = Read-FoundationJson (
            [string]$result.ModelPackagePath)
        if ([int]$modelPackage.SchemaVersion -ne 5 `
            -or [string]$modelPackage.DeploymentTier -ne "experimental" `
            -or [string]$modelPackage.QualityCertification -ne "incomplete" `
            -or -not [bool]$modelPackage.SameModelEvidenceBound `
            -or [string]$modelPackage.CompletionKind `
                -ne $expectedExperimentalCompletion `
            -or [string]$modelPackage.Acceptance.Classification `
                -ne "experimental-runtime-test" `
            -or [int]$modelPackage.Acceptance.SchemaVersion -ne 2 `
            -or -not [bool]$modelPackage.Acceptance.RuntimeSafetyPassed `
            -or $null -ne $modelPackage.Model `
            -or $null -eq $modelPackage.ModelArtifact `
            -or -not (Test-Path -LiteralPath (Join-Path (
                    Split-Path -Parent ([string]$result.ModelPackagePath)) (
                    [string]$modelPackage.ModelArtifact.WeightsFile)) -PathType Leaf)) {
            throw "Experimental foundation model package is invalid."
        }
    }
    elseif ($result.CompletionKind -ne "training-rejected-resumable" `
        -or (-not $checkpointExists `
             -or -not $result.Resumable `
             -or [string]::IsNullOrWhiteSpace([string]$result.CheckpointPath))) {
        throw "Unaccepted foundation training must retain a resumable checkpoint."
    }
    if (-not $PreflightOnly `
        -and -not $semanticRejected `
        -and -not $result.Training.AcceptancePassed `
        -and [bool]$result.Resumable) {
        $checkpointFirstEpisode = Read-FoundationSnapshotFirstRecord `
            $checkpointSnapshotPath
        if ([int]$checkpoint.SchemaVersion -ne $protocolVersion `
            -or [int]$checkpoint.Resume.SchemaVersion -ne $protocolVersion `
            -or [int]$checkpoint.EpisodeSnapshot.StorageVersion -ne 5 `
            -or [int]$checkpoint.EpisodeSnapshot.EpisodeCount -le 0 `
            -or [int]$checkpoint.EpisodeSnapshot.SourceEpisodeCount `
                -lt [int]$checkpoint.EpisodeSnapshot.EpisodeCount `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.EpisodeSnapshot.SourceReplayIdentity) `
            -or @($checkpoint.EpisodeSnapshot.WarehouseReplayKeys).Count -le 0 `
            -or $null -eq $checkpoint.EpisodeSnapshot.FeatureTokenCatalog `
            -or -not $checkpointFirstEpisode.Contains(
                '"CompactStateFeatureTokenIds"') `
            -or -not $checkpointFirstEpisode.Contains(
                '"CompactFeatureTokenIds"') `
            -or $checkpointFirstEpisode.Contains('"StateFeatures"') `
            -or $checkpointFirstEpisode.Contains('"Observation"') `
            -or [int64]$checkpoint.EpisodeSnapshot.Length -ne (
                Get-Item -LiteralPath $checkpointSnapshotPath).Length `
            -or [string]$checkpoint.EpisodeSnapshot.ContentSha256 -ne (
                Get-FoundationSha256 $checkpointSnapshotPath) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.RulesetHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.NativeProgramPackageHash) `
            -or [string]$checkpoint.Resume.Compatibility.ContentSetHash `
                -ne [string]$request.ContentSetHash `
            -or [string]$checkpoint.Resume.Compatibility.OwnerModSetHash `
                -ne [string]$request.OwnerModSetHash `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.TrainingCampaignHash) `
            -or [string]::IsNullOrWhiteSpace(
                [string]$checkpoint.Resume.Compatibility.ValidationCampaignHash) `
            -or [string]$checkpoint.Resume.Compatibility.ActionContractVersion `
                -ne "action-contract-v2" `
            -or [string]$checkpoint.Resume.Compatibility.SemanticGateVersion `
                -ne "semantic-admission-v5-actual-selected-transition" `
            -or [string]$checkpoint.Resume.Compatibility.TrainingSemanticsVersion `
                -ne "decision-input-transition-partitioned-v5-actual-execution-v30" `
            -or [string]$checkpoint.Resume.Compatibility.SearchPolicyVersion `
                -ne "dynamic-search-v15-minimum-duration-enforced" `
            -or [string]$checkpoint.Resume.Compatibility.TrainingPolicyVersion `
                -ne "foundation-governance-v29-source-audit-partitioned-v4") {
            throw "Foundation checkpoint compatibility manifest is incomplete."
        }
    }

    if (-not $PreflightOnly) {
        $archiveLoad = $result.Training.CaseArchiveLoad
        if ([string]$archiveLoad.ProtocolVersion `
                -ne "success-case-archive-worker-v4" `
            -or [string]$archiveLoad.OwnerRuntime -ne ".NET 8 worker" `
            -or [int]$archiveLoad.StorageVersion -ne 4 `
            -or [string]::IsNullOrWhiteSpace(
                [string]$archiveLoad.CompatibilityKey)) {
            throw (
                "Foundation archive v4 load contract failed: " `
                + ($archiveLoad | ConvertTo-Json -Depth 8 -Compress))
        }
        $currentObservation = Get-ChildItem -LiteralPath $archiveRoot `
            -File -Recurse |
            Where-Object {
                $_.Directory.Name -eq "o" `
                -and ($_.Name.EndsWith(".json") `
                      -or $_.Name.EndsWith(".json.gz")) `
                -and $_.FullName.Contains(
                    [System.IO.Path]::DirectorySeparatorChar `
                    + "v4" `
                    + [System.IO.Path]::DirectorySeparatorChar)
            } |
            Select-Object -First 1
        $eligibleCases =
            [int]$result.Training.CaseAnalysis.ArchiveEligibleCases
        if ($eligibleCases -gt 0 -and $null -eq $currentObservation) {
            throw "Foundation worker did not write a v4 observation."
        }
        if ($null -ne $currentObservation) {
            $observation = Read-FoundationJson $currentObservation.FullName
            $expectedObservationPath = Join-Path $archiveRoot (
                "v4\" `
                + ([string]$observation.CompatibilityKey).Substring(0, 16) `
                + "\o\" `
                + ([string]$observation.CaseId).Substring(0, 24) `
                + ".json.gz")
            if ([int]$observation.SchemaVersion -ne 5 `
                -or [string]::IsNullOrWhiteSpace(
                    [string]$observation.CompatibilityKey) `
                -or -not (Test-Path -LiteralPath $expectedObservationPath `
                    -PathType Leaf)) {
                throw (
                    "Foundation archive v4 contract failed: " `
                    + ($archiveLoad | ConvertTo-Json -Depth 8 -Compress))
            }
        }
        if (-not (Test-Path -LiteralPath `
                $result.Training.BuildLimitedSeedIndexPath -PathType Leaf)) {
            throw "Foundation build-limited seed routing index is missing."
        }
        $expertReference = Get-ChildItem -LiteralPath $archiveRoot `
            -Filter "*.json" -File -Recurse |
            Where-Object {
                $_.Directory.Name -eq "e" `
                -and $_.FullName.Contains(
                    [System.IO.Path]::DirectorySeparatorChar `
                    + "v4" `
                    + [System.IO.Path]::DirectorySeparatorChar)
            } |
            Select-Object -First 1
        if ($null -ne $expertReference) {
            $reference = Read-FoundationJson $expertReference.FullName
            $canonicalPath = Join-Path (
                Join-Path $expertReference.Directory.Parent.FullName "c") (
                [string]$reference.CanonicalFileName)
            if ([string]$reference.ProtocolVersion `
                    -ne "success-case-archive-worker-v4" `
                -or [int]$reference.StorageVersion -ne 4 `
                -or [string]::IsNullOrWhiteSpace([string]$reference.CaseId) `
                -or -not (Test-Path -LiteralPath $canonicalPath -PathType Leaf) `
                -or $expertReference.Length -ge (
                    Get-Item -LiteralPath $canonicalPath).Length) {
                throw "Foundation archive v4 expert reference is not deduplicated."
            }
        }
    }

    $autoTuneSummary = if ($ParallelismProfile -eq "auto") {
        ", auto={0}/{1}, cacheHit={2}" -f `
            $result.Training.AutoTune.SelectedParallelism, `
            @($result.Training.AutoTune.Measurements).Count, `
            [bool]$result.Training.AutoTune.CacheHit
    }
    else { "" }
    Write-Host ("Aura foundation trainer smoke passed: campaigns={0}/{1}, battles={2}, semanticSelected={3}/{4}, runtime={5}, execution={6}/{7}, parallel={8}, peak={9}, cpu={10:N1}%, alloc={11:N0}MB/s{12}, executed={13}" -f `
        $result.Training.CompletedCampaigns, `
        $result.Training.RequestedCampaigns, `
        $result.Training.CompletedBattles, `
        $result.Training.SemanticAudit.SelectedInvalidActions, `
        $result.Training.SemanticAudit.SelectedUnexplainedMismatchActions, `
        $result.Runtime, `
        $result.Training.ParallelismProfile, `
        $result.Training.InferenceExecutionMode, `
        $result.Training.EffectiveParallelism, `
        $result.Training.PeakConcurrentCampaigns, `
        $result.Training.CpuUtilizationPercent, `
        $result.Training.AllocationMegabytesPerSecond, `
        $autoTuneSummary, `
        $result.Training.ExecutedCampaigns)
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
