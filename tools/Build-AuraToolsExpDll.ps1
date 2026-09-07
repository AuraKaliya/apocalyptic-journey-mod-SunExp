param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

& (Join-Path $repoRoot "tools\Build-MainSharedConsumers.ps1") `
    -Configuration $Configuration `
    -ManagedPath $ManagedPath
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp product build transaction failed."
}
