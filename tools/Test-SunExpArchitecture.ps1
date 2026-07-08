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
    "SunExp-Dev\GameApi\CardVisualSkinApi.cs",
    "SunExp-Dev\GameApi\CardVisualEffectApi.cs",
    "SunExp-Dev\GameApi\BattleRewardApi.cs",
    "SunExp-Dev\GameApi\CardApi.cs",
    "SunExp-Dev\GameApi\CardConfigApi.cs",
    "SunExp-Dev\GameApi\EnemyApi.cs",
    "SunExp-Dev\GameApi\ModeChoiceSaveCacheApi.cs",
    "SunExp-Dev\GameApi\ProjectionUiApi.cs",
    "SunExp-Dev\GameApi\FamiliarGrowthApi.cs",
    "SunExp-Dev\GameApi\SunExpResourceCache.cs",
    "SunExp-Dev\Infrastructure\SunExpDirtyState.cs",
    "SunExp-Dev\Infrastructure\SunExpPerformanceSettings.cs",
    "SunExp-Dev\Infrastructure\SunExpPerformanceCounters.cs",
    "SunExp-Dev\Infrastructure\SunExpFrameDispatcher.cs",
    "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs",
    "SunExp-Dev\Mechanics\DialogueFlowDefinition.cs",
    "SunExp-Dev\Mechanics\DialogueFlowRegistry.cs",
    "SunExp-Dev\Mechanics\DialogueFlowService.cs",
    "SunExp-Dev\Mechanics\SunExpConfigIndex.cs",
    "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs",
    "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs",
    "SunExp-Dev\Mechanics\MapNodeSafetyService.cs",
    "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs",
    "SunExp-Dev\Mechanics\EndlessSeaNodeKind.cs",
    "SunExp-Dev\Mechanics\EndlessSeaNodePoolService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaEnemyPool.cs",
    "SunExp-Dev\Mechanics\EndlessSeaRewardPlan.cs",
    "SunExp-Dev\Mechanics\EndlessAbyssEnemyInjectionService.cs",
    "SunExp-Dev\Mechanics\EmberAdventureStateService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaPressureService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaOriginService.cs",
    "SunExp-Dev\Mechanics\CardAttachmentSpec.cs",
    "SunExp-Dev\Mechanics\EndlessSeaCardAffixService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaRunStateStore.cs",
    "SunExp-Dev\Mechanics\EndlessSeaFloorPlan.cs",
    "SunExp-Dev\Mechanics\EndlessSeaFloorPlanner.cs",
    "SunExp-Dev\Mechanics\EndlessSeaFloorPlanStore.cs",
    "SunExp-Dev\Mechanics\EndlessSeaMapProjectionService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaSelectableNodeDeckPlanner.cs",
    "SunExp-Dev\Mechanics\EndlessSeaMapBuilder.cs",
    "SunExp-Dev\Mechanics\EndlessSeaStarterDeckCatalog.cs",
    "SunExp-Dev\Mechanics\EndlessSeaRichTextSanitizer.cs",
    "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs",
    "SunExp-Dev\Mechanics\SolarFinaleStateService.cs",
    "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs",
    "SunExp-Dev\Mechanics\VisualRegistry.cs",
    "SunExp-Dev\Mechanics\VisualRegistryModels.cs",
    "SunExp-Dev\Mechanics\CardVisualSkinSpec.cs",
    "SunExp-Dev\Mechanics\CardVisualSkinRule.cs",
    "SunExp-Dev\Mechanics\CardVisualSkinRegistry.cs",
    "SunExp-Dev\Mechanics\CardVisualEffectTarget.cs",
    "SunExp-Dev\Mechanics\CardVisualEffectSpec.cs",
    "SunExp-Dev\Mechanics\CardVisualEffectRegistry.cs",
    "SunExp-Dev\Mechanics\CardVisualInterestIndex.cs",
    "SunExp-Dev\Mechanics\CardVisualThemeCatalog.cs",
    "SunExp-Dev\Mechanics\SunCardThemeCatalog.cs",
    "SunExp-Dev\Mechanics\SunExpCardRefreshQueue.cs",
    "SunExp-Dev\Mechanics\CardGrantPostCommitQueue.cs",
    "SunExp-Dev\Mechanics\CompanionBattleModels.cs",
    "SunExp-Dev\Mechanics\CompanionBattleStateStore.cs",
    "SunExp-Dev\Mechanics\CompanionIntentExecutor.cs",
    "SunExp-Dev\Mechanics\CompanionIntentRegistry.cs",
    "SunExp-Dev\Mechanics\CompanionIntentSelector.cs",
    "SunExp-Dev\Mechanics\CompanionSlotService.cs",
    "SunExp-Dev\Mechanics\CompanionStatsService.cs",
    "SunExp-Dev\Mechanics\CompanionThreatService.cs",
    "SunExp-Dev\Mechanics\FamiliarGrowthModels.cs",
    "SunExp-Dev\Mechanics\FamiliarSpeciesCatalog.cs",
    "SunExp-Dev\Mechanics\FamiliarBlessingRegistry.cs",
    "SunExp-Dev\Mechanics\FamiliarBlessingRoller.cs",
    "SunExp-Dev\Mechanics\FamiliarRosterService.cs",
    "SunExp-Dev\Mechanics\FamiliarGrowthService.cs",
    "SunExp-Dev\Mechanics\ProjectionActivationService.cs",
    "SunExp-Dev\Mechanics\ProjectionOtherObj.cs",
    "SunExp-Dev\Mechanics\ProjectionState.cs",
    "SunExp-Dev\Mechanics\ProjectionStateStore.cs",
    "SunExp-Dev\Mechanics\ProjectionStrategyService.cs",
    "SunExp-Dev\Mechanics\ProjectionSummonService.cs",
    "SunExp-Dev\Mechanics\StarStonePouchService.cs",
    "SunExp-Dev\Hooks\DialogueFlowRuntime.cs",
    "SunExp-Dev\Hooks\SunExpHookTargets.cs",
    "SunExp-Dev\Hooks\SunExpHookRegistry.cs",
    "AuraSharedCore\AuraCardLifecycleRouter.cs",
    "SunExp-Dev\Hooks\SunExpBattleLifecycleRouter.cs",
    "SunExp-Dev\Hooks\SunExpCardLifecycleRouter.cs",
    "SunExp-Dev\Hooks\SunExpCombatActionRouter.cs",
    "SunExp-Dev\Hooks\SunExpStatusLifecycleRouter.cs",
    "SunExp-Dev\Hooks\SunExpCardPresentationRouter.cs",
    "SunExp-Dev\Hooks\SunExpCardPresentationLifecycleBridge.cs",
    "SunExp-Dev\Hooks\SunExpFrameScheduler.cs",
    "SunExp-Dev\Hooks\SunExpActionEventRouter.cs",
    "SunExp-Dev\Hooks\SunExpResourcePreloader.cs",
    "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs",
    "SunExp-Dev\Hooks\FamiliarGrowthRuntime.cs",
    "SunExp-Dev\Hooks\CompanionThreatRuntime.cs",
    "SunExp-Dev\Hooks\ProjectionRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs",
    "SunExp-Dev\Hooks\EmberAdventureStateRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaRewardRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaCardAffixRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaCombatRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaModeRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaModeEntryRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaRunLauncher.cs",
    "SunExp-Dev\Hooks\EndlessSeaSaveCacheRuntime.cs",
    "SunExp-Dev\Hooks\EndlessSeaIntroBoardRuntime.cs",
    "SunExp-Dev\Hooks\Ui\EndlessSeaMapViewPresenter.cs",
    "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs",
    "SunExp-Dev\Hooks\CardVisualSkinRuntime.cs",
    "SunExp-Dev\Hooks\SunCardFrameRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryMapVisualRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryModeEntryRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs",
    "SunExp-Dev\Hooks\SolarMemorySettlementPresenter.cs",
    "SunExp-Dev\Hooks\StarScoreHudRuntime.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudAssets.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudShaderController.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudShaderMaterials.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudView.cs",
    "SunExp-Dev\Hooks\Ui\StarScoreHudTooltipView.cs",
    "SunExp-Dev\Hooks\Ui\SunExpModalHost.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiLifetimeScope.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiPool.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiComponents.cs",
    "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs",
    "SunExp-Dev\Hooks\Ui\EndlessAbyssFramedTextCard.cs",
    "SunExp-Dev\Hooks\Ui\EndlessAbyssShockPanel.cs",
    "SunExp-Dev\Hooks\Ui\EndlessAbyssMilestoneRewardPanel.cs",
    "SunExp-Dev\Hooks\Ui\PolymorphRoleSelectionRequest.cs",
    "SunExp-Dev\Hooks\Ui\FamiliarGrowthPanel.cs",
    "SunExp-Dev\Scripting\FamiliarGrowthScripts.cs",
    "SunExp-Dev\Scripting\ProjectionScripts.cs",
    "SunExp-Dev\Hooks\Visual\FrameAnimationAttacher.cs",
    "SunExp-Dev\Hooks\Visual\AssetBundleCache.cs",
    "SunExp-Dev\Hooks\Visual\EffectMaterialFactory.cs",
    "SunExp-Dev\Hooks\Visual\EffectTextureCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameImageAnimator.cs",
    "SunExp-Dev\Hooks\Visual\CardVisualSkinApplier.cs",
    "SunExp-Dev\Hooks\Visual\CardPresentationRootResolver.cs",
    "SunExp-Dev\Hooks\Visual\CardVisualEffectApplier.cs",
    "SunExp-Dev\Hooks\Visual\CardFaceEffectApplier.cs",
    "SunExp-Dev\Hooks\Visual\CardFaceEffectMaterials.cs",
    "SunExp-Dev\Hooks\Visual\CardFrameEffectApplier.cs",
    "SunExp-Dev\Hooks\Visual\CardFrameEffectMaterials.cs",
    "SunExp-Dev\Hooks\Visual\CardFrameOverlay.cs",
    "SunExp-Dev\Hooks\Visual\CardVisualSkinSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\SunCardFrameApplier.cs",
    "SunExp-Dev\Hooks\Visual\SunCardFrameSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteAnimationSpec.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteRendererAnimator.cs",
    "SunExp-Dev\Hooks\Visual\ShaderAssetLoader.cs",
    "SunExp-Dev\Hooks\Visual\VisualBundleRuntimeValidator.cs",
    "SunExp-Dev\Hooks\Visual\WunaOrbitFireController.cs",
    "SunExp-Dev\VisualAssets\sunexp_visuals.pipeline.json",
    "SunExp-Dev\VisualAssets\Editor\SunExpVisualBundleBuilder.cs.txt",
    "SunExp-Dev\VisualAssets\Shaders\StarScoreHud.shader",
    "SunExp-Dev\VisualAssets\Shaders\CardFaceEffect.shader",
    "SunExp-Dev\VisualAssets\Shaders\CardFrameHoloFlow.shader",
    "tools\Build-SunExpVisualBundle.ps1",
    "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs",
    "SunExp-Dev\Mechanics\StarScoreDisplaySnapshot.cs",
    "SunExp-Dev\Mechanics\StarScoreNote.cs",
    "SunExp-Dev\Network\RpcEmberAdventureStateCommit.cs",
    "SunExp\visual.registry.json",
    "SunExp\familiar.blessing.registry.json"
)

foreach ($file in $requiredFiles) {
    Assert-True (Test-Path -LiteralPath (Join-Path $RepoRoot $file)) "Architecture file is missing: $file"
}

