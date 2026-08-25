param(
    [string]$Configuration = "Release",
    [string]$MatrixPath = "",
    [string]$Profile = "",
    [string[]]$Tag = @(),
    [string[]]$StepId = @(),
    [switch]$List
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($MatrixPath)) {
    $MatrixPath = Join-Path $repoRoot "tools\shared-release-matrix.json"
}
[object]$matrixDocument = Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json
$consumerManifestProperty = $matrixDocument.PSObject.Properties["consumerManifest"]
if ($null -eq $consumerManifestProperty -or [string]::IsNullOrWhiteSpace([string]$consumerManifestProperty.Value)) {
    throw "Shared release matrix must declare consumerManifest."
}
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
[void](Get-SharedConsumerManifest `
    -RepoRoot $repoRoot `
    -ManifestPath (Join-Path $repoRoot ([string]$consumerManifestProperty.Value)))
Import-Module (Join-Path $repoRoot "tools\modules\TestMatrixRunner.psm1") -Force
Invoke-TestMatrix `
    -RepoRoot $repoRoot `
    -MatrixPath $MatrixPath `
    -Configuration $Configuration `
    -Profile $Profile `
    -Tag $Tag `
    -StepId $StepId `
    -List:$List
