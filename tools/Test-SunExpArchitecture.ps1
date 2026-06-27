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
    "SunExp-Dev\GameApi\DialogueApi.cs",
    "SunExp-Dev\GameApi\DialogueUiApi.cs",
    "SunExp-Dev\GameApi\MapItemApi.cs",
    "SunExp-Dev\GameApi\BattleRewardApi.cs",
    "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs",
    "SunExp-Dev\Mechanics\DialogueFlowDefinition.cs",
    "SunExp-Dev\Mechanics\DialogueFlowRegistry.cs",
    "SunExp-Dev\Mechanics\DialogueFlowService.cs",
    "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs",
    "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs",
    "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs",
    "SunExp-Dev\Mechanics\SolarFinaleStateService.cs",
    "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs",
    "SunExp-Dev\Hooks\DialogueFlowRuntime.cs",
    "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs",
    "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs",
    "SunExp-Dev\Hooks\StarScoreHudRuntime.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudAssets.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudShaderController.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudShaderMaterials.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudView.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudTooltipView.cs",
    "SunExp-Dev\Hooks\Ui\SunExpModalHost.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs",
    "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs",
    "SunExp-Dev\Mechanics\StarScoreDisplaySnapshot.cs",
    "SunExp-Dev\Mechanics\StarScoreNote.cs"
)

foreach ($file in $requiredFiles) {
    Assert-True (Test-Path -LiteralPath (Join-Path $RepoRoot $file)) "Architecture file is missing: $file"
}

