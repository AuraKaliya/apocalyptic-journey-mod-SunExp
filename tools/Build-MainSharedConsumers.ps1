param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [switch]$SkipSharedBuild,
    [switch]$SkipPublish,
    [string]$InputSnapshotPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
Import-Module (Join-Path $repoRoot "tools\modules\AuraReleaseInputs.psm1") -Force
$releaseMutex = Enter-AuraReleaseLock -RepoRoot $repoRoot
try {
if (-not $SkipPublish -or -not [string]::IsNullOrWhiteSpace($InputSnapshotPath)) {
    if ([string]::IsNullOrWhiteSpace($InputSnapshotPath)) {
        $InputSnapshotPath = Join-Path $repoRoot "artifacts/shared-release/$Configuration/build-input.json"
        $null = New-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $InputSnapshotPath
    } else { $null = Assert-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $InputSnapshotPath }
}

if (-not $SkipSharedBuild) {
    & (Join-Path $repoRoot "tools\Build-AuraSharedRuntime.ps1") `
        -Configuration $Configuration `
        -ManagedPath $ManagedPath
}

$consumers = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly)

foreach ($consumer in $consumers) {
    $projectPath = Resolve-ConsumerPath -RepoRoot $repoRoot -RelativePath ([string]$consumer.projectPath)
    Write-Host "Building main shared runtime consumer: $($consumer.id)"
    dotnet build $projectPath `
        -c $Configuration `
        /p:ManagedPath="$ManagedPath" `
        /p:BuildProjectReferences=false `
        /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Main shared runtime consumer build failed: $($consumer.id)"
    }
}

if (-not $SkipPublish) {
    $null = Assert-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $InputSnapshotPath
    & (Join-Path $repoRoot "tools\Publish-MainSharedConsumers.ps1") -Configuration $Configuration -InputSnapshotPath $InputSnapshotPath
} elseif (-not [string]::IsNullOrWhiteSpace($InputSnapshotPath)) {
    $null = Assert-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $InputSnapshotPath
}

Write-Host "Main shared runtime consumers built successfully: $($consumers.Count) projects."

} finally { $releaseMutex.ReleaseMutex(); $releaseMutex.Dispose() }
