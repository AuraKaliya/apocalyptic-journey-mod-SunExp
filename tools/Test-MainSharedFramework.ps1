param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $repoRoot "tools\Test-AuraSharedCore.ps1") -Configuration $Configuration
& (Join-Path $repoRoot "tools\Build-MainSharedConsumers.ps1") -Configuration $Configuration

$mainProjects = @(
    "SunExp-Dev\SunExp.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
)

foreach ($project in $mainProjects) {
    $projectPath = Join-Path $repoRoot $project
    $text = Get-Content -Raw -LiteralPath $projectPath
    if (-not $text.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
        throw "Main shared consumer must reference Aura.Shared instead of linked shared source: $project"
    }

    if ($text -match 'Compile Include="[^"]*(AuraSharedCore|AuraAudioShared|AuraLogShared|AuraJourneyShared|AuraSkinShared|AudioArbiterShared|BattleBgmArbiterShared|StarterDeckArbiterShared|UiRaycastSafetyShared|UiTransitionGuardShared|AuraCgShared|AuraOnlineShared)') {
        throw "Main shared consumer still links shared source directly: $project"
    }
}

$auraToolsProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj")
if (-not $auraToolsProject.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
    throw "AuraToolsExp service surface must come from Aura.Shared."
}

$auraToolsStarterDeckRuntime = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\StarterDeck\AuraToolsStarterDeckRuntime.cs")
$auraToolsUi = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsUi.cs")
$auraToolsSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs")
$auraToolsConfigModels = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsConfigModels.cs")
$auraToolsSkinRuntime = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\Skin\AuraToolsSkinRuntime.cs")

