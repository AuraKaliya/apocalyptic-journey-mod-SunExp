param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required file is missing: $RelativePath"
    }

    return Get-Content -Raw -LiteralPath $path
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

$sharedRoots = @(
    "AuraAudioShared",
    "AudioArbiterShared",
    "BattleBgmArbiterShared",
    "AuraCgShared",
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

$audit = Read-RepoText "docs\SunExp\04-Aura共享层与核心层接入.md"
Require-Text $audit "AuraCgShared" "Shared architecture audit must include AuraCgShared."
Require-Text $audit "provider identity" "Shared architecture audit must include provider identity findings."

$journeyReadme = Read-RepoText "AuraJourneyShared\README.md"
Require-Text $journeyReadme "ownerModId:localJourneyId" "AuraJourneyShared README must document owner-qualified JourneyId."
Require-Text $journeyReadme "QualifyJourneyId" "AuraJourneyShared README must document JourneyId normalization."

Write-Host "Shared architecture guideline scan passed."
