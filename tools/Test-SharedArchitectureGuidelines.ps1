param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $RelativePath"
    }

    return [System.IO.File]::ReadAllText($path)
}

function Require-Text {
    param(
        [string]$Text,
        [string]$Pattern,
        [string]$Message
    )

    if ($Text -notmatch $Pattern) {
        throw $Message
    }
}

$globalRuntimes = @(
    "AuraSharedCore\AuraSharedRuntime.cs",
    "AuraSkinShared\AuraSkinRuntime.cs",
    "AudioArbiterShared\AudioArbiterRuntime.cs",
    "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs",
    "AuraCgShared\AuraCgRuntime.cs",
    "UiTransitionGuardShared\UiTransitionGuardRuntime.cs"
)

foreach ($relative in $globalRuntimes) {
    $text = Read-RepoText $relative
    Require-Text $text "CurrentProtocolVersion" "$relative must expose CurrentProtocolVersion."
    Require-Text $text "MinimumSupportedProtocolVersion" "$relative must expose MinimumSupportedProtocolVersion."
    Require-Text $text "CurrentBuildId" "$relative must expose CurrentBuildId."
    Require-Text $text "BuildId\s*=>\s*CurrentBuildId" "$relative must expose BuildId from CurrentBuildId."
    Require-Text $text "ValidateExisting" "$relative must validate existing global component compatibility."
    Require-Text $text "GetMethod\(" "$relative must check reflected public method shape."
}

$providerIdentityFiles = @(
    "AudioArbiterShared\AudioArbiterRuntime.cs",
    "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs",
    "AuraCgShared\AuraCgRuntime.cs"
)

foreach ($relative in $providerIdentityFiles) {
    $text = Read-RepoText $relative
    Require-Text $text "QualifiedProviderId" "$relative must keep an owner-qualified provider identity."
    Require-Text $text "QualifyProviderId" "$relative must normalize provider identity through QualifyProviderId."
    Require-Text $text "qualifiedProviderId" "$relative must include qualified provider ids in diagnostics."
}

$explicitProviderRequestFiles = @(
    "AudioArbiterShared\AudioArbiterRuntime.cs",
    "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs"
)

foreach ($relative in $explicitProviderRequestFiles) {
    $text = Read-RepoText $relative
    Require-Text $text "MatchesProviderId" "$relative must match both bare and qualified provider ids."
}

$audioRuntime = Read-RepoText "AudioArbiterShared\AudioArbiterRuntime.cs"
Require-Text $audioRuntime "MatchesProviderRequest" "AudioArbiterRuntime must expose owner-aware provider request matching."
Require-Text $audioRuntime "ownerStrict:\s*true" "AudioArbiterRuntime must have an owner-strict provider matching path."
Require-Text $audioRuntime "request\.IsRemote[\s\S]*Remote sound provider mismatch" "AudioArbiterRuntime must fail closed for remote owner/provider mismatches."
Require-Text $audioRuntime "OwnerModId to disambiguate" "AudioArbiterRuntime must document OwnerModId-based RPC provider disambiguation."
Require-Text $audioRuntime "RemoteReplacementPairingSeconds" "AudioArbiterRuntime must bound remote native-effect pairing before fallback playback."
Require-Text $audioRuntime "remote-fallback-played" "AudioArbiterRuntime must expose remote replacement fallback outcomes."

$architectureGuidelines = Read-RepoText "docs\aura-shared-core-v2-contract.md"
Require-Text $architectureGuidelines "provider identity[\s\S]*BuildId" "Shared architecture guidelines must require BuildId bumps for provider identity semantic changes."
Require-Text $architectureGuidelines "Tool-owned runtime caches[\s\S]*AuraSharedStorageCoordinator\.ExecuteWrite" "Shared architecture guidelines must document coordinated shared-cache writes."
Require-Text $architectureGuidelines "WriteTextAtomic[\s\S]*cache metadata" "Shared architecture guidelines must require atomic metadata writes for shared caches."

