param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [switch]$IncludeTestPrototypes
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

& (Join-Path $repoRoot "tools\Build-MainSharedConsumers.ps1") -Configuration $Configuration -ManagedPath $ManagedPath
if ($IncludeTestPrototypes) {
    & (Join-Path $repoRoot "tools\Build-TestSharedConsumers.ps1") -Configuration $Configuration -ManagedPath $ManagedPath
}

Write-Host "Shared runtime consumers built successfully."
