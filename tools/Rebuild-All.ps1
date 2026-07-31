param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [switch]$RunTests,
    [switch]$StopRunningFoundationTrainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}
$ManagedPath = [System.IO.Path]::GetFullPath($ManagedPath)
if (-not (Test-Path -LiteralPath $ManagedPath -PathType Container)) {
    throw "Managed reference directory is missing: $ManagedPath"
}
if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not available on PATH."
}

function Invoke-BuildStep {
    param(
        [string]$Name,
        [string]$Script,
        [hashtable]$Arguments
    )

    $scriptPath = Join-Path $PSScriptRoot $Script
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Build script is missing: $scriptPath"
    }

    Write-Host ""
    Write-Host "==> $Name" -ForegroundColor Cyan
    $global:LASTEXITCODE = 0
    & $scriptPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Rebuilding repository deliverables"
Write-Host "Configuration: $Configuration"
Write-Host "Managed references: $ManagedPath"

Invoke-BuildStep `
    -Name "Main MOD assemblies" `
    -Script "Build-MainSharedConsumers.ps1" `
    -Arguments @{
        Configuration = $Configuration
        ManagedPath = $ManagedPath
    }

Invoke-BuildStep `
    -Name "External foundation trainer" `
    -Script "Build-AuraFoundationTrainer.ps1" `
    -Arguments @{
        Configuration = $Configuration
        StopRunningTrainer = $StopRunningFoundationTrainer
    }

if ($RunTests) {
    Invoke-BuildStep `
        -Name "AuraToolsExp regression tests" `
        -Script "Test-AuraToolsExp.ps1" `
        -Arguments @{
            Configuration = $Configuration
        }
    Invoke-BuildStep `
        -Name "Foundation trainer smoke test" `
        -Script "Test-AuraFoundationTrainer.ps1" `
        -Arguments @{
            Configuration = $Configuration
            SkipPublish = $true
        }
}

$expectedOutputs = @(
    "Terrias\Scripts\Entry.dll",
    "Terrias\Scripts\Aura.Shared.dll",
    "SanGuoShaExp\Scripts\Entry.dll",
    "SanGuoShaExp\Scripts\Aura.Shared.dll",
    "AuraToolsExp\Scripts\Entry.dll",
    "AuraToolsExp\Scripts\Aura.Shared.dll",
    "AuraToolsExp\TrainingWorker\AuraFoundationTrainer.Worker.exe",
    "AuraToolsExp\TrainingWorker\AuraFoundationTrainer.ControlCenter.exe"
)

$artifacts = foreach ($relativePath in $expectedOutputs) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected build output is missing: $path"
    }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -le 0) {
        throw "Expected build output is empty: $path"
    }
    [pscustomobject]@{
        Artifact = $relativePath
        SizeMB = [Math]::Round($file.Length / 1MB, 2)
    }
}

Write-Host ""
Write-Host "All repository deliverables rebuilt successfully." `
    -ForegroundColor Green
$artifacts | Format-Table -AutoSize
