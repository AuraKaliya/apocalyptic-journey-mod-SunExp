param()

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

function Read-RepoText {
    param([string]$RelativePath)

    return [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot $RelativePath))
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Message
    )

    Assert-True ($Text.Contains($Needle)) $Message
}

function Assert-NotContains {
    param(
        [string]$Text,
        [string]$Needle,
        [string]$Message
    )

    Assert-True (-not $Text.Contains($Needle)) $Message
}

function Assert-Matches {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    Assert-True ([regex]::IsMatch($Text, $Pattern)) $Message
}

function Assert-NotMatches {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    Assert-True (-not [regex]::IsMatch($Text, $Pattern)) $Message
}

$script:RepoRoot = Get-RepoRoot

$requiredFiles = @(
    "SunExp-Dev\GameApi\ScriptVarApi.cs",
    "SunExp-Dev\GameApi\CombatVarApi.cs",
    "SunExp-Dev\GameApi\ScriptEventApi.cs",
    "SunExp-Dev\GameApi\TargetApi.cs",
    "SunExp-Dev\GameApi\DamageApi.cs",
    "SunExp-Dev\GameApi\SolarCombatApi.cs",
    "SunExp-Dev\GameApi\FieldApi.cs",
    "SunExp-Dev\GameApi\BuffOverflowApi.cs",
    "SunExp-Dev\GameApi\MapItemApi.cs",
    "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs",
    "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs",
    "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs",
    "SunExp-Dev\Mechanics\SolarFinaleStateService.cs",
    "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"
)

foreach ($file in $requiredFiles) {
    Assert-True (Test-Path -LiteralPath (Join-Path $RepoRoot $file)) "Architecture file is missing: $file"
}

$executorApi = Read-RepoText "SunExp-Dev\GameApi\ExecutorApi.cs"
$fieldApi = Read-RepoText "SunExp-Dev\GameApi\FieldApi.cs"
$buffOverflowApi = Read-RepoText "SunExp-Dev\GameApi\BuffOverflowApi.cs"
$mapItemApi = Read-RepoText "SunExp-Dev\GameApi\MapItemApi.cs"
$statusApi = Read-RepoText "SunExp-Dev\GameApi\StatusApi.cs"
$cardScripts = Read-RepoText "SunExp-Dev\Scripting\CardScripts.cs"
$buffScripts = Read-RepoText "SunExp-Dev\Scripting\BuffScripts.cs"
$relicScripts = Read-RepoText "SunExp-Dev\Scripting\RelicScripts.cs"
$eventScripts = Read-RepoText "SunExp-Dev\Scripting\EventScripts.cs"
$bossScripts = Read-RepoText "SunExp-Dev\Scripting\BossScripts.cs"
$mapNodeCardArtRegistry = Read-RepoText "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs"
$mapNodeTextureFitService = Read-RepoText "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs"
$modeChoiceDragRange = Read-RepoText "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs"
$solarFinaleService = Read-RepoText "SunExp-Dev\Mechanics\SolarFinaleStateService.cs"
$mapNodeCardArtRuntime = Read-RepoText "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"
$solarMemoryModeRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"
$modeChoiceEntryDefinition = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryDefinition.cs"
$modeChoiceEntryRegistry = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryRegistry.cs"
$modeChoiceLayoutRuntime = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceLayoutRuntime.cs"
$solarMemoryRunLauncher = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"
$scriptingSource = [string]::Join("`n", (Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp-Dev\Scripting") -File -Filter "*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))

Assert-Contains $executorApi "return ScriptVarApi.GetVar(executor, key, fallback);" "ExecutorApi must delegate script variables to ScriptVarApi."
Assert-Contains $executorApi "return ScriptEventApi.TryAddEvent(executor, eventName, script, context);" "ExecutorApi must delegate event registration to ScriptEventApi."
Assert-Contains $executorApi "return TargetApi.EnemyTargets(executor);" "ExecutorApi must delegate target selection to TargetApi."
Assert-Contains $executorApi "return DamageApi.DealDamage(executor, amount, damageType);" "ExecutorApi must delegate damage to DamageApi."
Assert-Contains $executorApi "return SolarCombatApi.SolarKeywordDamage(executor, baseDamage, target, coefficientScale);" "ExecutorApi must delegate solar keyword math to SolarCombatApi."
Assert-Contains $executorApi "FieldApi.ApplyFieldBuff(executor, fieldId, amount);" "ExecutorApi must delegate field application to FieldApi."
Assert-Contains $executorApi "BuffOverflowApi.HandleBurnOverflow(target, buffId, amount);" "ExecutorApi must delegate overflow conversion to BuffOverflowApi."
Assert-NotMatches $executorApi "private\s+static\s+.*(ConfiguredBuffUpperBound|TotalFieldBuffStacks|ApplySolarRadianceUpperBound|ReadIntProperty)" "ExecutorApi must remain a compatibility facade, not retain moved private implementations."