$executorApi = Read-RepoText "SunExp-Dev\GameApi\ExecutorApi.cs"
$fieldApi = Read-RepoText "SunExp-Dev\GameApi\FieldApi.cs"
$buffOverflowApi = Read-RepoText "SunExp-Dev\GameApi\BuffOverflowApi.cs"
$mapItemApi = Read-RepoText "SunExp-Dev\GameApi\MapItemApi.cs"
$cardVisualSkinApi = Read-RepoText "SunExp-Dev\GameApi\CardVisualSkinApi.cs"
$cardVisualEffectApi = Read-RepoText "SunExp-Dev\GameApi\CardVisualEffectApi.cs"
$dialogueApi = Read-RepoText "SunExp-Dev\GameApi\DialogueApi.cs"
$dialogueUiApi = Read-RepoText "SunExp-Dev\GameApi\DialogueUiApi.cs"
$battleRewardApi = Read-RepoText "SunExp-Dev\GameApi\BattleRewardApi.cs"
$cardApi = Read-RepoText "SunExp-Dev\GameApi\CardApi.cs"
$enemyApi = Read-RepoText "SunExp-Dev\GameApi\EnemyApi.cs"
$cardConfigApi = Read-RepoText "SunExp-Dev\GameApi\CardConfigApi.cs"
$familiarGrowthApi = Read-RepoText "SunExp-Dev\GameApi\FamiliarGrowthApi.cs"
$sunExpResourceCache = Read-RepoText "SunExp-Dev\GameApi\SunExpResourceCache.cs"
$dirtyState = Read-RepoText "SunExp-Dev\Infrastructure\SunExpDirtyState.cs"
$statusApi = Read-RepoText "SunExp-Dev\GameApi\StatusApi.cs"
$performanceSettings = Read-RepoText "SunExp-Dev\Infrastructure\SunExpPerformanceSettings.cs"
$performanceCounters = Read-RepoText "SunExp-Dev\Infrastructure\SunExpPerformanceCounters.cs"
$sunExpFrameDispatcher = Read-RepoText "SunExp-Dev\Infrastructure\SunExpFrameDispatcher.cs"
$cardScripts = Read-RepoText "SunExp-Dev\Scripting\CardScripts.cs"
$buffScripts = Read-RepoText "SunExp-Dev\Scripting\BuffScripts.cs"
$relicScripts = Read-RepoText "SunExp-Dev\Scripting\RelicScripts.cs"
$eventScripts = Read-RepoText "SunExp-Dev\Scripting\EventScripts.cs"
$bossScripts = Read-RepoText "SunExp-Dev\Scripting\BossScripts.cs"
$projectionScripts = Read-RepoText "SunExp-Dev\Scripting\ProjectionScripts.cs"
$mapNodeCardArtRegistry = Read-RepoText "SunExp-Dev\Mechanics\MapNodeCardArtRegistry.cs"
$mapNodeTextureFitService = Read-RepoText "SunExp-Dev\Mechanics\MapNodeTextureFitService.cs"
$mapNodeSafetyService = Read-RepoText "SunExp-Dev\Mechanics\MapNodeSafetyService.cs"
$solarMemoryMapNodePoolFactory = Read-RepoText "SunExp-Dev\Mechanics\SolarMemoryMapNodePoolFactory.cs"
$endlessSeaNodePoolService = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaNodePoolService.cs"
$endlessSeaEnemyPool = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaEnemyPool.cs"
$endlessSeaRewardPlan = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaRewardPlan.cs"
$endlessAbyssEnemyInjectionService = Read-RepoText "SunExp-Dev\Mechanics\EndlessAbyssEnemyInjectionService.cs"
$emberAdventureStateService = Read-RepoText "SunExp-Dev\Mechanics\EmberAdventureStateService.cs"
$endlessSeaPressureService = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaPressureService.cs"
$endlessSeaOriginService = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaOriginService.cs"
$endlessSeaCardAffixService = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaCardAffixService.cs"
$endlessSeaRunStateStore = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaRunStateStore.cs"
$endlessSeaFloorPlan = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaFloorPlan.cs"
$endlessSeaFloorPlanner = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaFloorPlanner.cs"
$endlessSeaFloorPlanStore = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaFloorPlanStore.cs"
$endlessSeaMapProjectionService = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaMapProjectionService.cs"
$endlessSeaSelectableNodeDeckPlanner = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaSelectableNodeDeckPlanner.cs"
$endlessSeaMapBuilder = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaMapBuilder.cs"
$endlessSeaStarterDeckCatalog = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaStarterDeckCatalog.cs"
$endlessSeaRichTextSanitizer = Read-RepoText "SunExp-Dev\Mechanics\EndlessSeaRichTextSanitizer.cs"
$sunExpConfigIndex = Read-RepoText "SunExp-Dev\Mechanics\SunExpConfigIndex.cs"
$modeChoiceDragRange = Read-RepoText "SunExp-Dev\Mechanics\ModeChoiceDragRange.cs"
$solarFinaleService = Read-RepoText "SunExp-Dev\Mechanics\SolarFinaleStateService.cs"
$visualRegistry = Read-RepoText "SunExp-Dev\Mechanics\VisualRegistry.cs"
$visualRegistryModels = Read-RepoText "SunExp-Dev\Mechanics\VisualRegistryModels.cs"
$visualRegistryJson = Read-RepoText "SunExp\visual.registry.json"
$dialogueFlowService = Read-RepoText "SunExp-Dev\Mechanics\DialogueFlowService.cs"
$battleRewardAdjustmentService = Read-RepoText "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs"
$solarMemoryStoryGateService = Read-RepoText "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs"
$dialogueFlowRuntime = Read-RepoText "SunExp-Dev\Hooks\DialogueFlowRuntime.cs"
$sunExpFrameScheduler = Read-RepoText "SunExp-Dev\Hooks\SunExpFrameScheduler.cs"
$sunExpActionEventRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpActionEventRouter.cs"
$sunExpCardRefreshQueue = Read-RepoText "SunExp-Dev\Mechanics\SunExpCardRefreshQueue.cs"
$cardGrantPostCommitQueue = Read-RepoText "SunExp-Dev\Mechanics\CardGrantPostCommitQueue.cs"
$companionBattleModels = Read-RepoText "SunExp-Dev\Mechanics\CompanionBattleModels.cs"
$companionBattleStateStore = Read-RepoText "SunExp-Dev\Mechanics\CompanionBattleStateStore.cs"
$companionIntentExecutor = Read-RepoText "SunExp-Dev\Mechanics\CompanionIntentExecutor.cs"
$companionIntentRegistry = Read-RepoText "SunExp-Dev\Mechanics\CompanionIntentRegistry.cs"
$companionIntentSelector = Read-RepoText "SunExp-Dev\Mechanics\CompanionIntentSelector.cs"
$companionSlotService = Read-RepoText "SunExp-Dev\Mechanics\CompanionSlotService.cs"
$companionStatsService = Read-RepoText "SunExp-Dev\Mechanics\CompanionStatsService.cs"
$companionThreatService = Read-RepoText "SunExp-Dev\Mechanics\CompanionThreatService.cs"
$familiarGrowthModels = Read-RepoText "SunExp-Dev\Mechanics\FamiliarGrowthModels.cs"
$familiarSpeciesCatalog = Read-RepoText "SunExp-Dev\Mechanics\FamiliarSpeciesCatalog.cs"
$familiarBlessingRegistry = Read-RepoText "SunExp-Dev\Mechanics\FamiliarBlessingRegistry.cs"
$familiarBlessingRoller = Read-RepoText "SunExp-Dev\Mechanics\FamiliarBlessingRoller.cs"
$familiarRosterService = Read-RepoText "SunExp-Dev\Mechanics\FamiliarRosterService.cs"
$familiarGrowthService = Read-RepoText "SunExp-Dev\Mechanics\FamiliarGrowthService.cs"
$familiarGrowthScripts = Read-RepoText "SunExp-Dev\Scripting\FamiliarGrowthScripts.cs"
$familiarGrowthRuntime = Read-RepoText "SunExp-Dev\Hooks\FamiliarGrowthRuntime.cs"
$familiarGrowthPanel = Read-RepoText "SunExp-Dev\Hooks\Ui\FamiliarGrowthPanel.cs"
$familiarBlessingRegistryJson = Read-RepoText "SunExp\familiar.blessing.registry.json"
$companionIntentRegistryJson = Read-RepoText "SunExp\companion.intent.registry.json"
$projectionActivationService = Read-RepoText "SunExp-Dev\Mechanics\ProjectionActivationService.cs"
$projectionOtherObj = Read-RepoText "SunExp-Dev\Mechanics\ProjectionOtherObj.cs"
$projectionStateStore = Read-RepoText "SunExp-Dev\Mechanics\ProjectionStateStore.cs"
$projectionStrategyService = Read-RepoText "SunExp-Dev\Mechanics\ProjectionStrategyService.cs"
$projectionSummonService = Read-RepoText "SunExp-Dev\Mechanics\ProjectionSummonService.cs"
$sunExpResourcePreloader = Read-RepoText "SunExp-Dev\Hooks\SunExpResourcePreloader.cs"
$battleRewardAdjustmentRuntime = Read-RepoText "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs"
$companionThreatRuntime = Read-RepoText "SunExp-Dev\Hooks\CompanionThreatRuntime.cs"
$solarMemoryRewardRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs"
$endlessSeaRewardRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaRewardRuntime.cs"
$endlessSeaCardAffixRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaCardAffixRuntime.cs"
$endlessSeaCombatRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaCombatRuntime.cs"
$endlessSeaModeRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaModeRuntime.cs"
$endlessSeaModeEntryRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaModeEntryRuntime.cs"
$endlessSeaRunLauncher = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaRunLauncher.cs"
$endlessSeaIntroBoardRuntime = Read-RepoText "SunExp-Dev\Hooks\EndlessSeaIntroBoardRuntime.cs"
$endlessSeaMapViewPresenter = Read-RepoText "SunExp-Dev\Hooks\Ui\EndlessSeaMapViewPresenter.cs"
$endlessSeaNetworkSync = Read-RepoText "SunExp-Dev\Network\EndlessSeaNetworkSync.cs"
$sunExpNetworkRuntime = Read-RepoText "SunExp-Dev\Network\SunExpNetworkRuntime.cs"
$mapNodeCardArtRuntime = Read-RepoText "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"
$projectionRuntime = Read-RepoText "SunExp-Dev\Hooks\ProjectionRuntime.cs"
$runtimeHooks = Read-RepoText "SunExp-Dev\Hooks\RuntimeHooks.cs"
$sunExpHookTargets = Read-RepoText "SunExp-Dev\Hooks\SunExpHookTargets.cs"
$sunExpHookRegistry = Read-RepoText "SunExp-Dev\Hooks\SunExpHookRegistry.cs"
$sunExpBattleLifecycleRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpBattleLifecycleRouter.cs"
$auraBattleLifecycleRouter = Read-RepoText "AuraSharedCore\AuraBattleLifecycleRouter.cs"
$auraCardLifecycleRouter = Read-RepoText "AuraSharedCore\AuraCardLifecycleRouter.cs"
$sunExpCardLifecycleRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpCardLifecycleRouter.cs"
$sunExpCombatActionRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpCombatActionRouter.cs"
$sunExpStatusLifecycleRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpStatusLifecycleRouter.cs"
$sunExpCardPresentationRouter = Read-RepoText "SunExp-Dev\Hooks\SunExpCardPresentationRouter.cs"
$sunExpCardPresentationLifecycleBridge = Read-RepoText "SunExp-Dev\Hooks\SunExpCardPresentationLifecycleBridge.cs"
$emberAdventureStateRuntime = Read-RepoText "SunExp-Dev\Hooks\EmberAdventureStateRuntime.cs"
$cardVisualSkinSpec = Read-RepoText "SunExp-Dev\Mechanics\CardVisualSkinSpec.cs"
$cardVisualSkinRule = Read-RepoText "SunExp-Dev\Mechanics\CardVisualSkinRule.cs"
$cardVisualSkinRegistry = Read-RepoText "SunExp-Dev\Mechanics\CardVisualSkinRegistry.cs"
$cardVisualEffectTarget = Read-RepoText "SunExp-Dev\Mechanics\CardVisualEffectTarget.cs"
$cardVisualEffectSpec = Read-RepoText "SunExp-Dev\Mechanics\CardVisualEffectSpec.cs"
$cardVisualEffectRegistry = Read-RepoText "SunExp-Dev\Mechanics\CardVisualEffectRegistry.cs"
$cardVisualInterestIndex = Read-RepoText "SunExp-Dev\Mechanics\CardVisualInterestIndex.cs"
$cardVisualThemeCatalog = Read-RepoText "SunExp-Dev\Mechanics\CardVisualThemeCatalog.cs"
$cardMutationService = Read-RepoText "SunExp-Dev\Mechanics\CardMutationService.cs"
$runtimeCardAttachmentService = Read-RepoText "SunExp-Dev\Mechanics\RuntimeCardAttachmentService.cs"
$cardVisualSkinRuntime = Read-RepoText "SunExp-Dev\Hooks\CardVisualSkinRuntime.cs"
$polymorphCardFaceRuntime = Read-RepoText "SunExp-Dev\Hooks\Visual\PolymorphCardFaceRuntime.cs"
$sunExpUiComponents = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiComponents.cs"
$endlessAbyssFramedTextCard = Read-RepoText "SunExp-Dev\Hooks\Ui\EndlessAbyssFramedTextCard.cs"
$endlessAbyssShockPanel = Read-RepoText "SunExp-Dev\Hooks\Ui\EndlessAbyssShockPanel.cs"
$endlessAbyssMilestoneRewardPanel = Read-RepoText "SunExp-Dev\Hooks\Ui\EndlessAbyssMilestoneRewardPanel.cs"
$cardVisualSkinMarker = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualSkinMarker.cs"
$cardVisualSkinApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualSkinApplier.cs"
$cardVisualEffectApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualEffectApplier.cs"
$cardFaceEffectApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\CardFaceEffectApplier.cs"
$cardFaceEffectMaterials = Read-RepoText "SunExp-Dev\Hooks\Visual\CardFaceEffectMaterials.cs"
$cardFrameEffectApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\CardFrameEffectApplier.cs"
$cardFrameEffectMaterials = Read-RepoText "SunExp-Dev\Hooks\Visual\CardFrameEffectMaterials.cs"
$cardFrameOverlay = Read-RepoText "SunExp-Dev\Hooks\Visual\CardFrameOverlay.cs"
$cardVisualSkinSpriteCache = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualSkinSpriteCache.cs"
$sunCardFrameRuntime = Read-RepoText "SunExp-Dev\Hooks\SunCardFrameRuntime.cs"
$sunCardFrameApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\SunCardFrameApplier.cs"
$sunCardFrameSpriteCache = Read-RepoText "SunExp-Dev\Hooks\Visual\SunCardFrameSpriteCache.cs"
$sunCardThemeCatalog = Read-RepoText "SunExp-Dev\Mechanics\SunCardThemeCatalog.cs"
$solarMemoryModeRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"
$solarMemoryMapVisualRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryMapVisualRuntime.cs"
$solarMemoryMapItemAnimationRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryMapItemAnimationRuntime.cs"
$solarMemoryModeEntryRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeEntryRuntime.cs"
$solarMemoryContentIsolationRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryContentIsolationRuntime.cs"
$solarMemorySettlementPresenter = Read-RepoText "SunExp-Dev\Hooks\SolarMemorySettlementPresenter.cs"
$animatedBlessingIconRuntime = Read-RepoText "SunExp-Dev\Hooks\AnimatedBlessingIconRuntime.cs"
$animatedBuffIconRuntime = Read-RepoText "SunExp-Dev\Hooks\AnimatedBuffIconRuntime.cs"
$animatedEnemyDictIconRuntime = Read-RepoText "SunExp-Dev\Hooks\AnimatedEnemyDictIconRuntime.cs"
$assetBundleCache = Read-RepoText "SunExp-Dev\Hooks\Visual\AssetBundleCache.cs"
$effectMaterialFactory = Read-RepoText "SunExp-Dev\Hooks\Visual\EffectMaterialFactory.cs"
$effectTextureCache = Read-RepoText "SunExp-Dev\Hooks\Visual\EffectTextureCache.cs"
$frameAnimationAttacher = Read-RepoText "SunExp-Dev\Hooks\Visual\FrameAnimationAttacher.cs"
$frameSpriteCache = Read-RepoText "SunExp-Dev\Hooks\Visual\FrameSpriteCache.cs"
$frameImageAnimator = Read-RepoText "SunExp-Dev\Hooks\Visual\FrameImageAnimator.cs"
$frameSpriteRendererAnimator = Read-RepoText "SunExp-Dev\Hooks\Visual\FrameSpriteRendererAnimator.cs"
$shaderAssetLoader = Read-RepoText "SunExp-Dev\Hooks\Visual\ShaderAssetLoader.cs"
$visualBundleRuntimeValidator = Read-RepoText "SunExp-Dev\Hooks\Visual\VisualBundleRuntimeValidator.cs"
$wunaOrbitFireController = Read-RepoText "SunExp-Dev\Hooks\Visual\WunaOrbitFireController.cs"
$visualPipeline = Read-RepoText "SunExp-Dev\VisualAssets\sunexp_visuals.pipeline.json"
$visualBundleBuilder = Read-RepoText "SunExp-Dev\VisualAssets\Editor\SunExpVisualBundleBuilder.cs.txt"
$starScoreHudShaderSource = Read-RepoText "SunExp-Dev\VisualAssets\Shaders\StarScoreHud.shader"
$cardFaceEffectShaderSource = Read-RepoText "SunExp-Dev\VisualAssets\Shaders\CardFaceEffect.shader"
$cardFrameHoloShaderSource = Read-RepoText "SunExp-Dev\VisualAssets\Shaders\CardFrameHoloFlow.shader"
$visualBundleBuildScript = Read-RepoText "tools\Build-SunExpVisualBundle.ps1"
$starScoreService = Read-RepoText "SunExp-Dev\Mechanics\StarScoreService.cs"
$starStonePouchService = Read-RepoText "SunExp-Dev\Mechanics\StarStonePouchService.cs"
$starScoreRuntime = Read-RepoText "SunExp-Dev\Hooks\StarScoreRuntime.cs"
$specialTagRuntime = Read-RepoText "SunExp-Dev\Hooks\SpecialTagRuntime.cs"
$loneerRuntime = Read-RepoText "SunExp-Dev\Hooks\LoneerRuntime.cs"
$starScoreHudRuntime = Read-RepoText "SunExp-Dev\Hooks\StarScoreHudRuntime.cs"
$starScoreHudView = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudView.cs"
$starScoreHudHoverProbe = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudHoverProbe.cs"
$starScoreHudAssets = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudAssets.cs"
$starScoreHudShaderController = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudShaderController.cs"
$starScoreHudShaderMaterials = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudShaderMaterials.cs"
$starScoreHudTooltipView = Read-RepoText "SunExp-Dev\Hooks\Ui\StarScoreHudTooltipView.cs"
$starScoreCadenceCatalog = Read-RepoText "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs"
$sunExpIds = Read-RepoText "SunExp-Dev\Infrastructure\SunExpIds.cs"
$entrySource = Read-RepoText "SunExp-Dev\Entry.cs"
$sunExpProject = Read-RepoText "SunExp-Dev\SunExp.Dll.csproj"
$modeChoiceEntryDefinition = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryDefinition.cs"
$modeChoiceEntryRegistry = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceEntryRegistry.cs"
$modeChoiceLayoutRuntime = Read-RepoText "SunExp-Dev\Hooks\ModeChoiceLayoutRuntime.cs"
$solarMemoryPreparationRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryPreparationRuntime.cs"
$solarMemoryRunLauncher = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRunLauncher.cs"
$solarMemoryStarterDeckRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryStarterDeckRuntime.cs"
$solarMemorySetupFlowRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemorySetupFlowRuntime.cs"
$solarMemoryBlessingPickerRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryBlessingPickerRuntime.cs"
$sunExpModalHost = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpModalHost.cs"
$sunExpUiSafety = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiSafety.cs"
$sunExpUiLifetimeScope = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiLifetimeScope.cs"
$sunExpUiPool = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiPool.cs"
$sunExpUiSprites = Read-RepoText "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs"
$sunExpHardTagRuntime = Read-RepoText "SunExp-Dev\Hooks\SunExpHardTagRuntime.cs"
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp-Dev") -Recurse -File -Filter "*.cs"
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
Assert-Contains $battleRewardApi "public static bool AppendRandomCardRewards" "BattleRewardApi must own native random card reward mutation."
Assert-Contains $battleRewardApi "rewardUi.RandomSetCard()" "Random card rewards must reuse the native BattleRewardsUI card flow."
Assert-Contains $battleRewardAdjustmentService "public static class BattleRewardAdjustmentService" "Reusable battle reward adjustment rules must live in Mechanics."
Assert-Contains $battleRewardAdjustmentService "ConditionalWeakTable<BattleRewardsUI, AppliedRuleSet>" "Battle reward adjustments must be applied once per reward UI."
Assert-Contains $battleRewardAdjustmentRuntime '"BattleRewardsUI.ModeSetReward"' "Battle reward adjustment runtime must hook reward generation after native rewards are set."
Assert-Contains $battleRewardAdjustmentRuntime "BattleRewardAdjustmentService.ApplyAll" "Battle reward hooks must delegate rule application to Mechanics."
Assert-Contains $solarMemoryRewardRuntime "SolarMemoryModeRuntime.IsSolarMemoryRun()" "Solar memory reward adjustments must be gated to Solar Memory runs."
Assert-Contains $solarMemoryRewardRuntime "BattleRewardApi.AppendRandomRelicReward" "Solar memory reward runtime must add its relic through BattleRewardApi."
Assert-Contains $endlessSeaRewardRuntime "EndlessSeaModeRuntime.IsEndlessSeaRun()" "Endless Sea reward adjustments must be gated to Endless Sea runs."
Assert-Contains $endlessSeaRewardRuntime "BattleRewardApi.ReplaceWithRewardSpec" "Endless Sea battle rewards must replace native rewards with the Endless Sea reward plan."
Assert-Contains $endlessSeaCardAffixRuntime "AfterCardChoiceItemInitialize = ApplyToChoiceItem" "Endless Sea reward-card affixes must update visible choice cards through the card lifecycle router."
Assert-Contains $endlessSeaCardAffixRuntime "BeforeCardChoiceUiSelect = ApplyToSelectedCard" "Endless Sea reward-card affixes must be applied before cards enter the deck through the card lifecycle router."
Assert-Contains $endlessSeaCardAffixRuntime "EndlessSeaCardAffixService.ApplyBurnout" "Endless Sea card affix hooks must delegate Burnout application to the shared service."
Assert-Contains $endlessSeaCardAffixRuntime "EndlessSeaCardAffixService.NormalizeOwnedCards" "Endless Sea card affix hooks must normalize cards gained outside reward choices."
Assert-Contains $endlessSeaCardAffixService "CardAttachmentService.AttachToConfig" "Endless Sea card affix service must use the shared card attachment service."
Assert-Contains $endlessSeaCardAffixService "EndlessSeaStarterDeckBaselineMarker" "Endless Sea card affix service must protect starter deck baseline cards."
Assert-Contains $endlessSeaCardAffixService "RunWithStarterDeckSuppressed" "Endless Sea starter deck writes must suppress automatic Burnout attachment."
Assert-Contains $endlessSeaCardAffixService '"Burnout"' "Endless Sea gained cards must receive the native Burnout limiter."
Assert-Contains $endlessSeaCardAffixService "role.cardList" "Endless Sea card affix service must normalize equipped owned cards."
Assert-Contains $endlessSeaCardAffixService "role.UnCardList" "Endless Sea card affix service must normalize reserve owned cards."
Assert-Contains $endlessSeaCardAffixRuntime "EndlessSeaModeRuntime.IsEndlessSeaRun()" "Endless Sea reward-card affixes must be gated to Endless Sea runs."
Assert-Contains $runtimeHooks "BattleRewardAdjustmentRuntime.Initialize(modConfig)" "RuntimeHooks must initialize generic battle reward adjustment hooks."
Assert-Contains $runtimeHooks "SolarMemoryRewardRuntime.Initialize()" "RuntimeHooks must register Solar Memory reward adjustment rules."
Assert-Contains $runtimeHooks "EndlessSeaRewardRuntime.Initialize(modConfig)" "RuntimeHooks must register Endless Sea battle reward replacement hooks."
Assert-Contains $runtimeHooks "EndlessSeaCardAffixRuntime.Initialize(modConfig)" "RuntimeHooks must register Endless Sea reward-card affixes."
Assert-Contains $runtimeHooks "EndlessSeaIntroBoardRuntime.Initialize(modConfig)" "RuntimeHooks must register Endless Sea intro board."
Assert-Contains $runtimeHooks "StarScoreHudRuntime.Initialize(modConfig)" "RuntimeHooks must initialize star score HUD hooks."
Assert-Contains $runtimeHooks "CardVisualSkinRuntime.Initialize(modConfig)" "RuntimeHooks must initialize card visual skin hooks."
Assert-Contains $runtimeHooks "ProjectionRuntime.Initialize(modConfig)" "RuntimeHooks must initialize projection combat hooks."
Assert-Contains $runtimeHooks "RunHookStep(" "RuntimeHooks must isolate runtime initialization into logged hook steps."
Assert-Contains $runtimeHooks "AuraSharedHooks.RunStep" "RuntimeHooks must use the shared step guard for hook initialization."
Assert-Contains $runtimeHooks "SunExpBattleLifecycleRouter.Initialize(modConfig)" "RuntimeHooks must initialize the shared battle lifecycle router before feature handlers register."
Assert-Contains $runtimeHooks "SunExpCardLifecycleRouter.Initialize(modConfig)" "RuntimeHooks must initialize the shared card lifecycle router before feature handlers register."
Assert-Contains $sunExpHookRegistry "RegisterBeforeRouted" "SunExp hook registry must expose routed before-hook registration."
Assert-Contains $sunExpHookRegistry "RegisterAfterRouted" "SunExp hook registry must expose routed after-hook registration."
Assert-Contains $sunExpBattleLifecycleRouter "AuraBattleLifecycleRouter.Register" "SunExp battle lifecycle router must delegate native hook ownership to Aura shared lifecycle."
Assert-Contains $auraBattleLifecycleRouter "FightStartInit" "Aura battle lifecycle router must own fight-start native hooks."
Assert-Contains $auraBattleLifecycleRouter "FightWinResetStates" "Aura battle lifecycle router must own fight-ending native hooks."
Assert-Contains $sunExpCardLifecycleRouter "AuraCardLifecycleRouter.Register" "SunExp card lifecycle router must delegate native hook ownership to Aura shared lifecycle."
Assert-NotContains $sunExpCardLifecycleRouter "SunExpHookTargets.CardItemInit" "SunExp card lifecycle router must not own native card-item init hooks."
Assert-NotContains $sunExpCardLifecycleRouter "SunExpHookTargets.CommonCardItemTrueUse" "SunExp card lifecycle router must not own native common-card use hooks."
Assert-Contains $auraCardLifecycleRouter "CardItemInit" "Aura card lifecycle router must own shared card-item init hooks."
Assert-Contains $auraCardLifecycleRouter "CommonCardItemTrueUse" "Aura card lifecycle router must own shared common-card use hooks."
Assert-Contains $auraCardLifecycleRouter "AuraCardLifecyclePhase" "Aura card lifecycle router must expose stable phase identifiers for shared dispatch."
Assert-Contains $auraCardLifecycleRouter "RegisteredPhases" "Aura card lifecycle router must install native card hooks lazily per subscribed phase."
Assert-Contains $auraCardLifecycleRouter "EnsurePhaseRegistrationsNoLock" "Aura card lifecycle router must avoid full-stage native hook registration for unrelated subscribers."
Assert-Contains $sunExpHookTargets "public const string FightStartInit" "Hook target names must be centralized in SunExpHookTargets."
Assert-Contains $endlessSeaCardAffixRuntime "SunExpCardLifecycleRouter.Register(`"EndlessSeaCardAffix`"" "Endless Sea card affix runtime must register through the shared card lifecycle router."
Assert-Contains $endlessSeaCardAffixRuntime "EnsureCardLifecycleRegisteredForEndlessSea" "Endless Sea card affix runtime must delay card hot-path hook registration until Endless Sea is active."
Assert-Contains $starScoreRuntime "SunExpCardLifecycleRouter.Register(`"StarScore`"" "Star score runtime must register card-use hooks through the shared card lifecycle router."
Assert-Contains $starScoreRuntime "HasSelectionPreviewInterest" "Star score drag hooks must return before preview work when no StarScore/AbyssGaze state is active."
Assert-Contains $starScoreRuntime "HasCardUseInterest" "Star score card-use hooks must return before action-router work when no StarScore/AbyssGaze state is active."
Assert-Contains $sunExpHardTagRuntime "SunExpCardLifecycleRouter.Register(`"HardTag`"" "Hard-tag runtime must register card hot-path hooks through the shared card lifecycle router."
Assert-Contains $sunExpHardTagRuntime "EnsureCardLifecycleRegistered()" "Hard-tag runtime must delay card hot-path hook registration until a SunExp hard tag is active."
Assert-Contains $entrySource 'RunStep("performance runtime", () => SunExpFrameScheduler.Initialize(modConfig))' "Entry must initialize the performance scheduler before gameplay hooks."
Assert-NotContains $performanceSettings "SunExpPerformanceQuality" "Performance quality tiers must not re-enter unified runtime settings."
Assert-NotContains $performanceSettings "SunExpLowSpec" "Performance settings must not keep legacy high/low config keys."
Assert-Contains $performanceSettings 'typeof(ScriptExecutor).GetNestedType("PlayerInfo"' "Performance settings must read game variables without depending on GameApi."
Assert-NotContains $performanceSettings "using UnityEngine" "Performance settings must stay out of Unity lifecycle concerns."
Assert-Contains $performanceSettings "public static bool UiPoolEnabled" "Performance settings must expose a runtime UI pool feature gate."
Assert-Contains $performanceSettings "public static int UiPoolCapacityPerKey => 64;" "Performance settings must expose the unified pooled UI cap."
Assert-Contains $performanceCounters "Stopwatch.GetTimestamp()" "Performance counters must use low-allocation timestamp accounting."
Assert-Contains $dirtyState "public sealed class SunExpDirtyState" "Dirty-state gating must be centralized for repeated UI rebuilds."
Assert-Contains $dirtyState 'SunExpPerformanceCounters.Record("DirtyState.Skipped")' "Dirty-state skips must be measurable when counters are enabled."
Assert-Contains $sunExpFrameScheduler "public static bool RunOnceNextFrame" "Frame scheduler must expose a keyed next-frame merge API."
Assert-Contains $sunExpFrameScheduler "MonoBehaviour" "Frame scheduler must keep Unity lifecycle ownership inside Hooks."
Assert-Contains $sunExpFrameScheduler "SunExpPerformanceSettings.FrameSchedulerBudget" "Frame scheduler must respect the performance quality budget."
Assert-Contains $sunExpActionEventRouter 'AddEventListener("Action" + statusId' "SunExp Action listeners must be centralized in SunExpActionEventRouter."
Assert-Contains $sunExpActionEventRouter 'AddEventListener("ActionAfter" + statusId' "SunExp ActionAfter listeners must be centralized in SunExpActionEventRouter."
Assert-Contains $sunExpActionEventRouter "CardConfigApi.FromActionPayload(payload)" "The Action router must parse card payloads once before handler fanout."
Assert-Contains $cardScripts "[SunExpIds.ProjectionCardShortId] = UseProjection" "CardScripts must route the projection selection card."
Assert-Contains $cardScripts "[SunExpIds.ProjectionRoleTemplateShortId] = UseProjectionRoleCard" "CardScripts must route generated projection role cards."
Assert-Contains $runtimeHooks "CompanionIntentRegistry.Load(modConfig)" "RuntimeHooks must load companion intent registry before projection combat hooks."
Assert-Contains $runtimeHooks "CompanionThreatRuntime.Initialize(modConfig)" "RuntimeHooks must initialize companion threat targeting."
Assert-Contains $runtimeHooks "FamiliarGrowthRuntime.Initialize(modConfig)" "RuntimeHooks must initialize familiar growth through an isolated hook step."
Assert-Contains $entrySource "SunExp.Dll.Scripting.FamiliarGrowthScripts" "Entry must register FamiliarGrowthScripts for CSV-callable familiar operations."
Assert-Contains $familiarGrowthApi "FamiliarSidecarProfileStore" "Familiar growth persistent roster storage must stay behind the GameApi facade."
Assert-Contains $familiarGrowthApi "SelectedCanManifest" "Familiar growth API must expose manifest capability checks for future combat adapters."
Assert-Contains $familiarGrowthModels "IFamiliarProfileStore" "Familiar growth storage must be abstracted behind a reusable profile-store contract."
Assert-Contains $familiarGrowthModels "Aptitude" "Familiar instances must persist aptitude for tier-weighted blessing rolls."
Assert-Contains $familiarGrowthModels "PendingBlessingChoices" "Familiar instances must persist pending blessing choices until the player selects one."
Assert-Contains $familiarSpeciesCatalog "SunExpConfigIndex.Rows(DataType.Partner)" "Familiar species must be sourced from the registered Partner table."
Assert-Contains $familiarBlessingRegistry "FamiliarBlessingRegistryDocument" "Familiar blessing definitions must be data-driven through a registry document."
Assert-Contains $familiarBlessingRegistry "ManifestEnable" "Familiar blessing registry must reserve the manifest effect hook."
Assert-Contains $familiarBlessingRoller "TierWeights" "Familiar blessing rolls must use aptitude-specific tier weights."
Assert-Contains $familiarBlessingRoller "BodyDefaultAptitude = 70" "Familiar body instances must default to aptitude 70."
Assert-Contains $familiarRosterService "EnsureBody" "Familiar roster service must auto-create protected body instances."
Assert-Contains $familiarRosterService "instance.IsBody" "Familiar roster service must prevent deleting body instances."
Assert-Contains $familiarRosterService "EnqueueBlessingChoice" "Familiar level-ups must create a player-facing blessing choice."
Assert-Contains $familiarRosterService "ChooseBlessing" "Familiar roster service must apply selected pending blessing choices."
Assert-NotContains $familiarRosterService "GrantLevelBlessings" "Familiar level-ups must not auto-grant every eligible blessing."
Assert-Contains $familiarGrowthService "FamiliarRosterService.Normalize" "Familiar growth service must normalize rosters before exposing snapshots."
Assert-Contains $familiarGrowthApi "ChooseBlessing" "Familiar growth API must expose pending blessing selection through the GameApi facade."
Assert-Contains $familiarGrowthRuntime '"GameEntryUI.NormalGame"' "Familiar growth runtime must record the selected familiar at run start."
Assert-Contains $familiarGrowthRuntime "SunExpBattleLifecycleRouter.Register(`"FamiliarGrowth`"" "Familiar growth runtime must award battle-win experience through the shared battle lifecycle router."
Assert-Contains $familiarGrowthRuntime "HouseManager.Awake" "Familiar growth runtime must inject a house entry point without CSV glue."
Assert-Contains $familiarGrowthPanel "SunExpModalHost.Close" "Familiar growth UI must use the shared modal close path."
Assert-Contains $familiarGrowthPanel "SunExpUiPool.ReleaseOrDestroyChildren" "Familiar growth UI must use shared transient UI cleanup."
Assert-Contains $familiarGrowthScripts "FamiliarGrowthApi" "Familiar growth scripts must delegate CSV-callable behavior to GameApi."
Assert-Contains $familiarBlessingRegistryJson '"ManifestEnable"' "Familiar blessing registry JSON must declare the manifest capability effect."
Assert-Contains $familiarBlessingRegistryJson '"id": "*familiar_guard_paw"' "Familiar blessing registry JSON must keep familiar blessings isolated from native random blessing ids."
Assert-Contains $familiarBlessingRegistryJson '"tier": 5' "Familiar blessing registry JSON must include high-tier blessings for perfect aptitude rolls."
Assert-Contains $entrySource "SunExp.Dll.Scripting.ProjectionScripts" "Entry must register ProjectionScripts for generated enemy-card actions."
Assert-Contains $projectionActivationService "CardGrantRequest" "Projection generated cards must use the shared card grant API."
Assert-Contains $projectionActivationService "DictionaryUtil.Set(config.Vars" "Projection generated cards must write runtime overrides to Vars."
Assert-NotContains $projectionActivationService "DictionaryUtil.Set(config.data" "Projection generated cards must not mutate base config data."
Assert-Contains $projectionSummonService "RealPlayerCount() + ProjectionStateStore.ActiveCount()" "Projection summon must respect the four-unit friendly cap."
Assert-Contains $projectionSummonService 'SunExpResourceCache.Load<GameObject>("Model/player", true, "projection")' "Projection summon must load the player model through the shared resource cache."
Assert-Contains $projectionSummonService "SunExpIds.ProjectionActionStaffTapCardId" "Projection summon must attach the shared staff-tap action."
Assert-Contains $projectionSummonService "SunExpIds.ProjectionActionShieldBlessingCardId" "Projection summon must attach the shared shield action."
Assert-Contains $projectionSummonService "CompanionSlotService.FindOpenPlayerSlot" "Projection summon must occupy an open player-side slot instead of a fixed left-of-player offset."
Assert-Contains $projectionSummonService "CompanionStatsService.ProjectionStats" "Projection summon must derive independent companion stats at creation."
Assert-Contains $projectionOtherObj "public sealed class ProjectionOtherObj : OtherObj" "Projection actors must stay friendly OtherObj objects, not real partners."
Assert-Contains $projectionOtherObj "EnsureActionIcons" "Projection actors must create action icons because native OtherObj does not."
Assert-Contains $projectionOtherObj "CompanionBattleStateStore.Create" "Projection actors must create a companion battle state before revealing intent."
Assert-Contains $projectionOtherObj "CompanionIntentSelector.Select" "Projection actors must choose intents through the companion intent selector."
Assert-Contains $projectionOtherObj "CompanionThreatService.SetPreview" "Projection actors must publish preview threat from the selected intent."
Assert-Contains $projectionOtherObj 'RefreshProjectionIntent("InitProjection")' "Projection actors must reveal intent immediately after summon."
Assert-Contains $projectionOtherObj "FightAction.ActionExecute()" "Projection turns must execute queued actions without native head/Msg announcement UI."
Assert-NotContains $projectionOtherObj "return base.DoAction();" "Projection turns must not use native OtherObj.DoAction because the player model lacks head/Msg."
Assert-Contains $projectionRuntime 'SunExpStatusLifecycleRouter.Register("Projection"' "Projection runtime must retire dead projections through the shared status lifecycle router."
Assert-Contains $projectionRuntime "AfterHit = RetireProjectionAfterDamage" "Projection runtime must retire dead projections after full damage resolves."
Assert-Contains $projectionRuntime "AfterCurHpChanged = RetireProjectionAfterHpChange" "Projection runtime must retire dead projections after direct HP changes."
Assert-Contains $projectionRuntime "AfterMaxHpChanged = RetireProjectionAfterHpChange" "Projection runtime must retire projections whose max HP is reduced to zero."
Assert-NotContains $projectionRuntime "SetDamageFilter" "Projection runtime must not use temporary damage filters after protection redirects were removed."
Assert-NotContains $projectionRuntime "RedirectThreatBeforeHit" "Projection runtime must not redirect enemy attacks away from players."
Assert-NotContains $projectionRuntime "ProjectionThreatService" "Projection runtime must not depend on retired threat redirection."
Assert-Contains $projectionStateStore "RetireIfDead" "Projection state store must expose a shared death retirement guard."
Assert-Contains $projectionStateStore "SunExpFrameDispatcher.RunOnceNextFrame" "Projection retirement must delay status-record removal until native queues settle."
Assert-Contains $projectionStateStore "removeStatusRecords: false" "Projection retirement must leave status records long enough for native hit queues to settle."
Assert-Contains $projectionStateStore "CompanionBattleStateStore.Remove" "Projection retirement must clear companion runtime state."
Assert-NotContains $projectionStateStore "ThreatBoost" "Projection state must not keep retired threat-weight state."
Assert-NotContains $projectionStrategyService "MarkShielded" "Projection shield behavior must not modify retired threat weights."
Assert-Contains $projectionStrategyService "CompanionIntentExecutor.UseAction" "Projection strategy must delegate shared action behavior to companion intents."
Assert-Contains $projectionScripts "ProjectionStrategyService.UseAction" "ProjectionScripts must keep CSV actions routed through Mechanics."
Assert-Contains $companionBattleModels "CompanionIntentTendency" "Companion models must define attack/defense intent tendencies."
Assert-Contains $companionBattleModels "CompanionIntentType" "Companion models must define companion intent types."
Assert-Contains $companionIntentSelector "Take(3)" "Companion intent selection must sample from the top three priority candidates."
Assert-Contains $companionIntentSelector "PickWeighted" "Companion intent selection must use normalized weighted random selection."
Assert-Contains $companionIntentSelector "CompanionThreatService.ThreatPercent" "Companion intent priority must react to current companion threat."
Assert-Contains $companionBattleStateStore "CompanionThreatService.Register" "Companion battle state creation must register threat state."
Assert-Contains $companionBattleStateStore "CompanionThreatService.Remove" "Companion battle state removal must clear threat state."
Assert-Contains $companionIntentRegistry "companion.intent.registry.json" "Companion intent pools must be data-driven through the registry."
Assert-Contains $companionIntentRegistryJson '"staff_tap"' "Companion intent registry must define the common staff-tap intent."
Assert-Contains $companionIntentRegistryJson '"shield_blessing"' "Companion intent registry must define the common magic-shield intent."
Assert-Contains $companionIntentRegistryJson '"threat"' "Companion intent registry must declare intent threat."
Assert-Contains $companionSlotService "MaxFriendlySlots = 4" "Companion slots must use the four friendly player-side slots."
Assert-Contains $companionThreatService "TryRedirectEnemySingleTarget" "Companion threat must expose weighted enemy single-target redirection."
Assert-Contains $companionThreatService "AddActiveCompanionsToAllTargets" "Companion threat must expose all-target companion expansion."
Assert-NotContains $companionThreatService "roleQueue.Add" "Companion threat must not add projections to the native player role queue."
Assert-Contains $companionThreatRuntime 'RegisterAfter(modConfig, "ScriptExecutor.SetStatus", ExtendEnemyTargetsAfterSetStatus);' "Companion threat runtime must hook enemy SetStatus after native target construction."
Assert-Contains $companionThreatRuntime "executor.Self?.fatherObject is not Enemy" "Companion threat runtime must only extend enemy target selection."
Assert-Contains $companionStatsService '"Strength"' "Companion stats must derive magic from the Strength origin key."
Assert-Contains $companionStatsService '"Lucky"' "Companion stats must derive spirit from the Lucky origin key."
Assert-Contains $companionStatsService '"Wisdom"' "Companion stats must derive luck from the Wisdom origin key."
Assert-Contains $companionStatsService '"Perceive"' "Companion stats must derive perception from the Perceive origin key."
Assert-Contains $specialTagRuntime 'SunExpActionEventRouter.RegisterHandler("SpecialTag.WhiteRadiance"' "SpecialTagRuntime must register through the shared Action router."
Assert-Contains $starScoreRuntime 'SunExpActionEventRouter.RegisterHandler("StarScore"' "StarScoreRuntime must register through the shared Action router."
Assert-NotContains $loneerRuntime 'SunExpActionEventRouter.RegisterHandler("Loneer"' "LoneerRuntime must not own Star Stone Pouch action dispatch."
Assert-Contains $starStonePouchService 'ExecutorApi.TryAddTokenedEvent(self, "ActionAfter"' "Star Stone Pouch buff service must own its after-action draw hook."
Assert-Contains $buffScripts "StarStonePouchService.Apply(self)" "BuffScripts must attach Star Stone Pouch behavior through the buff lifecycle."
Assert-Contains $buffScripts "StarScoreService.ApplyScoreBuff(self)" "BuffScripts must attach Star Score HUD state through the buff lifecycle."
Assert-NotContains ($specialTagRuntime + "`n" + $starScoreRuntime + "`n" + $loneerRuntime + "`n" + $starStonePouchService) "AddEventListener(" "Card-use feature runtimes must not register duplicate direct Action listeners."
Assert-Contains $sunExpCardRefreshQueue "RunOnceNextFrame" "Card refresh queue must debounce repeated card presentation refreshes."
Assert-Contains $sunExpCardRefreshQueue "RequestConfigTagRefresh" "Config tag refreshes must be queued with card presentation refreshes."
Assert-Contains $sunExpCardRefreshQueue "RefreshBudgetPerFrame" "Card refresh queue flushes must be split across frame-budgeted batches."
Assert-Contains $sunExpCardRefreshQueue "CardRefreshQueue.FlushContinued" "Card refresh queue must reschedule overflow work instead of draining all items in one frame."
Assert-Contains $cardApi "CardGrantPostCommitQueue.Request" "CardApi must submit SunExp post-commit refresh work after successful native hand grants."
Assert-Contains $cardGrantPostCommitQueue "SunExpCardRefreshQueue.RequestConfigTagRefresh" "Card grant post-commit work must route tag updates through the card refresh queue."
Assert-Contains $cardGrantPostCommitQueue "SunExpCardPresentationRouter.RequestApply" "Card grant post-commit work must route visuals through the presentation router."
Assert-Contains $cardGrantPostCommitQueue "MaterializeRetryBudget" "Card grant post-commit visual work must respect the native FightUI create-card queue."
Assert-NotContains $cardGrantPostCommitQueue "AddCardByData" "Card grant post-commit work must not own native card grants."
Assert-NotContains $cardGrantPostCommitQueue "GetCardFromDeck" "Card grant post-commit work must not move cards through the native battle flow."
Assert-Contains $starScoreRuntime "SunExpCardRefreshQueue.RequestDataUpdate" "Star score card refreshes must be queued instead of immediate DataUpdate calls."
Assert-Contains $cardMutationService "SunExpCardRefreshQueue.RequestConfigTagRefresh" "CardMutationService config tag refreshes must use the shared refresh queue."
Assert-Contains $runtimeCardAttachmentService "SunExpCardRefreshQueue.RequestConfigTagRefresh" "Runtime card attachment config tag refreshes must use the shared refresh queue."
Assert-NotContains $cardMutationService "FightCardManager.Instance?.RefreshTag" "CardMutationService must not synchronously refresh config tags."
Assert-NotContains $runtimeCardAttachmentService "FightCardManager.Instance?.RefreshTag" "RuntimeCardAttachmentService must not synchronously refresh config tags."
Assert-Contains $sunExpResourcePreloader "SunExpResourceCache.Preload" "Resource preloader must warm core visual resources through the shared cache."
Assert-Contains $runtimeHooks "SunExpResourcePreloader.Initialize(modConfig)" "RuntimeHooks must initialize the resource preloader as an isolated hook step."
Assert-Contains $runtimeHooks "SunExpCombatActionRouter.Initialize(modConfig)" "RuntimeHooks must initialize the shared combat action router before feature runtimes."
Assert-Contains $runtimeHooks "SunExpStatusLifecycleRouter.Initialize(modConfig)" "RuntimeHooks must initialize the shared status lifecycle router before feature runtimes."
Assert-Contains $sunExpStatusLifecycleRouter "SunExpHookTargets.StatusManagerAddBuff" "Status lifecycle router must own the StatusManager.AddBuff hook target."
Assert-Contains $sunExpStatusLifecycleRouter "SunExpHookTargets.StatusManagerHit" "Status lifecycle router must own the StatusManager.Hit hook target."
Assert-Contains $sunExpStatusLifecycleRouter "SunExpHookTargets.EnemyInit" "Status lifecycle router must own enemy initialization status hot-path hooks."
Assert-Contains $sunExpCombatActionRouter "SunExpHookTargets.OtherObjDoOneAction" "Combat action router must own OtherObj action hooks."
Assert-Contains $sunExpCombatActionRouter "SunExpHookTargets.FightUiCallActionAnimation" "Combat action router must own FightUI action animation hooks."
Assert-NotContains $runtimeHooks 'SunExpHookRegistry.Before(modConfig, "StatusManager.AddBuff"' "RuntimeHooks must not directly register StatusManager.AddBuff."
Assert-NotContains $projectionRuntime 'RegisterAfter(modConfig, "StatusManager.Hit"' "Projection runtime must not directly register status hot-path hooks."
Assert-NotContains $cardVisualSkinRuntime "SunExpCardLifecycleRouter.Register(`"CardVisualSkin`"" "Card visual skin runtime must not own card lifecycle hook mapping."
Assert-Contains $sunExpUiComponents "CreateTextButton" "Repeated text-button construction must live in the shared UI component factory."
Assert-Contains $sunExpUiComponents "AddTextBlock" "Repeated text block construction must live in the shared UI component factory."
Assert-Contains $sunExpUiComponents "CreateVerticalWindow" "Modal window shell construction must live in the shared UI component factory."
Assert-Contains $sunExpUiComponents "CreatePanelSection" "Header/content panel section construction must live in the shared UI component factory."
Assert-Contains $sunExpUiComponents "CreateFooterRow" "Footer row construction must live in the shared UI component factory."
Assert-Contains $sunExpUiComponents "CreateVerticalScrollArea" "Repeated ScrollRect construction must live in the shared UI component factory."
Assert-Contains $endlessAbyssShockPanel "SunExpUiComponents.CreateVerticalWindow" "Endless Abyss shock panel must use the shared modal window component."
Assert-Contains $endlessAbyssShockPanel "SunExpUiComponents.CreateVerticalScrollArea" "Endless Abyss shock panel must use the shared scroll component."
Assert-Contains $endlessAbyssMilestoneRewardPanel "SunExpUiComponents.CreateVerticalWindow" "Endless Abyss milestone panel must use the shared modal window component."
Assert-Contains $endlessAbyssMilestoneRewardPanel "SunExpUiComponents.CreateVerticalScrollArea" "Endless Abyss milestone panel must use the shared scroll component."
Assert-Contains $endlessAbyssFramedTextCard "SunExpUiComponents.AddTextBlock" "Framed option cards must use shared text block construction."
Assert-NotContains $endlessAbyssShockPanel "private static Text AddTextBlock" "Endless Abyss shock panel must not keep local text construction wrappers."
Assert-NotContains $endlessAbyssMilestoneRewardPanel "private static Text AddTextBlock" "Endless Abyss milestone panel must not keep local text construction wrappers."
Assert-NotContains $endlessAbyssFramedTextCard "private static Text AddTextBlock" "Framed option cards must not keep local text construction wrappers."
Assert-NotContains $endlessAbyssMilestoneRewardPanel "private static Transform CreateScroll" "Endless Abyss milestone panel must not keep local ScrollRect construction."
Assert-Contains $sunExpResourceCache "AuraSharedResourceCache.Load<T>" "SunExp resource cache must delegate native single-asset resource loading to shared cache."
Assert-Contains $sunExpResourceCache "AuraSharedResourceCache.LoadAll<T>" "SunExp resource cache must delegate native multi-asset resource loading to shared cache."
Assert-Contains $sunExpConfigIndex "public static List<Dictionary<string, string>> Rows" "Config index must own cached table row access."
Assert-Contains $sunExpConfigIndex "public static Dictionary<string, string>? Row" "Config index must own id-normalized row lookup."
Assert-Contains $sunExpConfigIndex "public static List<Dictionary<string, string>> FilteredRows" "Config index must own reusable filtered-row caches."
Assert-NotContains $sunExpConfigIndex "SunExp.Dll.Hooks" "Config index in Mechanics must not depend on Hook runtimes."
Assert-Contains $sunExpIds "SunCardVisualSkinId" "SunExpIds must centralize the Sun card visual skin id."
Assert-Contains $sunExpIds "MorningStarCardVisualSkinId" "SunExpIds must centralize the Morning Star card visual skin id."
Assert-Contains $sunExpIds "CardFaceEffectShaderId" "SunExpIds must centralize the reusable card-face effect shader id."
Assert-Contains $sunExpIds "CardFaceFoilHoloVisualEffectId" "SunExpIds must centralize the reusable card-face foil visual effect id."
Assert-Contains $sunExpIds "CardFaceStardustVisualEffectId" "SunExpIds must centralize the reusable card-face stardust visual effect id."
Assert-Contains $sunExpIds "BlazingCrownCollapseHoloEffectBindingId" "SunExpIds must centralize the Blazing Crown Collapse visual effect binding id."
Assert-Contains $sunExpIds "StellarOvertureStardustEffectBindingId" "SunExpIds must centralize the Stellar Overture stardust visual effect binding id."
Assert-Contains $sunExpIds "BlazingCrownCollapseCardEffectIds" "SunExpIds must centralize the card ids that receive the foil card-face effect."
Assert-Contains $sunExpIds "SunThemeCardPackIds" "SunExpIds must centralize Sun theme card-pack ids."
Assert-Contains $sunExpIds "MorningStarThemeCardPackIds" "SunExpIds must centralize Morning Star theme card-pack ids."
Assert-Contains $sunExpIds "StellarOvertureCardIds" "SunExpIds must centralize Stellar Overture card ids."
Assert-Contains $sunExpIds "StellarOvertureCardEffectIds" "SunExpIds must centralize the card ids that receive the Stellar Overture stardust effect."
Assert-Contains $sunExpIds "SunThemeExplicitCardIds" "SunExpIds must centralize explicit Sun theme card ids."
Assert-Contains $sunExpIds "StellarOvertureCardIconPathPrefix" "SunExpIds must centralize Stellar Overture icon-path fallback rules."
Assert-Contains $sunExpIds "WunaCoronationTokenCardId" "SunExpIds must centralize Wuna's generated Coronation token id."
Assert-Contains $sunExpIds "SunCardFramePath" "SunExpIds must centralize the Sun card frame resource path."
Assert-Contains $sunExpIds "SunCardBackgroundPath" "SunExpIds must centralize the optional Sun card background resource path."
Assert-Contains $sunExpIds "MorningStarCardFramePath" "SunExpIds must centralize the Morning Star card frame resource path."
Assert-Contains $cardVisualSkinSpec "public sealed class CardVisualSkinSpec" "Card visual skins must use a typed skin specification."
Assert-Contains $cardVisualThemeCatalog 'DictionaryUtil.Get(config.data, "PackBelong")' "Card visual themes must primarily resolve Sun cards by PackBelong."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunThemeCardPackIds" "Card visual themes must use centralized Sun card-pack ids."
Assert-Contains $cardVisualSkinApi "SunExpIds.SunCardFramePath" "Card visual skin defaults must attach the Sun frame path through the registration API."
Assert-Contains $cardVisualSkinApi "SunExpIds.SunCardBackgroundPath" "Card visual skin defaults must attach the optional Sun background path through the registration API."
Assert-Contains $cardVisualSkinApi "SunExpIds.MorningStarThemeCardPackIds" "Morning Star card visual skin defaults must include Morning Star card packs."
Assert-Contains $cardVisualSkinApi "SunExpIds.StellarOvertureCardEffectIds" "Morning Star card visual skin defaults must include generated Stellar Overture runtime ids."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.StellarOvertureCardIds" "Card visual themes must resolve Stellar Overture cards from centralized ids."
Assert-Contains $cardVisualThemeCatalog "StarScoreService.IsStellarOvertureCard" "Card visual themes must reuse the Star Score card-id classifier."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.StellarOvertureCardIconPathPrefix" "Card visual themes must fall back to Stellar Overture icon paths."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunThemeExplicitCardIds" "Card visual themes must support explicit generated Sun-theme cards."
Assert-Contains $cardVisualSkinApi "SunExpIds.MorningStarCardFramePath" "Card visual skin defaults must attach the Morning Star frame path through the registration API."
Assert-Contains $cardVisualThemeCatalog "IsStellarOvertureCard" "Card visual themes must expose a Stellar Overture theme predicate."
Assert-Contains $sunCardThemeCatalog "CardVisualThemeCatalog.Resolve" "Legacy Sun card theme checks must delegate to the generic card visual theme catalog."
Assert-Contains $runtimeHooks "SunExpCardPresentationLifecycleBridge.Initialize" "RuntimeHooks must initialize the card lifecycle to presentation bridge."
Assert-Contains $cardVisualSkinRuntime 'SunExpCardPresentationRouter.Register("CardVisualSkin"' "Card visual skin runtime must subscribe to the shared card presentation router."
Assert-Contains $polymorphCardFaceRuntime 'SunExpCardPresentationRouter.Register("PolymorphCardFace"' "Polymorph card face runtime must subscribe to the shared card presentation router."
Assert-Contains $sunExpCardPresentationLifecycleBridge 'AfterSetCardStyle = ApplyFromSetCardStyle' "Card presentation bridge must use native card-style initialization as the only full visual apply entry."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterCardItemInit" "Card presentation bridge must not use card init as a full visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterAttackCardItemInit" "Card presentation bridge must not use attack-card init as a full visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterCardItemDataUpdate" "Card presentation bridge must not repaint card visuals from DataUpdate."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterAttackCardItemDataUpdate" "Card presentation bridge must not repaint attack-card visuals from DataUpdate."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterCardItemDrawEffect" "Card presentation bridge must not repaint card visuals from DrawEffect."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterCommonCardItemDrawEffect" "Card presentation bridge must not repaint common-card visuals from DrawEffect."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterAttackCardItemDrawEffect" "Card presentation bridge must not repaint attack-card visuals from DrawEffect."
Assert-Contains $cardVisualSkinRuntime "BeforeCommonCardUse" "Card visual skin runtime must suppress frame-effect overlays before burnout common-card use animations."
Assert-Contains $cardVisualSkinRuntime "BeforeAttackCardUse" "Card visual skin runtime must suppress frame-effect overlays before burnout attack-card use animations."
Assert-Contains $cardVisualSkinRuntime "SuppressBurnoutFrameEffect" "Card visual skin runtime must keep burnout animation suppression isolated from normal card skin application."
Assert-Contains $cardVisualSkinRuntime "HasBurnoutTag" "Card visual skin runtime must only suppress frame-effect overlays for burnout cards."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterFightUiCreateCardItem" "Card presentation bridge must not rescan combat cards after native card creation."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterFightUiCreateCardItemInternal" "Card presentation bridge must not reapply generated hand cards outside native style initialization."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterScriptExecutorGetCardFromDeck" "Card presentation bridge must not reapply card visuals from deck movement."
Assert-Contains $sunExpCardPresentationRouter "ReapplyActiveCombatCards" "Card presentation router must centralize active combat-card reapplication."
Assert-Contains $sunExpCardPresentationRouter "RequestActiveCombatCardsReapply" "Card presentation full-hand reapply requests must be merged before scanning the active hand."
Assert-Contains $sunExpCardPresentationRouter "CardPresentation.ReapplyDeduped" "Card presentation merged reapply requests must be measurable."
Assert-NotContains $cardVisualSkinRuntime "ReapplyActiveCombatCardsNowAndLater" "Card visual skin runtime must not immediately scan the whole hand before the merged scheduled reapply."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterDictItemInit" "Card presentation bridge must not use dictionary item init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterDictionaryShowItemInit" "Card presentation bridge must not use dictionary detail init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterDisplayCardInit" "Card presentation bridge must not use display-card init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterShowCardInit" "Card presentation bridge must not use show-card init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterSafeBoxItemInit" "Card presentation bridge must not use safe-box init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterEnchCardItemInit" "Card presentation bridge must not use enchantment-card init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterCardChoiceItemInitialize" "Card presentation bridge must not use reward-choice init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterPackShowItemInit" "Card presentation bridge must not use card-pack init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterShopItemInit" "Card presentation bridge must not use shop init as a visual fallback."
Assert-NotContains $sunExpCardPresentationLifecycleBridge "AfterWarehouseItemInit" "Card presentation bridge must not use warehouse init as a visual fallback."
Assert-Contains $cardVisualSkinRuntime "CardVisualSkinApplier.Apply" "Card visual hooks must delegate Unity mutation to the generic visual applier."
Assert-Contains $cardVisualInterestIndex "public static bool MayAffect" "Card visual interest index must expose a lightweight visual-interest gate."
Assert-Contains $cardVisualInterestIndex "CardVisualSkinRegistry.Resolve(config)" "Card visual interest index must include skin rules without touching Unity objects."
Assert-Contains $cardVisualInterestIndex "CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config)" "Card visual interest index must include frame-effect-only cards."
Assert-Contains $cardVisualSkinRuntime "CardVisualInterestIndex.MayAffect(config)" "Card visual runtime must return before Unity work when a card has no visual interest."
Assert-Contains $cardVisualSkinRuntime "CardVisualSkin.InterestMiss" "Card visual interest misses must be measurable."
Assert-Contains $cardVisualSkinRuntime "CardPresentationRootResolver.FindCardVisualRoot" "Card visual hooks must use the shared card root resolver."
Assert-NotContains $cardVisualSkinRuntime ".deferred" "Card visual runtime must not keep deferred visual fallback passes."
Assert-Contains $cardVisualSkinApplier "CardVisualInterestIndex.MayAffect(config)" "Card visual applier must protect direct callers with the same interest gate."
Assert-Contains $cardVisualSkinApplier "LastAppliedRootInstanceId" "Card visual applier must record per-instance visual application state."
Assert-Contains $cardVisualSkinMarker "LastAppliedStage" "Card visual marker must record the stage that applied the current signature."
Assert-Contains $cardConfigApi "payload is IDictionary<string, string> row" "Card config payload parsing must support dictionary table rows used by card dictionaries."
Assert-Contains $cardConfigApi "payload is string cardId" "Card config payload parsing must support card-id strings used by lightweight UI cards."
Assert-Contains $cardVisualSkinApplier "CardVisualThemeCatalog.Resolve" "Card visual skin applier must gate visuals through the theme catalog."
Assert-Contains $entrySource 'RunStep("card visual skin registry", CardVisualSkinApi.RegisterSunExpDefaults)' "Entry must register card visual skins before gameplay hooks."
Assert-Contains $cardVisualSkinApi "RegisterTheme" "Card visual skins must expose a modular registration API."
Assert-Contains $cardVisualSkinApi "RegisterSunExpDefaults" "SunExp default card skins must be registered through the card visual skin API."
Assert-Contains $cardVisualSkinRegistry "HitCache" "Card visual skin registry must cache positive rule matches."
Assert-Contains $cardVisualSkinRegistry "MissCache" "Card visual skin registry must cache missed rule matches."
Assert-Contains $cardVisualSkinRegistry "CardVisualInterestIndex.Invalidate()" "Card visual skin registry changes must invalidate the lightweight interest index."
Assert-Contains $cardVisualSkinRule "PackBelong" "Card visual skin rules must support pack-based matching."
Assert-Contains $cardVisualSkinRule "iconPrefixes" "Card visual skin rules must support theme icon-prefix matching."
Assert-NotContains $cardVisualThemeCatalog "private static readonly CardVisualSkinSpec SunSkin" "Card visual theme catalog must not hard-code Sun skin specs outside the registry."
Assert-Contains $cardVisualSkinApplier "CardVisualSkinMarker" "Card visual skin applier must cache per-card UI lookup state."
Assert-Contains $entrySource 'RunStep("card visual effect registry", CardVisualEffectApi.RegisterSunExpDefaults)' "Entry must register card visual effects independently from card visual skins."
Assert-Contains $cardVisualEffectApi "CardVisualEffectRegistry.Register" "Card visual effects must expose a modular registration API."
Assert-Contains $cardVisualEffectApi "RegisterFaceEffect" "Card visual effects must expose a face-target convenience registration API."
Assert-Contains $cardVisualEffectApi "RegisterFrameEffect" "Card visual effects must expose a frame-target convenience registration API."
Assert-Contains $cardVisualEffectApi "CardVisualEffectTarget.Frame" "Frame visual effect registration must target the card frame, not the card face."
Assert-Contains $cardVisualEffectApi "SunExpIds.CardFaceFoilHoloVisualEffectId" "SunExp default card visual effect must reference the reusable foil card-face visual effect id."
Assert-Contains $cardVisualEffectApi "SunExpIds.CardFaceStardustVisualEffectId" "SunExp default card visual effect must register the reusable stardust card-face visual effect id."
Assert-Contains $cardVisualEffectApi "SunExpIds.BlazingCrownCollapseCardEffectIds" "SunExp default card visual effect must be registered to explicit card ids."
Assert-Contains $cardVisualEffectApi "SunExpIds.StellarOvertureCardEffectIds" "SunExp default card visual effect must bind Stellar Overture stardust to explicit generated-card ids."
Assert-Contains $cardVisualEffectTarget "Face" "Card visual effects must carry an explicit card-face render target."
Assert-Contains $cardVisualEffectTarget "Frame = 1" "Card visual effects must keep frame and face targets distinct."
Assert-NotContains $cardVisualEffectTarget "Frame = Face" "Card visual frame effects must not alias the card-face target."
Assert-Contains $cardVisualEffectSpec "public sealed class CardVisualEffectSpec" "Card visual effects must use a typed effect specification."
Assert-Contains $cardVisualEffectSpec "CardVisualEffectTarget Target" "Card visual effect specs must carry a render target."
Assert-Contains $cardVisualEffectSpec "CardIds" "Card visual effect specs must carry explicit target card ids."
Assert-Contains $cardVisualEffectRegistry "Resolve(CardVisualEffectTarget target, IDataConfig? config)" "Card visual effect registry must resolve effects from target plus card config."
Assert-NotContains $cardVisualEffectRegistry "CardVisualSkinSpec" "Card visual effect registry must not bind visual effects to skin specs."
Assert-Contains $cardVisualEffectRegistry "CardConfigApi.Id(config)" "Card visual effect registry must match against the concrete card id."
Assert-Contains $cardVisualEffectRegistry "IsWildcardPattern(pattern)" "Card visual effect registry must not treat generated-card leading star ids as broad wildcards."
Assert-Contains $cardVisualEffectRegistry "HitCache" "Card visual effect registry must cache positive target+card matches."
Assert-Contains $cardVisualEffectRegistry "MissCache" "Card visual effect registry must cache missed target+card matches."
Assert-Contains $cardVisualEffectRegistry "CardVisualInterestIndex.Invalidate()" "Card visual effect registry changes must invalidate the lightweight interest index."
Assert-Contains $cardVisualSkinApplier "CardVisualEffectApplier.Apply(marker, config)" "Card visual skin applier must apply independent visual effects after optional skin replacement."
Assert-Contains $cardVisualEffectApplier "CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Face, config)" "Card visual effect applier must resolve card-face effects independently from skins."
Assert-Contains $cardVisualEffectApplier "CardVisualEffectRegistry.Resolve(CardVisualEffectTarget.Frame, config)" "Card visual effect applier must resolve card-frame effects independently from card-face effects."
Assert-Contains $cardVisualEffectApplier "CardFaceEffectApplier.Clear" "Card visual effect applier must clear stale face effects when a card has no face effect."
Assert-Contains $cardVisualEffectApplier "CardFrameEffectApplier.Clear" "Card visual effect applier must clear stale frame effects when a card has no frame effect."
Assert-Contains $cardVisualEffectApplier "CardFrameEffectApplier.Apply" "Card visual effect applier must apply frame-target effects to the card frame."
Assert-NotContains $cardVisualSkinApplier "CardFaceEffectApplier.Apply(marker, skin" "Card visual effects must not be applied through skin-bound effect calls."
Assert-Contains $cardFrameEffectApplier "marker.FrameImage" "Card-frame effects must target the card-frame UI image when available."
Assert-Contains $cardFrameEffectApplier "marker.BackgroundImage != null && marker.LastFrameSprite != null" "Card-frame effects may use a background-sized overlay only when a dedicated frame sprite was resolved."
Assert-Contains $cardFrameEffectApplier "marker.FrameMesh" "Card-frame effects must support mesh-rendered card frames as a fallback."
Assert-Contains $cardFrameEffectApplier "CreateOwnedMaterial" "Card-frame effects must create owned integrated materials for real card-frame nodes."
Assert-Contains $cardFrameEffectApplier "ApplyFrameImageEffectMaterial" "Card-frame UI effects must apply as integrated dynamic frame-skin materials."
Assert-Contains $cardFrameEffectApplier "ApplyFrameMeshEffectMaterial" "Card-frame mesh effects must apply as integrated dynamic frame-skin materials."
Assert-Contains $cardFrameEffectApplier "frame-mesh-integrated-material" "Card-frame mesh effects must route through the integrated material path."
Assert-Contains $cardFrameEffectApplier "marker.LastFrameTexture == null" "Card-frame mesh effects must require the resolved frame texture instead of falling back to the game material."
Assert-NotContains $cardFrameEffectApplier "IsWholeCardFoil" "Card-frame foil effects must stay constrained to the card-frame image mask."
Assert-Contains $cardVisualSkinMarker "var source = FrameImage;" "Card-frame image overlays must use the card-frame image as the alpha mask."
Assert-NotContains $cardVisualSkinMarker "FrameImage ?? BackgroundImage" "Card-frame image overlays must not infer frame alpha from the card background."
Assert-Contains $cardVisualSkinMarker "ApplyFallbackFrameImageEffectOverlay" "Card-frame image fallbacks must render a dedicated frame sprite instead of background alpha."
Assert-Contains $cardVisualSkinMarker "ApplyFrameImageEffectMaterial" "Card-frame image effects must support integrated dynamic frame skins."
Assert-Contains $cardVisualSkinMarker "ApplyFrameMeshEffectMaterial" "Card-frame mesh effects must support integrated dynamic frame skins."
Assert-Contains $cardVisualSkinMarker "originalFrameMeshMaterial" "Card-frame effects must restore original mesh materials when integrated effects clear."
Assert-Contains $cardVisualSkinMarker "CardFrameOverlay" "Card visual skin marker must delegate card-frame overlays to the unified overlay component."
Assert-Contains $cardVisualSkinMarker "SuppressFrameEffectOverlay" "Card visual skin marker must hide only the frame-effect overlay during burnout animations."
Assert-Contains $cardVisualSkinMarker "ResumeFrameEffectOverlayFor" "Card visual skin marker must restore frame-effect overlays when pooled UI is reused for a different card."
Assert-Contains $cardFrameOverlay 'OverlayName = "SunExp_CardFrameEffectOverlay"' "Card-frame overlays must use a stable named runtime object."
Assert-Contains $cardFrameOverlay "SetVisible" "Card-frame overlays must support temporary animation-time visibility suppression."
Assert-Contains $cardFrameOverlay "ApplyImage" "Card-frame overlay must support UI Image-backed dictionary cards."
Assert-Contains $cardFrameOverlay "ApplyMesh" "Card-frame overlay must support MeshRenderer-backed battle cards."
Assert-Contains $cardFrameOverlay "BuildFullUvMesh" "Card-frame mesh overlays must normalize UVs to the resolved frame texture."
Assert-Contains $cardFrameOverlay "raycastTarget = false" "Card-frame UI overlays must not intercept card input."
Assert-Contains $cardFrameOverlay "FindFirstTextContentSiblingIndex" "Card-frame UI overlays must stay below card text content."
Assert-Contains $cardFrameOverlay "RaiseTextRenderersAbove" "Card-frame mesh overlays must keep text renderers above the frame overlay without hiding the frame overlay."
Assert-Contains $cardFrameOverlay "DestroyMeshOverlay" "Card-frame mesh overlays must be cleaned up independently."
Assert-Contains $cardFrameEffectMaterials "EffectMaterialFactory.CreateMaterial" "Card-frame effect materials must be created through the private visual effect factory."
Assert-Contains $cardFrameEffectMaterials "CreateOwnedMaterial" "Card-frame effects must use owned integrated materials."
Assert-Contains $cardFrameEffectMaterials "ApplyIntegratedMode" "Card-frame effect materials must separate integrated frame-skin and fallback overlay material modes."
Assert-Contains $cardFrameEffectMaterials "material.SetFloat(CardFrameEffectShaderIds.OverlayMode, enabled ? 0f : 1f)" "Integrated card-frame materials must render as native frame skins while fallback overlays stay transparent."
Assert-Contains $cardFrameEffectMaterials "material.SetFloat(CardFrameEffectShaderIds.FrameOnlyOverlay, 0f)" "Integrated card-frame materials must not use overlay-only frame masking."
Assert-Contains $cardFrameEffectMaterials "material.SetFloat(CardFrameEffectShaderIds.QualityScale, 1f)" "Card-frame effect materials must stay visually consistent across performance quality settings."
Assert-NotContains $cardFrameEffectApplier "frameOnlyOverlay" "Card-frame effects must not use dictionary background frame-masking fallbacks."
Assert-Contains $cardFrameEffectMaterials 'ShaderName = "SunExp/CardFaceEffect"' "Card-frame effects must use the shared card-effect shader declared by the visual registry."
Assert-Contains $cardFaceEffectApplier "marker.FaceImage" "Card-face effects must target the card-face UI image when available."
Assert-Contains $cardFaceEffectApplier "SharedUiOverlayMaterial" "Card-face UI effects must use transparent overlay materials instead of replacing the background material."
Assert-Contains $cardFaceEffectApplier "ApplyFaceImageEffectOverlay" "Card-face UI effects must apply through an overlay layer."
Assert-Contains $cardFaceEffectApplier "marker.FaceMesh" "Card-face effects must support mesh-rendered card faces as a fallback."
Assert-Contains $cardFaceEffectMaterials "EffectMaterialFactory.CreateMaterial" "Card-face effect materials must be created through the private visual effect factory."
Assert-Contains $cardFaceEffectMaterials "OverlayMode" "Card-face effect materials must control shader overlay mode explicitly."
Assert-Contains $performanceSettings "CardFaceEffectsEnabled" "Card-face effects must obey performance quality settings."
Assert-Contains $cardVisualSkinMarker 'Front/FrontBack' "Card visual skin marker must replace the card-frame layer."
Assert-Contains $cardVisualSkinMarker 'Front/background' "Card visual skin marker must support the optional card-background layer."
Assert-Contains $cardVisualSkinMarker 'SunExp_CardFaceEffectOverlay' "Card visual skin marker must create a named card-face effect overlay."
Assert-Contains $cardFrameOverlay 'SunExp_CardFrameEffectOverlay' "Card frame overlay must create a named card-frame effect overlay."
Assert-Contains $cardVisualSkinMarker 'raycastTarget = false' "Card-face effect overlays must not intercept card UI input."
Assert-Contains $cardVisualSkinMarker 'frameOverlay.Clear()' "Card visual skin marker must clean up delegated card-frame effect overlays."
Assert-Contains $sunExpCardPresentationRouter "SunExpFrameScheduler.RunOnceNextFrame" "Card presentation full reapply must be merged through the performance scheduler."
Assert-Contains $sunExpCardPresentationRouter "CardPresentation.ReapplyActiveCombatCards" "Card presentation reapply must be measured by performance counters."
Assert-Contains $cardVisualSkinSpriteCache "SunExpResourceCache.Load<Sprite>" "Card visual skin sprites must load through the shared resource cache."
Assert-Contains $cardVisualSkinSpriteCache "private static readonly Dictionary<string, Sprite?> Cache" "Card visual skin sprites must be cached."
Assert-Contains $sunCardFrameRuntime "CardVisualSkinRuntime.Initialize(modConfig)" "Legacy Sun card frame runtime must delegate to the generic visual skin runtime."
Assert-Contains $sunCardFrameApplier "CardVisualSkinApplier.Apply" "Legacy Sun card frame applier must delegate to the generic visual skin applier."
Assert-Contains $sunCardFrameSpriteCache "CardVisualSkinSpriteCache.Load" "Legacy Sun card frame sprite cache must delegate to the generic visual skin sprite cache."
Assert-NotContains $cardVisualSkinRuntime "RedirectSourcePath" "Card visual skin runtime must not globally redirect native rarity templates."
Assert-NotContains $cardVisualSkinRuntime "ResourceLoader.RedirectPath" "Card visual skin runtime must not globally redirect native rarity templates."
Assert-Contains $entrySource 'RunStep("visual registry", () => VisualRegistry.Load(modConfig))' "Entry must load visual declarations before gameplay hooks."
Assert-Contains $visualRegistry "JsonConvert.DeserializeObject<VisualRegistryDocument>" "VisualRegistry must load the shipped JSON declaration."
Assert-Contains $visualRegistry "VisualRegistryDefaults.Create()" "VisualRegistry must keep built-in defaults for missing or broken visual declarations."
Assert-Contains $visualRegistryModels "public sealed class FrameAnimationVisualSpec" "Visual registry models must declare frame animation entries."
Assert-Contains $visualRegistryModels "public sealed class MapNodeArtVisualSpec" "Visual registry models must declare map-node art entries."
Assert-Contains $visualRegistryModels "public sealed class ShaderVisualSpec" "Visual registry models must declare shader entries."
Assert-Contains $visualRegistryModels "public sealed class VisualEffectVisualSpec" "Visual registry models must declare visual effect entries."
Assert-Contains $visualRegistry "public static VisualEffectVisualSpec? Effect" "VisualRegistry must expose private visual effect declarations."
Assert-Contains $visualRegistry "ResolveContentPath" "VisualRegistry must resolve private visual bundle paths under the SunExp mod directory."
Assert-Contains $visualRegistryJson '"frameAnimations"' "Shipped visual registry must declare frame animation assets."
Assert-Contains $visualRegistryJson '"mapNodeArt"' "Shipped visual registry must declare map-node art assets."
Assert-Contains $visualRegistryJson '"modeEntries"' "Shipped visual registry must declare mode-entry visuals."
Assert-Contains $visualRegistryJson '"shaders"' "Shipped visual registry must declare shader lookup entries."
Assert-Contains $visualRegistryJson '"effects"' "Shipped visual registry must declare private visual effect entries."
Assert-Contains $visualRegistryJson '"sunexp.star_score_hud.lit_slot"' "Shipped visual registry must declare the star-score lit-slot effect."
Assert-Contains $visualRegistryJson '"sunexp.card_visual_effect.card_face.shader"' "Shipped visual registry must declare the reusable card-face effect shader."
Assert-Contains $visualRegistryJson '"sunexp.card_visual_effect.foil_holo"' "Shipped visual registry must declare the reusable foil card-face effect."
Assert-Contains $visualRegistryJson '"sunexp.card_visual_effect.stardust_overture"' "Shipped visual registry must declare the reusable stardust card-face effect."
Assert-Contains $visualRegistryJson '"_FoilTex"' "Shipped visual registry must keep the optional foil texture binding for compatibility."
Assert-Contains $visualRegistryJson '"_SunExpOverlayMode":' "Shipped visual registry must carry the Lab-exported overlay-mode shader flag."
Assert-Contains $visualRegistryJson '"_SunExpFrameOnlyOverlay":' "Shipped visual registry must carry the Lab-exported frame-only shader flag."
Assert-Contains $visualRegistryJson '"_SunExpFoilMode":' "Shipped visual registry must carry the Lab-exported foil shader mode."
Assert-Contains $visualRegistry "SunExpIds.CardFaceFoilHoloVisualEffectId" "Built-in visual defaults must include the reusable foil card-face effect."
Assert-Contains $visualRegistry "SunExpIds.CardFaceStardustVisualEffectId" "Built-in visual defaults must include the reusable stardust card-face effect."
Assert-Contains $visualPipeline '"bundleName": "sunexp_visuals"' "Visual pipeline must declare the private SunExp bundle name."
Assert-Contains $visualPipeline '"materialPath": "SunExp/Materials/StarScoreHudLit"' "Visual pipeline must match the runtime star-score material asset path."
Assert-Contains $visualPipeline '"cardFaceEffectMaterialPath": "SunExp/Materials/CardFaceEffect"' "Visual pipeline must match the runtime card-face effect material asset path."
Assert-Contains $visualRegistry "public static IReadOnlyList<string> BundlePaths()" "VisualRegistry must expose declared visual bundle paths for release checks."
Assert-Contains $runtimeHooks "VisualBundleRuntimeValidator.ValidateDeclaredBundles" "Runtime hooks must validate declared visual bundles during startup."
Assert-Contains $visualBundleRuntimeValidator "VisualRegistry.BundlePaths()" "Visual bundle validator must scan registry-declared bundle paths."
Assert-Contains $visualBundleRuntimeValidator "WunaOrbitFireBack" "Visual bundle validator must probe Wuna back orbit material."
Assert-Contains $visualBundleRuntimeValidator "WunaOrbitFireFront" "Visual bundle validator must probe Wuna front orbit material."
Assert-Contains $visualBundleRuntimeValidator "CardFaceEffect" "Visual bundle validator must probe the card-face effect material."
Assert-Contains $visualBundleBuildScript "SunExpVisualBundleBuilder.BuildVisualBundle" "Visual bundle build script must call the Unity Editor builder entrypoint."
Assert-Contains $visualBundleBuildScript "CardFaceEffect.shader" "Visual bundle build script must copy the card-face effect shader into the Unity project."
Assert-Contains $visualBundleBuildScript "Stop-StaleUnityProjectProcesses" "Visual bundle build script must stop stale batchmode Unity processes before opening the project."
Assert-Contains $visualBundleBuildScript "UnityLockfile" "Visual bundle build script must remove an orphaned Unity project lock after stale batchmode cleanup."
Assert-Contains $visualBundleBuilder "BuildPipeline.BuildAssetBundles" "Visual pipeline must provide a Unity Editor bundle build entrypoint."
Assert-Contains $visualBundleBuilder "new AssetBundleBuild" "Visual pipeline must explicitly build the sunexp_visuals bundle asset list."
Assert-Contains $visualBundleBuilder "EnsureCardFaceEffectMaterial" "Visual pipeline must build the card-face effect material."
Assert-Contains $visualBundleBuilder 'material.SetFloat("_SunExpFoilMode", 1f)' "Visual bundle builder must default foil materials to laser mode."
Assert-Contains $visualBundleBuilder 'private const string BundleName = "sunexp_visuals"' "Visual bundle builder must match the runtime bundle name."
Assert-Contains $visualBundleBuilder 'private const string MaterialAssetPath = "Assets/SunExp/Visuals/Materials/StarScoreHudLit.mat"' "Visual bundle builder must create the declared star-score material asset."
Assert-Contains $sunExpProject "UnityEngine.AssetBundleModule" "SunExp must reference UnityEngine.AssetBundleModule for private shader bundles."
Assert-Contains $assetBundleCache "AssetBundle.LoadFromFile" "AssetBundleCache must load private visual bundles from files."
Assert-Contains $assetBundleCache "VisualRegistry.ResolveContentPath" "AssetBundleCache must resolve SunExp-private bundle paths through the visual registry."
Assert-Contains $effectMaterialFactory "AssetBundleCache.LoadAsset<Material>" "EffectMaterialFactory must prefer declared material assets from private bundles."
Assert-Contains $effectMaterialFactory "ShaderAssetLoader.ResolveShader" "EffectMaterialFactory must fall back to declared shaders when no material asset is bundled."
Assert-Contains $effectMaterialFactory "EffectTextureCache.Load" "EffectMaterialFactory must apply declared effect textures."
Assert-Contains $effectTextureCache "SunExpResourceCache.Load<Texture>" "EffectTextureCache must load declared effect textures through the shared resource cache."
Assert-Contains $frameSpriteCache "private static readonly Dictionary<string, Sprite[]> Cache" "FrameSpriteCache must centralize loaded sprite-frame caching."
Assert-Contains $frameSpriteCache "SunExpResourceCache.Load<Sprite>" "FrameSpriteCache must own sprite-frame resource loading through the shared resource cache."
Assert-Contains $frameAnimationAttacher "FrameImageAnimator" "FrameAnimationAttacher must attach UI Image animations through the shared component."
Assert-Contains $frameAnimationAttacher "FrameSpriteRendererAnimator" "FrameAnimationAttacher must attach SpriteRenderer animations through the shared component."
Assert-Contains $animatedBlessingIconRuntime "VisualRegistry.FrameAnimationBySpriteName" "Animated blessing icons must resolve frame specs from the visual registry."
Assert-Contains $animatedBlessingIconRuntime "FrameAnimationAttacher.Attach" "Animated blessing icons must use the shared frame animation attacher."
Assert-Contains $animatedBuffIconRuntime "VisualRegistry.FrameAnimationByMatchId" "Animated buff icons must resolve frame specs from the visual registry."
Assert-Contains $animatedBuffIconRuntime "FrameAnimationAttacher.Attach" "Animated buff icons must use the shared frame animation attacher."
Assert-Contains $animatedEnemyDictIconRuntime "VisualRegistry.FrameAnimationByMatchId" "Animated enemy dictionary icons must resolve frame specs from the visual registry."
Assert-Contains $animatedEnemyDictIconRuntime "FrameAnimationAttacher.Attach" "Animated enemy dictionary icons must use the shared frame animation attacher."
Assert-NotContains ($animatedBlessingIconRuntime + $animatedBuffIconRuntime + $animatedEnemyDictIconRuntime) "public sealed class AnimatedBlessingIcon" "Animated blessing icons must not own a duplicate frame animator component."
Assert-NotContains ($animatedBlessingIconRuntime + $animatedBuffIconRuntime + $animatedEnemyDictIconRuntime) "public sealed class AnimatedBuffSpriteIcon" "Animated buff icons must not own a duplicate frame animator component."
Assert-NotContains ($animatedBlessingIconRuntime + $animatedBuffIconRuntime + $animatedEnemyDictIconRuntime) "public sealed class AnimatedEnemyDictIcon" "Animated enemy dictionary icons must not own a duplicate frame animator component."
Assert-Contains $starScoreService "public static event Action<StarScoreDisplaySnapshot>? Changed" "StarScoreService must publish typed display snapshots for UI hooks."
Assert-Contains $starScoreHudRuntime "StarScoreService.Changed += OnStarScoreChanged" "StarScoreHudRuntime must subscribe to star score mechanics changes."
Assert-Contains $starScoreHudRuntime "UIManager.Instance?.canvasTf" "StarScoreHudRuntime must attach fixed HUD to the main canvas."
Assert-Contains $starScoreHudRuntime "FightPlayer.Instance?.Status?.InstanceId" "StarScoreHudRuntime must keep star score presentation local-player scoped."
Assert-Contains $starScoreHudRuntime 'activeView.Close("StarScoreHudRuntime.Close")' "StarScoreHudRuntime must close HUD roots through the view safety path."
Assert-NotContains $starScoreHudRuntime "Object.Destroy(activeView.gameObject)" "StarScoreHudRuntime must not directly destroy HUD roots."
Assert-Contains $starScoreCadenceCatalog "public static class StarScoreCadenceCatalog" "StarScoreCadenceCatalog must own tooltip cadence copy in Mechanics."
Assert-Contains $starScoreCadenceCatalog "CandidatesForPrefix" "StarScoreCadenceCatalog must calculate available cadence candidates from current notes."
Assert-NotContains $starScoreHudView "ProgressPartThresholds" "StarScoreHudView must keep the full star-score frame visible once shown."
Assert-Contains $starScoreHudView "SunExpUiSafety.CloseTransient(gameObject" "StarScoreHudView must close through shared UI safety."
Assert-Contains $starScoreHudView "StarScoreHudTooltipView.Create" "StarScoreHudView must own the hover tooltip view."
Assert-Contains $starScoreHudView "StarScoreHudShaderController" "StarScoreHudView must delegate star-score lighting effects to the shader controller."
Assert-Contains $starScoreHudView "RectMask2D" "StarScoreHudView must use masked full-frame lighting slots instead of mismatched sliced art."
Assert-Contains $starScoreHudView "SlotTops = { 0f, 146f, 226f }" "StarScoreHudView lighting masks must merge head and space art into the three overture stages."
Assert-Contains $starScoreHudView "SlotHeights = { 146f, 80f, 100f }" "StarScoreHudView lighting masks must cover head+slot1, space+slot2, and space+slot3."
Assert-NotContains $starScoreHudView "StarScoreHudAssets.HeadPath" "StarScoreHudView must not rebuild the star-score frame from mismatched sliced art."
Assert-Contains $starScoreHudShaderController "StarScoreHudShaderMaterials.CreateLitMaterial" "StarScoreHudShaderController must use the material factory for optional shader binding."
Assert-Contains $starScoreHudShaderController "ApplySnapshot(StarScoreDisplaySnapshot snapshot)" "StarScoreHudShaderController must be driven by typed star-score snapshots."
Assert-Contains $starScoreHudShaderMaterials "EffectMaterialFactory.CreateMaterial" "Star score HUD materials must be created through the private visual effect factory."
Assert-Contains $starScoreHudShaderMaterials "StarScoreHudShaderIds.LitSlotEffectId" "Star score HUD materials must use the declared lit-slot effect id."
Assert-Contains $shaderAssetLoader "AssetBundleCache.LoadAsset<Material>" "ShaderAssetLoader must support material assets from private bundles."
Assert-Contains $shaderAssetLoader "AssetBundleCache.LoadAsset<Shader>" "ShaderAssetLoader must support shader assets from private bundles."
Assert-Contains $shaderAssetLoader "SunExpResourceCache.Load<Shader>" "ShaderAssetLoader must support declared shader resource paths through the shared resource cache."
Assert-Contains $shaderAssetLoader "SunExpResourceCache.Load<Material>" "ShaderAssetLoader must support declared material resource paths through the shared resource cache."
Assert-Contains $starScoreHudShaderSource 'Shader "SunExp/StarScoreHud"' "Star score HUD shader source must match the runtime shader id."
Assert-Contains $starScoreHudShaderSource "_SunExpFlowStrength" "Star score HUD shader source must expose the runtime flow property."
Assert-Contains $starScoreHudShaderSource "_SunExpPulse" "Star score HUD shader source must expose the runtime pulse property."
Assert-Contains $starScoreHudShaderSource "UnityGet2DClipping" "Star score HUD shader source must remain UI clipping compatible."
Assert-Contains $cardFaceEffectShaderSource 'Shader "SunExp/CardFaceEffect"' "Card-face effect shader source must match the runtime shader id."
Assert-Contains $cardFaceEffectShaderSource "_SunExpEffectMode" "Card-face effect shader must switch between foil and stardust presets by material parameters."
Assert-Contains $cardFaceEffectShaderSource "_SunExpOverlayMode" "Card-face effect shader must support transparent overlay rendering."
Assert-Contains $cardFaceEffectShaderSource "_SunExpFrameOnlyOverlay" "Card-face effect shader may expose registry-controlled frame-only overlay masking."
Assert-Contains $cardFaceEffectShaderSource "frameOnlyOverlayMask" "Card-face effect shader must keep shader-side frame masking isolated behind its explicit material flag."
Assert-Contains $cardFaceEffectShaderSource "_FoilTex" "Card-face effect shader must support a local holofoil texture layer."
Assert-Contains $cardFaceEffectShaderSource "_SunExpFoilMode" "Card-face effect shader must expose selectable foil modes."
Assert-Contains $cardFaceEffectShaderSource "buildFoilOverlay(float2 uv" "Foil card-face overlay must derive visible shimmer from effect coordinates instead of only color delta."
Assert-Contains $cardFaceEffectShaderSource "holoRamp" "Foil shader must keep a saturated rainbow ramp for laser shimmer."
Assert-Contains $cardFaceEffectShaderSource "sweep * 0.9" "Foil overlay must emphasize moving laser sweep bands."
Assert-Contains $cardFaceEffectShaderSource "mirror * 0.72" "Foil overlay must include a mirror sweep layer for reflective punch."
Assert-Contains $cardFaceEffectShaderSource "foilFrameWeight" "Foil card-frame effect must bias shimmer toward the frame and alpha edge."
Assert-Contains $cardFaceEffectShaderSource "prismSheen" "Foil card-frame effect must use a broad prism sheen instead of dense flakes."
Assert-Contains $cardFaceEffectShaderSource "diffractionLine" "Foil card-frame effect must keep subtle diffraction-line detail."
Assert-Contains $cardFaceEffectShaderSource "cornerGlint" "Foil card-frame effect must reserve high sparkle for sparse corner glints."
Assert-Contains $cardFaceEffectShaderSource "_StencilComp" "Card-face effect shader must remain compatible with UI stencil masks."
Assert-Contains $cardFaceEffectShaderSource "UnityGet2DClipping" "Card-face effect shader must remain UI clipping compatible."
Assert-Contains $cardFaceEffectShaderSource "mask = saturate(face.a)" "Card-face effect shader must mask effects to the card-face sprite alpha."
Assert-Contains $cardFaceEffectShaderSource "stardustField" "Card-face effect shader must contain the Stellar Overture stardust pass."
Assert-Contains $cardFaceEffectShaderSource "stardustSweep" "Card-face effect shader must contain a visible Stellar Overture sweep pass."
Assert-Contains $cardFaceEffectShaderSource "_SunExpStardustTwinkleSpeed" "Stellar Overture stardust must expose independent twinkle speed control."
Assert-Contains $cardFaceEffectShaderSource "_SunExpStardustGlowRadius" "Stellar Overture stardust must expose compact glow radius control."
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
Assert-Contains $starScoreHudTooltipView "SunExpUiPool.AcquireComponent" "StarScoreHudTooltipView must reuse tooltip rows through the shared UI pool."
Assert-Contains $starScoreHudTooltipView "SunExpUiPool.ReleaseOrDestroyChildren" "StarScoreHudTooltipView must clear row rebuilds through pooled UI teardown."
Assert-NotContains $starScoreHudTooltipView "Destroy(child.gameObject)" "StarScoreHudTooltipView must not directly destroy tooltip rows."
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
Assert-Contains $mapNodeCardArtRegistry "VisualRegistry.MapNodeArtSpecs()" "MapNodeCardArtRegistry must resolve map-node art from the visual registry."
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

Assert-Contains $solarMemoryModeEntryRuntime "SolarMemoryRunLauncher.Start" "SolarMemoryModeEntryRuntime must delegate run startup to SolarMemoryRunLauncher."
Assert-Contains $solarMemoryModeRuntime "SolarMemoryModeEntryRuntime.Initialize(modConfig)" "SolarMemoryModeRuntime must delegate mode-choice entry visuals to SolarMemoryModeEntryRuntime."
Assert-Contains $solarMemoryModeRuntime "SolarMemoryMapVisualRuntime.Initialize(modConfig)" "SolarMemoryModeRuntime must delegate map presentation hooks to SolarMemoryMapVisualRuntime."
Assert-Contains $solarMemoryModeRuntime "SolarMemorySettlementPresenter.Show()" "SolarMemoryModeRuntime must delegate settlement UI presentation."
Assert-Contains $solarMemoryModeRuntime "SunExpConfigIndex.Row(DataType.Map" "SolarMemoryModeRuntime must resolve map rows through the shared config index."
Assert-Contains $solarMemoryModeRuntime "SunExpConfigIndex.Rows(DataType.CardPack)" "SolarMemoryModeRuntime must resolve visible card packs through the shared config index."
Assert-Contains $solarMemoryModeRuntime "SunExpResourceCache.Load<Texture>" "SolarMemoryModeRuntime must resolve map-card textures through the shared resource cache."
Assert-NotContains $solarMemoryModeRuntime "ModeChoiceEntryRegistry.Register" "SolarMemoryModeRuntime must not own mode-choice entry registration."
Assert-NotContains $solarMemoryModeRuntime "ConfigureEntryTitleSprites" "SolarMemoryModeRuntime must not own mode-entry title sprite composition."
Assert-NotContains $solarMemoryModeRuntime "private static void OpenPackWindow" "SolarMemoryModeRuntime must not retain the retired pack-selection UI."
Assert-Contains $solarMemoryModeEntryRuntime "ModeChoiceEntryRegistry.Register" "SolarMemoryModeEntryRuntime must register the Solar Memory mode-choice entry."
Assert-Contains $solarMemoryModeEntryRuntime "ModeChoiceLayoutRuntime.Initialize(modConfig)" "SolarMemoryModeEntryRuntime must delegate mode-choice positioning to ModeChoiceLayoutRuntime."
Assert-Contains $solarMemoryModeEntryRuntime 'VisualRegistry.ModeEntry("solar_memory")' "SolarMemoryModeEntryRuntime must resolve title art from the visual registry."
Assert-Contains $solarMemoryModeRuntime 'VisualRegistry.TexturePath("solar_memory.event_map_card")' "SolarMemory fixed event cards must resolve their custom background texture from the visual registry."
Assert-Contains $endlessSeaModeEntryRuntime "ModeChoiceEntryRegistry.Register" "Endless Sea mode entry must register itself through the shared mode-choice entry registry."
Assert-Contains $endlessSeaModeEntryRuntime "EndlessSeaRunLauncher.Start" "Endless Sea mode entry must delegate run startup to EndlessSeaRunLauncher."
Assert-Contains $endlessSeaModeEntryRuntime '"SublimationMode",' "Endless Sea mode entry must reuse a native mode-choice template."
Assert-Contains $endlessSeaModeEntryRuntime "110," "Endless Sea mode entry must be ordered immediately after Solar Memory."
Assert-Contains $endlessSeaModeEntryRuntime "ModeChoiceLayoutRuntime.Initialize(modConfig)" "Endless Sea mode entry must use the shared mode-choice layout runtime."
Assert-Contains $endlessSeaModeRuntime "EndlessSeaModeEntryRuntime.Initialize(modConfig)" "Endless Sea runtime must delegate mode-choice entry visuals to EndlessSeaModeEntryRuntime."
Assert-NotContains $endlessSeaModeRuntime "ModeChoiceEntryRegistry.Register" "Endless Sea runtime must not own mode-choice entry registration."
Assert-Contains $endlessSeaRunLauncher "public static SaveInfo CreateSave" "Endless Sea run launcher must own save creation."
Assert-Contains $endlessSeaRunLauncher "EndlessSeaRunStateStore.FindLatestUnfinishedRun" "Endless Sea launcher must resume unfinished Endless Sea saves before creating a new run."
Assert-Contains $endlessSeaRunLauncher "ShowContinuePrompt" "Endless Sea launcher must prompt before continuing or replacing an unfinished Endless Sea run."
Assert-Contains $endlessSeaRunLauncher "buttonLayout.childControlWidth = true" "Endless Sea continue prompt buttons must be laid out horizontally with controlled widths."
Assert-Contains $endlessSeaRunLauncher "element.minHeight = 50f" "Endless Sea continue prompt buttons must keep a readable minimum height."
Assert-Contains $endlessSeaRunLauncher "EndlessSeaRunStateStore.DeleteUnfinishedRuns" "Endless Sea launcher must delete unfinished Endless Sea saves only after the player chooses a new run."
Assert-Contains $endlessSeaRunLauncher "modeType = NativeMapModeType" "Endless Sea saves must use native Normal mode so the official map manager can start."
Assert-Contains $endlessSeaRunLauncher "private const string NativeMapModeType = SunExpIds.NativeNormalModeType" "Endless Sea must keep native map startup on the official Normal mode manager."
Assert-Contains $endlessSeaRunLauncher "SetLobbyModeType(NativeMapModeType)" "Endless Sea lobby launch must reuse the native Normal mode manager."
Assert-NotContains $endlessSeaRunLauncher "SetLobbyModeType(SunExpIds.EndlessSeaModeType)" "Endless Sea must not pass its custom save mode type into the native lobby map startup."
Assert-NotContains $endlessSeaRunLauncher "modeType = SunExpIds.EndlessSeaModeType" "Endless Sea saves must not store custom modeType values that break native map startup."
Assert-Contains $endlessSeaRunLauncher "EndlessSeaRunStateStore.InitializeNewRun" "Endless Sea launcher must delegate save initialization to the run-state store."
Assert-Contains $endlessSeaRunStateStore "SunExpIds.EndlessSeaModeKey" "Endless Sea saves must persist a mode flag."
Assert-Contains $endlessSeaRunStateStore "saveInfo.modeType = SunExpIds.NativeNormalModeType" "Endless Sea run-state repair must migrate Endless Sea saves back to native Normal mode."
Assert-Contains $endlessSeaModeRuntime "EndlessSeaSaveCacheRuntime.Initialize(modConfig)" "Endless Sea runtime must isolate Endless Sea saves from the official Normal continue cache."
Assert-Contains (Read-RepoText "SunExp-Dev\Hooks\EndlessSeaSaveCacheRuntime.cs") '"ModeChoiceUI.DeleteExistingSavesForMode"' "Endless Sea runtime must protect Endless Sea saves from native Normal cleanup."
Assert-Contains $endlessSeaRunStateStore "DeleteUnfinishedRuns" "Endless Sea run-state store must own unfinished-run deletion."
Assert-Contains $endlessSeaRunStateStore "SunExpIds.EndlessSeaIntroSeenKey" "Endless Sea saves must initialize the intro-board flag."
Assert-Contains $endlessSeaRunStateStore "SunExpIds.EndlessSeaStarterDeckAppliedKey" "Endless Sea saves must initialize starter-deck state."
Assert-Contains $endlessSeaRunStateStore "SunExpIds.EndlessSeaRunIdKey" "Endless Sea saves must persist a run id."
Assert-Contains $endlessSeaRunStateStore "SunExpIds.EndlessSeaRunPhaseKey" "Endless Sea saves must persist the current run phase."
Assert-Contains $endlessSeaRunLauncher 'GameVar.ExLockDes.ToString()] = "0"' "Endless Sea saves must not pre-lock editable map slots."
Assert-Contains $endlessSeaIntroBoardRuntime "SunExpModalHost.CreateFullscreenRoot" "Endless Sea intro board must render through the shared modal host."
Assert-Contains $endlessSeaIntroBoardRuntime "ScrollRect" "Endless Sea intro body must support vertical scrolling."
Assert-Contains $endlessSeaIntroBoardRuntime "EndlessSeaRichTextSanitizer.Sanitize" "Endless Sea intro board must sanitize rich text before rendering."
Assert-Contains $endlessSeaIntroBoardRuntime "StarterDeckArbiterRuntime.ApplyDeck" "Endless Sea starter deck choices must use the shared starter-deck arbiter."
Assert-Contains $endlessSeaIntroBoardRuntime "sync: true" "Endless Sea starter deck choices must persist through the shared role sync path."
Assert-Contains $endlessSeaIntroBoardRuntime "SunExpIds.StarterDeckOwnerEndlessSea" "Endless Sea starter deck ownership must be mode-specific."
Assert-Contains $endlessSeaIntroBoardRuntime "AddTextFill(header.transform" "Endless Sea intro board subtitle must be rendered through the header."
Assert-Contains $endlessSeaIntroBoardRuntime "SetDeckButtonsInteractable" "Endless Sea starter deck buttons must expose visible application feedback and prevent repeat clicks."
Assert-Contains $endlessSeaIntroBoardRuntime '"MapManager.MapUIStart"' "Endless Sea intro board must open from map UI startup."
Assert-Contains $endlessSeaIntroBoardRuntime '"MapSelectUI.Start"' "Endless Sea intro board must retry once map selection UI exists."
Assert-NotContains $endlessSeaIntroBoardRuntime '"RoleTable.Init"' "Endless Sea intro board must not display during early role initialization."
Assert-NotContains $endlessSeaIntroBoardRuntime "WebView" "Endless Sea intro board must not embed a WebView."
Assert-NotContains $endlessSeaIntroBoardRuntime "Html" "Endless Sea intro board must not render arbitrary HTML."
Assert-NotContains $endlessSeaIntroBoardRuntime "ExecuteScript" "Endless Sea intro board must not execute script content."
Assert-Contains $endlessSeaStarterDeckCatalog "public const int FixedDeckSize = 11" "Endless Sea starter decks must keep an 11-card fixed opening package."
Assert-Contains $endlessSeaStarterDeckCatalog "public const int ThemeDeckSize = 4" "Endless Sea starter decks must add a 4-card theme package."
Assert-Contains $endlessSeaStarterDeckCatalog "public const int DeckSize = FixedDeckSize + ThemeDeckSize" "Endless Sea starter decks must total 15 cards from fixed plus theme packages."
Assert-Contains $endlessSeaStarterDeckCatalog '"academy_required"' "Endless Sea starter deck catalog must provide the Academy Required deck."
Assert-Contains $endlessSeaStarterDeckCatalog '"church_defense_tactics"' "Endless Sea starter deck catalog must provide the Church Defense Tactics theme deck."
Assert-Contains $endlessSeaStarterDeckCatalog '"chrono_journey"' "Endless Sea starter deck catalog must provide the Chrono Journey theme deck."
Assert-Contains $endlessSeaStarterDeckCatalog '"origin_of_elements"' "Endless Sea starter deck catalog must provide the Origin of Elements theme deck."
Assert-Contains $endlessSeaStarterDeckCatalog '"card_3"' "Endless Sea starter decks must use official base cards."
Assert-Contains $endlessSeaStarterDeckCatalog '"burningcard_1"' "Endless Sea starter decks must use official base cards."
Assert-NotContains $endlessSeaStarterDeckCatalog '"spark"' "Endless Sea starter decks must not use unresolved SunExp short card ids."
Assert-NotContains $endlessSeaStarterDeckCatalog '"solar_prayer"' "Endless Sea starter decks must not use unresolved SunExp short card ids."
Assert-Contains $endlessSeaStarterDeckCatalog "new DataConfig(cardId, DataType.Card)" "Endless Sea starter deck validation must resolve card ids through DataConfig."
Assert-NotMatches $endlessSeaStarterDeckCatalog "\r?\n\s+`"\*" "Endless Sea hardcoded starter decks must not include hidden/generated cards."
Assert-Contains $endlessSeaRichTextSanitizer "AllowedSimpleTags" "Endless Sea rich text sanitizer must use an explicit simple-tag allowlist."
Assert-Contains $endlessSeaRichTextSanitizer "AllowedScopedTags" "Endless Sea rich text sanitizer must use an explicit scoped-tag allowlist."
Assert-Contains $endlessSeaRichTextSanitizer "IsAllowedColorTag" "Endless Sea rich text sanitizer must validate color tags."
Assert-NotContains $endlessSeaRichTextSanitizer "link" "Endless Sea rich text sanitizer must not allow link tags."
Assert-NotContains $endlessSeaRichTextSanitizer "sprite" "Endless Sea rich text sanitizer must not allow sprite tags."
Assert-NotContains $endlessSeaRichTextSanitizer "font" "Endless Sea rich text sanitizer must not allow font tags."
Assert-Contains $solarMemoryMapVisualRuntime '"MapSelectUI.DataUpdate"' "SolarMemoryMapVisualRuntime must own the map title hook registration."
Assert-Contains $solarMemoryMapVisualRuntime '"NormalMapManager.MapItemInit"' "SolarMemoryMapVisualRuntime must own fixed-slot visual hook registration."
Assert-Contains $solarMemoryMapVisualRuntime '"MapSelectUI.ShowMap"' "SolarMemoryMapVisualRuntime must own map visual reapply hook registration."
Assert-Contains $solarMemoryMapItemAnimationRuntime "SunExpResourceCache.LoadAll<Texture2D>" "Solar memory map animation preview probes must use the shared LoadAll cache."
Assert-Contains $solarMemoryMapItemAnimationRuntime "SunExpConfigIndex.Row(type, id)" "Solar memory map animation row lookup must use the shared config index."
Assert-Contains $solarMemoryMapNodePoolFactory "SunExpConfigIndex.FilteredRows" "Solar memory boss candidate expansion must use a cached filtered config index."
Assert-Contains $endlessSeaNodePoolService "SunExpConfigIndex.FilteredRows(DataType.Map" "Endless Sea map nodes must draw from cached game map rows."
Assert-Contains $endlessSeaNodePoolService "IsEndlessSeaBossCandidate(row, floor)" "Endless Sea boss pool must be resolved separately from monster nodes."
Assert-Contains $endlessSeaEnemyPool "NormalBossEnemyIds" "Endless Sea must own an explicit normal boss enemy pool."
Assert-Contains $endlessSeaEnemyPool "SpecialBossEnemyIds" "Endless Sea must own an explicit special boss enemy pool."
Assert-Contains $endlessSeaNodePoolService "EndlessSeaNodeKind.Building => IsBuilding(row)" "Endless Sea building pool must be resolved separately from monster nodes."
Assert-Contains $endlessSeaNodePoolService "EndlessSeaNodeKind.Rest => IsRest(row)" "Endless Sea rest pool must be resolved separately from building nodes."
Assert-Contains $endlessSeaNodePoolService 'Type"), "Event"' "Endless Sea node pool must exclude event map rows."
Assert-Contains $endlessSeaFloorPlan "public sealed class EndlessSeaFloorPlan" "Endless Sea floor plans must be represented as a dedicated persisted model."
Assert-Contains $endlessSeaFloorPlanner "EndlessSeaNodeKind.Monster" "Endless Sea floor planning must fix the native start slot as a monster."
Assert-Contains $endlessSeaFloorPlanner "EndlessSeaNodeKind.Boss" "Endless Sea floor planning must fix the final boss slot."
Assert-Contains $endlessSeaFloorPlanner "EndlessSeaNodePoolService.CreateNode" "Endless Sea floor planning must consume the dedicated Endless Sea node pool."
Assert-Contains $endlessSeaFloorPlanner "new List<EndlessSeaSlotPlan>(SunExpIds.EndlessSeaNativeDefaultNodeCount)" "Endless Sea floor planning must prefill only native fixed slots."
Assert-Contains $endlessSeaFloorPlanStore "SunExpIds.EndlessSeaFloorPlanKey" "Endless Sea floor plans must persist through a centralized save key."
Assert-Contains $endlessSeaMapProjectionService "EndlessSeaNativeDefaultNodeCount" "Endless Sea native bootstrap must keep only the native start placeholder and boss defaults."
Assert-Contains $endlessSeaMapProjectionService "EndlessSeaNodeKind.Rest" "Endless Sea native bootstrap must feed the native Start slot a safe non-fight placeholder."
Assert-Contains $endlessSeaMapProjectionService 'NodeType(tree.DefaultNode[0]) != "Fight"' "Endless Sea native bootstrap projection must keep DefaultNode[0] safe for native Start initialization."
Assert-Contains $endlessSeaMapProjectionService 'NodeType(tree.DefaultNode[1]) == "Fight"' "Endless Sea native bootstrap projection must keep DefaultNode[1] as the boss fight."
Assert-Contains $endlessSeaSelectableNodeDeckPlanner "EndlessSeaNodeKind.Rest" "Endless Sea selectable node deck must include a rest node card."
Assert-Contains $endlessSeaSelectableNodeDeckPlanner "EndlessSeaNodeKind.Building" "Endless Sea selectable node deck must include a building node card."
Assert-NotContains $endlessSeaMapBuilder "TypeGenerate(" "Endless Sea map building must not reuse the native world-projection map generator."
Assert-Contains $endlessSeaMapBuilder "EndlessSeaFloorPlanner.Create" "Endless Sea map building must delegate visible floor composition to the floor planner."
Assert-Contains $endlessSeaMapBuilder "EndlessSeaFloorPlanStore.Save(plan)" "Endless Sea map building must persist the visible floor plan before native projection."
Assert-Contains $endlessSeaMapBuilder "EndlessSeaMapProjectionService.NativeDefaultOrder" "Endless Sea map building must delegate native bootstrap ordering to the projection service."
Assert-Contains $endlessSeaMapBuilder "EndlessSeaSelectableNodeDeckPlanner.CreateKinds" "Endless Sea map building must delegate selectable node composition to the deck planner."
Assert-Contains $endlessSeaMapBuilder "NativeDefaultOrder" "Endless Sea map building must adapt visual slots to native DefaultNode ordering."
Assert-Contains $endlessSeaMapBuilder "RepairFixedMapArrays" "Endless Sea map building must repair fixed start and boss sync arrays."
Assert-Contains $endlessSeaMapBuilder 'SetSaveValue(GameVar.ExLockDes.ToString(), "0")' "Endless Sea must leave editable native map slots unlocked."
Assert-Contains $endlessSeaMapViewPresenter "public static class EndlessSeaMapViewPresenter" "Endless Sea visible map presentation must be isolated from lifecycle hooks."
Assert-Contains $endlessSeaMapViewPresenter "EndlessSeaFloorPlanStore.TryLoad" "Endless Sea map presentation must restore visible slots from the persisted floor plan."
Assert-Contains $endlessSeaMapViewPresenter "ClearEditableSlots" "Endless Sea map presentation must clear middle slots for player node-card placement."
Assert-Contains $endlessSeaMapViewPresenter "nodes[slot].data = null" "Endless Sea editable map slots must start empty."
Assert-Contains $endlessSeaMapViewPresenter "PrefabNameForType" "Endless Sea map presentation must centralize native prefab selection."
Assert-Contains $endlessSeaMapViewPresenter '"FightPrefab"' "Endless Sea map presentation must use the native fight prefab for fight nodes."
Assert-Contains $endlessSeaMapViewPresenter '"EventPrefab"' "Endless Sea map presentation must use the native event prefab for building nodes."
Assert-Contains $endlessSeaMapViewPresenter "MapItemApi.ApplyCardBackgroundTexture" "Endless Sea map presentation must apply building card textures through the map item API."
Assert-NotContains $endlessSeaMapViewPresenter '"BuildPrefab"' "Endless Sea map presentation must not request a non-native BuildPrefab."
Assert-Contains $endlessSeaModeRuntime '"MapSelectUI.ReadyToSelect", EnsureSeaMapBeforeSelect' "Endless Sea must prepare SelectNode before native map candidates are created."
Assert-Contains $endlessSeaModeRuntime '"NormalMapManager.ReadyToChangeMap", AdvanceSeaFloorBeforeMapChange' "Endless Sea must intercept native map changes to advance infinite floors."
Assert-Contains $endlessSeaModeRuntime "MapManager.Instance.SetLevel(0)" "Endless Sea must reset native level before the base 32-layer cap can apply."
Assert-Contains $endlessSeaModeRuntime "RepairSeaMapSelection" "Endless Sea must repair fixed boss/building slots during map sync."
Assert-Contains $endlessSeaModeRuntime "EndlessSeaMapViewPresenter.ApplySlots" "Endless Sea runtime must delegate native map prefab repair to the map presenter."
Assert-Contains $endlessSeaModeRuntime "EndlessSeaMapViewPresenter.SetLayerTitle" "Endless Sea runtime must delegate visible layer title rendering to the map presenter."
Assert-NotContains $endlessSeaModeRuntime '"MapSelectUI.DataUpdate", ScheduleAbyssMapPanels' "Endless Sea must not request abyss panels from repeated MapSelectUI.DataUpdate ticks."
Assert-Contains $endlessSeaNetworkSync "applyAllSlots: false" "Endless Sea snapshot UI refresh must be fixed-slot only."
Assert-NotContains $endlessSeaNetworkSync "applyAllSlots: true" "Endless Sea snapshots must not clear editable map slots during interaction."
Assert-Contains $endlessSeaNetworkSync "SnapshotRequestThrottleSeconds" "Endless Sea client snapshot requests must be throttled."
Assert-Contains $endlessSeaNetworkSync "SunExpNetworkRuntime.HasRemotePlayers()" "Endless Sea snapshots must only run for real multiplayer sessions."
Assert-Contains $sunExpNetworkRuntime "public static bool HasRemotePlayers()" "SunExp network runtime must expose an actual remote-player guard."
Assert-Contains $endlessSeaCombatRuntime "EndlessSeaModeRuntime.IsEndlessSeaRun()" "Endless Sea combat tuning must be gated to Endless Sea runs."
Assert-Contains $endlessSeaCombatRuntime "EndlessAbyssEnemyInjectionService.TryInjectAfterFightInit" "Endless Abyss extra enemy injection must delegate to the dedicated service."
Assert-NotContains $endlessSeaCombatRuntime "CmdAddEnemy" "Endless Sea combat hooks must not directly issue native enemy-add commands."
Assert-Contains $endlessAbyssEnemyInjectionService "EnemyApi.IsClientOnlyDynamicEnemyObserver()" "Endless Abyss enemy injection must skip client-only observers before planning enemies."
Assert-Contains $endlessAbyssEnemyInjectionService "EnemyApi.AddDynamicEnemyAuthoritative" "Endless Abyss enemy injection must enter the native add path through SunExp's EnemyApi wrapper."
Assert-Contains $enemyApi "EnemyManager.Instance" "EnemyApi must resolve the native enemy manager from an authoritative path."
Assert-Contains $enemyApi "manager.AddEnemy(enemyId)" "EnemyApi must use the native dynamic enemy-add entrypoint from an authoritative path."
Assert-NotContains $enemyApi "CmdAddEnemy" "EnemyApi must not bypass native dynamic-add flow by issuing CmdAddEnemy directly."
Assert-Contains $runtimeHooks "EmberAdventureStateRuntime.Initialize(modConfig)" "Generic Ember adventure state restore must be registered outside Wuna career scripts."
Assert-Contains $emberAdventureStateRuntime "Fight_Start.Init" "Generic Ember adventure state restore must run at battle start."
Assert-Contains $emberAdventureStateService "RpcEmberAdventureStateCommit" "Ember adventure state commits must use the renamed generic RPC."
Assert-Contains $emberAdventureStateService "SunExpIds.WunaPersistentEmber" "Ember adventure state must keep the old Wuna persistent key as a compatibility fallback."
Assert-Contains $solarMemoryContentIsolationRuntime "SunExpConfigIndex.Rows(DataType.Map)" "Solar memory isolation replacement candidates must use cached map rows."
Assert-Contains $mapNodeSafetyService "SunExpConfigIndex.Row(DataType.Map, id)" "Map node safety fallback map lookup must use the shared config index."
Assert-Contains $solarMemorySettlementPresenter 'ShowUI<GameExitUI>("GameExitUI", true)' "SolarMemorySettlementPresenter must own settlement UI display."
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
Assert-Contains $modeChoiceLayoutRuntime "PlaceAfterNativeEntries(modeChoice" "ModeChoiceLayoutRuntime fallback placement must receive the ModeChoiceUI drag host."
Assert-Contains $modeChoiceLayoutRuntime "ConfigureHorizontalDrag(modeChoice, reference" "ModeChoiceLayoutRuntime fallback placement must expand drag bounds for appended entries."
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
Assert-Contains $solarMemoryPreparationRuntime "SolarMemorySetupFinishedKey" "SolarMemoryPreparationRuntime must gate completion on the final setup-finished flag."
Assert-Contains $solarMemoryPreparationRuntime "setup completion is pending retry" "SolarMemoryPreparationRuntime must keep failed final role commits retryable."
Assert-Contains $solarMemoryPreparationRuntime 'SolarMemoryPlayerSetupState.SetValue(SunExpIds.SolarMemorySetupCommitTokenKey, "")' "SolarMemoryPreparationRuntime must clear failed local commit tokens."
Assert-Contains $sunExpModalHost "public static Transform? ModalParent()" "SunExp modal windows must share a single modal parent resolver."
Assert-Contains $sunExpModalHost "SunExpUiSafety.CloseTransient" "SunExp modal close paths must route through the transient UI safety helper."
Assert-Contains $sunExpUiSafety "UiRaycastSafeDestroyRuntime.DisableAndHide" "SunExp transient UI teardown must disable and hide raycast surfaces before destroying."
Assert-Contains $sunExpUiSafety "ScrubGraphicRegistryForFrames" "SunExp transient UI teardown must scrub Unity's Graphic registry after destroying modal UI."
Assert-Contains $sunExpUiLifetimeScope "button.onClick.RemoveListener(action)" "Pooled UI event listeners must be removable through a scope."
Assert-Contains $sunExpUiPool "public static class SunExpUiPool" "Reusable SunExp UI pooling must stay in the Hooks UI boundary."
Assert-Contains $sunExpUiPool "ReleaseOrDestroyChildren" "SunExp UI pool must provide pooled child teardown for repeated list rebuilds."
Assert-Contains $sunExpUiPool "SunExpPerformanceSettings.UiPoolCapacityPerKey" "SunExp UI pool must obey performance-tier pool caps."
Assert-Contains $sunExpUiPool "button.onClick.RemoveAllListeners()" "SunExp UI pool must scrub button listeners before reuse."
Assert-Contains $sunExpUiSprites "private static readonly Dictionary<string, Sprite?> Cache" "SunExp UI sprites must be cached instead of loaded per window."
Assert-Contains $sunExpUiSprites "Sprite.Create(" "SunExp UI sprite helper must own nine-slice sprite creation."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpModalHost.Close(ref activePanel" "Starter deck modal close must use SunExpModalHost."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpModalHost.Close(ref activeOriginRoot" "Origin setup modal close must use SunExpModalHost."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpModalHost.Close(ref activeBlessingChrome" "Blessing setup chrome close must use SunExpModalHost."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpModalHost.Close(ref activePanel" "Blessing picker modal close must use SunExpModalHost."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpUiPool.AcquireComponent" "Starter deck rows must use the shared local UI pool."
Assert-Contains $solarMemoryStarterDeckRuntime "deckListDirty.ShouldRefresh" "Starter deck selected-card list must skip unchanged rebuilds."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpUiPool.AcquireComponent" "Blessing picker rows must use the shared local UI pool."
Assert-Contains $solarMemoryBlessingPickerRuntime "candidateListDirty.ShouldRefresh" "Blessing picker candidates must skip unchanged rebuilds."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpUiSprites.Button" "Starter deck modal must use shared cached button sprites."
Assert-Contains $solarMemorySetupFlowRuntime "SunExpUiSprites.Button" "Setup modal must use shared cached button sprites."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpUiSprites.Button" "Blessing picker modal must use shared cached button sprites."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "CreateNineSliceSprite" "Solar Memory setup windows must not duplicate nine-slice sprite creation."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "GetButtonSprite" "Solar Memory setup windows must not own duplicate button sprite caches."
Assert-NotContains ($solarMemoryStarterDeckRuntime + $solarMemorySetupFlowRuntime + $solarMemoryBlessingPickerRuntime) "Object.Destroy(active" "Solar Memory setup windows must not directly destroy active modal roots."
Assert-Contains $solarMemoryStarterDeckRuntime "SunExpResourceCache.Load<Sprite>" "Starter deck icons must use the shared resource cache."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpConfigIndex.Rows(DataType.Bless)" "Blessing picker pools must use cached blessing rows before native CardPackCheck."
Assert-Contains $solarMemoryBlessingPickerRuntime "SunExpResourceCache.Load<Sprite>" "Blessing picker icons must use the shared resource cache."
Assert-Contains $wunaOrbitFireController "SunExpPerformanceSettings.WunaOrbitFireEnabled" "Wuna orbit fire visuals must support performance-tier disabling."
Assert-Contains $wunaOrbitFireController "SunExpPerformanceSettings.WunaGeometryInterval" "Wuna orbit fire geometry rebuilds must be throttled by performance quality."
Assert-Contains $wunaOrbitFireController "SunExpPerformanceSettings.WunaCoreSections" "Wuna orbit fire geometry density must be quality-controlled."
Assert-Contains $wunaOrbitFireController "WunaOrbitFire.BuildGeometry" "Wuna orbit fire geometry rebuilds must be measured by performance counters."
Assert-NotContains $sunExpHardTagRuntime "WhiteRadiance.ScanFightZones" "White Radiance Court must not retain the retired fight-zone scan performance counter."
Assert-NotContains $sunExpHardTagRuntime "ApplyWhiteRadianceToFightZones" "White Radiance Court must not mutate combat card zones."

Assert-NotContains $scriptingSource "using SunExp.Dll.Hooks" "Scripting layer must not import Hooks."
Assert-NotContains $scriptingSource "SunExpFrameScheduler" "Scripting layer must not use the hook-owned frame scheduler directly."
Assert-NotMatches $scriptingSource "\.\s*Add(?:Temp)?Event\s*\(" "Scripting layer must register events through ScriptEventApi or ExecutorApi wrappers."

$resourceLoaderBypass = @($sourceFiles | Where-Object { $_.Name -ne "SunExpResourceCache.cs" } | Select-String -Pattern "ResourceLoader\.Load(?:All)?(?:<|\s*\()")
Assert-True ($resourceLoaderBypass.Count -eq 0) "ResourceLoader.Load/LoadAll calls must be centralized in SunExpResourceCache."

$configTableBypass = @($sourceFiles | Where-Object {
        $_.Name -ne "SunExpConfigIndex.cs" -and $_.FullName -notmatch "\\GameApi\\"
    } | Select-String -Pattern "GetTable\(")
Assert-True ($configTableBypass.Count -eq 0) "Hook and Mechanics table scans must be centralized in SunExpConfigIndex."

$dataFiles = Get-ChildItem -LiteralPath (Join-Path $RepoRoot "SunExp\Data") -Recurse -File -Filter "*.csv"
foreach ($file in $dataFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($text, "CS\.SunExp\.Dll\.([A-Za-z0-9_\.]+)")) {
        $target = $match.Groups[1].Value
        Assert-True ($target.StartsWith("Scripting.", [System.StringComparison]::Ordinal)) "Data script target must route through Scripting: $($file.FullName) -> $($match.Value)"
    }
}

$dialogueData = Read-RepoText "SunExp\Data\Dialogue\sunexp.csv"
Assert-NotContains $dialogueData "CS.SunExp.Dll.Scripting" "Managed dialogue rows must not call C# through native Dialogue script columns."

Write-Host "SunExp architecture assertions passed."
