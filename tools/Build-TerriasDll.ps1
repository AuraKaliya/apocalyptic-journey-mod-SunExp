param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    if ([string]::IsNullOrWhiteSpace($GamePath)) {
        $ManagedPath = Join-Path $repoRoot "Managed"
    }
    else {
        $ManagedPath = Join-Path $GamePath "Witch's Apocalyptic Journey_Data\Managed"
    }
}

& (Join-Path $repoRoot "tools\Build-MainSharedConsumers.ps1") `
    -Configuration $Configuration `
    -ManagedPath $ManagedPath
if ($LASTEXITCODE -ne 0) {
    throw "Terrias product build transaction failed."
}
