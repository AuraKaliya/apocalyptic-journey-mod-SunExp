param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
& (Join-Path $repoRoot "tools\Build-AuraSharedRuntime.ps1") `
    -Configuration $Configuration `
    -ManagedPath $ManagedPath
$consumers = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification test -DefaultOnly)

foreach ($consumer in $consumers) {
    $projectPath = Resolve-ConsumerPath -RepoRoot $repoRoot -RelativePath ([string]$consumer.projectPath)
    Write-Host "Building test shared runtime consumer: $($consumer.id)"
    dotnet build $projectPath `
        -c $Configuration `
        /p:ManagedPath="$ManagedPath" `
        /p:BuildProjectReferences=false `
        /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Test shared runtime consumer build failed: $($consumer.id)"
    }
}

Write-Host "Test shared runtime consumers built successfully: $($consumers.Count) projects."
