param(
    [string]$Configuration = "Release",
    [string]$MatrixPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($MatrixPath)) {
    $MatrixPath = Join-Path $repoRoot "tools\shared-release-matrix.json"
}

if (-not (Test-Path -LiteralPath $MatrixPath -PathType Leaf)) {
    throw "Shared release matrix is missing: $MatrixPath"
}

$matrix = Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json
if ($matrix.schemaVersion -ne 1) {
    throw "Unsupported shared release matrix schemaVersion: $($matrix.schemaVersion)"
}

foreach ($step in $matrix.steps) {
    if ($step.enabled -eq $false) {
        Write-Host "Skipping shared release step: $($step.id)"
        continue
    }

    if ($step.kind -ne "script") {
        throw "Unsupported shared release step kind: $($step.kind)"
    }

    $script = Join-Path $repoRoot $step.path
    if (-not (Test-Path -LiteralPath $script -PathType Leaf)) {
        throw "Shared release step script is missing: $($step.id) -> $script"
    }

    Write-Host "Running shared release step: $($step.id)"
    if ($step.path -match "Test-AuraSharedCore|Test-MainSharedFramework") {
        & $script -Configuration $Configuration
    }
    else {
        & $script
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Shared release step failed: $($step.id)"
    }
}

Write-Host "Shared release gate passed: $($matrix.name)"
