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
Import-Module (Join-Path $repoRoot "tools\modules\TestMatrixRunner.psm1") -Force
Invoke-TestMatrix `
    -RepoRoot $repoRoot `
    -MatrixPath $MatrixPath `
    -Configuration $Configuration `
    -Profile $Profile `
    -Tag $Tag `
    -StepId $StepId `
    -List:$List
