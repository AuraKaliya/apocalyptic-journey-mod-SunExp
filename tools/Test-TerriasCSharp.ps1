param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = "",
    [string]$GamePath = "",
    [switch]$SkipBuild,
    [switch]$KeepTemp
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

if (-not $SkipBuild) {
    & (Join-Path $repoRoot "tools\Build-TerriasDll.ps1") `
        -Configuration $Configuration `
        -ManagedPath $ManagedPath | Out-Host
}

if ($KeepTemp) {
    Write-Warning "-KeepTemp is retained for command compatibility; formal tests no longer create a temporary project."
}

$testProject = Join-Path $repoRoot "Terrias-Dev.Tests\Terrias-Dev.Tests.csproj"
dotnet run --project $testProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Terrias C# tests failed."
}