$auraCgRuntime = Read-RepoText "AuraCgShared\AuraCgRuntime.cs"
Require-Text $auraCgRuntime "RenderMode\.ScreenSpaceOverlay" "AuraCgShared overlay must render on an independent screen-space canvas."
Require-Text $auraCgRuntime "overlayCanvas\.overrideSorting\s*=\s*true" "AuraCgShared overlay canvas must control its own sorting order."
Require-Text $auraCgRuntime "overlayGroup\.blocksRaycasts\s*=\s*false" "AuraCgShared overlay canvas group must not block raycasts."
Require-Text $auraCgRuntime "overlayImage\.raycastTarget\s*=\s*false" "AuraCgShared overlay image must not receive raycasts."
Require-Text $auraCgRuntime "DontDestroyOnLoad\(overlayRoot\)" "AuraCgShared overlay root must survive scene transitions without attaching to game UI canvases."
if ($auraCgRuntime -match "manager\?\.(upperCanvasTf|canvasTf)|GameUIManager|GraphicRaycaster") {
    throw "AuraCgShared overlay must not attach to game UI canvases or add a GraphicRaycaster."
}

$uiTransitionGuardRuntime = Read-RepoText "UiTransitionGuardShared\UiTransitionGuardRuntime.cs"
Require-Text $uiTransitionGuardRuntime "ui-transition-guard-2026-07-08-v3" "UiTransitionGuard must bump BuildId for per-frame UI guard dedupe semantics."
Require-Text $uiTransitionGuardRuntime "LeaseRaycasters" "UiTransitionGuard must use scoped raycaster leases."
Require-Text $uiTransitionGuardRuntime "OnDisable\(\)[\s\S]*RestoreRaycasters" "UiTransitionGuard must restore raycaster leases when disabled."
Require-Text $uiTransitionGuardRuntime "UiTransitionGuardOptions[\s\S]*MaxGuardFrames[\s\S]*RegistryScrubFrames[\s\S]*ScrubEveryFrames" "UiTransitionGuard options must bound guard and scrub windows."
if ($uiTransitionGuardRuntime -match "FindObjectsOfTypeAll<GraphicRaycaster>|SuspendRaycasters") {
    throw "UiTransitionGuard must not globally enumerate or suspend all GraphicRaycasters."
}

$uiRaycastSafetyRuntime = Read-RepoText "UiRaycastSafetyShared\UiRaycastSafeDestroyRuntime.cs"
if ($uiRaycastSafetyRuntime -match "raycast-target-false|inactive-or-disabled") {
    throw "UiRaycastSafety scrub must not unregister healthy non-raycast or inactive graphics."
}

$journeyRuntime = Read-RepoText "AuraJourneyShared\AuraJourneyRuntime.cs"
Require-Text $journeyRuntime "QualifyJourneyId" "AuraJourneyRuntime must expose QualifyJourneyId."
Require-Text $journeyRuntime "IsQualifiedJourneyId" "AuraJourneyRuntime must expose IsQualifiedJourneyId."
Require-Text $journeyRuntime "LocalJourneyId" "AuraJourneyRuntime must expose LocalJourneyId."
Require-Text $journeyRuntime "RegisterJourney[\s\S]*QualifyJourneyId" "RegisterJourney must normalize JourneyId through QualifyJourneyId."
Require-Text $journeyRuntime "TryCommit[\s\S]*QualifyJourneyId" "TryCommit must normalize JourneyId through QualifyJourneyId."
Require-Text $journeyRuntime "Read legacy unqualified journey" "AuraJourneyRuntime must keep legacy short-id read fallback."
Require-Text $journeyRuntime "PublishActiveMode" "AuraJourneyRuntime must expose shared active-mode projection for content/tool decoupling."
Require-Text $journeyRuntime "IsJourneyActive" "AuraJourneyRuntime must expose shared active journey checks for tool consumers."
Require-Text $journeyRuntime "AuraJourneyCurrentNodeProjectionRuntime\.Initialize" "AuraJourneyRuntime must initialize the shared current-node projection guard."

