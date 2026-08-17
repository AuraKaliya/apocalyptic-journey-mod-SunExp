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

$modConfig = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\ModConfig.json") | ConvertFrom-Json
if ([string]$modConfig.ModVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$' `
        -or $modConfig.MustSame -ne $false) {
    throw "AuraToolsExp must use semantic release metadata without a global exact-version lobby gate."
}

$protocolManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\protocol.compatibility.json") |
    ConvertFrom-Json
$requiredProtocolFeatures = @{
    "multiplayer.mod-sync" = @(1, 2)
    "presentation.pixel-emoji" = @(2, 2)
    "records.damage-meter" = @(4, 4)
    "presentation.damage-settlement-cg" = @(1, 1)
    "records.match-replay" = @(8, 8)
}
if ($protocolManifest.schemaVersion -ne 1 `
        -or $protocolManifest.releaseBaseline -ne $modConfig.ModVersion `
        -or $protocolManifest.globalExactModVersionRequired -ne $false) {
    throw "AuraToolsExp protocol inventory must disable the global exact-version gate."
}
foreach ($entry in $requiredProtocolFeatures.GetEnumerator()) {
    $feature = @($protocolManifest.features | Where-Object id -eq $entry.Key)
    if ($feature.Count -ne 1 `
            -or [int]$feature[0].minimumSupportedVersion -ne $entry.Value[0] `
            -or [int]$feature[0].currentVersion -ne $entry.Value[1] `
            -or @($feature[0].requiredCapabilities).Count -eq 0 `
            -or [string]::IsNullOrWhiteSpace([string]$feature[0].fallback)) {
        throw "AuraToolsExp protocol inventory is incomplete or invalid: $($entry.Key)"
    }
}
$damageProtocol = @($protocolManifest.features | Where-Object id -eq "records.damage-meter")[0]
if ([int]$damageProtocol.minimumReadableVersion -ne 3) {
    throw "AuraToolsExp damage-meter protocol inventory must distinguish v4 live networking from v3 persisted-data migration."
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
if ($matchSettings.schemaVersion -ne 30 `
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
if ($matchSettings.matchRecords.enabled -ne $false `
        -or $matchSettings.matchRecords.statistics.enabled -ne $true `
        -or $matchSettings.matchRecords.statistics.displayMode -ne "Table" `
        -or $matchSettings.matchRecords.statistics.displayScope -ne "Fight" `
        -or $matchSettings.matchRecords.statistics.teamFilter -ne "All" `
        -or $matchSettings.matchRecords.statistics.captureTeamAvatars -ne $false `
        -or $matchSettings.matchRecords.statistics.uiRefreshIntervalMs -ne 1000 `
        -or $matchSettings.matchRecords.statistics.submitBatchIntervalMs -ne 250 `
        -or $matchSettings.matchRecords.statistics.maxEventsPerBatch -ne 24 `
        -or $matchSettings.matchRecords.replay.enabled -ne $false `
        -or $matchSettings.matchRecords.replay.autoRecordLimit -ne 20 `
        -or $matchSettings.matchRecords.replay.chunkTargetBytes -ne 262144) {
    throw "AuraToolsExp match-record shipped defaults are invalid."
}

$rootSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\AuraTools.json") | ConvertFrom-Json
$audioSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\AudioSettings.json") | ConvertFrom-Json
$pixelEmojiSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\PixelEmojiSettings.json") | ConvertFrom-Json
if ($audioSettings.schemaVersion -ne 3 `
        -or $null -ne $audioSettings.audioSystemVersion `
        -or $rootSettings.pixelEmoji.configFile -ne "PixelEmojiSettings.json" `
        -or $pixelEmojiSettings.schemaVersion -ne 1 `
        -or $pixelEmojiSettings.enabled -ne $false `
        -or $pixelEmojiSettings.syncRemote -ne $true `
        -or $pixelEmojiSettings.maxFavorites -ne 64) {
    throw "AuraToolsExp pixel emoji bundled config defaults drifted."
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

$moduleSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolsBuiltInModules.cs")
$moduleIdSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleIds.cs")
$expectedModuleIds = @(
    "gameplay.starter-deck",
    "gameplay.card-refresh",
    "gameplay.feast",
    "gameplay.safe-box",
    "presentation.skin",
    "presentation.battle-bgm",
    "presentation.card-use-audio",
    "presentation.pixel-emoji",
    "presentation.skill-cg",
    "presentation.card-use-cg",
    "records.damage-statistics",
    "records.battle-replay",
    "multiplayer.mod-sync",
    "intelligence.auto-battle",
    "system.file-logging"
)
foreach ($moduleId in $expectedModuleIds) {
    if ($moduleIdSource -notmatch [regex]::Escape('"' + $moduleId + '"')) {
        throw "AuraToolsExp built-in module catalog is missing: $moduleId"
    }
}

$entrySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Entry.cs")
if ($entrySource -notmatch "AuraToolModuleHost\.Initialize" `
        -or $entrySource -match "AuraTools(?:Audio|AutoBattle|CardRefresh|DamageMeter|CardUiBenchmark|Feast|FileLog|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)Runtime\.Initialize") {
    throw "AuraToolsExp Entry must compose feature initialization through AuraToolModuleHost."
}

$settingsSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs")
$shellSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxSettingsShell.cs")
if ($settingsSource -notmatch "ToolboxSettingsShell\.Build\(panel\)" `
        -or @($settingsSource -split "`n").Count -gt 900 `
        -or $settingsSource -match "AuraToolsConfigService" `
        -or $settingsSource -match "using\s+AuraToolsExp\.Dll\.Features\.(?:Audio|AutoBattle|CardRefresh|DamageMeter|Diagnostics|Feast|Logging|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)" `
        -or $settingsSource -match "Show(?:Audio|StarterDeck|Replay|AutoBattle|Logging)Settings" `
        -or $settingsSource -match "AutoInstallBundledSkins\s*=\s*true" `
        -or $settingsSource -match "PreferRoleModProfile\s*=\s*true" `
        -or $settingsSource -match "feast\.PlayCg\s*=\s*true" `
        -or $shellSource -match "using\s+AuraToolsExp\.Dll\.Features\.(?:Audio|AutoBattle|CardRefresh|DamageMeter|Diagnostics|Feast|Logging|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)") {
    throw "AuraToolsExp toolbox shell boundary or render-purity contract is invalid."
}

$moduleSettingsPages = @(
    "AuraToolsExp-Dev\Features\Audio\AuraToolsAudioSettingsPage.cs",
    "AuraToolsExp-Dev\Features\StarterDeck\AuraToolsStarterDeckSettingsPage.cs",
    "AuraToolsExp-Dev\Features\MatchRecords\AuraToolsReplaySettingsPage.cs",
    "AuraToolsExp-Dev\Features\Logging\AuraToolsLoggingSettingsPage.cs",
    "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSettingsPage.cs"
)
foreach ($relativePage in $moduleSettingsPages) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relativePage) -PathType Leaf)) {
        throw "AuraToolsExp feature-owned settings page is missing: $relativePage"
    }
}
if ($moduleSource -match "AuraToolsSettingsRuntime" `
        -or $moduleSource -notmatch "AuraToolsAudioSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsStarterDeckSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsReplaySettingsPage" `
        -or $moduleSource -notmatch "AuraToolsLoggingSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsAutoBattleSettingsPage") {
    throw "AuraToolsExp built-in modules must route to feature-owned settings pages."
}

$configSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsConfigService.cs")
$moduleConfigSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolModuleConfig.cs")
foreach ($saveMethod in @(
        "SaveBattleBgm",
        "SaveCardUseAudio",
        "SaveStarterDeck",
        "SaveCardRefresh",
        "SaveFeast",
        "SaveSafeBox",
        "SaveModSync",
        "SaveDamageStatistics",
        "SaveBattleReplay",
        "SaveAutoBattle",
        "SavePixelEmoji",
        "SaveSkillCg",
        "SaveCardUseCg",
        "SaveSkin",
        "SaveLogging")) {
    if ($configSource -notmatch ("public static void " + $saveMethod + "\s*\(")) {
        throw "AuraToolsExp module-scoped config save is missing: $saveMethod"
    }
}
if ($moduleConfigSource -notmatch 'ConfigSystem = "AuraTools\.Modules"' `
        -or $moduleConfigSource -notmatch "AuraToolConfigChangeBus" `
        -or $moduleConfigSource -notmatch "AuraToolModuleConfigDocument") {
    throw "AuraToolsExp module config store, document, or change bus contract is invalid."
}

$consumerConfigSource = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features") `
        -Recurse -File -Filter "*.cs"
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Modules") `
        -Recurse -File -Filter "*.cs"
) | ForEach-Object { Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName }
$consumerConfigText = $consumerConfigSource -join "`n"
if ($consumerConfigText -match "AuraToolsConfigService\.(?:Changed|AudioChanged|MatchExperienceChanged|LoggingChanged)\s*\+=" `
        -or $consumerConfigText -match "AuraToolsConfigService\.(?:SaveAudio|SaveMatchExperience)\s*\(" `
        -or $consumerConfigText -match "AuraToolsConfigService\.Root\.(?:Audio|MatchExperience|PixelEmoji|SkillCg|Skin|Logging)\.Enabled") {
    throw "AuraToolsExp feature consumers must use module-scoped config saves, subscriptions, and enablement."
}

$uiSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsUi.cs")
$viewStateSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraUiShared\AuraUiViewState.cs")
if ($uiSource -notmatch "SetIsOnWithoutNotify" `
        -or $viewStateSource -notmatch "AnchorId" `
        -or $viewStateSource -notmatch "FocusedId" `
        -or $viewStateSource -notmatch "AuraUiKeyedListReconciler") {
    throw "AuraToolsExp stable toggle, scroll-anchor, focus, or keyed-list contract is invalid."
}

$toolingProtocolSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolingShared\AuraToolExtensionProtocol.cs")
$extensionAdapterSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolSharedExtensionAdapter.cs")
$moduleHostSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleHost.cs")
if ($toolingProtocolSource -notmatch "CurrentVersion = 1" `
        -or $toolingProtocolSource -notmatch "MinimumSupportedVersion = 1" `
        -or $toolingProtocolSource -notmatch "ownerModId" `
        -or $toolingProtocolSource -notmatch "RegistrationHandle" `
        -or $extensionAdapterSource -notmatch "IAuraToolExtensionProvider" `
        -or $moduleHostSource -notmatch "AuraToolExtensionRegistry\.Changed" `
        -or $shellSource -notmatch 'Id = "extensions"') {
    throw "AuraToolsExp shared tooling extension protocol or dynamic catalog integration is invalid."
}

Write-Host "AuraToolsExp behavior and Tool-owned content tests passed."
