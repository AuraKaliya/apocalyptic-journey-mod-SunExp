param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [switch]$SkipSharedBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force

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
    & (Join-Path $repoRoot "tools\Publish-MainSharedConsumers.ps1") -Configuration $Configuration
}

Write-Host "Main shared runtime consumers built successfully: $($consumers.Count) projects."
