param(
    [Parameter(Mandatory = $true)]
    [string[]]$ResultPath,

    [Parameter(Mandatory = $true)]
    [string]$JobPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$RulesetPath = "",

    [string]$CampaignPath = "",

    [ValidateRange(1, 512)]
    [int]$CampaignsPerDifficulty = 64,

    [UInt64]$SeedStart = 3000000,

    [ValidateRange(1, 256)]
    [int]$Parallelism = [Math]::Max(
        1,
        [Math]::Min(16, [Environment]::ProcessorCount)),

    [switch]$ReuseCompletedPairs
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$labProject = Join-Path $PSScriptRoot `
    "AuraFoundationChampionLab\AuraFoundationChampionLab.csproj"

if ($ResultPath.Count -lt 2 -or $ResultPath.Count -gt 8) {
    throw "Frozen tournament requires between 2 and 8 worker results."
}
if (-not (Test-Path -LiteralPath $labProject -PathType Leaf)) {
    throw "Champion lab project is missing: $labProject"
}

$resolvedJob = (Resolve-Path -LiteralPath $JobPath).Path
$resolvedResults = @($ResultPath | ForEach-Object {
    (Resolve-Path -LiteralPath $_).Path
})
if (($resolvedResults | Select-Object -Unique).Count -ne `
        $resolvedResults.Count) {
    throw "Tournament result paths must be distinct."
}

$models = for ($index = 0; $index -lt $resolvedResults.Count; $index++) {
    $result = Get-Content -LiteralPath $resolvedResults[$index] -Raw |
        ConvertFrom-Json
    $champion = $result.Training.Champion
    if ($null -eq $champion) {
        throw "Worker result has no champion: $($resolvedResults[$index])"
    }
    $compatibility = $result.Training.Compatibility
    [pscustomobject]@{
        Index = $index
        Label = "M{0:D2}" -f ($index + 1)
        ResultPath = $resolvedResults[$index]
        JobId = [string]$result.JobId
        ModelId = [string]$champion.ModelId
        RulesetHash = [string]$result.RulesetHash
        NativeProgramPackageHash =
            [string]$compatibility.NativeProgramPackageHash
        TrainingSemanticsVersion =
            [string]$compatibility.TrainingSemanticsVersion
        FeatureSchemaVersion = [int]$champion.FeatureSchemaVersion
        ExclusiveWins = 0
        ExclusiveLosses = 0
        ConclusivePairWins = 0
        ConclusivePairLosses = 0
        Victories = 0
        CampaignRuns = 0
        CompletedBattles = 0L
        InvalidRuns = 0
    }
}

$compatibilityKeys = @($models | ForEach-Object {
    "{0}|{1}|{2}|{3}" -f `
        $_.RulesetHash,
        $_.NativeProgramPackageHash,
        $_.TrainingSemanticsVersion,
        $_.FeatureSchemaVersion
} | Select-Object -Unique)
if ($compatibilityKeys.Count -ne 1) {
    throw "Tournament models are not training/runtime compatible."
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

& dotnet build $labProject -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Champion lab build failed with exit code $LASTEXITCODE."
}

$pairs = [Collections.Generic.List[object]]::new()
for ($left = 0; $left -lt $models.Count - 1; $left++) {
    for ($right = $left + 1; $right -lt $models.Count; $right++) {
        $pairName = "pair-{0:D2}-{1:D2}" -f ($left + 1), ($right + 1)
        $pairDirectory = Join-Path $resolvedOutput $pairName
        $arguments = @(
            "run", "--project", $labProject,
            "-c", "Release", "--no-build", "--",
            "--result-a", $models[$left].ResultPath,
            "--result-b", $models[$right].ResultPath,
            "--job", $resolvedJob,
            "--output", $pairDirectory,
            "--campaigns", $CampaignsPerDifficulty,
            "--seed-start", $SeedStart,
            "--parallelism", $Parallelism
        )
        if (-not [string]::IsNullOrWhiteSpace($RulesetPath)) {
            $arguments += @(
                "--ruleset",
                (Resolve-Path -LiteralPath $RulesetPath).Path)
        }
        if (-not [string]::IsNullOrWhiteSpace($CampaignPath)) {
            $arguments += @(
                "--campaign",
                (Resolve-Path -LiteralPath $CampaignPath).Path)
        }

        $reportPath = Join-Path $pairDirectory "champion-ab-report.json"
        $reuseReport = $false
        if ($ReuseCompletedPairs -and
            (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            $candidateReport = Get-Content -LiteralPath $reportPath -Raw |
                ConvertFrom-Json
            $expectedPairCount = 2 * $CampaignsPerDifficulty
            $reuseReport = [bool]$candidateReport.Deterministic `
                -and [int]$candidateReport.CampaignsPerDifficulty -eq `
                    $CampaignsPerDifficulty `
                -and [UInt64]$candidateReport.SeedStart -eq $SeedStart `
                -and [int]$candidateReport.EffectiveParallelism -eq `
                    $Parallelism `
                -and @($candidateReport.Pairs).Count -eq `
                    $expectedPairCount `
                -and [IO.Path]::GetFullPath(
                    [string]$candidateReport.ChampionA.SourceResultPath) -eq `
                    $models[$left].ResultPath `
                -and [IO.Path]::GetFullPath(
                    [string]$candidateReport.ChampionB.SourceResultPath) -eq `
                    $models[$right].ResultPath
            if ($reuseReport) {
                Write-Host "Reusing completed $pairName."
            }
        }
        if (-not $reuseReport) {
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) {
                throw "$pairName failed determinism or execution checks."
            }
        }

        $report = Get-Content -LiteralPath $reportPath -Raw |
            ConvertFrom-Json
        if (-not [bool]$report.Deterministic) {
            throw "$pairName did not reproduce the frozen seed."
        }

        $models[$left].ExclusiveWins += [int]$report.ChampionAOnlyWins
        $models[$left].ExclusiveLosses += [int]$report.ChampionBOnlyWins
        $models[$right].ExclusiveWins += [int]$report.ChampionBOnlyWins
        $models[$right].ExclusiveLosses += [int]$report.ChampionAOnlyWins
        if ([string]$report.Verdict -eq "champion-a") {
            $models[$left].ConclusivePairWins++
            $models[$right].ConclusivePairLosses++
        }
        elseif ([string]$report.Verdict -eq "champion-b") {
            $models[$right].ConclusivePairWins++
            $models[$left].ConclusivePairLosses++
        }
        foreach ($run in $report.Pairs) {
            $models[$left].CampaignRuns++
            $models[$right].CampaignRuns++
            $models[$left].Victories += [int][bool]$run.ChampionAVictory
            $models[$right].Victories += [int][bool]$run.ChampionBVictory
            $models[$left].InvalidRuns += [int][bool]$run.ChampionAInvalid
            $models[$right].InvalidRuns += [int][bool]$run.ChampionBInvalid
            $models[$left].CompletedBattles +=
                [long]$run.ChampionACompletedBattles
            $models[$right].CompletedBattles +=
                [long]$run.ChampionBCompletedBattles
        }
        $pairs.Add([pscustomobject]@{
            Left = $models[$left].Label
            Right = $models[$right].Label
            Verdict = [string]$report.Verdict
            LeftOnlyWins = [int]$report.ChampionAOnlyWins
            RightOnlyWins = [int]$report.ChampionBOnlyWins
            RightWinWilsonLowerBound =
                [double]$report.ChampionBWinWilsonLowerBound
            RightWinWilsonUpperBound =
                [double]$report.ChampionBWinWilsonUpperBound
            InvalidPairs = [int]$report.InvalidPairs
            ReportPath = $reportPath
        })
    }
}

