param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$utf8 = [System.Text.Encoding]::UTF8

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$cardRows = @(Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Card\sunexp.csv"))
$ball = $cardRows | Where-Object Id -eq "spirit_ball" | Select-Object -First 1
$template = $cardRows | Where-Object Id -eq "*spirit_card_template" | Select-Object -First 1
$courtPurification = $cardRows | Where-Object Id -eq "afterglow_omen_card" | Select-Object -First 1
Assert-True ($null -ne $ball) "spirit_ball card row is missing."
Assert-True ($ball.Rarity -eq "3" -and $ball.Expend -eq "1") "spirit_ball must be rarity 3 and cost 1."
Assert-True ($ball.Tag -eq "Retain,Annihilation") "spirit_ball must have Retain and Annihilation."
Assert-True ($ball.Icon -eq "Mods/SunExp/ModResource/Images/Card/MoreDimension/spirit_ball") "spirit_ball must use its independent card face."
$runtimeFacePath = Join-Path $repoRoot "SunExp\ModResource\Images\Card\MoreDimension\spirit_ball.png"
$sourceFacePath = Join-Path $repoRoot "SunExp-Dev\VisualAssets\CardSource512\MoreDimension\spirit_ball.png"
Assert-True (Test-Path -LiteralPath $runtimeFacePath) "spirit_ball card face is missing."
Assert-True (Test-Path -LiteralPath $sourceFacePath) "spirit_ball 512 source is missing."
Add-Type -AssemblyName System.Drawing
$runtimeFace = [System.Drawing.Image]::FromFile($runtimeFacePath)
$sourceFace = [System.Drawing.Image]::FromFile($sourceFacePath)
try {
    Assert-True ($runtimeFace.Width -eq 256 -and $runtimeFace.Height -eq 256) "spirit_ball runtime card face must be 256x256."
    Assert-True ($sourceFace.Width -eq 512 -and $sourceFace.Height -eq 512) "spirit_ball source card face must remain 512x512."
}
finally {
    $runtimeFace.Dispose()
    $sourceFace.Dispose()
}
Assert-True ($null -ne $template) "spirit card template row is missing."
Assert-True ($template.Tag -eq "Retain,Burnout") "spirit card must have Retain and Burnout only."
Assert-True ($template.Icon -eq $ball.Icon) "spirit card template must fall back to the spirit_ball card face."
$courtPurificationTag = $utf8.GetString([Convert]::FromBase64String("UmV0YWluLOeZveabnCxBbm5paGlsYXRpb24="))
Assert-True ($null -ne $courtPurification -and $courtPurification.Tag -eq $courtPurificationTag) "Court Purification must retain its visible Annihilation tag."

$intentPath = Join-Path $repoRoot "SunExp\spirit.intent.registry.json"
$capturePath = Join-Path $repoRoot "SunExp\spirit.capture.registry.json"
$intentJson = Get-Content -LiteralPath $intentPath -Raw
$intent = $intentJson | ConvertFrom-Json
$capture = Get-Content -LiteralPath $capturePath -Raw | ConvertFrom-Json
Assert-True ($intent.schemaVersion -eq 3) "spirit intent registry schema must be 3."
Assert-True ($capture.schemaVersion -eq 1) "spirit capture registry schema must be 1."

$intentProfileListFields = @(
    'sourceEnemyCardIds',
    'pveAttackTendency',
    'pveDefenseTendency',
    'pvpAttackTendency',
    'pvpDefenseTendency',
    'fallbackAttackTendency',
    'fallbackDefenseTendency',
    'pvpSourceEnemyCardIds',
    'fallbackSourceEnemyCardIds'
)
foreach ($profile in @($intent.profiles)) {
    foreach ($field in $intentProfileListFields) {
        $property = $profile.PSObject.Properties[$field]
        Assert-True ($null -ne $property) "spirit profile $($profile.enemyId) is missing list field $field."
        $actualType = if ($null -eq $property.Value) { 'null' } else { $property.Value.GetType().Name }
        Assert-True ($property.Value -is [System.Array]) "spirit profile $($profile.enemyId) field $field must be a JSON array, actual=$actualType."
    }
}
foreach ($profile in @($capture.profiles)) {
    Assert-True ($profile.suppressedSuccessorIds -is [System.Array]) "capture profile $($profile.enemyId) suppressedSuccessorIds must be a JSON array."
}

$registryTestProject = Join-Path $repoRoot "SunExp-Dev.RegistryTests\SunExp-Dev.RegistryTests.csproj"
Assert-True (Test-Path -LiteralPath $registryTestProject) "C# registry smoke-test project is missing."
& dotnet run --project $registryTestProject -c Release -- $intentPath
if ($LASTEXITCODE -ne 0) {
    throw "C# SpiritIntentRegistryDocument deserialization smoke test failed."
}

$explicitIntents = @($intent.profiles | Where-Object enemyId -ne "*")
$explicitCapture = @($capture.profiles | Where-Object enemyId -ne "*")
Assert-True ($explicitIntents.Count -ge 59) "expected at least 59 explicit spirit intent profiles."
Assert-True ($explicitCapture.Count -eq $explicitIntents.Count) "intent and capture profile counts must match."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "intent fallback profile is missing."
Assert-True ((@($capture.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "capture fallback profile is missing."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "10026" -and $_.variantId -eq "*" })).Count -eq 1) "base-game enemy 10026 must retain its canonical dedicated intent profile."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "enemy_10026" })).Count -eq 0) "runtime enemy prefixes must be handled by the shared resolver, not duplicated into registry data."