$journeyProjection = Read-RepoText "AuraJourneyShared\AuraJourneyCurrentNodeProjectionRuntime.cs"
Require-Text $journeyProjection "TryFindIdentity" "Journey current-node repair must require an exact synced identity match."
Require-Text $journeyProjection "matches == 1" "Journey current-node repair must reject ambiguous synced identities."
Require-Text $journeyProjection "MaximumDeferredAttempts" "Journey current-node repair must bound delayed retries."
Require-Text $journeyProjection "!IsClientOnly" "Journey current-node repair must remain client-only."
Require-Text $journeyProjection "current \?\? saved \?\? authoritativeIdentity \?\? snapshot\.VerifiedIdentity" "Journey current-node capture must preserve native, host-authoritative, and last verified identities in priority order."
Require-Text $journeyProjection "PlayerInfo\.EventTryChangeMap" "Journey current-node repair must preflight the native map transition."
Require-Text $journeyProjection "RpcAuraJourneyNodeProjection" "Journey current-node repair must accept a host-published read-only identity projection."
Require-Text $journeyProjection "MaximumRecentProjections" "Journey current-node repair must keep only a bounded recent native-array window."
Require-Text $journeyProjection "TryFindInProjections" "Journey current-node repair must search recent native projection arrays for delayed RPC ordering."
if ($journeyProjection -match "CmdSelectMap|tree\.SelectNode|tree\.DefaultNode") {
    throw "Journey current-node projection guard must not choose or rewrite map routes."
}

$sunExpPreloader = Read-RepoText "SunExp-Dev\Hooks\SunExpResourcePreloader.cs"
Require-Text $sunExpPreloader "AdventureStarting" "SunExp resource warmup must start from the adventure lifecycle."
Require-Text $sunExpPreloader "AuraSharedFramePhase\.Background" "SunExp resource warmup must use the shared background frame phase."
Require-Text $sunExpPreloader "battleActive" "SunExp resource warmup must pause during combat."
Require-Text $sunExpPreloader "StarScoreHudAssets\.StructuralPaths" "SunExp warmup must cover first-use structural Star Score HUD sprites."
if ($sunExpPreloader -match "PolymorphCardFaceCache\.GetOrCreate") {
    throw "SunExp warmup must not generate polymorph card faces on the preload path."
}
if ($sunExpPreloader -match "SunExpResourceCache\.Preload<") {
    throw "SunExp resource warmup must not synchronously preload the whole visual catalog in one frame action."
}

$battleLifecycleRouter = Read-RepoText "AuraSharedCore\AuraBattleLifecycleRouter.cs"
Require-Text $battleLifecycleRouter "EnsureBattleSession" "AuraBattleLifecycleRouter must expose a battle session scope for duplicate suppression."
Require-Text $battleLifecycleRouter "AuraLifecycleOperationLedger\.ClearScopePrefix" "AuraBattleLifecycleRouter must clear battle-scoped operation claims on battle boundaries."
Require-Text $battleLifecycleRouter "FightInitializing" "AuraBattleLifecycleRouter must expose the FightInit.Init before phase."
Require-Text $battleLifecycleRouter "FightInitialized" "AuraBattleLifecycleRouter must expose the FightInit.Init after phase."
Require-Text $battleLifecycleRouter "FightOpening" "AuraBattleLifecycleRouter must expose the Fight_Start.Init opening phase."

$lifecycleStepRunner = Read-RepoText "AuraSharedCore\AuraSharedLifecycleStepRunner.cs"
Require-Text $lifecycleStepRunner "AuraSharedLifecycleDeduplicateScope" "AuraSharedLifecycleStepRunner must expose reusable lifecycle dedupe scopes."
Require-Text $lifecycleStepRunner "AuraSharedLifecycleStepRequest" "AuraSharedLifecycleStepRunner must expose a reusable request model."
Require-Text $lifecycleStepRunner "AuraSharedFrameStepRunner\.Run" "AuraSharedLifecycleStepRunner must delegate frame splitting to the shared frame step runner."

$cardLifecycleRouter = Read-RepoText "AuraSharedCore\AuraCardLifecycleRouter.cs"
Require-Text $cardLifecycleRouter "AuraCardLifecyclePhase" "AuraCardLifecycleRouter must expose shared card lifecycle phase markers."
Require-Text $cardLifecycleRouter "AuraHookRegistry" "AuraCardLifecycleRouter must centralize native card hook registration through the shared registry."
Require-Text $cardLifecycleRouter "BeforeRouted" "AuraCardLifecycleRouter must own card before-hook routing."
Require-Text $cardLifecycleRouter "AfterRouted" "AuraCardLifecycleRouter must own card after-hook routing."
Require-Text $cardLifecycleRouter "RegisteredPhases" "AuraCardLifecycleRouter must install native card hooks lazily per subscribed phase."
Require-Text $cardLifecycleRouter "EnsurePhaseRegistrationsNoLock" "AuraCardLifecycleRouter must not register every native card hook for an unrelated subscriber."
Require-Text $cardLifecycleRouter "owner \+ `"`:`" \+ localId" "AuraCardLifecycleRouter must owner-qualify handler identities."
Require-Text $cardLifecycleRouter "OrderByDescending\(handler => handler\.Subscription\.Priority\)" "AuraCardLifecycleRouter must dispatch handlers in deterministic priority order."
Require-Text $cardLifecycleRouter "ThenBy\(handler => handler\.Id, StringComparer\.OrdinalIgnoreCase\)" "AuraCardLifecycleRouter must use deterministic id ordering for equal priorities."
Require-Text $cardLifecycleRouter "CommonCardItemTrueUse" "AuraCardLifecycleRouter must own common-card use hooks."
Require-Text $cardLifecycleRouter "CardItemInit" "AuraCardLifecycleRouter must own card-item refresh hooks."