$ranking = @($models | ForEach-Object {
    [pscustomobject]@{
        Label = $_.Label
        Rank = 0
        ModelId = $_.ModelId
        ResultPath = $_.ResultPath
        ConclusivePairWins = $_.ConclusivePairWins
        ConclusivePairLosses = $_.ConclusivePairLosses
        ExclusiveWins = $_.ExclusiveWins
        ExclusiveLosses = $_.ExclusiveLosses
        VictoryRate = if ($_.CampaignRuns -le 0) { 0.0 } else {
            [double]$_.Victories / $_.CampaignRuns
        }
        AverageCompletedBattles = if ($_.CampaignRuns -le 0) { 0.0 } else {
            [double]$_.CompletedBattles / $_.CampaignRuns
        }
        InvalidRuns = $_.InvalidRuns
    }
} | Sort-Object `
    @{ Expression = "InvalidRuns"; Ascending = $true },
    @{ Expression = "ConclusivePairWins"; Descending = $true },
    @{ Expression = { $_.ExclusiveWins - $_.ExclusiveLosses }; Descending = $true },
    @{ Expression = "VictoryRate"; Descending = $true },
    @{ Expression = "AverageCompletedBattles"; Descending = $true },
    @{ Expression = "Label"; Ascending = $true })
for ($index = 0; $index -lt $ranking.Count; $index++) {
    $ranking[$index].Rank = $index + 1
}

$manifest = [ordered]@{
    ProtocolVersion = "foundation-frozen-tournament-v1"
    CreatedUtc = [DateTime]::UtcNow.ToString("o")
    DiagnosticOnly = $true
    AutomaticallyActivatesModel = $false
    JobPath = $resolvedJob
    CompatibilityKey = $compatibilityKeys[0]
    CampaignsPerDifficulty = $CampaignsPerDifficulty
    SeedStart = $SeedStart
    EffectiveParallelism = $Parallelism
    Difficulties = @("normal", "advanced")
    Models = @($models | Select-Object `
        Label, ModelId, JobId, ResultPath, RulesetHash,
        NativeProgramPackageHash, TrainingSemanticsVersion,
        FeatureSchemaVersion)
    Pairs = @($pairs)
    Ranking = $ranking
    ProvisionalWinner = if ($ranking.Count -gt 0 `
            -and $ranking[0].InvalidRuns -eq 0) {
        $ranking[0].Label
    } else { "" }
}
$manifestPath = Join-Path $resolvedOutput `
    "foundation-frozen-tournament-v1.json"
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 12),
    [Text.UTF8Encoding]::new($false))

Write-Host "Frozen tournament complete."
Write-Host "Manifest: $manifestPath"
Write-Host "Provisional winner: $($manifest.ProvisionalWinner)"