foreach ($profile in $explicitIntents) {
    Assert-True (@($profile.fallbackAttackTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no attack fallback."
    Assert-True (@($profile.fallbackDefenseTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no defense fallback."
    Assert-True ($profile.attackWeight -gt 0 -and $profile.defenseWeight -gt 0) "spirit profile $($profile.enemyId) has invalid tendency weights."
}

$adaptedSources = @($intent.intents | Where-Object pool -eq 'Pve' | ForEach-Object enemyCardId | Sort-Object -Unique)
$pvpSources = @($explicitIntents.pvpSourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$fallbackSources = @($explicitIntents.fallbackSourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$allSources = @($explicitIntents.sourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$classifiedSources = @(($adaptedSources + $pvpSources + $fallbackSources) | Sort-Object -Unique)
Assert-True (@($intent.intents).Count -ge 66) "expected generated PvE composite and PvP reserved spirit intents."
Assert-True ((@($intent.intents | Where-Object pool -eq 'Pve').Count) -eq $adaptedSources.Count) "each adapted enemy card must map to exactly one PvE intent."
Assert-True ((@($intent.intents | Where-Object pool -eq 'Pve').Count) -eq 54) "expected 54 generated PvE spirit intents."
Assert-True ((@($intent.intents | Where-Object pool -eq 'PvpReserved').Count) -eq 12) "expected 12 generated PvP-reserved spirit intents."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq 'Pve' -and @($_.effects).Count -eq 0 })).Count -eq 0) "every PvE spirit intent must declare its authoritative effect list."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq 'Pve' } | ForEach-Object { @($_.effects) } | Where-Object { $_.displayIndex -le 0 })).Count -eq 0) "every PvE effect must bind a positive description placeholder index."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq 'Pve' } | ForEach-Object { @($_.effects) } | Where-Object {
    if ($_.handlerId -eq 'buff.apply') { [string]::IsNullOrWhiteSpace([string]$_.buffId) -or [int]$_.buffStacks -le 0 }
    else { [double]$_.flatValue -le 0 -and [double]$_.attackScale -le 0 -and [double]$_.armorScale -le 0 -and [double]$_.magicScale -le 0 }
})).Count -eq 0) "every executable PvE effect must resolve from a positive formula or buff stack count."
foreach ($pveIntent in @($intent.intents | Where-Object pool -eq 'Pve')) {
    $indices = @($pveIntent.effects | ForEach-Object { [int]$_.displayIndex } | Sort-Object)
    Assert-True (($indices -join ',') -eq ((1..$indices.Count) -join ',')) "intent $($pveIntent.id) must use contiguous description slots."
}
Assert-True ((@($intent.intents | Where-Object enemyCardId -eq 'enemycard_CAR_Shield').effects).Count -eq 2) "multi-buff enemy cards must preserve every supported buff effect."
Assert-True ((@($intent.intents | Where-Object enemyCardId -eq 'enemycard_specialAttack').effects).Count -eq 2) "damage-plus-block enemy cards must remain one composite intent."
Assert-True ((Compare-Object $allSources $classifiedSources).Count -eq 0) "every source enemy card must be adapted, PvP-reserved, or explicitly fallback."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq 'PvpReserved' -and $_.handlerId -ne 'pvp.reserved' })).Count -eq 0) "PvP intents must remain on the inert reserved handler."
$expectedPvpSources = @("enemycard_Dragon'sMajesty",'enemycard_EvilCurse','enemycard_obtainMoney','enemycard_OriginalSinCard','enemycard_PlugCards1','enemycard_PlugCards2','enemycard_PlugCards3','enemycard_PowerlessCurse','enemycard_psychologicalShock','enemycard_thief','enemycard_Thieves','enemycard_VenomSpray') | Sort-Object
$expectedFallbackSources = @('enemycard_Charge1','enemycard_Charge2','enemycard_Come','enemycard_Wake','enemycard_WhereverYouGo') | Sort-Object
Assert-True (($pvpSources -join '|') -eq ($expectedPvpSources -join '|')) "PvP source reservation set drifted."
Assert-True (($fallbackSources -join '|') -eq ($expectedFallbackSources -join '|')) "unsupported fallback source set drifted."

