param(
    [string]$Configuration = "Release",
    [ValidateRange(0, 10000)]
    [int]$IntegritySweepCampaigns = 64
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraToolsExp.NativeReward.Tests\AuraToolsExp.NativeReward.Tests.csproj"
$campaign = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-world-simulation-v2.campaign.json"
$ruleset = Join-Path $root "AuraToolsExp\Config\combat-simulation\witch-base-evaluation-v2.ruleset.json"

& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Native reward test build failed with exit code $LASTEXITCODE."
}

$testDll = Join-Path $root (
    "AuraToolsExp.NativeReward.Tests\bin\" +
    "$Configuration\net8.0\AuraToolsExp.NativeReward.Tests.dll")
& dotnet $testDll $campaign $ruleset $IntegritySweepCampaigns
if ($LASTEXITCODE -ne 0) {
    throw "Native reward tests failed with exit code $LASTEXITCODE."
}