Assert-Contains $fieldApi "public static class FieldApi" "FieldApi must own field state behavior."
Assert-Contains $buffOverflowApi "public static class BuffOverflowApi" "BuffOverflowApi must own buff upper-bound and overflow behavior."
Assert-Contains $mapItemApi "public static class MapItemApi" "MapItemApi must own Unity MapItem icon access."
Assert-Contains $mapItemApi "MapNodeTextureFitService.Fit" "MapItemApi must delegate node-card geometry to Mechanics."
Assert-Contains $mapNodeTextureFitService "public static class MapNodeTextureFitService" "MapNodeTextureFitService must own map-node texture fitting math."
Assert-Contains $modeChoiceDragRange "public static class ModeChoiceDragRangeService" "ModeChoiceDragRangeService must own mode-choice viewport and overflow math."
Assert-Contains $mapNodeCardArtRegistry "public static class MapNodeCardArtRegistry" "MapNodeCardArtRegistry must own map-node art specs."
Assert-Contains $mapNodeCardArtRuntime "MapItemApi.ApplyTexture" "MapNodeCardArtRuntime must delegate Unity icon mutation to MapItemApi."
Assert-Contains $mapNodeCardArtRuntime "MapNodeCardArtRegistry.Resolve" "MapNodeCardArtRuntime must resolve configured node art through the registry."
Assert-Contains $statusApi "public static int MaxHp" "StatusApi must expose MaxHp so scripting does not use reflection."
Assert-NotContains $buffScripts 'GetType().GetProperty("MaxHp")' "BuffScripts must not use reflection for MaxHp."

Assert-Matches $cardScripts "InitHandlers\s*=\s*new" "CardScripts must use an Init handler registry."
Assert-Matches $cardScripts "UseHandlers\s*=\s*new" "CardScripts must use a Use handler registry."
Assert-Matches $buffScripts "ApplyHandlers\s*=\s*new" "BuffScripts must use an Apply handler registry."
Assert-Matches $buffScripts "ClearHandlers\s*=\s*new" "BuffScripts must use a Clear handler registry."
Assert-Matches $relicScripts "FightHandlers\s*=\s*new" "RelicScripts must use a Fight handler registry."
Assert-NotMatches $cardScripts "switch\s*\(\s*id\s*\)" "CardScripts must not route card ids with a top-level switch."
Assert-NotMatches $buffScripts "switch\s*\(\s*id\s*\)" "BuffScripts must not route buff ids with a top-level switch."
Assert-NotMatches $relicScripts "switch\s*\(\s*id\s*\)" "RelicScripts must not route relic ids with a top-level switch."

Assert-Contains $solarFinaleService "public static class SolarFinaleStateService" "Solar finale state must be centralized in SolarFinaleStateService."
Assert-NotMatches ($eventScripts + "`n" + $bossScripts) "PlayerApi\.SetGameVar\(SunExpIds\.SolarFinale" "Solar finale GameVar writes must stay inside SolarFinaleStateService."
Assert-Contains $eventScripts "SolarFinaleStateService.EnsureLedger()" "EventScripts must use SolarFinaleStateService for ledger initialization."
Assert-Contains $bossScripts "SolarFinaleStateService.MarkSecondSunDefeated(false)" "BossScripts must use SolarFinaleStateService for finale boss state."