function Get-Chance([int]$CurrentHp, [int]$MaxHp) {
    $missing = 10000 - [int][Math]::Round($CurrentHp * 10000.0 / $MaxHp, [MidpointRounding]::AwayFromZero)
    return [Math]::Max(1000, [Math]::Min(9000, 1000 + [int][Math]::Round($missing * 0.8, [MidpointRounding]::AwayFromZero)))
}

Assert-True ((Get-Chance 100 100) -eq 1000) "full-health capture chance must be 10%."
Assert-True ((Get-Chance 50 100) -eq 5000) "half-health capture chance must be 50%."
Assert-True ((Get-Chance 0 100) -eq 9000) "zero-health formula cap must be 90%."

$requiredSources = @(
    "SunExp-Dev\GameApi\EnemyCatalogApi.cs",
    "SunExp-Dev\GameApi\EnemyCaptureSettlementApi.cs",
    "SunExp-Dev\Mechanics\SpiritCaptureService.cs",
    "SunExp-Dev\Mechanics\SpiritProfileIdentityResolver.cs",
    "SunExp-Dev\Mechanics\SpiritSummonService.cs",
    "SunExp-Dev\Hooks\SpiritRuntime.cs",
    "SunExp-Dev\Network\RpcSpiritCapture.cs",
    "SunExp-Dev\Network\RpcSpiritCompanion.cs"
)
foreach ($relative in $requiredSources) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "required spirit source missing: $relative"
}