$executorApi = Read-RepoText "SunExp-Dev\GameApi\ExecutorApi.cs"
$fieldApi = Read-RepoText "SunExp-Dev\GameApi\FieldApi.cs"
$buffOverflowApi = Read-RepoText "SunExp-Dev\GameApi\BuffOverflowApi.cs"
$mapItemApi = Read-RepoText "SunExp-Dev\GameApi\MapItemApi.cs"
$dialogueApi = Read-RepoText "SunExp-Dev\GameApi\DialogueApi.cs"
$dialogueUiApi = Read-RepoText "SunExp-Dev\GameApi\DialogueUiApi.cs"
$battleRewardApi = Read-RepoText "SunExp-Dev\GameApi\BattleRewardApi.cs"
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
$dialogueFlowService = Read-RepoText "SunExp-Dev\Mechanics\DialogueFlowService.cs"
$battleRewardAdjustmentService = Read-RepoText "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs"
$solarMemoryStoryGateService = Read-RepoText "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs"
$dialogueFlowRuntime = Read-RepoText "SunExp-Dev\Hooks\DialogueFlowRuntime.cs"
$battleRewardAdjustmentRuntime = Read-RepoText "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs"
$solarMemoryRewardRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs"
$mapNodeCardArtRuntime = Read-RepoText "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"
$runtimeHooks = Read-RepoText "SunExp-Dev\Hooks\RuntimeHooks.cs"
$solarMemoryModeRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"
$starScoreService = Read-RepoText "SunExp-Dev\Mechanics\StarScoreService.cs"
$starScoreHudRuntime = Read-RepoText "SunExp-Dev\Hooks\StarScoreHudRuntime.cs"
$starScoreHudView = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudView.cs"
$starScoreHudHoverProbe = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs"
$starScoreHudAssets = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudAssets.cs"
$starScoreHudShaderController = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudShaderController.cs"
$starScoreHudShaderMaterials = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudShaderMaterials.cs"
$starScoreHudTooltipView = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudTooltipView.cs"
$starScoreCadenceCatalog = Read-RepoText "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs"
$sunExpProject = Read-RepoText "SunExp-Dev\SunExp.Dll.csproj"
$modeChoiceEntryDefinition = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryDefinition.cs"
$modeChoiceEntryRegistry = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryRegistry.cs"
$modeChoiceLayoutRuntime = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceLayoutRuntime.cs"
$solarMemoryRunLauncher = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"
$solarMemoryStarterDeckRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"
$solarMemorySetupFlowRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"
$solarMemoryBlessingPickerRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"
$sunExpModalHost = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpModalHost.cs"
$sunExpUiSafety = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs"
$sunExpUiSprites = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs"
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
Assert-Contains $dialogueApi "public static bool ShowDialogue" "DialogueApi must own game dialogue display calls."
Assert-Contains $dialogueApi "Singleton<DialogueManager>.Instance.ShowDialogue(dialogueId)" "DialogueApi must route dialogue display through the native dialogue manager."
Assert-Contains $dialogueUiApi "public static bool TryGetDialogueId" "DialogueUiApi must own reflected DialogueUI state access."
Assert-Contains $dialogueUiApi 'GetField(' "DialogueUiApi must centralize DialogueUI private-field reflection."
Assert-Contains $battleRewardApi "public static bool AppendRandomRelicReward" "BattleRewardApi must own native battle reward UI mutation."
Assert-Contains $battleRewardApi "rewardUi.RandomSetRelic(candidates)" "BattleRewardApi must reuse the native relic reward item flow."
Assert-Contains $battleRewardAdjustmentService "public static class BattleRewardAdjustmentService" "Reusable battle reward adjustment rules must live in Mechanics."
Assert-Contains $battleRewardAdjustmentService "ConditionalWeakTable<BattleRewardsUI, AppliedRuleSet>" "Battle reward adjustments must be applied once per reward UI."
Assert-Contains $battleRewardAdjustmentRuntime '"BattleRewardsUI.ModeSetReward"' "Battle reward adjustment runtime must hook reward generation after native rewards are set."
Assert-Contains $battleRewardAdjustmentRuntime "BattleRewardAdjustmentService.ApplyAll" "Battle reward hooks must delegate rule application to Mechanics."
Assert-Contains $solarMemoryRewardRuntime "SolarMemoryModeRuntime.IsSolarMemoryRun()" "Solar memory reward adjustments must be gated to Solar Memory runs."
Assert-Contains $solarMemoryRewardRuntime "BattleRewardApi.AppendRandomRelicReward" "Solar memory reward runtime must add its relic through BattleRewardApi."
Assert-Contains $runtimeHooks "BattleRewardAdjustmentRuntime.Initialize(modConfig)" "RuntimeHooks must initialize generic battle reward adjustment hooks."
Assert-Contains $runtimeHooks "SolarMemoryRewardRuntime.Initialize()" "RuntimeHooks must register Solar Memory reward adjustment rules."
Assert-Contains $runtimeHooks "StarScoreHudRuntime.Initialize(modConfig)" "RuntimeHooks must initialize star score HUD hooks."
Assert-Contains $runtimeHooks "RunHookStep(" "RuntimeHooks must isolate runtime initialization into logged hook steps."
Assert-Contains $runtimeHooks "AuraSharedHooks.RunStep" "RuntimeHooks must use the shared step guard for hook initialization."
Assert-Contains $starScoreService "public static event Action<StarScoreDisplaySnapshot>? Changed" "StarScoreService must publish typed display snapshots for UI hooks."
Assert-Contains $starScoreHudRuntime "StarScoreService.Changed += OnStarScoreChanged" "StarScoreHudRuntime must subscribe to star score mechanics changes."
Assert-Contains $starScoreHudRuntime "UIManager.Instance?.canvasTf" "StarScoreHudRuntime must attach fixed HUD to the main canvas."
Assert-Contains $starScoreHudRuntime "FightPlayer.Instance?.Status?.InstanceId" "StarScoreHudRuntime must keep star score presentation local-player scoped."
Assert-Contains $starScoreCadenceCatalog "public static class StarScoreCadenceCatalog" "StarScoreCadenceCatalog must own tooltip cadence copy in Mechanics."
Assert-Contains $starScoreCadenceCatalog "CandidatesForPrefix" "StarScoreCadenceCatalog must calculate available cadence candidates from current notes."
Assert-NotContains $starScoreHudView "ProgressPartThresholds" "StarScoreHudView must keep the full star-score frame visible once shown."
Assert-Contains $starScoreHudView "StarScoreHudTooltipView.Create" "StarScoreHudView must own the hover tooltip view."
Assert-Contains $starScoreHudView "StarScoreHudShaderController" "StarScoreHudView must delegate star-score lighting effects to the shader controller."
Assert-Contains $starScoreHudView "RectMask2D" "StarScoreHudView must use masked full-frame lighting slots instead of mismatched sliced art."
Assert-Contains $starScoreHudView "SlotTops = { 0f, 146f, 226f }" "StarScoreHudView lighting masks must merge head and space art into the three overture stages."
Assert-Contains $starScoreHudView "SlotHeights = { 146f, 80f, 100f }" "StarScoreHudView lighting masks must cover head+slot1, space+slot2, and space+slot3."
Assert-NotContains $starScoreHudView "StarScoreHudAssets.HeadPath" "StarScoreHudView must not rebuild the star-score frame from mismatched sliced art."
Assert-Contains $starScoreHudShaderController "StarScoreHudShaderMaterials.CreateLitMaterial" "StarScoreHudShaderController must use the material factory for optional shader binding."
Assert-Contains $starScoreHudShaderController "ApplySnapshot(StarScoreDisplaySnapshot snapshot)" "StarScoreHudShaderController must be driven by typed star-score snapshots."
Assert-Contains $starScoreHudShaderMaterials 'Shader.Find(StarScoreHudShaderIds.ShaderName)' "Star score shader lookup must stay centralized in the material factory."
Assert-Contains $starScoreHudShaderMaterials "using UI layered fallback" "Star score shader material factory must log the fallback path when no shader is bundled."
Assert-Contains $starScoreHudAssets "FullPath" "StarScoreHudAssets must expose the full star-score frame for shader and fallback rendering."
Assert-NotContains $starScoreHudView "Input.mousePosition" "StarScoreHudView must not use legacy input polling for hover."
Assert-NotContains $starScoreHudView "RectTransformUtility.RectangleContainsScreenPoint" "StarScoreHudView hover detection must use UI pointer events."
Assert-Contains $starScoreHudHoverProbe "IPointerEnterHandler" "StarScoreHudHoverProbe must receive hover entry through Unity UI events."
Assert-Contains $starScoreHudHoverProbe "IPointerExitHandler" "StarScoreHudHoverProbe must receive hover exit through Unity UI events."
Assert-Contains $starScoreHudView "image.raycastTarget = true" "StarScoreHudView must expose a small hover hotspot for pointer events."
Assert-Contains $starScoreHudView "image.raycastTarget = false" "StarScoreHudView images must not intercept pointer input."
Assert-Contains $starScoreHudTooltipView "group.blocksRaycasts = false" "StarScoreHudTooltipView must not block native battle controls."
Assert-Contains $starScoreHudTooltipView "image.raycastTarget = false" "StarScoreHudTooltipView images must not intercept pointer input."
Assert-Contains $starScoreHudAssets "StarScoreNote.Opening => Load(OpeningIconPath)" "StarScoreHudAssets must map typed notes to icon sprites."
Assert-NotContains $sunExpProject "UnityEngine.InputLegacyModule" "Star score hover detection must not depend on the legacy input module."
Assert-Contains $dialogueFlowService "public static class DialogueFlowService" "DialogueFlowService must own reusable managed dialogue session behavior."
Assert-Contains $dialogueFlowService "DialogueApi.ShowDialogue" "DialogueFlowService must open managed dialogues through DialogueApi."
Assert-Contains $dialogueFlowService "DialogueApi.EndDialogue" "DialogueFlowService must close managed dialogues through DialogueApi after C# choice handling."
Assert-Contains $dialogueFlowRuntime "DialogueUI.ChooseOption" "DialogueFlowRuntime must hook native dialogue choices."
Assert-Contains $dialogueFlowRuntime "DialogueFlowService.CompleteChoice" "DialogueFlowRuntime must route native choices into the C# dialogue flow service."
Assert-Contains $runtimeHooks "DialogueFlowRuntime.Initialize(modConfig)" "RuntimeHooks must initialize managed dialogue flow hooks."
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
Assert-Contains $solarMemoryStoryGateService "public static class SolarMemoryStoryGateService" "Solar Memory story gates must be centralized in Mechanics."
Assert-Contains $solarMemoryStoryGateService "DialogueFlowService.Start" "Solar Memory story gates must start managed dialogue flows instead of owning native choice scripts."
Assert-NotContains $solarMemoryStoryGateService "SunExp.Dll.Hooks" "Mechanics story gates must not depend on Hook runtime classes."
Assert-NotMatches ($eventScripts + "`n" + $bossScripts) "PlayerApi\.SetGameVar\(SunExpIds\.SolarFinale" "Solar finale GameVar writes must stay inside SolarFinaleStateService."
Assert-NotContains $eventScripts "SolarFinaleStateService" "Retired solar finale events must not leave EventScripts coupled to finale state."
Assert-Contains $bossScripts "SolarFinaleStateService.MakeNameless(1)" "BossScripts must keep name-ledger changes centralized in SolarFinaleStateService."

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
Assert-Contains $sunExpModalHost "public static Transform? ModalParent()" "SunExp modal windows must share a single modal parent resolver."
Assert-Contains $sunExpModalHost "SunExpUiSafety.CloseTransient" "SunExp modal close paths must route through the transient UI safety helper."
Assert-Contains $sunExpUiSafety "UiRaycastSafeDestroyRuntime.DisableAndHide" "SunExp transient UI teardown must disable and hide raycast surfaces before destroying."
Assert-Contains $sunExpUiSafety "ScrubGraphicRegistryForFrames" "SunExp transient UI teardown must scrub Unity's Graphic registry after destroying modal UI."
Assert-Contains $sunExpUiSprites "private static readonly Dictionary<string, Sprite?> Cache" "SunExp UI sprites must be cached instead of loaded per window."
Assert-Contains $sunExpUiSprites "Sprite.Create(" "SunExp UI sprite helper must own nine-slice sprite creation."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpModalHost.Close(ref activePanel" "Starter deck modal close must use SunExpModalHost."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpModalHost.Close(ref activeOriginRoot" "Origin setup modal close must use SunExpModalHost."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpModalHost.Close(ref activeBlessingChrome" "Blessing setup chrome close must use SunExpModalHost."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpModalHost.Close(ref activePanel" "Blessing picker modal close must use SunExpModalHost."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpUiSprites.Button" "Starter deck modal must use shared cached button sprites."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpUiSprites.Button" "Setup modal must use shared cached button sprites."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpUiSprites.Button" "Blessing picker modal must use shared cached button sprites."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "CreateNineSliceSprite" "Solar Memory setup windows must not duplicate nine-slice sprite creation."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "GetButtonSprite" "Solar Memory setup windows must not own duplicate button sprite caches."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "Object.Destroy(active" "Solar Memory setup windows must not directly destroy active modal roots."

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

$dialogueData = Read-RepoText "SunExp\Data\Dialogue\sunexp.csv"
Assert-NotContains $dialogueData "CS.SunExp.Dll.Scripting.EventScripts.ContinueSolarMemory();" "Managed dialogue choices must not call C# through Dialogue ChoiceScript columns."

Write-Host "SunExp architecture assertions passed."