Assert-Contains $solarMemoryModeRuntime "SolarMemoryRunLauncher.Start" "SolarMemoryModeRuntime must delegate run startup to SolarMemoryRunLauncher."
Assert-Contains $solarMemoryModeRuntime "ModeChoiceEntryRegistry.Register" "SolarMemoryModeRuntime must register its mode-choice entry instead of owning layout."
Assert-Contains $solarMemoryModeRuntime "ModeChoiceLayoutRuntime.Initialize(modConfig)" "SolarMemoryModeRuntime must delegate mode-choice positioning to ModeChoiceLayoutRuntime."
Assert-Contains $modeChoiceEntryRegistry "public static class ModeChoiceEntryRegistry" "Mode-choice custom entry registration must stay centralized."
Assert-Contains $modeChoiceLayoutRuntime "public static class ModeChoiceLayoutRuntime" "Mode-choice custom entry layout must stay centralized."
Assert-Contains $modeChoiceLayoutRuntime "AppendRegisteredEntries" "ModeChoiceLayoutRuntime must append custom entries without owning native layout."
Assert-Contains $modeChoiceLayoutRuntime "PlaceAfterNativeEntries" "ModeChoiceLayoutRuntime must place custom entries after the real last native entry."
Assert-Contains $modeChoiceLayoutRuntime "KnownNativeEntryNames" "ModeChoiceLayoutRuntime must protect known native mode entries explicitly."
Assert-Contains $modeChoiceLayoutRuntime '"StoryMode"' "ModeChoiceLayoutRuntime must not allow Solar Memory to occupy the native StoryMode slot."
Assert-Contains $modeChoiceLayoutRuntime "EnsureFallbackButton" "ModeChoiceLayoutRuntime must provide a separate fallback entry when native-card append is unsafe."
Assert-Contains $modeChoiceLayoutRuntime "EnsureLayoutSlot" "ModeChoiceLayoutRuntime must create transparent LayoutGroup slots for custom entry placement."
Assert-Contains $modeChoiceLayoutRuntime "FindProtectedNativeEntries" "ModeChoiceLayoutRuntime must reserve inactive native slots before placing custom entries."
Assert-Contains $modeChoiceLayoutRuntime "NativeReserveSlotPrefix" "ModeChoiceLayoutRuntime must reserve inactive native mode slots explicitly."
Assert-Contains $modeChoiceLayoutRuntime "NativeProxySlotPrefix" "ModeChoiceLayoutRuntime must render inactive native slots through visible proxy entries."
Assert-Contains $modeChoiceLayoutRuntime "EnsureNativeProxySlot" "ModeChoiceLayoutRuntime must clone inactive native entries into visible LayoutGroup proxies."
Assert-Contains $modeChoiceLayoutRuntime "CustomSlotPrefix" "ModeChoiceLayoutRuntime must create a fifth custom slot through the native LayoutGroup."
Assert-Contains $modeChoiceLayoutRuntime "ModeChoiceHorizontalDrag" "ModeChoiceLayoutRuntime must own overflow dragging for mode-choice overlays."
Assert-Contains $modeChoiceLayoutRuntime "ModeChoiceDragRangeService.Calculate" "ModeChoiceLayoutRuntime must delegate overflow bounds to testable mechanics."
Assert-Contains $modeChoiceLayoutRuntime "DisableLegacyDragSurface" "ModeChoiceLayoutRuntime must disable stale raycast-blocking drag surfaces."
Assert-Contains $modeChoiceLayoutRuntime "image.raycastTarget = false" "ModeChoiceLayoutRuntime must make stale drag surfaces non-blocking before hiding them."
Assert-NotContains $modeChoiceLayoutRuntime "ConfigureDragSurface" "ModeChoiceLayoutRuntime must not create a full-screen raycast-blocking drag surface."
Assert-Contains $modeChoiceLayoutRuntime "EnsureBackgroundDragSurface" "ModeChoiceLayoutRuntime must provide a background-only drag raycast surface."
Assert-Contains $modeChoiceLayoutRuntime "surface.SetAsFirstSibling()" "The background drag surface must remain behind clickable mode entries."
Assert-Contains $modeChoiceLayoutRuntime "modeChoice.gameObject" "The shared drag handler must live on the ModeChoiceUI root."
Assert-Contains $modeChoiceLayoutRuntime "preferred.gameObject.activeSelf" "Custom mode entries must prefer an active native visual template."
Assert-Contains $modeChoiceLayoutRuntime "ModeChoiceSidePadding" "ModeChoiceLayoutRuntime must keep side padding in the scroll range."
Assert-NotContains $modeChoiceLayoutRuntime "strategy=overlay-layout-group" "ModeChoiceLayoutRuntime must not use the failed overlay placement strategy."
Assert-NotContains $modeChoiceLayoutRuntime "layout-group=sibling-order" "ModeChoiceLayoutRuntime must not use sibling order as a LayoutGroup placement strategy."
Assert-Contains $modeChoiceEntryDefinition "Action<ModeChoiceUI>? Activate" "Mode-choice entry definitions must expose activation for fallback UI."
Assert-NotContains $solarMemoryModeRuntime "CreateSolarMemorySave" "SolarMemoryModeRuntime must not own save creation."
Assert-NotContains $solarMemoryModeRuntime "private static void StartSolarMemoryRun" "SolarMemoryModeRuntime must not own run startup."
Assert-Contains $solarMemoryRunLauncher "public static SaveInfo CreateSave" "SolarMemoryRunLauncher must own save creation."
Assert-Contains $solarMemoryRunLauncher "SolarMemoryPrepStep.DeckSelection" "SolarMemoryRunLauncher must initialize preparation state."

Assert-NotContains $scriptingSource "using SunExp.Dll.Hooks" "Scripting layer must not import Hooks."
Assert-NotMatches $scriptingSource "\.\s*Add(?:Temp)?Event\s*\(" "Scripting layer must register events through ScriptEventApi or ExecutorApi wrappers."

$dataFiles = Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp\Data") -Recurse -File -Filter "*.csv"
foreach ($file in $dataFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($text, "CS\.SunExp\.Dll\.([A-Za-z0-9_\.]+)")) {
        $target = $match.Groups[1].Value
        Assert-True ($target.StartsWith("Scripting.", [System.StringComparison]::Ordinal)) "Data script target must route through Scripting: $($file.FullName) -> $($match.Value)"
    }
}

Write-Host "SunExp architecture assertions passed."
