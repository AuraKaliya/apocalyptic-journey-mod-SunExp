param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey",
    [switch]$SkipBuild,
    [switch]$BuildLegacyGoldExp
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

Write-Host "Running archived TestMods validation. This suite is not a product release gate."
if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-TestSharedConsumers.ps1") `
        -Configuration $Configuration `
        -ManagedPath $ManagedPath
}

& (Join-Path $repoRoot "tools\Test-SkinExp.ps1") -RepoRoot $repoRoot

$goldArguments = @{ Configuration = $Configuration }
if ($BuildLegacyGoldExp) {
    $goldArguments.GamePath = $GamePath
}
else {
    $goldArguments.SkipBuild = $true
}
& (Join-Path $repoRoot "tools\Test-GoldExpCSharp.ps1") @goldArguments

Write-Host "Archived TestMods validation passed."
