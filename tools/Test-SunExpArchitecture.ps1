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
    "SunExp-Dev\Mechanics\VisualRegistry.cs",
    "SunExp-Dev\Mechanics\VisualRegistryModels.cs",
    "SunExp-Dev\Mechanics\CardVisualSkinSpec.cs",
    "SunExp-Dev\Mechanics\CardVisualThemeCatalog.cs",
    "SunExp-Dev\Mechanics\SunCardThemeCatalog.cs",
    "SunExp-Dev\Hooks\DialogueFlowRuntime.cs",
    "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs",
    "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs",
    "SunExp-Dev\Hooks\CardVisualSkinRuntime.cs",
    "SunExp-Dev\Hooks\SunCardFrameRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryMapVisualRuntime.cs",
    "SunExp-Dev\Hooks\SolarMemoryModeEntryRuntime.cs",
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
    "SunExp-Dev\Hooks\Ui\SunExpUiSprites.cs",
    "SunExp-Dev\Hooks\Visual\FrameAnimationAttacher.cs",
    "SunExp-Dev\Hooks\Visual\AssetBundleCache.cs",
    "SunExp-Dev\Hooks\Visual\EffectMaterialFactory.cs",
    "SunExp-Dev\Hooks\Visual\EffectTextureCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameImageAnimator.cs",
    "SunExp-Dev\Hooks\Visual\CardVisualSkinApplier.cs",
    "SunExp-Dev\Hooks\Visual\CardVisualSkinSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\SunCardFrameApplier.cs",
    "SunExp-Dev\Hooks\Visual\SunCardFrameSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteAnimationSpec.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteCache.cs",
    "SunExp-Dev\Hooks\Visual\FrameSpriteRendererAnimator.cs",
    "SunExp-Dev\Hooks\Visual\ShaderAssetLoader.cs",
    "SunExp-Dev\VisualAssets\sunexp_visuals.pipeline.json",
    "SunExp-Dev\VisualAssets\Editor\SunExpVisualBundleBuilder.cs.txt",
    "SunExp-Dev\VisualAssets\Shaders\StarScoreHud.shader",
    "SunExp-Dev\Mechanics\StarScoreCadenceCatalog.cs",
    "SunExp-Dev\Mechanics\StarScoreDisplaySnapshot.cs",
    "SunExp-Dev\Mechanics\StarScoreNote.cs",
    "SunExp\visual.registry.json"
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
$visualRegistry = Read-RepoText "SunExp-Dev\Mechanics\VisualRegistry.cs"
$visualRegistryModels = Read-RepoText "SunExp-Dev\Mechanics\VisualRegistryModels.cs"
$visualRegistryJson = Read-RepoText "SunExp\visual.registry.json"
$dialogueFlowService = Read-RepoText "SunExp-Dev\Mechanics\DialogueFlowService.cs"
$battleRewardAdjustmentService = Read-RepoText "SunExp-Dev\Mechanics\BattleRewardAdjustmentService.cs"
$solarMemoryStoryGateService = Read-RepoText "SunExp-Dev\Mechanics\SolarMemoryStoryGateService.cs"
$dialogueFlowRuntime = Read-RepoText "SunExp-Dev\Hooks\DialogueFlowRuntime.cs"
$battleRewardAdjustmentRuntime = Read-RepoText "SunExp-Dev\Hooks\BattleRewardAdjustmentRuntime.cs"
$solarMemoryRewardRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryRewardRuntime.cs"
$mapNodeCardArtRuntime = Read-RepoText "SunExp-Dev\Hooks\MapNodeCardArtRuntime.cs"
$runtimeHooks = Read-RepoText "SunExp-Dev\Hooks\RuntimeHooks.cs"
$cardVisualSkinSpec = Read-RepoText "SunExp-Dev\Mechanics\CardVisualSkinSpec.cs"
$cardVisualThemeCatalog = Read-RepoText "SunExp-Dev\Mechanics\CardVisualThemeCatalog.cs"
$cardVisualSkinRuntime = Read-RepoText "SunExp-Dev\Hooks\CardVisualSkinRuntime.cs"
$cardVisualSkinApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualSkinApplier.cs"
$cardVisualSkinSpriteCache = Read-RepoText "SunExp-Dev\Hooks\Visual\CardVisualSkinSpriteCache.cs"
$sunCardFrameRuntime = Read-RepoText "SunExp-Dev\Hooks\SunCardFrameRuntime.cs"
$sunCardFrameApplier = Read-RepoText "SunExp-Dev\Hooks\Visual\SunCardFrameApplier.cs"
$sunCardFrameSpriteCache = Read-RepoText "SunExp-Dev\Hooks\Visual\SunCardFrameSpriteCache.cs"
$sunCardThemeCatalog = Read-RepoText "SunExp-Dev\Mechanics\SunCardThemeCatalog.cs"
$solarMemoryModeRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeRuntime.cs"
$solarMemoryMapVisualRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryMapVisualRuntime.cs"
$solarMemoryModeEntryRuntime = Read-RepoText "SunExp-Dev\Hooks\SolarMemoryModeEntryRuntime.cs"
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
$visualPipeline = Read-RepoText "SunExp-Dev\VisualAssets\sunexp_visuals.pipeline.json"
$visualBundleBuilder = Read-RepoText "SunExp-Dev\VisualAssets\Editor\SunExpVisualBundleBuilder.cs.txt"
$starScoreHudShaderSource = Read-RepoText "SunExp-Dev\VisualAssets\Shaders\StarScoreHud.shader"
$starScoreService = Read-RepoText "SunExp-Dev\Mechanics\StarScoreService.cs"
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
Assert-Contains $runtimeHooks "CardVisualSkinRuntime.Initialize(modConfig)" "RuntimeHooks must initialize card visual skin hooks."
Assert-Contains $runtimeHooks "RunHookStep(" "RuntimeHooks must isolate runtime initialization into logged hook steps."
Assert-Contains $runtimeHooks "AuraSharedHooks.RunStep" "RuntimeHooks must use the shared step guard for hook initialization."
Assert-Contains $sunExpIds "SunCardVisualSkinId" "SunExpIds must centralize the Sun card visual skin id."
Assert-Contains $sunExpIds "MorningStarCardVisualSkinId" "SunExpIds must centralize the Morning Star card visual skin id."
Assert-Contains $sunExpIds "SunThemeCardPackIds" "SunExpIds must centralize Sun theme card-pack ids."
Assert-Contains $sunExpIds "StellarOvertureCardIds" "SunExpIds must centralize Stellar Overture card ids."
Assert-Contains $sunExpIds "SunThemeExplicitCardIds" "SunExpIds must centralize explicit Sun theme card ids."
Assert-Contains $sunExpIds "StellarOvertureCardIconPathPrefix" "SunExpIds must centralize Stellar Overture icon-path fallback rules."
Assert-Contains $sunExpIds "WunaCoronationTokenCardId" "SunExpIds must centralize Wuna's generated Coronation token id."
Assert-Contains $sunExpIds "SunCardFramePath" "SunExpIds must centralize the Sun card frame resource path."
Assert-Contains $sunExpIds "SunCardBackgroundPath" "SunExpIds must centralize the optional Sun card background resource path."
Assert-Contains $sunExpIds "MorningStarCardFramePath" "SunExpIds must centralize the Morning Star card frame resource path."
Assert-Contains $cardVisualSkinSpec "public sealed class CardVisualSkinSpec" "Card visual skins must use a typed skin specification."
Assert-Contains $cardVisualThemeCatalog 'DictionaryUtil.Get(config.data, "PackBelong")' "Card visual themes must primarily resolve Sun cards by PackBelong."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunThemeCardPackIds" "Card visual themes must use centralized Sun card-pack ids."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunCardFramePath" "Card visual themes must attach the Sun frame path through the theme spec."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunCardBackgroundPath" "Card visual themes must attach the optional Sun background path through the theme spec."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.StellarOvertureCardIds" "Card visual themes must resolve Stellar Overture cards from centralized ids."
Assert-Contains $cardVisualThemeCatalog "StarScoreService.IsStellarOvertureCard" "Card visual themes must reuse the Star Score card-id classifier."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.StellarOvertureCardIconPathPrefix" "Card visual themes must fall back to Stellar Overture icon paths."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.SunThemeExplicitCardIds" "Card visual themes must support explicit generated Sun-theme cards."
Assert-Contains $cardVisualThemeCatalog "SunExpIds.MorningStarCardFramePath" "Card visual themes must attach the Morning Star frame path through the theme spec."
Assert-Contains $cardVisualThemeCatalog "IsStellarOvertureCard" "Card visual themes must expose a Stellar Overture theme predicate."
Assert-Contains $sunCardThemeCatalog "CardVisualThemeCatalog.Resolve" "Legacy Sun card theme checks must delegate to the generic card visual theme catalog."
Assert-Contains $cardVisualSkinRuntime '"ICard.SetCardStyle"' "Card visual skin runtime must keep a shared card-style fallback hook."
Assert-Contains $cardVisualSkinRuntime '"CardItem.Init"' "Card visual skin runtime must hook concrete base card initialization."
Assert-Contains $cardVisualSkinRuntime '"AttackCardItem.Init"' "Card visual skin runtime must hook concrete attack-card initialization."
Assert-Contains $cardVisualSkinRuntime '"CardItem.DataUpdate"' "Card visual skin runtime must keep a battle-card repaint fallback."
Assert-Contains $cardVisualSkinRuntime '"FightUI.CreateCardItemInternal"' "Card visual skin runtime must reapply generated hand cards after native UI creation."
Assert-Contains $cardVisualSkinRuntime '"ScriptExecutor.GetCardFromDeck"' "Card visual skin runtime must cover dynamic script-delivered cards."
Assert-Contains $cardVisualSkinRuntime "ReapplyActiveCombatCards" "Card visual skin runtime must centralize active combat-card reapplication."
Assert-Contains $cardVisualSkinRuntime '"DictItem.Init"' "Card visual skin runtime must cover dictionary item cards."
Assert-Contains $cardVisualSkinRuntime '"DictionaryShowItem.Init"' "Card visual skin runtime must cover dictionary detail cards."
Assert-Contains $cardVisualSkinRuntime '"DisplayCard.Init"' "Card visual skin runtime must cover display cards."
Assert-Contains $cardVisualSkinRuntime '"ShowCard.Init"' "Card visual skin runtime must cover deck-show full cards."
Assert-Contains $cardVisualSkinRuntime '"SafeBoxItem.Init"' "Card visual skin runtime must cover safe-box full cards."
Assert-Contains $cardVisualSkinRuntime '"EnchCardItem.Init"' "Card visual skin runtime must cover enchantment cards."
Assert-Contains $cardVisualSkinRuntime '"CardChoiceItem.Initialize"' "Card visual skin runtime must cover reward choice cards."
Assert-Contains $cardVisualSkinRuntime '"PackShowItem.Init"' "Card visual skin runtime must cover card-pack display cards."
Assert-Contains $cardVisualSkinRuntime '"ShopItem.Init"' "Card visual skin runtime must cover shop cards."
Assert-Contains $cardVisualSkinRuntime '"WarehouseItem.Init"' "Card visual skin runtime must cover warehouse cards."
Assert-Contains $cardVisualSkinRuntime "CardVisualSkinApplier.Apply" "Card visual hooks must delegate Unity mutation to the generic visual applier."
Assert-Contains $cardVisualSkinApplier "CardVisualThemeCatalog.Resolve" "Card visual skin applier must gate visuals through the theme catalog."
Assert-Contains $cardVisualSkinApplier 'cardRoot.Find("Front/FrontBack")' "Card visual skin applier must replace the card-frame layer."
Assert-Contains $cardVisualSkinApplier 'cardRoot.Find("Front/background")' "Card visual skin applier must support the optional card-background layer."
Assert-Contains $cardVisualSkinSpriteCache "ResourceLoader.Load<Sprite>" "Card visual skin sprites must load through ResourceLoader."
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
Assert-Contains $visualPipeline '"bundleName": "sunexp_visuals"' "Visual pipeline must declare the private SunExp bundle name."
Assert-Contains $visualPipeline '"materialPath": "SunExp/Materials/StarScoreHudLit"' "Visual pipeline must match the runtime star-score material asset path."
Assert-Contains $visualBundleBuilder "BuildPipeline.BuildAssetBundles" "Visual pipeline must provide a Unity Editor bundle build entrypoint."
Assert-Contains $visualBundleBuilder 'private const string BundleName = "sunexp_visuals"' "Visual bundle builder must match the runtime bundle name."
Assert-Contains $visualBundleBuilder 'private const string MaterialAssetPath = "Assets/SunExp/Visuals/Materials/StarScoreHudLit.mat"' "Visual bundle builder must create the declared star-score material asset."
Assert-Contains $sunExpProject "UnityEngine.AssetBundleModule" "SunExp must reference UnityEngine.AssetBundleModule for private shader bundles."
Assert-Contains $assetBundleCache "AssetBundle.LoadFromFile" "AssetBundleCache must load private visual bundles from files."
Assert-Contains $assetBundleCache "VisualRegistry.ResolveContentPath" "AssetBundleCache must resolve SunExp-private bundle paths through the visual registry."
Assert-Contains $effectMaterialFactory "AssetBundleCache.LoadAsset<Material>" "EffectMaterialFactory must prefer declared material assets from private bundles."
Assert-Contains $effectMaterialFactory "ShaderAssetLoader.ResolveShader" "EffectMaterialFactory must fall back to declared shaders when no material asset is bundled."
Assert-Contains $effectMaterialFactory "EffectTextureCache.Load" "EffectMaterialFactory must apply declared effect textures."
Assert-Contains $effectTextureCache "ResourceLoader.Load<Texture>" "EffectTextureCache must load declared effect textures through ResourceLoader."
Assert-Contains $frameSpriteCache "private static readonly Dictionary<string, Sprite[]> Cache" "FrameSpriteCache must centralize loaded sprite-frame caching."
Assert-Contains $frameSpriteCache "ResourceLoader.Load<Sprite>" "FrameSpriteCache must own sprite-frame resource loading."
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
Assert-Contains $shaderAssetLoader "ResourceLoader.Load<Shader>" "ShaderAssetLoader must support declared shader resource paths."
Assert-Contains $shaderAssetLoader "ResourceLoader.Load<Material>" "ShaderAssetLoader must support declared material resource paths."
Assert-Contains $starScoreHudShaderSource 'Shader "SunExp/StarScoreHud"' "Star score HUD shader source must match the runtime shader id."
Assert-Contains $starScoreHudShaderSource "_SunExpFlowStrength" "Star score HUD shader source must expose the runtime flow property."
Assert-Contains $starScoreHudShaderSource "_SunExpPulse" "Star score HUD shader source must expose the runtime pulse property."
Assert-Contains $starScoreHudShaderSource "UnityGet2DClipping" "Star score HUD shader source must remain UI clipping compatible."
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
Assert-Contains $starScoreHudTooltipView "SunExpUiSafety.DestroyChildren" "StarScoreHudTooltipView must clear row rebuilds through shared UI safety."
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
Assert-NotContains $solarMemoryModeRuntime "ModeChoiceEntryRegistry.Register" "SolarMemoryModeRuntime must not own mode-choice entry registration."
Assert-NotContains $solarMemoryModeRuntime "ConfigureEntryTitleSprites" "SolarMemoryModeRuntime must not own mode-entry title sprite composition."
Assert-NotContains $solarMemoryModeRuntime "private static void OpenPackWindow" "SolarMemoryModeRuntime must not retain the retired pack-selection UI."
Assert-Contains $solarMemoryModeEntryRuntime "ModeChoiceEntryRegistry.Register" "SolarMemoryModeEntryRuntime must register the Solar Memory mode-choice entry."
Assert-Contains $solarMemoryModeEntryRuntime "ModeChoiceLayoutRuntime.Initialize(modConfig)" "SolarMemoryModeEntryRuntime must delegate mode-choice positioning to ModeChoiceLayoutRuntime."
Assert-Contains $solarMemoryModeEntryRuntime 'VisualRegistry.ModeEntry("solar_memory")' "SolarMemoryModeEntryRuntime must resolve title art from the visual registry."
Assert-Contains $solarMemoryModeRuntime 'VisualRegistry.TexturePath("solar_memory.event_map_card")' "SolarMemory fixed event cards must resolve their custom background texture from the visual registry."
Assert-Contains $solarMemoryMapVisualRuntime '"MapSelectUI.DataUpdate"' "SolarMemoryMapVisualRuntime must own the map title hook registration."
Assert-Contains $solarMemoryMapVisualRuntime '"NormalMapManager.MapItemInit"' "SolarMemoryMapVisualRuntime must own fixed-slot visual hook registration."
Assert-Contains $solarMemoryMapVisualRuntime '"MapSelectUI.ShowMap"' "SolarMemoryMapVisualRuntime must own map visual reapply hook registration."
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
Assert-NotContains $dialogueData "CS.SunExp.Dll.Scripting" "Managed dialogue rows must not call C# through native Dialogue script columns."

Write-Host "SunExp architecture assertions passed."
