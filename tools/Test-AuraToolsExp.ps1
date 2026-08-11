param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp-Dev.Tests\AuraToolsExp-Dev.Tests.csproj"
$skinModule = Join-Path $repoRoot "tools\modules\SkinPackageValidation.psm1"
$bundledModelIntegration = Join-Path $repoRoot (
    "tools\Test-AuraToolsBundledModelRegistrationIntegration.ps1")

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraToolsExp behavior test project is missing: $project"
}

& dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp behavior tests failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $bundledModelIntegration -PathType Leaf)) {
    throw "AuraToolsExp bundled-model integration test is missing: $bundledModelIntegration"
}
& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $bundledModelIntegration `
    -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp bundled-model integration failed with exit code $LASTEXITCODE."
}

Import-Module $skinModule -Force
$skinValidation = Test-SkinPackageContent -PackagePath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\Skins\package.json")
if ($skinValidation.Package.packageId -ne "AuraToolsExp.BundledSkins" `
        -or $skinValidation.ParticipantKind -ne "Tool") {
    throw "AuraToolsExp bundled skin package identity or Tool ownership is invalid."
}

$officialSummerSkins = @($skinValidation.Skins | Where-Object {
    $_.TargetCareerId -eq "career_1" `
        -and $_.SkinId -eq "AuraToolsExp.career_1.summer_cool"
})
if ($officialSummerSkins.Count -ne 1) {
    throw "AuraToolsExp must publish its official career_1 summer skin exactly once."
}

$matchSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\MatchExperienceSettings.json") | ConvertFrom-Json
$standardPreset = @($matchSettings.autoBattle.gameParameters.presets | Where-Object id -eq "standard")
if ($matchSettings.schemaVersion -ne 28 `
        -or $matchSettings.autoBattle.experimentalModelAcknowledgement -ne "" `
        -or $matchSettings.cardRefresh.enabled -ne $false `
        -or $matchSettings.autoBattle.enabled -ne $false `
        -or $matchSettings.autoBattle.training.preset -ne "steady" `
        -or $matchSettings.autoBattle.simulation.scenarioId -ne "witch.world-simulation.standard-v2" `
        -or $matchSettings.autoBattle.simulation.difficultyId -ne "normal" `
        -or $standardPreset.Count -ne 1 `
        -or $standardPreset[0].partnerId -ne "Partner_10001" `
        -or $standardPreset[0].preferredDeckSizeMinimum -ne 15) {
    throw "AuraToolsExp match-experience configuration contract is invalid."
}
if ($matchSettings.damageMeter.showPanelByDefault -ne $false `
        -or $matchSettings.damageMeter.loadHistoryOnStartup -ne $false `
        -or $matchSettings.damageMeter.captureTeamAvatars -ne $false `
        -or $matchSettings.damageMeter.uiRefreshIntervalMs -ne 1000 `
        -or $matchSettings.damageMeter.submitBatchIntervalMs -ne 250 `
        -or $matchSettings.damageMeter.maxEventsPerBatch -ne 24) {
    throw "AuraToolsExp damage-meter shipped defaults are invalid."
}

$loggingSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\LoggingSettings.json") | ConvertFrom-Json
if ($loggingSettings.schemaVersion -ne 4 `
        -or $loggingSettings.minimumLevel -ne "Info" `
        -or $loggingSettings.performanceDiagnostics -ne $false `
        -or $loggingSettings.mirrorUnityLog -ne $false `
        -or $loggingSettings.mirrorCommandsLog -ne $false) {
    throw "AuraToolsExp logging configuration contract is invalid."
}

$skillCgSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\SkillCgSettings.json") | ConvertFrom-Json
if ($skillCgSettings.schemaVersion -ne 3 `
        -or $skillCgSettings.disableAfterFailures -ne $true `
        -or $null -ne $skillCgSettings.PSObject.Properties["preloadOnFightStart"]) {
    throw "AuraToolsExp Skill CG configuration contract is invalid."
}

$registration = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\aura.registration.json") | ConvertFrom-Json
$cgRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\cg.registry.json") | ConvertFrom-Json
$officialSkillCg = @($cgRegistry.entries | Where-Object {
    $_.kind -eq "skill" -and $_.cgId -in @(
        "official.career_1.careercard_1",
        "official.career_3.careercard_4")
})
if ($registration.schemaVersion -ne 4 `
        -or $registration.ownerModId -ne "AuraToolsExp" `
        -or $registration.participantKind -ne "Tool" `
        -or $cgRegistry.ownerModId -ne "AuraToolsExp" `
        -or $officialSkillCg.Count -ne 2) {
    throw "AuraToolsExp shared resource and CG ownership contract is invalid."
}

Write-Host "AuraToolsExp behavior and Tool-owned content tests passed."