$cardUseFxRegistry = Read-RepoText "AuraCardUseFxShared\AuraCardUseFxRegistry.cs"
$cardUseFxRuntime = Read-RepoText "AuraCardUseFxShared\AuraCardUseFxRuntime.cs"
$cardUseFxRibbon = Read-RepoText "AuraCardUseFxShared\AuraBezierRibbonGraphic.cs"
Require-Text $cardUseFxRegistry "OwnerModId" "Card-use FX entries must retain owner-qualified identity."
Require-Text $cardUseFxRegistry "WriteShared" "Card-use FX registry writes must route through AuraShared Core."
Require-Text $cardUseFxRegistry "OrderByDescending\(entry => entry\.Priority\)" "Card-use FX resolution must be priority deterministic."
Require-Text $cardUseFxRegistry "AuraCardUseFxPresentationScopes" "Card-use FX entries must declare whether they target the owner, observers, or both."
Require-Text $cardUseFxRuntime "AuraCardLifecycleRouter" "Card-use FX must capture the real local card before native use processing."
Require-Text $cardUseFxRuntime "AuraCombatActionRouter" "Card-use FX must use the successful local action-animation commit boundary."
Require-Text $cardUseFxRuntime "LocalCommitted" "Card-use FX must distinguish local committed uses from remote observations."
Require-Text $cardUseFxRuntime 'FightUI\.DoCardUseAnimation' "Card-use FX bridge must scope the native central-card animation."
Require-Text $cardUseFxRuntime 'ICard\.SetCardStyle' "Card-use FX bridge must capture the nested native central clone."
Require-Text $cardUseFxRuntime "DedupeSeconds" "Card-use FX presentation triggers must have bounded duplicate suppression."
Require-Text $cardUseFxRuntime "AuraCardUseFxSourceSnapshot" "Card-use FX must snapshot its source before native burn or throw destroys the card view."
Require-Text $cardUseFxRibbon "raycastTarget = false" "Shared Bezier ribbons must never intercept UI input."
if ($cardUseFxRuntime.Contains("SunExp")) {
    throw "Shared card-use FX runtime must not contain SunExp content semantics."
}

$lifecycleSession = Read-RepoText "AuraSharedCore\AuraLifecycleSessionRuntime.cs"
Require-Text $lifecycleSession "BeginBattleSession" "Shared lifecycle session runtime must own battle session start."
Require-Text $lifecycleSession "RestartBattleSession" "Shared lifecycle session runtime must advance the battle epoch when FightInit.Init restarts an active fight."
Require-Text $lifecycleSession "EndBattleSession" "Shared lifecycle session runtime must own battle session end."

$cardPresentationDelta = Read-RepoText "AuraSharedCore\AuraCardPresentationDelta.cs"
Require-Text $cardPresentationDelta "TrySetCost" "Shared card presentation deltas must expose a cost-only refresh path."
if ($cardPresentationDelta -match "SunExp|AuraTools") {
    throw "Shared card presentation deltas must remain consumer-semantic-free."
}

$operationLedger = Read-RepoText "AuraSharedCore\AuraLifecycleOperationLedger.cs"
Require-Text $operationLedger "TryClaimBattleOperation" "Shared lifecycle ledger must expose battle-scoped operation claiming."
Require-Text $operationLedger "effectCategory" "Shared lifecycle ledger keys must distinguish different effect categories."
Require-Text $operationLedger "effectId" "Shared lifecycle ledger keys must distinguish different concrete effects."

