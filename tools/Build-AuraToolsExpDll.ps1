param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [switch]$StopRunningFoundationTrainer
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"

if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

dotnet build $project `
    -c $Configuration `
    /p:ManagedPath="$ManagedPath" `
    /p:BuildFoundationTrainer=true `
    /p:StopRunningFoundationTrainer=$([bool]$StopRunningFoundationTrainer) `
    /v:minimal