if (-not ($auraToolsStarterDeckRuntime.Contains("IsWorldSimulationRun"))) {
    throw "AuraTools starter deck must guard application to confirmed World Simulation runs."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("!IsWorldSimulationRun()"))) {
    throw "AuraTools starter deck must skip non-World-Simulation role initialization."
}
if (-not ($auraToolsStarterDeckRuntime.Contains('"GameEntryUI.StartGame"')) -or -not ($auraToolsStarterDeckRuntime.Contains("ApplyStarterDeckBeforeGameStart"))) {
    throw "AuraTools starter deck must apply the host deck immediately before GameEntryUI.StartGame."
}
if (-not ($auraToolsStarterDeckRuntime.Contains('"PlayerManager.CmdSyncRoleTable"')) -or -not ($auraToolsStarterDeckRuntime.Contains("ApplyStarterDeckBeforeRoleSubmit")) -or -not ($auraToolsStarterDeckRuntime.Contains("context.Arguments?.OfType<RoleTable>().FirstOrDefault()"))) {
    throw "AuraTools starter deck must apply each client's owned role-table argument before its native submission."
}
if ($auraToolsStarterDeckRuntime.Contains('"NormalMapManager.InitRoleTable"')) {
    throw "AuraTools starter deck must not write a provisional deck during early role-table initialization."
}
if ($auraToolsStarterDeckRuntime.Contains("GameEntryUI.career")) {
    throw "AuraTools starter deck must not resolve multiplayer decks through global lobby career state."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("ReadDataId(roleTable.Career)")) -or -not ($auraToolsStarterDeckRuntime.Contains("ResolveRuntimeRole"))) {
    throw "AuraTools starter deck must resolve the final deck from the owned RoleTable.Career."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("AppliedRoleKey")) -or -not ($auraToolsStarterDeckRuntime.Contains("WriteAppliedRoleMetadata"))) {
    throw "AuraTools starter deck must persist the applied role id for stale-role correction."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("correcting stale starter deck")) -or -not ($auraToolsStarterDeckRuntime.Contains("AppliedRoleKey"))) {
    throw "AuraTools starter deck must allow correction when a stale role deck was already applied."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("StarterDeckEditorSession"))) {
    throw "AuraTools starter deck editor must use per-window edit sessions."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("BuildCandidateCardPackGroups")) -or -not ($auraToolsStarterDeckRuntime.Contains("StarterDeckCardPackGroup"))) {
    throw "AuraTools starter deck editor must group candidate cards by existing card packs."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("StarterDeckCardCatalogSnapshot")) -or -not ($auraToolsStarterDeckRuntime.Contains("WarmStarterDeckCardCatalog"))) {
    throw "AuraTools starter deck editor must cache the registered card catalog after the game/mod card tables are ready."
}
if (-not ($auraToolsStarterDeckRuntime.Contains('"GameEntryUI.Init"')) -or -not ($auraToolsStarterDeckRuntime.Contains('"GameEntryUI.ShowCareer"'))) {
    throw "AuraTools starter deck card catalog must warm up from post-game-entry hooks instead of ModInitialize-time scanning."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("BuildRegisteredExplicitCardIds")) -or -not ($auraToolsStarterDeckRuntime.Contains("BuildRegisteredHiddenCardIds")) -or -not ($auraToolsStarterDeckRuntime.Contains("BuildRegisteredSkillCardIds")) -or -not ($auraToolsStarterDeckRuntime.Contains("SystemSkillCardIds"))) {
    throw "AuraTools starter deck card catalog must keep explicit, hidden, skill, and system skill card tables."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("expandedCandidateGroups")) -or -not ($auraToolsStarterDeckRuntime.Contains("RefreshCandidateGroups")) -or -not ($auraToolsStarterDeckRuntime.Contains("ToggleCandidateGroup"))) {
    throw "AuraTools starter deck pack groups must render as default-collapsed foldouts."
}
if ($auraToolsStarterDeckRuntime.Contains('.Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("*", StringComparison.Ordinal))')) {
    throw "AuraTools custom starter deck candidates must not filter special * cards."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("CardDisplayNameWithSpecialMarker")) -or -not ($auraToolsStarterDeckRuntime.Contains("\u3010*\u3011"))) {
    throw "AuraTools starter deck editor must visibly mark special * cards."
}
$packGroupedSelectionTitleText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("5oyJ5Y2h5YyF6YCJ5oup"))
if (-not ($auraToolsStarterDeckRuntime.Contains('"' + $packGroupedSelectionTitleText + '"')) -or -not ($auraToolsStarterDeckRuntime.Contains("\u5176\u5b83"))) {
    throw "AuraTools starter deck editor must expose pack-grouped selection with an Other bucket."
}
if ($auraToolsStarterDeckRuntime.Contains("private static StarterDeckLocalProfileSettings? editingProfile")) {
    throw "AuraTools starter deck editor must not keep a static editing profile."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("StarterDeckProfileResolutionReasons.LocalRole"))) {
    throw "AuraTools role-specific starter decks must resolve through the local-role priority path."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("settings.Mode == StarterDeckModes.Global")) -or -not ($auraToolsStarterDeckRuntime.Contains("CreateGlobalLocalProfile(settings.GlobalProfile)"))) {
    throw "AuraTools global starter deck mode must resolve the global profile before any role-specific selection."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("ConfiguredSelectedProfileIdForRole")) -or -not ($auraToolsStarterDeckRuntime.Contains("ProfileSelectionStatus"))) {
    throw "AuraTools starter deck profile picker must visibly mark configured and effective role selections."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("StarterDeckProfilePickerSession")) -or -not ($auraToolsStarterDeckRuntime.Contains("RefreshProfiles()"))) {
    throw "AuraTools starter deck profile picker must refresh the current window after selection changes."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("SelectProfileForRole(role.Id, profile.QualifiedProfileId);")) -or -not ($auraToolsStarterDeckRuntime.Contains("enableButton.interactable = !isConfiguredSelected"))) {
    throw "AuraTools starter deck profile picker must disable the active selection and refresh after enabling another profile."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("SetLocalHint")) -or -not ($auraToolsStarterDeckRuntime.Contains("localHintText"))) {
    throw "AuraTools starter deck profile picker must use its own hint text instead of leaking messages to the role list."
}
if (-not ($auraToolsStarterDeckRuntime.Contains("ShowGlobal(overlayParent")) -or -not ($auraToolsStarterDeckRuntime.Contains("ShowRole(overlayParent, role.Id, role.DisplayName)"))) {
    throw "AuraTools starter deck profile picker must distinguish global and role-local editing."
}
if ($auraToolsStarterDeckRuntime.Contains('role.DisplayName + "\n" + role.Id')) {
    throw "AuraTools starter deck role manager must not display role ids in the role list."
}
$effectiveDeckEmptyText = '? "' + (-join [char[]](0x751F, 0x6548, 0xFF1A, 0x65E0, 0x5B8C, 0x6574, 0x5361, 0x7EC4)) + '"'
$effectiveDeckNameText = ': "' + (-join [char[]](0x751F, 0x6548, 0xFF1A)) + '" + resolved.Profile.DisplayName'
if (-not ($auraToolsStarterDeckRuntime.Contains($effectiveDeckEmptyText)) -or -not ($auraToolsStarterDeckRuntime.Contains($effectiveDeckNameText))) {
    throw "AuraTools starter deck role manager must keep the effective deck column to a single effective deck name."
}
if (-not ($auraToolsUi -like "*CloseOverlay(overlayRoot, name*")) {
    throw "AuraTools overlays must close same-name windows before creating a new one."
}
if (($auraToolsUi.Contains("new Vector2(-0.2f, 0f)")) -or ($auraToolsUi.Contains("new Vector2(1.2f, 1f)"))) {
    throw "AuraTools overlays must not use off-screen width anchors."
}
if (-not ($auraToolsUi.Contains("Mathf.Max(width, 48f)"))) {
    throw "AuraTools compact action buttons must not be forced to the default 120px minimum width."
}
if (-not ($auraToolsUi.Contains("SuccessText")) -or -not ($auraToolsUi.Contains("ActiveRow"))) {
    throw "AuraTools UI must expose selection highlight colors for starter deck profile rows."
}
if (-not ($auraToolsSettings.Contains("starterDeckEnabled ?")) -or -not ($auraToolsSettings.Contains("AuraToolsUi.MutedText"))) {
    throw "AuraTools starter deck settings must expose an obvious enabled/disabled title state."
}
if ($auraToolsSettings -like "*settings.PreferRoleModProfile, value =>*") {
    throw "AuraTools starter deck role-mod fallback policy must be explanatory text, not a checkbox."
}
$roleFallbackPolicyText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("5rKh5pyJ5pys5Zyw6KeS6Imy5Y2h57uE5pe277yM5Lya6Ieq5Yqo5L2/55So6KeS6Imy5omA5bGeIE1PRCDms6jlhoznmoTmjqjojZDlvIDlsYDljaHnu4Q="))
if (-not ($auraToolsSettings.Contains($roleFallbackPolicyText))) {
    throw "AuraTools starter deck settings must explain the always-on role-mod fallback policy."
}
if ($auraToolsSettings -like "*AuraToolsConfigService.Skin.AutoInstallBundledSkins, value =>*") {
    throw "AuraTools skin auto-install policy must be explanatory text, not a checkbox."
}
$skinInstallPolicyText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("5YaF572u6KeS6Imy55qu6IKk5Lya6Ieq5Yqo5a6J6KOF5bm26KGl6b2Q5Yiw5YWx5Lqr55qu6IKk55uu5b2V"))
if (-not ($auraToolsSettings.Contains($skinInstallPolicyText))) {
    throw "AuraTools skin settings must explain the always-on bundled skin auto-install policy."
}
if (-not ($auraToolsConfigModels -like "*PreferRoleModProfile = true;*") -or -not ($auraToolsConfigModels -like "*AutoInstallBundledSkins = true;*")) {
    throw "AuraTools config normalization must keep always-on starter deck and skin policies enabled."
}
if ($auraToolsSkinRuntime -like "*|| !AuraToolsConfigService.Skin.AutoInstallBundledSkins*") {
    throw "AuraTools skin runtime must not skip bundled package registration because of the removed auto-install checkbox."
}

foreach ($registry in @("SunExp\audio.registry.json", "SanGuoShaExp\audio.registry.json")) {
    $registryText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot $registry)
    if ($registryText.Contains("ModResource/audio")) {
        throw "Main audio registry still points at direct ModResource audio: $registry"
    }
    if (-not $registryText.Contains("Shared:Audio/")) {
        throw "Main audio registry does not resolve through AuraShared audio paths: $registry"
    }
}

Write-Host "Main shared framework validation passed."