$featureSwitch = Read-RepoText "AuraSharedCore\AuraFeatureSwitchRuntime.cs"
Require-Text $featureSwitch "RegisterFeature" "Shared feature switch runtime must separate registered defaults from tool overrides."
Require-Text $featureSwitch "SetLocalOverride" "Shared feature switch runtime must support tool-local effective overrides."

$sharedResourceCache = Read-RepoText "AuraSharedCore\AuraSharedResourceCache.cs"
Require-Text $sharedResourceCache "ResourceLoader\.Load<T>" "Shared resource cache must centralize native single-asset loads."
Require-Text $sharedResourceCache "ResourceLoader\.LoadAll<T>" "Shared resource cache must centralize native multi-asset loads."
Require-Text $sharedResourceCache "ClearCategory" "Shared resource cache must support category invalidation."

$sharedRpcAuthority = Read-RepoText "AuraSharedCore\AuraRpcAuthorityRuntime.cs"
$sharedRpcSender = Read-RepoText "AuraSharedCore\AuraRpcSender.cs"
$authoritativeSync = Read-RepoText "AuraSharedCore\AuraAuthoritativeSyncRuntime.cs"
Require-Text $sharedRpcAuthority "DefaultReceiveHookTargets" "Shared RPC authority must own receive hook target registration."
Require-Text $sharedRpcAuthority "CreateLocalServerSender" "Shared RPC authority must expose local host sender creation."
Require-Text $sharedRpcAuthority "LobbyContains" "Shared RPC authority must bind sender membership centrally."
Require-Text $sharedRpcSender "public sealed class AuraRpcSender" "Shared RPC sender context must be available without consumer-private sender types."
Require-Text $authoritativeSync "public static class AuraAuthoritativeSyncRuntime" "Shared authoritative sync runtime must be a semantic-free Core service."
Require-Text $authoritativeSync "OwnerModId" "Shared authoritative sync domains must be owner-qualified."
Require-Text $authoritativeSync "DomainId" "Shared authoritative sync domains must be domain-qualified."
Require-Text $authoritativeSync "TryBeginSnapshotRequest" "Shared authoritative sync runtime must coalesce snapshot requests."
Require-Text $authoritativeSync "TryClaimToken" "Shared authoritative sync runtime must provide bounded duplicate suppression."
Require-Text $authoritativeSync "AcceptRemoteSnapshotSession" "Shared authoritative sync runtime must validate host-session freshness."

$sharedModalHost = Read-RepoText "AuraUiShared\AuraUiModalHost.cs"
Require-Text $sharedModalHost "CreateFullscreenRoot" "Shared UI modal host must own fullscreen modal root creation."
Require-Text $sharedModalHost "UiRaycastSafeDestroyRuntime" "Shared UI modal host must close transient UI through raycast-safe cleanup."
Require-Text $sharedModalHost "NativeUiParent" "Shared UI host must expose the game's ordinary UI plane for native overlays."
Require-Text $sharedModalHost "return UIManager\.Instance\?\.canvasTf;" "Shared native UI host must share the host Canvas used by Tooltip and Floating Window."
Require-Text $sharedModalHost "CreateNativeFullscreenRoot" "Shared UI host must create fullscreen native-plane roots without changing modal defaults."