$factorySource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritCardFactory.cs") -Raw -Encoding UTF8
Assert-True ($factorySource.Contains("RoleTable.Instance?.cardList")) "spirit cards must persist into the current adventure deck."
Assert-True ($factorySource.Contains('"RawData"') -and $factorySource.Contains("AuraSharedJson.Serialize(persistedData)")) "spirit cards must persist merged dynamic data for safe-box restoration."
Assert-True (-not $factorySource.Contains("var data = config.data;")) "spirit cards must not treat DataConfig.data as a writable dictionary."
Assert-True (-not [regex]::IsMatch($factorySource, 'Set\s*\(\s*config\.data')) "spirit cards must never mutate read-only DataConfig.data."
Assert-True ($factorySource.Contains("var runtime = new Dictionary<string, string>();")) "spirit cards must stage dynamic values in a writable runtime dictionary."
Assert-True ($factorySource.Contains("DictionaryUtil.Set(config.Vars, entry.Key, entry.Value);")) "spirit cards must write runtime overrides through Vars."
Assert-True ($factorySource.Contains("new Dictionary<string, string>(config.data)")) "spirit cards must copy base data before composing persistent RawData."
Assert-True ($factorySource.Contains("RuntimeValue(runtimeConfig")) "spirit card reads must prefer Vars and fall back to base data."
Assert-True ($factorySource.Contains(".WithRuntimePresentation(runtime)")) "spirit cards must compose native-readable presentation before hand materialization."
Assert-True ($factorySource.Contains("GrantCapturedToHand") -and $factorySource.Contains("persistToAdventureDeck: true")) "captured spirit cards must use the persistent adventure-deck path."
Assert-True ($factorySource.Contains("GrantReturnedToHand") -and $factorySource.Contains("persistToAdventureDeck: false")) "withdrawn spirit cards must use the combat-hand-only path."
Assert-True ($factorySource.Contains("persistToAdventureDeck ? RoleTable.Instance?.cardList : null")) "returned spirit cards must not require or mutate the adventure deck."
Assert-True ($factorySource.Contains("SunExpIds.SpiritExchangeCountKey") -and $factorySource.Contains('Set(runtime, "TotalExCost", exchangeCount.ToString());')) "returned spirit cards must persist an independent exchange count and apply it as additive cost."
Assert-True ($factorySource.Contains("SunExpIds.SpiritIntentTurnIndexKey") -and $factorySource.Contains("SunExpIds.SpiritIntentReadyOnTurnKey")) "returned spirit cards must persist intent turn and cooldown state in Vars."
Assert-True ($factorySource.Contains("ReadBattleState") -and $factorySource.Contains("RuntimeValue(config")) "spirit intent cooldown restoration must read writable runtime Vars first."
$summonOne = $utf8.GetString([Convert]::FromBase64String("5Y+s5ZSk5LiA5Y+q"))
$oldProjectionDescription = $utf8.GetString([Convert]::FromBase64String("5Y+s5ZSk44CQ"))
$expectedDescriptionLine = 'var description = "' + $summonOne + '" + snapshot.DisplayName;'
Assert-True ($factorySource.Contains($expectedDescriptionLine)) "spirit cards must use the exact dynamic summon description."
Assert-True (-not $factorySource.Contains($oldProjectionDescription)) "spirit cards must not retain the old projection-position description."

$cardApiSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\GameApi\CardApi.cs") -Raw -Encoding UTF8
$cardScriptsSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Scripting\CardScripts.cs") -Raw -Encoding UTF8
Assert-True ($cardApiSource.Contains("DictionaryUtil.Set(config.Vars, `"NeedRemove`", `"True`");")) "adventure removal must use the host NeedRemove runtime contract."
Assert-True ($cardScriptsSource.Contains("[`"afterglow_omen_card`"] = InitAnnihilatingTargetedAttackCard")) "Court Purification must route through annihilating initialization."
Assert-True (([regex]::Matches($cardScriptsSource, "CardApi\.MarkForAdventureRemoval\(self\?\.dataConfig\);")).Count -eq 3) "Spirit Ball, Court Purification, and Fate Star must share the permanent-removal facade."

$stateStoreSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritStateStore.cs") -Raw -Encoding UTF8
Assert-True ($stateStoreSource.Contains("var spirit = state.Spirit;")) "spirit cleanup must capture the Unity object before validity checks."
Assert-True ($stateStoreSource.Contains("if (spirit != null)")) "spirit cleanup must use Unity-aware null checks."
Assert-True (-not $stateStoreSource.Contains("state.Spirit?.gameObject")) "spirit cleanup must not use CLR null-conditional access on destroyed Unity objects."
$withdrawBody = [regex]::Match($stateStoreSource, 'public static bool Withdraw[\s\S]*?public static void ClearAll').Value
Assert-True ($withdrawBody.Contains("RemoveFightRecords") -and $withdrawBody.Contains("CompanionBattleStateStore.Remove")) "spirit exchange-out must remove all fight and companion state."
Assert-True ($withdrawBody.Contains("UnityEngine.Object.Destroy(spirit.gameObject)")) "spirit exchange-out must destroy the withdrawn runtime object."
Assert-True (-not $withdrawBody.Contains("DeadEffect")) "spirit exchange-out must not use death semantics."

$summonSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritSummonService.cs") -Raw -Encoding UTF8
$identityResolverSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritProfileIdentityResolver.cs") -Raw -Encoding UTF8
$spiritModelsSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritModels.cs") -Raw -Encoding UTF8
$intentRegistrySource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritIntentRegistry.cs") -Raw -Encoding UTF8
$captureRegistrySource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritCaptureRegistry.cs") -Raw -Encoding UTF8
$catalogSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\GameApi\EnemyCatalogApi.cs") -Raw -Encoding UTF8
$rpcSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Network\RpcSpiritCompanion.cs") -Raw -Encoding UTF8
$runtimeSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Hooks\SpiritRuntime.cs") -Raw -Encoding UTF8
$sceneRuntimeSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Hooks\CompanionSceneLifecycleRuntime.cs") -Raw -Encoding UTF8
$sceneApiSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\GameApi\CompanionSceneApi.cs") -Raw -Encoding UTF8
$spiritPresenterSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Hooks\Visual\SpiritAttachmentPresenter.cs") -Raw -Encoding UTF8
$spiritObjectSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritOtherObj.cs") -Raw -Encoding UTF8
$presentationComposerSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritIntentPresentationDataComposer.cs") -Raw -Encoding UTF8
$sunExpIdsSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Infrastructure\SunExpIds.cs") -Raw -Encoding UTF8
$enemyCardData = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp\Data\EnemyCard\sunexp.csv") -Raw -Encoding UTF8
$enemyCardText = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp\Text\EnemyCard\sunexp.csv") -Raw -Encoding UTF8
$companionModelsSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\CompanionBattleModels.cs") -Raw -Encoding UTF8
$companionPlannerSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\CompanionIntentPlanner.cs") -Raw -Encoding UTF8
$companionExecutorSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\CompanionIntentExecutor.cs") -Raw -Encoding UTF8
$companionPresentationSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\CompanionIntentPresentationSnapshot.cs") -Raw -Encoding UTF8
$intentPresenterSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Hooks\Visual\ProjectionIntentPresenter.cs") -Raw -Encoding UTF8
$turnSource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\ProjectionTurnCoordinator.cs") -Raw -Encoding UTF8
$authoritySource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\CompanionAuthorityService.cs") -Raw -Encoding UTF8
Assert-True ($summonSource.Contains("ProjectionStateStore.HasForOwner") -and $summonSource.Contains("var outgoing = SpiritStateStore.FindByOwner")) "spirit use must block projections while treating an active spirit as replaceable."
Assert-True ($summonSource.Contains("outgoing.ExchangeCount + 1") -and $summonSource.Contains("SpiritStateStore.Withdraw(outgoing.StatusId")) "spirit replacement must increment and return the withdrawn spirit card."
Assert-True ($summonSource.Contains("SpiritCardBattleState.From(CompanionBattleStateStore.Find(outgoing.StatusId))") -and $summonSource.Contains("state?.ApplyReadyOnTurn(initialBattleState?.ReadyOnTurn)")) "spirit replacement must snapshot and restore per-card intent cooldown state."
Assert-True ($summonSource.Contains("CardGrantEventId") -and $summonSource.Contains("GrantedCardEvents") -and $summonSource.Contains("PendingCardGrants")) "spirit return delivery must be owner-local, retryable, and duplicate-suppressed."
Assert-True ($summonSource.Contains("snapshot.Generation < ownerExisting.Generation") -and $summonSource.Contains("OwnerGenerations")) "spirit synchronization must reject stale owner generations."
Assert-True ($rpcSource.Contains("public int ExchangeCount") -and $rpcSource.Contains("public int Generation") -and $rpcSource.Contains("ReturnedCard") -and $rpcSource.Contains("CardGrantEventId") -and $rpcSource.Contains("ReturnedReadyOnTurn") -and $rpcSource.Contains("public Dictionary<string, int> ReadyOnTurn")) "spirit RPC state must carry exchange cost, owner generation, one-shot return data, and cooldown state."
Assert-True ($spiritObjectSource.Contains("SpiritIntentPresentationDataComposer.Compose(source, presentationAdapterData)") -and $spiritObjectSource.Contains("SunExpIds.SpiritIntentAdapterCardId") -and $spiritObjectSource.Contains("SunExpIds.SpiritIntentSourceCardVar")) "spirit intent cards must preserve source presentation while replacing native enemy identity and scripts with the registered adapter."
Assert-True ($spiritObjectSource.Contains("VerifyPresentationBinding(config, sourceCardId)") -and $spiritObjectSource.Contains("CompanionIntentExecutor.PresentedPlanVar") -and $spiritObjectSource.Contains("[SpiritIntentPresentationAdapter] binding failed")) "spirit presentation must diagnose an adapter entry that did not bind the committed plan."
Assert-True ($presentationComposerSource.Contains('"Id"') -and $presentationComposerSource.Contains('"InitScript"') -and $presentationComposerSource.Contains('"TargetScript"') -and $presentationComposerSource.Contains('"UseScript"') -and $presentationComposerSource.Contains("new Dictionary<string, string>(source, StringComparer.Ordinal)")) "spirit presentation composition must preserve source visuals while replacing all executable identity fields."
Assert-True ($sunExpIdsSource.Contains('SpiritIntentAdapterCardId = "SunExp_sunexp_enemycard_spirit_intent_adapter"') -and $sunExpIdsSource.Contains('SpiritIntentSourceCardVar = "SunExpSpiritIntentSourceCardId"')) "spirit adapter identity and source-card trace key must be centralized in SunExpIds."
Assert-True ($enemyCardData.Contains("enemycard_spirit_intent_adapter") -and $enemyCardData.Contains('ProjectionScripts.InitAction(self, ""spirit-adapted"")')) "the shipped EnemyCard table must register the dedicated spirit intent adapter."
Assert-True ($enemyCardText.Contains("enemycard_spirit_intent_adapter") -and $enemyCardText.Contains("Spirit Intent")) "the dedicated spirit intent adapter must have localized fallback text."
Assert-True ($spiritObjectSource.Contains("PresentationTemplates") -and $spiritObjectSource.Contains('RecordHotspot(') -and $spiritObjectSource.Contains('"Spirit.Intent.PresentationBuild"')) "spirit intent presentation must cache source templates and expose focused build timing."
Assert-True ($runtimeSource.Contains('PlayerRoundStarted = _ => SpiritSummonService.FlushPendingCardReturns')) "deferred spirit-card returns must retry on the next player round."
Assert-True ($runtimeSource.Contains('RunCleanupStep("SummonDedupe"') -and $runtimeSource.Contains('RunCleanupStep("CaptureDedupe"') -and $runtimeSource.Contains('RunCleanupStep("UseGates"')) "spirit lifecycle cleanup must reset summon, capture, and card-use transient state independently."
Assert-True ($sceneRuntimeSource.Contains("SceneManager.sceneUnloaded += OnSceneUnloaded") -and $sceneRuntimeSource.Contains("SpiritRuntime.ClearBattle(source, sweepVisualOrphans: false)")) "direct battle-scene replacement must clear tracked spirit state before the centralized orphan sweep."
Assert-True ($sceneApiSource.Contains("SceneManager.MoveGameObjectToScene") -and $summonSource.Contains("CompanionSceneApi.MoveToOwnerScene")) "spirit runtime objects must be owned by the battle scene."
Assert-True ($spiritPresenterSource.Contains("public static void ClearAll") -and $spiritPresenterSource.Contains('StartsWith("SunExp_SpiritVisualProxy:"')) "spirit visual cleanup must sweep registered and orphaned proxy objects."
Assert-True ($turnSource.Contains('"owner:" + ownerPlayerId + ":slot:" + slotIndex')) "companion action claims must stay bound to the shared owner slot across replacements."
Assert-True ($authoritySource.Contains("ProjectionProtocolVersion = 6")) "spirit cooldown RPC changes must bump the companion protocol version."
Assert-True ($intentRegistrySource.Contains("SpiritProfileIdentityResolver.Resolve")) "spirit intent profiles must use the shared identity resolver."
Assert-True ($intentRegistrySource.Contains("private static string registryHash") -and $intentRegistrySource.Contains("private static void SetDocument") -and $intentRegistrySource.Contains("intentById.TryGetValue")) "spirit intent registry hash and id lookup must be cached at load time."
Assert-True ($intentRegistrySource.Contains("NormalizeProfileListFields") -and $intentRegistrySource.Contains("normalized legacy list field")) "spirit intent registry must normalize legacy scalar list fields before typed deserialization."
Assert-True ($intentRegistrySource.Contains("rejected profile=") -and $intentRegistrySource.Contains("rejected intent index=")) "spirit intent registry must isolate malformed profiles and intents."
Assert-True ($companionModelsSource.Contains("public List<CompanionIntentEffectSpec> Effects") -and $companionModelsSource.Contains("public int DisplayIndex")) "schema 3 companion intents must model composite effects and stable description slots."
Assert-True ($companionPlannerSource.Contains("foreach (var effectSpec in CompanionIntentEffects.Expand(intent))") -and $companionPlannerSource.Contains("ResolvedEffects = resolvedEffects")) "the authoritative planner must resolve every effect in one composite intent."
Assert-True ($companionExecutorSource.Contains("CompanionIntentPresentationSnapshot.Resolve(effect, displayIndex)") -and $companionExecutorSource.Contains('DictionaryUtil.Set(executor.Vars, "DesVal" + snapshot.DisplayIndex, snapshot.DisplayText)') -and $companionExecutorSource.Contains("SunExpCompanionPresentedFingerprint") -and $companionExecutorSource.Contains("if (isCurrentSnapshot)")) "intent presentation must write an authoritative snapshot only when the committed values change."
Assert-True (-not $companionExecutorSource.Contains("executor.AddDescription")) "committed companion presentation must not run native damage or defence calculations a second time."
Assert-True ($companionPresentationSource.Contains('displayText += "*" + repeatCount') -and $companionPresentationSource.Contains('StartsWith("damage."')) "multi-hit companion intents must display per-hit value and hit count."
Assert-True ($intentPresenterSource.Contains("SpiritStateStore.IntentPresented += BindCommittedPlan") -and $intentPresenterSource.Contains("ResolveLineTarget")) "projection and spirit intent lines must share committed-target resolution."
Assert-True ($captureRegistrySource.Contains("SpiritProfileIdentityResolver.Resolve")) "spirit capture profiles must use the shared identity resolver."
Assert-True ($identityResolverSource.Contains('BaseGameRuntimePrefix = "enemy_"') -and $identityResolverSource.Contains('SunExpRuntimePrefix = "SunExp_sunexp_"')) "spirit identity resolution must cover base-game and SunExp runtime prefixes."
Assert-True ($identityResolverSource.Contains('"alias-enemy-wildcard"') -and $identityResolverSource.Contains('"global-fallback"')) "spirit identity diagnostics must distinguish canonical alias matches from global fallback."
Assert-True ($spiritModelsSource.Contains("SpiritProfileIdentityResolver.CreateProfileKey")) "persisted spirit profile keys must use the shared identity boundary without rewriting raw ids."
Assert-True ($summonSource.Contains("SpiritIntentRegistry.ResolveProfile") -and $summonSource.Contains("[SpiritProfile] summon resolve")) "spirit summon must resolve and log the matched intent profile."
Assert-True ($catalogSource.Contains('normalized.StartsWith("enemy_", StringComparison.Ordinal)') -and $catalogSource.Contains('return "BaseGame";')) "base-game runtime enemy ids must not be misclassified as an enemy mod."

$hotspotSources = @(
    "SunExp-Dev\GameApi\EnemyCatalogApi.cs",
    "SunExp-Dev\GameApi\EnemyCaptureSettlementApi.cs",
    "SunExp-Dev\Hooks\Visual\SpiritCardFaceRuntime.cs",
    "SunExp-Dev\Mechanics\SpiritCardFactory.cs",
    "SunExp-Dev\Mechanics\SpiritSummonService.cs",
    "SunExp-Dev\Mechanics\CompanionIntentPlanner.cs"
) | ForEach-Object { Get-Content -LiteralPath (Join-Path $repoRoot $_) -Raw }
$hotspotText = $hotspotSources -join "`n"
foreach ($name in @(
    "Spirit.Catalog.Inspect",
    "Spirit.Catalog.DictProbe",
    "Spirit.Catalog.IdleProbe",
    "Spirit.CardFace.Load",
    "Spirit.Card.GrantToHand",
    "Spirit.Capture.Settlement",
    "Spirit.Summon.CanSummon",
    "Spirit.Summon.IdleProbe",
    "Spirit.Summon.Spawn",
    "Spirit.Intent.Plan"
)) {
    Assert-True ($hotspotText.Contains($name)) "spirit hotspot instrumentation is missing: $name"
}

Write-Host "Spirit capture assertions passed: profiles=$($explicitIntents.Count)."