$sharedUiTheme = Read-RepoText "AuraUiShared\AuraUiTheme.cs"
$sharedUiRegistry = Read-RepoText "AuraUiShared\AuraUiStyleRegistry.cs"
$sharedUiNativeBridge = Read-RepoText "AuraUiShared\AuraUiNativeBridge.cs"
$sharedUiNativeButtonClone = Read-RepoText "AuraUiShared\AuraUiNativeButtonCloneAdapter.cs"
$sharedUiNativeInteraction = Read-RepoText "AuraUiShared\AuraUiNativeInteraction.cs"
$sharedUiNativeGameItems = Read-RepoText "AuraUiShared\AuraUiNativeGameItemAdapter.cs"
$sharedUiNativeOverlayVisibility = Read-RepoText "AuraUiShared\AuraUiNativeOverlayVisibility.cs"
$sharedUiButtonFeedback = Read-RepoText "AuraUiShared\AuraUiButtonFeedback.cs"
$sharedUiComponents = Read-RepoText "AuraUiShared\AuraUiComponents.cs"
$sharedUiRenderer = Read-RepoText "AuraUiShared\AuraUiStandardRenderer.cs"
Require-Text $sharedUiTheme "AuraUiStyleIds" "AuraUiShared must expose owner-qualified stable style ids."
Require-Text $sharedUiTheme "WitchNative" "AuraUiShared must keep the game-native style separate from Aura default styling."
Require-Text $sharedUiRegistry "RegisterDerived" "AuraUiShared must support consumer-owned derived styles."
Require-Text $sharedUiNativeBridge "HarmonyOS_Sans_Medium SDF" "AuraUiShared must resolve the game's HarmonyOS TMP font asset."
Require-Text $sharedUiNativeBridge "ResolveLegacyFont" "AuraUiShared must keep a legacy Text compatibility font bridge."
Require-Text $sharedUiNativeButtonClone "StripOwnerBehaviours" "AuraUiShared native button cloning must leave consumer business cleanup to the owner adapter."
Require-Text $sharedUiNativeButtonClone "TryValidateOwnedTextReferences" "AuraUiShared native button cloning must audit cloned text ownership."
Require-Text $sharedUiNativeButtonClone "ReferenceEquals\(template\.normalText, clone\.normalText\)" "AuraUiShared native button cloning must reject references shared with the template."
Require-Text $sharedUiNativeButtonClone "configuring the clone changed the template label" "AuraUiShared native button cloning must verify that the source label remains unchanged."
Require-Text $sharedUiNativeButtonClone "AuraUiNativeButtonLabelOwner" "AuraUiShared native button cloning must keep cloned label content under Aura ownership."
Require-Text $sharedUiNativeButtonClone "AuraUiOwnedNativeButtonText" "AuraUiShared native button cloning must replace template labels with owned TMP nodes."
Require-Text $sharedUiNativeButtonClone "TextSizeOverride" "AuraUiShared native button cloning must expose consumer-scoped text sizing."
Require-Text $sharedUiNativeButtonClone "MinimumTextSizeOverride" "AuraUiShared native button cloning must expose an optional auto-fit minimum size."
Require-Text $sharedUiNativeButtonClone "enableAutoSizing" "AuraUiShared native button labels must preserve configured auto-fit behavior across native refreshes."
Require-Text $sharedUiNativeButtonClone "manager\.onClick\s*=\s*new UnityEvent\(\)" "AuraUiShared native button clones must sever serialized native click listeners."
Require-Text $sharedUiNativeButtonClone "manager\.onDoubleClick\s*=\s*new UnityEvent\(\)" "AuraUiShared native button clones must sever serialized native double-click listeners."
Require-Text $sharedUiNativeButtonClone "manager\.onRightClick\s*=\s*new UnityEvent\(\)" "AuraUiShared native button clones must sever serialized native right-click listeners."
Require-Text $sharedUiNativeButtonClone "manager\.onHover\s*=\s*new UnityEvent\(\)" "AuraUiShared native button clones must sever serialized native hover listeners."
Require-Text $sharedUiNativeButtonClone "manager\.onLeave\s*=\s*new UnityEvent\(\)" "AuraUiShared native button clones must sever serialized native leave listeners."
Require-Text $sharedUiNativeButtonClone "unityButton\.onClick\s*=\s*new Button\.ButtonClickedEvent\(\)" "AuraUiShared native Unity buttons must sever serialized native click listeners."
Require-Text $sharedUiNativeInteraction "class AuraUiNativeButtonBinding" "AuraUiShared must expose a reusable binding for ButtonManagers inside consumer-cloned native trees."
Require-Text $sharedUiNativeInteraction "NeutralizeTree" "AuraUiShared native button binding must neutralize inherited native events before consumer activation."
Require-Text $sharedUiNativeInteraction "bool disable = true" "AuraUiShared tree neutralization must let native-visual consumers preserve interaction states explicitly."
Require-Text $sharedUiNativeInteraction "target\.onRightClick\s*=\s*new UnityEvent\(\)" "AuraUiShared adopted native buttons must sever serialized native right-click listeners."
Require-Text $sharedUiNativeInteraction "target\.SetText\(label" "AuraUiShared adopted native buttons must own all native visual-state labels through ButtonManager."
Require-Text $sharedUiNativeInteraction "string\? label" "AuraUiShared adopted native buttons must support icon-only controls without a synthetic label."
Require-Text $sharedUiNativeInteraction "if \(label != null\)" "AuraUiShared icon-only native buttons must preserve their cloned icon and text configuration."
if ($sharedUiNativeInteraction -match "HasCompleteVisualState") {
    throw "AuraUiShared adopted native buttons must not reject a whole native shell because optional visual states are absent."
}
Require-Text $sharedUiNativeInteraction "class AuraUiPointerSurface" "AuraUiShared must expose semantic-free pointer enter, exit, left-click, and right-click callbacks."
Require-Text $sharedUiNativeInteraction "class AuraUiNativeItemSurface" "AuraUiShared must combine native ButtonManager presentation with semantic-free item callbacks."
Require-Text $sharedUiNativeInteraction "class AuraUiNativeItemAnchor" "AuraUiShared must preserve exact native event, tooltip, and ButtonManager anchors before consumer sanitization."
Require-Text $sharedUiNativeInteraction "KeywordDisplay\? tooltip" "AuraUiShared native item anchors must retain the host's serialized tooltip reference."
Require-Text $sharedUiNativeInteraction "anchor\.VisualManager" "AuraUiShared anchored item binding must use the captured exact ButtonManager instead of a descendant guess."
Require-Text $sharedUiNativeInteraction "onRightClick: surface\.InvokeRight" "AuraUiShared anchored item binding must route right-click through the exact native ButtonManager event."
Require-Text $sharedUiNativeInteraction "lastRightFrame == frame" "AuraUiShared anchored item binding must suppress duplicate native-manager and pointer-surface clicks in one frame."
Require-Text $sharedUiNativeInteraction "tooltip\.enabled\s*=\s*true" "AuraUiShared native item anchors must explicitly restore captured tooltip response."
Require-Text $sharedUiNativeInteraction "manager\.SetIcon\(sprite\)" "AuraUiShared native item icons must update all ButtonManager visual states through the host API."
Require-Text $sharedUiNativeGameItems "AuraUiSafeSellItem\s*:\s*SellItem" "AuraUiShared must provide a native SellItem-derived safe presenter."
Require-Text $sharedUiNativeGameItems "AuraUiSafeRelicItem\s*:\s*RelicItemConfig" "AuraUiShared must provide a native RelicItemConfig-derived safe presenter."
Require-Text $sharedUiNativeGameItems "public override void ShowFloatingWindow\(\)" "AuraUiShared native-derived presenters must replace unsafe host settlement menus."
Require-Text $sharedUiNativeGameItems "manager\.enableIcon\s*=\s*sprite != null" "AuraUiShared native item icon updates must clear empty states instead of retaining template sprites."
Require-Text $sharedUiNativeGameItems "item\.keywordDisplay\s*=\s*EnsureTooltip\(item\)" "AuraUiShared native presenters must preserve the host KeywordDisplay before native Init."
Require-Text $sharedUiNativeOverlayVisibility "SharesRootCanvas" "AuraUiShared must reject native overlays hosted on a different root Canvas."
Require-Text $sharedUiNativeOverlayVisibility "IsVisibleAbove" "AuraUiShared must expose actual native overlay visibility verification."
Require-Text $sharedUiNativeOverlayVisibility "aboveAnchor" "AuraUiShared native overlay verification must include render-order checks."
Require-Text $sharedUiNativeOverlayVisibility "effectiveAlpha" "AuraUiShared native overlay verification must reject fully transparent overlays."
Require-Text $sharedUiNativeInteraction "EnsureRaycastTarget" "AuraUiShared pointer surfaces must establish an explicit raycast target."
Require-Text $sharedUiNativeInteraction "graphic\.raycastTarget\s*=\s*true" "AuraUiShared pointer surfaces must enable their exact hit graphic."
Require-Text $sharedUiButtonFeedback "ButtonSound" "AuraUiShared buttons must reuse the game's button sound component."
Require-Text $sharedUiButtonFeedback "IsInteractable" "AuraUiShared button sounds must be gated by Selectable interactability."
Require-Text $sharedUiButtonFeedback "GetComponent<ButtonManager>" "AuraUiShared feedback must not duplicate native ButtonManager behavior."
Require-Text $sharedUiButtonFeedback "target\.CrossFadeColor\(" "AuraUiShared buttons must synchronize their initial renderer tint before first render."
Require-Text $sharedUiButtonFeedback "initialColor \* colors\.colorMultiplier" "AuraUiShared initial button tint must match the configured ColorBlock multiplier."
Require-Text $sharedUiComponents "ConfigureTmpText" "AuraUiShared must expose its standard TMP text component."
Require-Text $sharedUiRenderer "class AuraUiContext" "AuraUiShared styles must be scoped by a UI context instead of mutable global state."
Require-Text $sharedUiRenderer "CreateButton" "AuraUiShared must expose a standard button surface."
Require-Text $sharedUiRenderer "CreateToggle" "AuraUiShared must expose a standard toggle surface."
Require-Text $sharedUiRenderer "CreateInput" "AuraUiShared must expose a standard TMP input surface."
Require-Text $sharedUiRenderer "CreateDropdown" "AuraUiShared must expose a standard TMP dropdown surface."
Require-Text $sharedUiRenderer "CreateScrollArea" "AuraUiShared must expose a standard scroll/list surface."
Require-Text $sharedUiRenderer "CreateTooltip" "AuraUiShared must expose a non-blocking tooltip surface."
Require-Text $sharedUiRenderer "CreateToast" "AuraUiShared must expose a non-blocking toast surface."

$sharedRoots = @(
    "AuraAudioShared",
    "AuraCardUseFxShared",
    "AudioArbiterShared",
    "BattleBgmArbiterShared",
    "AuraCgShared",
    "AuraDirectorShared",
    "AuraDirectorDetour-Dev",
    "AuraJourneyShared",
    "AuraLogShared",
    "AuraOnlineShared",
    "AuraSharedCore",
    "AuraSkinShared",
    "AuraUiShared",
    "StarterDeckArbiterShared",
    "UiRaycastSafetyShared",
    "UiTransitionGuardShared"
)

$rawWriteAllowed = @(
    "AuraSharedCore\AuraSharedStorageCoordinator.cs",
    "AuraSharedCore\AuraSharedPackageCoordinator.cs",
    "AuraSharedCore\AuraSharedOperationLog.cs",
    "AuraSharedCore\AuraSharedLogStore.cs",
    "AuraLogShared\AuraLogRuntime.cs",
    "AuraOnlineShared\AuraOnlineHostModSyncSession.cs"
)

$rawWritePatterns = @(
    "File\.WriteAllText",
    "File\.WriteAllBytes",
    "new FileStream",
    "File\.Move",
    "File\.Copy",
    "File\.Delete",
    "Directory\.Move",
    "Directory\.Delete"
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($root in $sharedRoots) {
    $path = Join-Path $repoRoot $root
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $files = Get-ChildItem -LiteralPath $path -Recurse -File -Filter "*.cs" | Where-Object {
        $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\"
    }

    foreach ($file in $files) {
        $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/').Replace('/', '\')
        if ($rawWriteAllowed -contains $relative) {
            continue
        }

        $text = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $rawWritePatterns) {
            if ($text -match $pattern) {
                $violations.Add("${relative}: raw shared write matched ${pattern}")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ }
    throw "Shared architecture guideline scan failed: $($violations.Count) raw write violation(s)."
}

$guidelines = Read-RepoText "docs\aura-shared-core-v2-contract.md"
Require-Text $guidelines "Ownership And Mutability" "Shared guidelines must document ownership and mutability."
Require-Text $guidelines "Conflict And Candidate Policy" "Shared guidelines must document conflict and candidate policy."
Require-Text $guidelines "Resolution Priority" "Shared guidelines must document resolution priority."

$auditFile = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs\SunExp") -File -Filter "04-Aura*.md")
if ($auditFile.Count -ne 1) {
    throw "Expected exactly one SunExp Aura shared-layer audit document."
}
$audit = [System.IO.File]::ReadAllText($auditFile[0].FullName)
Require-Text $audit "AuraCgShared" "Shared architecture audit must include AuraCgShared."
Require-Text $audit "provider identity" "Shared architecture audit must include provider identity findings."

$journeyReadme = Read-RepoText "AuraJourneyShared\README.md"
Require-Text $journeyReadme "ownerModId:localJourneyId" "AuraJourneyShared README must document owner-qualified JourneyId."
Require-Text $journeyReadme "QualifyJourneyId" "AuraJourneyShared README must document JourneyId normalization."

Write-Host "Shared architecture guideline scan passed."
