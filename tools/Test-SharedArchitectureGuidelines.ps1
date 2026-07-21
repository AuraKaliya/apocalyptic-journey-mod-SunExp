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

function Read-RepoSourceTree {
    param([string]$RelativeDirectory)

    $directory = Join-Path $repoRoot $RelativeDirectory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Required source directory is missing: $RelativeDirectory"
    }

    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter "*.cs" -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Required source directory has no C# files: $RelativeDirectory"
    }

    return (($files | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join [Environment]::NewLine)
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
    @{ Name = "AuraSharedCore\AuraSharedRuntime.cs"; Text = (Read-RepoText "AuraSharedCore\AuraSharedRuntime.cs") },
    @{ Name = "AuraSkinShared\AuraSkinRuntime.cs"; Text = (Read-RepoText "AuraSkinShared\AuraSkinRuntime.cs") },
    @{ Name = "AudioArbiterShared"; Text = (Read-RepoSourceTree "AudioArbiterShared") },
    @{ Name = "BattleBgmArbiterShared"; Text = (Read-RepoSourceTree "BattleBgmArbiterShared") },
    @{ Name = "AuraCgShared"; Text = (Read-RepoSourceTree "AuraCgShared") },
    @{ Name = "UiTransitionGuardShared\UiTransitionGuardRuntime.cs"; Text = (Read-RepoText "UiTransitionGuardShared\UiTransitionGuardRuntime.cs") }
)

foreach ($runtime in $globalRuntimes) {
    Require-Text $runtime.Text "CurrentProtocolVersion" "$($runtime.Name) must expose CurrentProtocolVersion."
    Require-Text $runtime.Text "MinimumSupportedProtocolVersion" "$($runtime.Name) must expose MinimumSupportedProtocolVersion."
    Require-Text $runtime.Text "CurrentBuildId" "$($runtime.Name) must expose CurrentBuildId."
    Require-Text $runtime.Text "BuildId\s*=>\s*CurrentBuildId" "$($runtime.Name) must expose BuildId from CurrentBuildId."
    Require-Text $runtime.Text "ValidateExisting" "$($runtime.Name) must validate existing global component compatibility."
    Require-Text $runtime.Text "GetMethod\(" "$($runtime.Name) must check reflected public method shape."
}

$providerIdentityFiles = @(
    @{ Name = "AudioArbiterShared"; Text = (Read-RepoSourceTree "AudioArbiterShared") },
    @{ Name = "BattleBgmArbiterShared"; Text = (Read-RepoSourceTree "BattleBgmArbiterShared") },
    @{ Name = "AuraCgShared"; Text = (Read-RepoSourceTree "AuraCgShared") }
)

foreach ($runtime in $providerIdentityFiles) {
    Require-Text $runtime.Text "QualifiedProviderId" "$($runtime.Name) must keep an owner-qualified provider identity."
    Require-Text $runtime.Text "QualifyProviderId" "$($runtime.Name) must normalize provider identity through QualifyProviderId."
    Require-Text $runtime.Text "qualifiedProviderId" "$($runtime.Name) must include qualified provider ids in diagnostics."
}

$explicitProviderRequestFiles = @(
    @{ Name = "AudioArbiterShared"; Text = (Read-RepoSourceTree "AudioArbiterShared") },
    @{ Name = "BattleBgmArbiterShared"; Text = (Read-RepoSourceTree "BattleBgmArbiterShared") }
)

foreach ($runtime in $explicitProviderRequestFiles) {
    Require-Text $runtime.Text "MatchesProviderId" "$($runtime.Name) must match both bare and qualified provider ids."
}

$audioRuntime = Read-RepoSourceTree "AudioArbiterShared"
$audioComponentRuntime = Read-RepoText "AudioArbiterShared\AudioArbiterRuntime.cs"
$audioContracts = Read-RepoText "AudioArbiterShared\AudioContracts.cs"
$audioNetworkContracts = Read-RepoText "AudioArbiterShared\AudioNetworkContracts.cs"
$audioNetworkEventMapper = Read-RepoText "AudioArbiterShared\AudioNetworkEventMapper.cs"
$audioNetworkPolicy = Read-RepoText "AudioArbiterShared\AudioNetworkPolicy.cs"
$audioNetworkSession = Read-RepoText "AudioArbiterShared\AudioNetworkSessionState.cs"
$audioNetworkRuntime = Read-RepoText "AudioArbiterShared\AudioNetworkRuntime.cs"
$audioPropertyReader = Read-RepoText "AudioArbiterShared\AudioPropertyReader.cs"
$audioFileLoadPolicy = Read-RepoText "AudioArbiterShared\AudioFileLoadPolicy.cs"
$audioFileFormatProbe = Read-RepoText "AuraAudioShared\AudioFileFormatProbe.cs"
$audioUnityFileLoadPolicy = Read-RepoText "AudioArbiterShared\AudioUnityFileLoadPolicy.cs"
$battleBgmRuntime = Read-RepoText "BattleBgmArbiterShared\BattleBgmArbiterRuntime.cs"
$audioVariantSelectionPolicy = Read-RepoText "AudioArbiterShared\AudioVariantSelectionPolicy.cs"
$audioHookCatalog = Read-RepoText "AudioArbiterShared\AudioHookCatalog.cs"
$audioHookAdapter = Read-RepoText "AudioArbiterShared\AudioHookAdapter.cs"
$audioHookModels = Read-RepoText "AudioArbiterShared\AudioHookModels.cs"
$audioGameStateReader = Read-RepoText "AudioArbiterShared\AudioGameStateReader.cs"
$audioHookContextMapper = Read-RepoText "AudioArbiterShared\AudioHookContextMapper.cs"
$audioLowHealthCoordinator = Read-RepoText "AudioArbiterShared\AudioLowHealthCoordinator.cs"
$audioRequestFactory = Read-RepoText "AudioArbiterShared\AudioRequestFactory.cs"
$audioProviderAdapter = Read-RepoText "AudioArbiterShared\AudioProviderAdapter.cs"
$audioFileSoundProvider = Read-RepoText "AudioArbiterShared\AudioFileSoundProvider.cs"
$audioManifestLoader = Read-RepoText "AudioArbiterShared\AudioManifestLoader.cs"
$audioManifestMatchPolicy = Read-RepoText "AudioArbiterShared\AudioManifestMatchPolicy.cs"
$audioProviderResolver = Read-RepoText "AudioArbiterShared\AudioProviderResolver.cs"
$audioPresentationPolicy = Read-RepoText "AudioArbiterShared\AudioPresentationPolicy.cs"
$audioReplacementCoordinator = Read-RepoText "AudioArbiterShared\AudioReplacementCoordinator.cs"
$audioUnityPlaybackService = Read-RepoText "AudioArbiterShared\AudioUnityPlaybackService.cs"
Require-Text $battleBgmRuntime "ResolveProviderRequest" "Battle BGM explicit switches must use the owner-aware provider resolver."
Require-Text $battleBgmRuntime "rejected ambiguous bare provider id" "Battle BGM must reject ambiguous bare provider ids instead of selecting the first registration."
Require-Text $audioRuntime "MatchesProviderRequest" "AudioArbiterRuntime must expose owner-aware provider request matching."
Require-Text $audioRuntime "ownerStrict:\s*true" "AudioArbiterRuntime must have an owner-strict provider matching path."
Require-Text $audioRuntime "request\.IsRemote[\s\S]*Remote sound provider mismatch" "AudioArbiterRuntime must fail closed for remote owner/provider mismatches."
Require-Text $audioRuntime "OwnerModId to disambiguate" "AudioArbiterRuntime must document OwnerModId-based RPC provider disambiguation."
Require-Text $audioRuntime "RemoteReplacementPairingSeconds" "AudioArbiterRuntime must bound remote native-effect pairing before fallback playback."
Require-Text $audioRuntime "remote-fallback-played" "AudioArbiterRuntime must expose remote replacement fallback outcomes."
Require-Text $audioContracts "public sealed class AudioRegistryManifest" "Audio manifest DTOs must stay in the dedicated contracts file."
Require-Text $audioContracts "public sealed class SoundPlaybackRequest" "Audio playback request must stay in the dedicated contracts file."
Require-Text $audioContracts "AudioPropertyReader\.ReadString" "Audio request projection must use the isolated property reader."
Require-Text $audioNetworkContracts "public sealed class RpcAudioEvent" "Audio RPC types must stay in the dedicated network contracts file."
Require-Text $audioNetworkContracts "IAudioArbiterServerBoundRpcCommand" "Audio server-bound RPC marker must stay with network contracts."
Require-Text $audioNetworkContracts "AudioNetworkEventMapper\.CreateRemoteCopy" "Audio RPC payload construction must use the pure network mapper."
Require-Text $audioNetworkEventMapper "internal static class AudioNetworkEventMapper" "Audio network payload mapping must stay in its pure mapper."
Require-Text $audioNetworkEventMapper "DisableSync\s*=\s*true" "Audio network payload copies must disable relay sync."
Require-Text $audioNetworkPolicy "internal static class AudioNetworkPolicy" "Audio network envelope and expiry validation must stay in its pure policy."
Require-Text $audioNetworkPolicy "ValidateServerCardUsePresentation" "Audio server-bound presentation validation must stay in the network policy."
Require-Text $audioNetworkPolicy "IsExpiredPresentation" "Audio transient presentation expiry must stay in the network policy."
Require-Text $audioNetworkPolicy "ValidateLocalPresentationIdentity" "Audio multiplayer local presentations must reject ambiguous issuer or owner identity."
Require-Text $audioNetworkPolicy "expired presentation" "Audio server validation must reject expired client presentation requests before timestamps are renewed."
Require-Text $audioNetworkSession "internal sealed class AudioNetworkSessionState" "Audio fight-scoped network identity must stay in dedicated session state."
Require-Text $audioNetworkSession "receivedEventOrder\.Count > maximumPlaybackClaims" "Audio presentation claims must remain bounded in session state."
Require-Text $audioNetworkSession "ReuseOrCreateLocalPlayId" "Audio local action identity reuse must stay in session state."
Require-Text $audioNetworkSession "public void ResetTransient\(\)" "Audio fight cleanup must reset transient network state."
Require-Text $audioNetworkRuntime "internal sealed class AudioNetworkRuntime" "Audio RPC and multiplayer orchestration must stay in a dedicated network runtime."
Require-Text $audioNetworkRuntime "public void RegisterAuthority" "Audio RPC sender authority initialization must stay in the network runtime."
Require-Text $audioNetworkRuntime "AuraRpcAuthorityRuntime\.Register" "Audio network runtime must delegate receive-hook sender binding to shared authority."
Require-Text $audioNetworkRuntime "MaximumPlaybackClaims\s*=\s*512" "Audio network runtime must preserve the playback-claim budget."
Require-Text $audioNetworkRuntime "AudioNetworkPolicy\.ValidateServerCardUsePresentation" "Audio network runtime must delegate bound-sender validation to policy."
Require-Text $audioNetworkRuntime "SenderOwnsStatus" "Audio network runtime must validate bound sender ownership."
Require-Text $audioNetworkRuntime "RpcAudioPresentationRequest" "Audio clients must submit card-use presentation through the server-bound request command."
Require-Text $audioComponentRuntime "new\(\);[\s\S]*AudioNetworkRuntime networkRuntime|AudioNetworkRuntime networkRuntime\s*=\s*new" "Audio component must delegate multiplayer state and RPC orchestration to the network runtime."
Require-Text $audioComponentRuntime "networkRuntime\.TryPrepareAndRelayLocalPresentation" "Audio local synchronized presentation must be prepared by the network runtime."
Require-Text $audioComponentRuntime "networkRuntime\.ApplyServerCardUsePresentation" "Audio server-bound presentation must be delegated to the network runtime."
Require-Text $audioComponentRuntime "networkRuntime\.TryAcceptRemotePresentation" "Audio received presentation must enter the network runtime claim boundary."
Require-Text $audioComponentRuntime "networkRuntime\.RegisterAuthority" "Audio component must delegate RPC authority initialization to the network runtime."
Require-Text $audioComponentRuntime '"audio-rpc-authority"' "Audio component must isolate RPC authority initialization as a named step."
Require-Text $audioComponentRuntime '"audio-hooks"' "Audio component must isolate Hook initialization as a named step."
Require-Text $audioComponentRuntime "AuraSharedHooks\.RunStep" "Audio component initialization steps must fail independently."
Require-Text $audioPropertyReader "internal static class AudioPropertyReader" "Audio reflection reads must stay in their isolated reader."
Require-Text $audioPropertyReader "BindingFlags\.Instance\s*\|\s*BindingFlags\.Public" "Audio property reader must only inspect public instance properties."
Require-Text $audioFileFormatProbe "public static class AudioFileFormatProbe" "Audio content format detection must stay in a shared pure policy."
Require-Text $audioFileFormatProbe '"RIFF"' "Audio format probe must recognize WAV content by signature."
Require-Text $audioFileFormatProbe '"OggS"' "Audio format probe must recognize Ogg content by signature."
Require-Text $audioFileFormatProbe '"OpusHead"' "Audio format probe must explicitly reject unsupported Ogg Opus content."
Require-Text $audioFileFormatProbe "TryFindMpegAudioFrame" "Audio format probe must validate MPEG audio frames instead of trusting MP3 extensions."
Require-Text $audioUnityFileLoadPolicy "AudioFileFormat\.OggVorbis[\s\S]*AudioType\.OGGVORBIS" "Unity audio type selection must map detected Ogg Vorbis content explicitly."
Require-Text $audioVariantSelectionPolicy "internal static class AudioVariantSelectionPolicy" "Audio variant selection must stay in a pure policy."
Require-Text $audioVariantSelectionPolicy "StableHash" "Audio variant selection must remain deterministic across synchronized peers."
Require-Text $audioHookCatalog "internal static class AudioHookCatalog" "Audio hook targets and phases must stay in a pure catalog."
Require-Text $audioHookCatalog "AudioHookRegistrationKind\.CombatActionBefore" "Audio hook catalog must preserve the routed combat-action entry."
Require-Text $audioHookCatalog "internal enum AudioHookCallbackKind" "Audio hook catalog must bind every definition to a stable callback kind."
Require-Text $audioHookCatalog "AudioHookCallbackKind\.PotentialHpChanged" "Audio hook catalog must share the ScriptExecutor HP callback kind."
Require-Text $audioHookCatalog '"Fight_Start\.Init"[\s\S]*AudioHookRegistrationKind\.Before[\s\S]*"Fight_Start\.Init"[\s\S]*AudioHookRegistrationKind\.After' "Audio hook catalog must preserve fight-start before/after ordering."
Require-Text $audioHookCatalog '"ScriptExecutor\.OnlineDamage"' "Audio hook catalog must retain online HP observation."
Require-Text $audioHookAdapter "internal sealed class AudioHookAdapter\s*:\s*IDisposable" "Audio hook lifecycle must stay in a disposable adapter."
Require-Text $audioHookAdapter "AudioHookCatalog\.All" "Audio hook adapter must register from the catalog instead of duplicating targets."
Require-Text $audioHookAdapter "new AuraHookRegistry" "Audio hook adapter must own routed Before/After registrations through AuraHookRegistry."
Require-Text $audioHookAdapter "hookRegistry\.BeforeRouted" "Audio hook adapter must use disposable routed Before registrations."
Require-Text $audioHookAdapter "hookRegistry\.AfterRouted" "Audio hook adapter must use disposable routed After registrations."
Require-Text $audioHookAdapter "AuraCombatActionRouter\.RegisterBefore" "Audio hook adapter must preserve the shared combat-action router."
Require-Text $audioHookAdapter "public void Dispose\(\)" "Audio hook adapter must release all routed subscriptions."
Require-Text $audioHookModels "internal sealed class AudioStatusSnapshot" "Audio hook observations must expose a game-object-free status snapshot."
Require-Text $audioHookModels "internal sealed class AudioCombatActionObservation" "Audio combat mapping must use a plain observation model."
Require-Text $audioGameStateReader "internal sealed class AudioGameStateReader" "Audio game state reads must stay in a dedicated adapter."
Require-Text $audioGameStateReader "ReadCurrentCareerId" "Audio game state reader must own current-career lookup."
Require-Text $audioGameStateReader "ReadStatusSnapshot" "Audio game state reader must project status identity and HP into a plain snapshot."
Require-Text $audioGameStateReader "ReadFightStatusSnapshots" "Audio game state reader must own fight-status enumeration."
Require-Text $audioGameStateReader "ReadExecutorStatusSnapshots" "Audio game state reader must own ScriptExecutor status traversal."
Require-Text $audioGameStateReader "intMemberCache" "Audio game state reader must own the cached HP member lookup."
Require-Text $audioHookContextMapper "internal sealed class AudioHookContextMapper" "Audio hook contexts must map through a dedicated adapter."
Require-Text $audioHookContextMapper "MapCareerDetail" "Audio hook mapper must centralize career-detail arguments."
Require-Text $audioHookContextMapper "MapCombatAction" "Audio hook mapper must centralize routed combat observations."
Require-Text $audioHookContextMapper "MapExecutorHpChanges" "Audio hook mapper must centralize ScriptExecutor HP contexts."
Require-Text $audioHookContextMapper "MapStatusHpChange" "Audio hook mapper must centralize StatusManager HP contexts."
Require-Text $audioLowHealthCoordinator "internal sealed class AudioLowHealthCoordinator" "Audio low-health state must stay in a dedicated coordinator."
Require-Text $audioLowHealthCoordinator "AudioLowHealthObservationDecision" "Audio low-health observations must return an explicit decision."
Require-Text $audioLowHealthCoordinator "ConfigureProviders" "Audio low-health provider threshold indexing must stay in the coordinator."
Require-Text $audioLowHealthCoordinator "RememberNoProvider" "Audio low-health no-provider cooldown must stay in the coordinator."
Require-Text $audioLowHealthCoordinator "ResetFight" "Audio low-health fight-scoped state must expose deterministic cleanup."
Require-Text $audioRequestFactory "internal static class AudioRequestFactory" "Audio hook observations must map through a pure request factory."
Require-Text $audioRequestFactory "CreateCombatActionBatch" "Audio request factory must preserve the card-use and skill-voice batch."
Require-Text $audioRequestFactory "CreateLowHealth" "Audio request factory must centralize low-health request shape."
Require-Text $audioRequestFactory "CreateBattleCompleted" "Audio request factory must centralize battle-completed request shape."
$audioHookDefinitionCount = ([regex]::Matches($audioHookCatalog, "new\s+AudioHookDefinition\s*\(")).Count
if ($audioHookDefinitionCount -ne 19) {
    throw "Audio hook catalog must retain exactly 19 current hook definitions. actual=$audioHookDefinitionCount"
}
foreach ($target in @(
    "GameEntryUI.Init",
    "Fight_Start.Init",
    "GameEntryUI.ShowDetail",
    "FightUI.CallActionAnimation",
    "EffectSound.Start",
    "BuffItem.Init",
    "StatusManager.PlayVocal",
    "NarrationManager.Play",
    "ScriptExecutor.ChangeHp",
    "ScriptExecutor.PureChangeHp",
    "ScriptExecutor.SetHp",
    "ScriptExecutor.ChangeMaxHp",
    "ScriptExecutor.Damage",
    "ScriptExecutor.OnlineDamage",
    "StatusManager.set_CurHp",
    "StatusManager.set_MaxHp",
    "Fight_Win.ResetStates",
    "Fight_Escape.ResetStates"
)) {
    Require-Text $audioHookCatalog ([regex]::Escape($target)) "Audio hook catalog is missing current target: $target"
    if ($audioHookAdapter -match [regex]::Escape($target)) {
        throw "Audio hook adapter must consume catalog definitions instead of duplicating target: $target"
    }
}
Require-Text $audioProviderAdapter "internal sealed class SoundProviderHandle" "Audio reflected provider compatibility must stay in a dedicated adapter."
Require-Text $audioProviderAdapter "AudioPropertyReader\.ReadString" "Audio provider adapter must delegate reflected property reads."
Require-Text $audioProviderAdapter 'GetMethod\("GetClip"' "Audio provider adapter must own reflected clip access."
Require-Text $audioProviderAdapter "internal readonly struct ResolvedSound" "Audio provider/clip result composition must stay with the provider adapter."
Require-Text $audioFileSoundProvider "public sealed class FileSoundProvider" "Audio file provider public type must stay in its dedicated adapter file."
Require-Text $audioFileSoundProvider "UnityWebRequestMultimedia\.GetAudioClip" "Audio file provider adapter must own Unity audio loading."
Require-Text $audioFileSoundProvider "private sealed class ProviderRunner\s*:\s*MonoBehaviour" "Audio file provider must isolate its Coroutine runner."
Require-Text $audioFileSoundProvider "completedGeneration != generation" "Audio file provider must reject stale load completions."
Require-Text $audioFileSoundProvider "AudioFileFormatProbe\.Probe" "Audio file provider must detect the real file format before Unity loading."
Require-Text $audioFileSoundProvider "AudioUnityFileLoadPolicy\.TryResolve" "Audio file provider must map detected formats through the shared Unity load policy."
Require-Text $battleBgmRuntime "AudioFileFormatProbe\.Probe" "Battle BGM provider must detect the real file format before Unity loading."
Require-Text $battleBgmRuntime "AudioUnityFileLoadPolicy\.TryResolve" "Battle BGM provider must share detected-format Unity type mapping with ordinary audio."
Require-Text $audioFileSoundProvider "AudioVariantSelectionPolicy\.SelectStartIndex" "Audio file provider must select synchronized variants through the pure policy."
Require-Text $audioFileSoundProvider "pendingLoads" "Audio multi-variant providers must wait for all configured paths before becoming ready."
Require-Text $audioManifestLoader "internal static class AudioManifestLoader" "Audio manifest loading and compatibility validation must stay in its dedicated loader."
Require-Text $audioManifestLoader "unsupported schemaVersion" "Audio manifest loader must reject unsupported schemas."
Require-Text $audioManifestLoader "protocol minVersion" "Audio manifest loader must reject unsupported protocols."
Require-Text $audioManifestLoader "CreateProviderPlan" "Audio manifest defaults and provider paths must be normalized into provider plans."
Require-Text $audioManifestLoader "ResolveProviderVariantPaths" "Audio manifest loader must normalize and deduplicate variant paths."
Require-Text $audioManifestMatchPolicy "internal static class AudioManifestMatchPolicy" "Audio manifest request matching must stay in its pure policy."
Require-Text $audioManifestMatchPolicy "hpRatioCrossDown" "Audio manifest match policy must preserve threshold-crossing semantics."
Require-Text $audioProviderResolver "internal static class AudioProviderResolver" "Audio provider identity and arbitration must stay in its pure resolver."
Require-Text $audioProviderResolver "ShouldWarnRemoteMismatch" "Audio provider resolver must expose remote fail-closed mismatches."
Require-Text $audioProviderResolver "HardClaimBlocked" "Audio provider resolver must preserve hard-claim fallback blocking."
Require-Text $audioProviderResolver "CompareProviderOrder" "Audio provider resolver must own deterministic priority ordering."
Require-Text $audioProviderResolver "internal static class AudioProviderCooldownPolicy" "Audio provider cooldown state transitions must stay in their pure policy."
Require-Text $audioPresentationPolicy "internal static class AudioPresentationPolicy" "Audio playback routing and native-effect decisions must stay in the presentation policy."
Require-Text $audioPresentationPolicy "QueueNativeEffectReplacement" "Audio presentation policy must identify native-effect replacement routing."
Require-Text $audioPresentationPolicy "PlayReplacementAfterDelay" "Audio presentation policy must preserve custom-volume delayed playback."
Require-Text $audioPresentationPolicy "internal static class AudioSuppressionPolicy" "Audio narration suppression planning must stay in its pure policy."
Require-Text $audioReplacementCoordinator "internal sealed class AudioReplacementCoordinator" "Audio pending replacement and remote pairing state must stay in its coordinator."
Require-Text $audioReplacementCoordinator "TryClaimPairedFallback" "Audio replacement coordinator must expose single-use remote pairing claims."
Require-Text $audioReplacementCoordinator "fallback-original-suppressed" "Audio replacement coordinator must preserve late-original suppression outcomes."
Require-Text $audioUnityPlaybackService "internal static class AudioUnityPlaybackService" "AudioManager and AudioSource mutation must stay in the Unity playback adapter."
Require-Text $audioUnityPlaybackService "AudioManager\.Instance" "Audio Unity playback adapter must own AudioManager access."
Require-Text $audioUnityPlaybackService "GetOrCreateVocalSource" "Audio Unity playback adapter must own vocal AudioSource compatibility."
if ($audioComponentRuntime -match "public sealed class AudioRegistryManifest|public sealed class SoundPlaybackRequest|public sealed class RpcAudioEvent|private static class PropertyReader|private static AudioRegistryManifest\? DeserializeManifest|private static Func<object\?, bool> BuildManifestCondition|ResolveWithProviderMatcher|private static string QualifyProviderId|private readonly struct PendingReplacement|pairedRemoteReplacementIds|private static bool IsReplacementPolicy|private static void PlayVocal|private static void PlayEffect|private static object\? ReadMember") {
    throw "AudioArbiterRuntime must delegate contracts, manifest/provider policy, playback decisions, replacement state, Unity playback, RPC payloads, and reflection reads to extracted boundaries."
}
if ($audioNetworkEventMapper -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|RpcCommandBase") {
    throw "Audio network event mapping must remain independent from Unity, game APIs, hooks, and RPC transport."
}
if ($audioNetworkPolicy -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|RpcCommandBase|DateTime\.UtcNow") {
    throw "Audio network policy must remain independent from Unity, game APIs, RPC transport, and wall-clock access."
}
if ($audioNetworkSession -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|RpcCommandBase|DateTime\.UtcNow|Time\.") {
    throw "Audio network session state must remain independent from Unity, game APIs, RPC transport, and clocks."
}
if ($audioNetworkRuntime -match "MonoBehaviour|StartCoroutine|AudioClip|AudioSource|AudioManager") {
    throw "Audio network runtime must not own Unity playback, Coroutine execution, or audio resources."
}
if ($audioPropertyReader -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext") {
    throw "Audio property reading must remain independent from Unity, game APIs, and hooks."
}
if ($audioFileLoadPolicy -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|MonoBehaviour|UnityWebRequest") {
    throw "Audio file extension classification must remain independent from Unity, game APIs, hooks, and transport."
}
if ($audioFileFormatProbe -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|MonoBehaviour|UnityWebRequest") {
    throw "Audio content format detection must remain independent from Unity, game APIs, hooks, and transport."
}
if ($audioHookCatalog -match "using\s+UnityEngine|using\s+Witch\.|ModHookContext|ModConfig|MonoBehaviour") {
    throw "Audio hook catalog must remain plain target/phase metadata without game or hook objects."
}
if ($audioHookAdapter -match "PlayerManager\.Instance|FightPlayer\.Instance|FightManager\.Instance|RoleTable\.Instance|GameEntryUI\.career|StatusManager|IScriptExecutor|IDataConfig|SoundPlaybackRequest|AudioRequestFactory|AudioNetworkRuntime|SoundProviderHandle|AudioUnityPlaybackService|StartCoroutine|AudioClip") {
    throw "Audio hook adapter must only own hook registration, callback routing, diagnostics, and subscription disposal."
}
if ($audioHookModels -match "using\s+UnityEngine|using\s+Witch\.|ModHookContext|ModConfig|StatusManager|IScriptExecutor|IDataConfig|AudioClip|MonoBehaviour") {
    throw "Audio hook observation models must remain independent from Unity, Witch, and raw game objects."
}
if ($audioGameStateReader -match "ModHookContext|ModConfig|SoundPlaybackRequest|AudioRequestFactory|AudioNetworkRuntime|SoundProviderHandle|AudioUnityPlaybackService|RegisterBefore|RegisterAfter|AddMethodHook") {
    throw "Audio game state reader must only read game state and return plain observations."
}
if ($audioHookContextMapper -match "PlayerManager\.Instance|FightPlayer\.Instance|FightManager\.Instance|RoleTable\.Instance|GameEntryUI\.career|new\s+SoundPlaybackRequest|AudioRequestFactory|AudioNetworkRuntime|SoundProviderHandle|AudioUnityPlaybackService|RegisterBefore|RegisterAfter|AddMethodHook") {
    throw "Audio hook context mapper must delegate singleton/game-state reads and must not create requests or own runtime services."
}
if ($audioLowHealthCoordinator -match "using\s+UnityEngine|using\s+Witch\.|ModHookContext|ModConfig|StatusManager|IScriptExecutor|IDataConfig|AudioClip|MonoBehaviour|SoundProviderHandle|AudioNetworkRuntime|AudioUnityPlaybackService|Time\.|Mathf\.|DateTime\.UtcNow|Guid\.") {
    throw "Audio low-health coordinator must remain a deterministic state machine over plain snapshots, requests, provider descriptors, and caller-supplied time."
}
if ($audioRequestFactory -match "using\s+UnityEngine|using\s+Witch\.|ModHookContext|ModConfig|StatusManager|IScriptExecutor|IDataConfig|AudioClip|MonoBehaviour|PlayerManager|FightPlayer|RoleTable|DateTime\.UtcNow|Time\.|Guid\.") {
    throw "Audio request factory must remain a deterministic plain-observation mapper."
}
if ($audioComponentRuntime -match "RoleTable\.Instance|GameEntryUI\.career|FightManager\.Instance|PlayerManager\.Instance|FightPlayer\.Instance|\.dataConfig|\.fatherObject|ReadIntMember|new\s+SoundPlaybackRequest") {
    throw "Audio component must delegate game-object reads and request construction to the reader, mapper, and factory boundaries."
}
Require-Text $audioComponentRuntime "hookContextMapper\.MapCareerDetail" "Audio component must delegate career hook mapping."
Require-Text $audioComponentRuntime "hookContextMapper\.MapCombatAction" "Audio component must delegate combat hook mapping."
Require-Text $audioComponentRuntime "hookContextMapper\.MapBuffApplied" "Audio component must delegate buff hook mapping."
Require-Text $audioComponentRuntime "hookContextMapper\.MapVocalState" "Audio component must delegate vocal hook mapping."
Require-Text $audioComponentRuntime "hookContextMapper\.MapExecutorHpChanges" "Audio component must delegate HP hook mapping."
Require-Text $audioComponentRuntime "AudioRequestFactory\.CreateLowHealth" "Audio component must create low-health requests through the pure factory."
Require-Text $audioComponentRuntime "lowHealthCoordinator\.ConfigureProviders" "Audio component must project provider descriptors into the low-health coordinator."
Require-Text $audioComponentRuntime "lowHealthCoordinator\.Observe" "Audio component must delegate low-health observation state."
Require-Text $audioComponentRuntime "lowHealthCoordinator\.RememberNoProvider" "Audio component must delegate low-health no-provider cooldown state."
Require-Text $audioComponentRuntime "lowHealthCoordinator\.MarkAnnounced" "Audio component must report accepted low-health playback to the coordinator."
if ($audioComponentRuntime -match "lowHealthAnnounced|lastHpRatioByStatus|lowHealthNoProviderUntil|LowHealthProviderIndex|lowHealthProviderIndex|ResetLowHealthAnnouncementIfRecovered|ShouldAttemptLowHealthRequest|LowHealthNoProviderKey") {
    throw "Audio component must not regain low-health cross-observation state, threshold indexing, recovery policy, or no-provider cooldown keys."
}
Require-Text $audioComponentRuntime "new AudioHookAdapter" "Audio component must delegate hook registration to AudioHookAdapter."
Require-Text $audioComponentRuntime "hookAdapter\.Register" "Audio component must explicitly start the hook adapter once."
Require-Text $audioComponentRuntime "private void OnDestroy\(\)" "Audio component must release hook subscriptions during destruction."
Require-Text $audioComponentRuntime "hookAdapter\?\.Dispose" "Audio component must dispose the hook adapter lifecycle."
if ($audioComponentRuntime -match "AddMethodHookBefore|AddMethodHookAfter|AuraSharedHooks\.Register|AuraCombatActionRouter\.RegisterBefore|private\s+void\s+RegisterBefore|private\s+void\s+RegisterAfter|hooksRegistered|OnActionAnimationBefore|MapLegacyCombatAction") {
    throw "Audio component must not own raw hook registration, registration flags, or the dead legacy combat handler."
}
if ($audioProviderAdapter -match "UnityWebRequest|DownloadHandlerAudioClip|StartCoroutine|MonoBehaviour|PlayerManager|ModHookContext") {
    throw "Audio reflected provider adapter must not regain file transport, Coroutine, network, or hook ownership."
}
if ($audioFileSoundProvider -match "PlayerManager|SendRpcCommand|ModHookContext|RegisterBefore|RegisterAfter") {
    throw "Audio file provider must remain a resource adapter without RPC or hook ownership."
}
if ($audioContracts -match "UnityEngine|AudioManager|PlayerManager|ModHookContext|RpcCommandBase") {
    throw "Audio data contracts must remain independent from Unity, game managers, hooks, and RPC transport."
}
if ($audioManifestLoader -match "using UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|AudioClip|MonoBehaviour") {
    throw "Audio manifest loading must remain independent from Unity objects, game APIs, managers, and hooks."
}
if ($audioManifestMatchPolicy -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|AudioClip|MonoBehaviour") {
    throw "Audio manifest request matching must remain a pure policy."
}
if ($audioProviderResolver -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|AudioClip|MonoBehaviour|FileSoundProvider") {
    throw "Audio provider identity, arbitration, and cooldown policy must remain independent from Unity and concrete provider adapters."
}
if ($audioPresentationPolicy -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|AudioClip|MonoBehaviour|Time\.") {
    throw "Audio presentation and suppression policy must remain independent from Unity, game APIs, and wall-clock access."
}
if ($audioReplacementCoordinator -match "UnityEngine|Witch\.|AudioManager|PlayerManager|ModHookContext|AudioClip|MonoBehaviour|Time\.") {
    throw "Audio replacement state and pairing claims must remain generic and independent from Unity and game APIs."
}
if ($audioUnityPlaybackService -match "MonoBehaviour|StartCoroutine|ModHookContext|PlayerManager") {
    throw "Audio Unity playback service must remain a delegated adapter without hook, RPC, or Coroutine ownership."
}
if ($audioComponentRuntime -match 'AuraRpcAuthorityRuntime\.Register|IAudioArbiterServerBoundRpcCommand|SendRpcCommand|private\s+readonly\s+HashSet<string>\s+receivedEventIds|private\s+readonly\s+Dictionary<string, string>\s+recentLocalPlayIds|private\s+string\s+fightToken|private\s+long\s+localPlaybackCounter|SenderOwnsStatus|class\s+SoundProviderHandle|class\s+FileSoundProvider|class\s+ProviderRunner|UnityWebRequestMultimedia|DownloadHandlerAudioClip|GetMethod\("GetClip"|readonly\s+struct\s+ResolvedSound') {
    throw "AudioArbiterComponent must not regain RPC, network-session, provider reflection, or file-loading responsibilities."
}

$architectureGuidelines = Read-RepoText "docs\aura-shared-core-v2-contract.md"
Require-Text $architectureGuidelines "provider identity[\s\S]*BuildId" "Shared architecture guidelines must require BuildId bumps for provider identity semantic changes."
Require-Text $architectureGuidelines "Tool-owned runtime caches[\s\S]*AuraSharedStorageCoordinator\.ExecuteWrite" "Shared architecture guidelines must document coordinated shared-cache writes."
Require-Text $architectureGuidelines "WriteTextAtomic[\s\S]*cache metadata" "Shared architecture guidelines must require atomic metadata writes for shared caches."

$auraCgRuntime = Read-RepoSourceTree "AuraCgShared"
$auraCgComponentRuntime = Read-RepoText "AuraCgShared\AuraCgRuntime.cs"
$auraCgOverlayPresenter = Read-RepoText "AuraCgShared\AuraCgOverlayPresenter.cs"
$auraCgNonPlaybackCoordinatorSource = ((Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraCgShared") -Filter "*.cs" -File |
    Where-Object { $_.Name -ne "AuraCgPlaybackCoordinator.cs" } |
    Sort-Object FullName |
    ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }) -join [Environment]::NewLine)
Require-Text $auraCgOverlayPresenter "internal sealed class AuraCgOverlayPresenter" "AuraCg overlay state and Unity mutation must stay in a dedicated presenter."
Require-Text $auraCgOverlayPresenter "RenderMode\.ScreenSpaceOverlay" "AuraCgShared overlay must render on an independent screen-space canvas."
Require-Text $auraCgOverlayPresenter "overlayCanvas\.overrideSorting\s*=\s*true" "AuraCgShared overlay canvas must control its own sorting order."
Require-Text $auraCgOverlayPresenter "overlayGroup\.blocksRaycasts\s*=\s*false" "AuraCgShared overlay canvas group must not block raycasts."
Require-Text $auraCgOverlayPresenter "overlayImage\.raycastTarget\s*=\s*false" "AuraCgShared overlay image must not receive raycasts."
Require-Text $auraCgOverlayPresenter "DontDestroyOnLoad\(overlayRoot\)" "AuraCgShared overlay root must survive scene transitions without attaching to game UI canvases."
Require-Text $auraCgOverlayPresenter "DisableAndDestroyAfterFrame" "AuraCg overlay presenter must use the shared safe-destroy path."
Require-Text $auraCgOverlayPresenter "public IEnumerator PlayImage" "AuraCg presenter must expose component-driven image animation without owning a Coroutine runner."
Require-Text $auraCgOverlayPresenter "public IEnumerator PlaySequence" "AuraCg presenter must expose component-driven sequence animation without owning a Coroutine runner."
if ($auraCgOverlayPresenter -match "class AuraCgOverlayPresenter\s*:\s*MonoBehaviour|\.StartCoroutine\(") {
    throw "AuraCg overlay presenter must remain a component-driven adapter rather than a second Coroutine owner."
}
if ($auraCgRuntime -match "manager\?\.(upperCanvasTf|canvasTf)|GameUIManager|GraphicRaycaster") {
    throw "AuraCgShared overlay must not attach to game UI canvases or add a GraphicRaycaster."
}

$auraCgRegistryQuery = Read-RepoText "AuraCgShared\AuraCgRegistryQueryService.cs"
$auraCgRegistry = Read-RepoText "AuraCgShared\AuraCgRegistry.cs"
$auraCgNetworkPolicy = Read-RepoText "AuraCgShared\AuraCgNetworkPolicy.cs"
$auraCgNetworkSession = Read-RepoText "AuraCgShared\AuraCgNetworkSessionState.cs"
$auraCgNetworkRuntime = Read-RepoText "AuraCgShared\AuraCgNetworkRuntime.cs"
$auraCgRegisteredRequestResolver = Read-RepoText "AuraCgShared\AuraCgRegisteredRequestResolver.cs"
$auraCgProviderCoordinator = Read-RepoText "AuraCgShared\AuraCgProviderCoordinator.cs"
$auraCgPlaybackClaims = Read-RepoText "AuraCgShared\AuraCgPlaybackClaimStore.cs"
$auraCgPlaybackCoordinator = Read-RepoText "AuraCgShared\AuraCgPlaybackCoordinator.cs"
$auraCgPresentationMath = Read-RepoText "AuraCgShared\AuraCgPresentationMath.cs"
$auraCgPresentationPolicy = Read-RepoText "AuraCgShared\AuraCgPresentationPolicy.cs"
$auraCgAdventurePreloadHistory = Read-RepoText "AuraCgShared\AuraCgAdventurePreloadHistory.cs"
$auraCgPreloadScheduler = Read-RepoText "AuraCgShared\AuraCgPreloadScheduler.cs"
$auraCgPreloadSubmission = Read-RepoText "AuraCgShared\AuraCgPreloadSubmission.cs"
$auraCgMediaCache = Read-RepoText "AuraCgShared\AuraCgMediaCache.cs"
$auraCgMediaCacheModels = Read-RepoText "AuraCgShared\AuraCgMediaCacheModels.cs"
$auraCgMediaRetentionLedger = Read-RepoText "AuraCgShared\AuraCgMediaRetentionLedger.cs"
$auraCgMediaReleaseQueue = Read-RepoText "AuraCgShared\AuraCgMediaReleaseQueue.cs"
$auraCgMediaCacheKeys = Read-RepoText "AuraCgShared\AuraCgMediaCacheKeys.cs"
$auraCgMediaPathResolver = Read-RepoText "AuraCgShared\AuraCgMediaPathResolver.cs"
$auraCgUnityMediaRepository = Read-RepoText "AuraCgShared\AuraCgUnityMediaRepository.cs"
Require-Text $auraCgRegistryQuery "internal static class AuraCgRegistryQueryService" "AuraCg registry matching must stay in its pure query service."
Require-Text $auraCgRegistryQuery "MatchesTrigger" "AuraCg registry query service must own trigger matching."
Require-Text $auraCgRegistry "cg-manifest-duplicate" "AuraCg registration must reject duplicate owner-qualified ids inside one contribution."
Require-Text $auraCgRegistry "cg-contribution-identity-conflict" "AuraCg registration must reject one qualified id across multiple owner contributions."
Require-Text $auraCgNetworkPolicy "internal static class AuraCgNetworkPolicy" "AuraCg network validation must stay in its pure policy service."
Require-Text $auraCgNetworkPolicy "HasValidPlaybackShape" "AuraCg network policy must own envelope shape validation."
Require-Text $auraCgNetworkSession "internal sealed class AuraCgNetworkSessionState" "AuraCg transient network identity must stay in a dedicated session state."
Require-Text $auraCgNetworkSession "AuraCgPlaybackClaimStore playbackClaims" "AuraCg network session must own bounded playback claims."
Require-Text $auraCgNetworkSession "ReuseOrCreateLocalPlayId" "AuraCg local action identity reuse must stay in network session state."
Require-Text $auraCgNetworkSession "public void ResetTransient\(\)" "AuraCg fight cleanup must reset transient network state."
Require-Text $auraCgNetworkRuntime "internal sealed class AuraCgNetworkRuntime" "AuraCg RPC and multiplayer orchestration must stay in a dedicated network runtime."
Require-Text $auraCgNetworkRuntime "MaximumEventsPerPlayback\s*=\s*4" "AuraCg network runtime must preserve the event-count budget."
Require-Text $auraCgNetworkRuntime "MaximumPayloadBytes\s*=\s*8192" "AuraCg network runtime must preserve the payload byte budget."
Require-Text $auraCgNetworkRuntime "MaximumIdentifierLength\s*=\s*160" "AuraCg network runtime must preserve identifier bounds."
Require-Text $auraCgNetworkRuntime "ValidateServerPlaybackRequest" "AuraCg network runtime must validate server-bound playback requests."
Require-Text $auraCgNetworkRuntime "SenderOwnsStatus" "AuraCg network runtime must validate bound sender ownership."
Require-Text $auraCgNetworkRuntime "AuraSharedPayloadBudget\.FitsSoftLimit" "AuraCg network runtime must enforce payload bytes before relay."
Require-Text $auraCgNetworkRuntime "RpcSkillCgPlaybackRequest" "AuraCg clients must send playback through the server-bound request command."
Require-Text $auraCgNetworkRuntime "registeredRequestResolver\(item, false\)" "AuraCg host validation must resolve registered identities without applying recipient-local activation."
if ($auraCgNetworkRuntime -match "StartCoroutine|AuraCgPlaybackCoordinator|AuraCgOverlayPresenter") {
    throw "AuraCg network runtime must not own Unity playback queues, Coroutine execution, or overlays."
}
Require-Text $auraCgRegisteredRequestResolver "internal sealed class AuraCgRegisteredRequestResolver" "AuraCg registered identity and local resource resolution must stay in a dedicated resolver."
Require-Text $auraCgRegisteredRequestResolver "ResolveNetworkRequest" "AuraCg registered resolver must own compact network identity resolution."
Require-Text $auraCgRegisteredRequestResolver "requireLocalActivation" "AuraCg registered resolver must preserve host-versus-recipient activation semantics."
Require-Text $auraCgRegisteredRequestResolver "ProviderIdentity" "AuraCg registered resolver must validate owner-qualified provider identity."
Require-Text $auraCgRegisteredRequestResolver "MediaExists" "AuraCg registered resolver must validate locally resolved media before playback."
Require-Text $auraCgProviderCoordinator "internal sealed class AuraCgProviderCoordinator" "AuraCg provider registration and reflection dispatch must stay in a dedicated coordinator."
Require-Text $auraCgProviderCoordinator "providers\.RemoveAll" "AuraCg provider coordinator must replace duplicate owner-qualified identities."
Require-Text $auraCgProviderCoordinator 'providerType\.GetMethod\("BuildRequests"' "AuraCg provider reflection dispatch must stay in the provider coordinator."
Require-Text $auraCgProviderCoordinator "output\.Sort\(CompareRequests\)" "AuraCg provider coordinator must own deterministic request ordering."
Require-Text $auraCgProviderCoordinator "AuraCgProviderBuildFailure" "AuraCg provider failures must remain isolated from other providers."
Require-Text $auraCgPlaybackClaims "internal sealed class AuraCgPlaybackClaimStore" "AuraCg duplicate claims must stay in a bounded lifecycle store."
Require-Text $auraCgPlaybackClaims "while \(order\.Count > capacity\)" "AuraCg playback claims must remain capacity-bounded."
Require-Text $auraCgPlaybackClaims "public void Clear\(\)" "AuraCg playback claims must expose explicit fight cleanup."
Require-Text $auraCgPlaybackCoordinator "internal sealed class AuraCgPlaybackCoordinator" "AuraCg playback queue state must stay in its dedicated coordinator."
Require-Text $auraCgPlaybackCoordinator "maximumQueueLength" "AuraCg playback coordinator must enforce a finite queue length."
Require-Text $auraCgPlaybackCoordinator "TryTakeNext" "AuraCg playback coordinator must own stale request filtering and ordered dequeue."
Require-Text $auraCgPlaybackCoordinator "generation\+\+" "AuraCg playback cleanup must invalidate active coroutine generations."
Require-Text $auraCgPresentationMath "internal static class AuraCgPresentationMath" "AuraCg presentation layout and timing curves must stay in a pure math service."
Require-Text $auraCgPresentationMath "CalculateCoverImageOffset" "AuraCg cover focus behavior must stay in the presentation math service."
Require-Text $auraCgPresentationMath "EvaluateSlideAlpha" "AuraCg slide alpha behavior must stay in the presentation math service."
Require-Text $auraCgPresentationPolicy "internal static class AuraCgPresentationPolicy" "AuraCg flash selection must stay in a pure presentation policy."
Require-Text $auraCgPresentationPolicy "UsesMaskedFlash" "AuraCg masked flash selection must stay outside the Unity presenter."
Require-Text $auraCgPresentationPolicy "UsesScreenBwFlash" "AuraCg screen flash selection must stay outside the Unity presenter."
Require-Text $auraCgAdventurePreloadHistory "while \(order\.Count > capacity\)" "AuraCg adventure preload history must remain bounded."
Require-Text $auraCgPreloadScheduler "maximumPending" "AuraCg preload scheduler must enforce a global pending limit."
Require-Text $auraCgPreloadScheduler "maximumPendingPerOwner" "AuraCg preload scheduler must prevent one owner from consuming the whole backlog."
Require-Text $auraCgPreloadScheduler "maximumConcurrent - activeKeys\.Count" "AuraCg preload scheduler must enforce global concurrency."
Require-Text $auraCgPreloadScheduler "ownerRotation" "AuraCg preload starts must rotate fairly across owners."
Require-Text $auraCgPreloadScheduler "CapacityRejectedCount" "AuraCg preload overload must remain observable."
Require-Text $auraCgPreloadScheduler "public bool Complete\(string key\)" "AuraCg active preload claims must have an explicit completion path."
Require-Text $auraCgPreloadSubmission "Take\(maximum \+ 1\)" "AuraCg preload submission must probe only one item beyond its hard limit."
Require-Text $auraCgMediaCache "internal sealed class AuraCgMediaCache<TSprite, TBundle>" "AuraCg media references must have one explicit cache owner."
Require-Text $auraCgMediaCache "maximumEstimatedBytes" "AuraCg media retention must have an estimated-byte budget."
Require-Text $auraCgMediaCache "EstimatedBytes > maximumEstimatedBytes" "AuraCg media LRU must enforce its byte budget."
Require-Text $auraCgMediaCache "LinkedList<AuraCgMediaCacheEntry<TSprite, TBundle>> recency" "AuraCg media entries must share one global LRU order."
Require-Text $auraCgMediaRetentionLedger "ReferenceCount" "AuraCg media eviction must account for the same resource referenced by multiple cache entries."
Require-Text $auraCgMediaCacheModels "AuraCgReferenceComparer<T>" "AuraCg media accounting must use resource instance identity instead of Unity-style value equality."
Require-Text $auraCgMediaRetentionLedger "onSpriteReleased" "AuraCg media eviction must expose owned-resource release notifications."
Require-Text $auraCgMediaReleaseQueue "internal sealed class AuraCgMediaReleaseQueue<TSprite, TBundle>" "AuraCg Unity releases must stay in a testable deferred queue."
Require-Text $auraCgMediaReleaseQueue "isSpriteRetained" "AuraCg deferred release must recheck resources retained before the safe idle point."
Require-Text $auraCgMediaCacheKeys "internal static class AuraCgMediaCacheKeys" "AuraCg media cache keys must stay centralized."
Require-Text $auraCgMediaPathResolver "internal static class AuraCgMediaPathResolver" "AuraCg file and bundle media paths must stay in a pure resolver."
Require-Text $auraCgMediaPathResolver "OrderBy\(item => item, StringComparer\.OrdinalIgnoreCase\)" "AuraCg file sequences must keep deterministic frame order."
foreach ($pureSource in @($auraCgRegistryQuery, $auraCgNetworkPolicy, $auraCgNetworkSession, $auraCgRegisteredRequestResolver, $auraCgProviderCoordinator, $auraCgPlaybackClaims, $auraCgPlaybackCoordinator, $auraCgPresentationMath, $auraCgPresentationPolicy, $auraCgAdventurePreloadHistory, $auraCgPreloadScheduler, $auraCgPreloadSubmission, $auraCgMediaCache, $auraCgMediaCacheModels, $auraCgMediaRetentionLedger, $auraCgMediaReleaseQueue, $auraCgMediaCacheKeys, $auraCgMediaPathResolver)) {
    if ($pureSource -match "using UnityEngine|using Witch|PlayerManager|GameObject|MonoBehaviour") {
        throw "AuraCg policies, preload coordination, and media ownership must remain independent of Unity and Witch runtime state."
    }
}
Require-Text $auraCgComponentRuntime "finally[\s\S]*preloadScheduler\.Complete" "AuraCg preload claims must be released when a loading coroutine exits."
Require-Text $auraCgComponentRuntime "MaxPendingPreloads" "AuraCg runtime must configure a finite preload backlog."
Require-Text $auraCgComponentRuntime "MaxPendingPreloadsPerOwner" "AuraCg runtime must configure a per-owner preload backlog."
Require-Text $auraCgComponentRuntime "AuraCgPreloadSubmission<SkillCgRequest>\.Capture" "AuraCg preload dispatch must not materialize an unbounded producer enumerable."
Require-Text $auraCgComponentRuntime "inspected >= MaxPreloadSubmissionItems" "AuraCg component must bound direct reflection submissions before queue admission."
Require-Text $auraCgComponentRuntime "request\.PreloadProducerId = producerId" "AuraCg preload owner limits must be charged to the producer rather than the referenced content owner."
Require-Text $auraCgComponentRuntime "MaxConcurrentPreloads" "AuraCg runtime must configure finite preload concurrency."
Require-Text $auraCgComponentRuntime "TakeReady\(MaxPreloadStartsPerFrame\)" "AuraCg runtime must budget preload coroutine starts per frame."
Require-Text $auraCgComponentRuntime "if \(playbackCoordinator\.IsPlaying\)[\s\S]*return;[\s\S]*preloadScheduler\.TakeReady" "AuraCg runtime must not start new preloads during CG playback."
Require-Text $auraCgComponentRuntime "new AuraCgUnityMediaRepository" "AuraCg component must delegate media ownership to the Unity repository."
Require-Text $auraCgComponentRuntime "mediaRepository\.LoadSprite" "AuraCg component must delegate image loading to the Unity repository."
Require-Text $auraCgComponentRuntime "mediaRepository\.LoadSequenceSprites" "AuraCg component must delegate sequence loading to the Unity repository."
Require-Text $auraCgComponentRuntime "new AuraCgOverlayPresenter" "AuraCg component must delegate overlay ownership to the presenter."
Require-Text $auraCgComponentRuntime "overlayPresenter\.ShowImage" "AuraCg component must delegate image presentation to the presenter."
Require-Text $auraCgComponentRuntime "overlayPresenter\.ShowSequence" "AuraCg component must delegate sequence presentation to the presenter."
Require-Text $auraCgComponentRuntime "overlayPresenter\.PlayImage" "AuraCg component must keep Coroutine generation checks while delegating image animation."
Require-Text $auraCgComponentRuntime "overlayPresenter\.PlaySequence" "AuraCg component must keep Coroutine generation checks while delegating sequence animation."
Require-Text $auraCgComponentRuntime "OnDestroy\(\)[\s\S]*overlayPresenter\?\.Destroy" "AuraCg component teardown must release presenter-owned Unity resources."
Require-Text $auraCgComponentRuntime "new AuraCgNetworkRuntime" "AuraCg component must delegate multiplayer state and RPC orchestration to the network runtime."
Require-Text $auraCgComponentRuntime "AuraCgProviderCoordinator providerCoordinator" "AuraCg component must delegate provider registration and request collection to the provider coordinator."
Require-Text $auraCgComponentRuntime "providerCoordinator\.BuildRequests" "AuraCg component must delegate reflected provider request collection."
Require-Text $auraCgComponentRuntime "AuraCgRegisteredRequestResolver RegisteredRequestResolver" "AuraCg runtime must centralize registered request resolution."
Require-Text $auraCgComponentRuntime "RegisteredRequestResolver\.ResolveNetworkRequest" "AuraCg network runtime must receive the dedicated registered request resolver."
Require-Text $auraCgComponentRuntime "networkRuntime\.TryPrepareLocalPlaybackBatch" "AuraCg local synchronized playback must be prepared by the network runtime."
Require-Text $auraCgComponentRuntime "networkRuntime\.ApplyServerPlaybackRequest" "AuraCg server-bound playback must be delegated to the network runtime."
Require-Text $auraCgComponentRuntime "networkRuntime\.ApplyNetworkPlayback" "AuraCg received playback must be delegated to the network runtime."
Require-Text $auraCgComponentRuntime "networkRuntime\.ResetTransient" "AuraCg fight cleanup must reset network session state."
Require-Text $auraCgComponentRuntime "!playbackCoordinator\.IsPlaying && preloadScheduler\.ActiveCount == 0" "AuraCg runtime must provide the safe idle boundary for deferred Unity resource destruction."
Require-Text $auraCgUnityMediaRepository "internal sealed class AuraCgUnityMediaRepository" "AuraCg Unity media loading must stay in a dedicated repository."
Require-Text $auraCgComponentRuntime "ContentDirectories\[ownerModId\]\s*=\s*modConfig\.DirectoryName" "AuraCg must retain each content owner's mod directory for owner-qualified media fallback."
Require-Text $auraCgComponentRuntime "ResolveImagePath\(ownerModId, bundleId, bundleId\)" "AuraCg bundle fallback must resolve through the registered content owner."
Require-Text $auraCgUnityMediaRepository "ResolveAssetBundle\(request\.OwnerModId, request\.BundlePath\)" "AuraCg bundle loading and caching must remain owner-qualified."
Require-Text $auraCgUnityMediaRepository "InvalidateBundleMiss" "Late bundle registration must invalidate earlier negative fallback cache entries."
Require-Text $auraCgUnityMediaRepository "MaximumCacheEntries\s*=\s*512" "AuraCg Unity media repository must retain a finite entry budget."
Require-Text $auraCgUnityMediaRepository "MaximumCacheEstimatedBytes\s*=\s*256L \* 1024L \* 1024L" "AuraCg Unity media repository must retain its estimated-byte budget."
Require-Text $auraCgUnityMediaRepository "UnityWebRequestTexture\.GetTexture" "AuraCg file texture loading must stay in the Unity media repository."
Require-Text $auraCgUnityMediaRepository "AssetBundle\.LoadFromFile" "AuraCg bundle loading must stay in the Unity media repository."
Require-Text $auraCgUnityMediaRepository "cache\.ContainsSpriteReference" "AuraCg deferred release must recheck media retained before the safe idle point."
Require-Text $auraCgUnityMediaRepository "bundle\.Unload\(false\)" "AuraCg must release evicted owned bundle handles without invalidating already loaded assets."
if ($auraCgComponentRuntime -match "AuraCgMediaCache<Sprite, AssetBundle>|AuraCgMediaReleaseQueue<Sprite, AssetBundle>|UnityWebRequestTexture|DownloadHandlerTexture|AssetBundle\.LoadFromFile|private\s+IEnumerator\s+Load(Sprite|SequenceSprites)") {
    throw "AuraCgArbiterComponent must not regain Unity media repository responsibilities."
}
if ($auraCgComponentRuntime -match "private\s+(GameObject|Canvas|CanvasGroup|Image|Material|Sprite)\??\s+overlay|RenderMode\.ScreenSpaceOverlay|Shader\.Find\(|private\s+IEnumerator\s+(Fade|SlideRightToLeft|PlaySequenceFrames)") {
    throw "AuraCgArbiterComponent must not regain overlay presenter responsibilities."
}
if ($auraCgComponentRuntime -match "PlayerManager|FightPlayer|GameServer|SendRpcCommand|private\s+readonly\s+AuraCgPlaybackClaimStore|private\s+string\s+fightToken|private\s+long\s+localPlaybackCounter") {
    throw "AuraCgArbiterComponent must not regain network runtime or transient session responsibilities."
}
if ($auraCgComponentRuntime -match 'List<ProviderHandle>|class\s+ProviderHandle|GetMethod\("BuildRequests"|TryBuildRegisteredNetworkRequest|RegisteredMediaExists') {
    throw "AuraCgArbiterComponent must not regain provider reflection or registered network request resolution responsibilities."
}
if ($auraCgComponentRuntime -match "Dictionary<string, Sprite>\s+spriteCache|Dictionary<string, List<Sprite>>\s+sequenceCache|Dictionary<string, AssetBundle\?>\s+assetBundleCache|HashSet<string>\s+preloadKeys") {
    throw "AuraCgArbiterComponent must not regain private media or preload cache ownership."
}
$ownsPlaybackState = ($auraCgNonPlaybackCoordinatorSource -match "(?m)^\s*private\s+(readonly\s+)?List<QueuedRequest>\s+queue\s*=") -or
    ($auraCgNonPlaybackCoordinatorSource -match "(?m)^\s*private\s+(readonly\s+)?Dictionary<string, float>\s+recentKeys\s*=") -or
    ($auraCgNonPlaybackCoordinatorSource -match "(?m)^\s*private\s+int\s+playGeneration\s*[;=]") -or
    ($auraCgNonPlaybackCoordinatorSource -match "(?m)^\s*private\s+bool\s+playing\s*[;=]")
if ($ownsPlaybackState) {
    throw "AuraCgArbiterComponent must not regain playback queue, duplicate-window, generation, or active-loop state."
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

$terriasPreloader = Read-RepoText "Terrias-Dev\Hooks\TerriasResourcePreloader.cs"
Require-Text $terriasPreloader "AdventureStarting" "Terrias resource warmup must start from the adventure lifecycle."
Require-Text $terriasPreloader "AuraSharedFramePhase\.Background" "Terrias resource warmup must use the shared background frame phase."
Require-Text $terriasPreloader "battleActive" "Terrias resource warmup must pause during combat."
Require-Text $terriasPreloader "StarScoreHudAssets\.StructuralPaths" "Terrias warmup must cover first-use structural Star Score HUD sprites."
if ($terriasPreloader -match "PolymorphCardFaceCache\.GetOrCreate") {
    throw "Terrias warmup must not generate polymorph card faces on the preload path."
}
if ($terriasPreloader -match "TerriasResourceCache\.Preload<") {
    throw "Terrias resource warmup must not synchronously preload the whole visual catalog in one frame action."
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
Require-Text $cardUseFxRegistry "manifest-duplicate:" "Card-use FX registration must reject duplicate owner-qualified manifest ids."
Require-Text $cardUseFxRuntime "AuraCardLifecycleRouter" "Card-use FX must capture the real local card before native use processing."
Require-Text $cardUseFxRuntime "AuraCombatActionRouter" "Card-use FX must use the successful local action-animation commit boundary."
Require-Text $cardUseFxRuntime "LocalCommitted" "Card-use FX must distinguish local committed uses from remote observations."
Require-Text $cardUseFxRuntime 'FightUI\.DoCardUseAnimation' "Card-use FX bridge must scope the native central-card animation."
Require-Text $cardUseFxRuntime 'ICard\.SetCardStyle' "Card-use FX bridge must capture the nested native central clone."
Require-Text $cardUseFxRuntime "DedupeSeconds" "Card-use FX presentation triggers must have bounded duplicate suppression."
Require-Text $cardUseFxRuntime "AuraCardUseFxSourceSnapshot" "Card-use FX must snapshot its source before native burn or throw destroys the card view."
Require-Text $cardUseFxRibbon "raycastTarget = false" "Shared Bezier ribbons must never intercept UI input."
Require-Text $cardUseFxRibbon "ConfigureStrands" "Shared Bezier ribbons must expose semantic-free parallel strand geometry."
Require-Text $cardUseFxRibbon "EvaluateTangent" "Shared Bezier ribbons must expose path sampling for consumer-owned moving glyphs."
if ($cardUseFxRuntime.Contains("Terrias")) {
    throw "Shared card-use FX runtime must not contain Terrias content semantics."
}

$lifecycleSession = Read-RepoText "AuraSharedCore\AuraLifecycleSessionRuntime.cs"
Require-Text $lifecycleSession "BeginBattleSession" "Shared lifecycle session runtime must own battle session start."
Require-Text $lifecycleSession "RestartBattleSession" "Shared lifecycle session runtime must advance the battle epoch when FightInit.Init restarts an active fight."
Require-Text $lifecycleSession "EndBattleSession" "Shared lifecycle session runtime must own battle session end."

$cardPresentationDelta = Read-RepoText "AuraSharedCore\AuraCardPresentationDelta.cs"
Require-Text $cardPresentationDelta "TrySetCost" "Shared card presentation deltas must expose a cost-only refresh path."
if ($cardPresentationDelta -match "Terrias|AuraTools") {
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
Require-Text $sharedResourceCache "EstimatedBytes" "Shared resource cache statistics must expose approximate retained memory."
Require-Text $sharedResourceCache "EstimateObjectBytes" "Shared resource cache must centralize approximate Unity resource sizing."

$sharedFrameScheduler = Read-RepoText "AuraSharedCore\AuraSharedFrameScheduler.cs"
Require-Text $sharedFrameScheduler "SoftPendingActionLimit" "Shared frame scheduler must expose a soft backlog waterline."
Require-Text $sharedFrameScheduler "MaxPromotionsPerFrame" "Shared frame scheduler must bound queue promotion work per frame."
Require-Text $sharedFrameScheduler "AuraSharedFrameSchedulerStats" "Shared frame scheduler must expose backlog and pump diagnostics."
Require-Text $sharedFrameScheduler "PendingByOwner" "Shared frame scheduler diagnostics must attribute backlog to owners."
Require-Text $sharedFrameScheduler "Actions are retained" "Shared frame scheduler soft limits must not silently drop queued work."

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
if ($sharedUiTheme -match "Terrias|AuraToolsExp") {
    throw "AuraUiShared must not own consumer-specific style ids."
}
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
    "AuraGameDataShared",
    "AuraDirectorDetour-Dev",
    "AuraJourneyShared",
    "AuraLogShared",
    "AuraModeShared",
    "AuraOnlineShared",
    "AuraRoleShared",
    "AuraSharedCore",
    "AuraSkinShared",
    "AuraUiShared",
    "StarterDeckArbiterShared",
    "UiRaycastSafetyShared",
    "UiTransitionGuardShared"
)

$gameDataModels = Read-RepoText "AuraGameDataShared\AuraGameDataModels.cs"
$gameDataCatalog = Read-RepoText "AuraGameDataShared\AuraGameDataCatalog.cs"
$gameDataFieldPolicy = Read-RepoText "AuraGameDataShared\AuraGameDataFieldPolicy.cs"
$gameDataApplication = Read-RepoText "AuraGameDataShared\Application\AuraGameInstanceServices.cs"
$gameDataHost = Read-RepoText "AuraGameDataShared\GameApi\AuraGameDataHostApi.cs"
foreach ($required in @("SchemaVersion = 5", "OwnerModId", "WriterId", "UserManual", "Registered", "Default", "Native", "StorageKind", "OwnerRules", "CatalogEpoch", "NativeReady", "IsComplete", "AwaitingNativeCapture")) {
    Require-Text $gameDataModels ([regex]::Escape($required)) "AuraGameDataShared must expose v5 identity, provenance, and generation state: $required"
}
foreach ($required in @("Register", "RegisterOwnerRules", "Patch", "Retire", "QueryHistory", "ValidateHandle", "AcquireSnapshot", "TryGet", "GetTable", "TryResolveUniqueType", "AuraGameDataCatalogCompiler")) {
    Require-Text $gameDataCatalog ([regex]::Escape($required)) "AuraGameDataShared v5 catalog is missing its compiled CRUD/search contract: $required"
}
foreach ($required in @("IAuraCardInstancePort", "IAuraRelicInstancePort", "AuraCardInstanceService", "AuraRelicInstanceService", "Authoritative")) {
    Require-Text $gameDataApplication ([regex]::Escape($required)) "AuraGameDataShared application layer is missing an aggregate boundary: $required"
}
foreach ($required in @("Capture", "PatchVars", "Materialize", "CloneWritable", "RegisterNativeOwnershipV5", "CopyTableForHostInterop", "IDataConfig", "GameConfigManager")) {
    Require-Text $gameDataHost ([regex]::Escape($required)) "AuraGameDataShared Witch adapter is missing: $required"
}
Require-Text $gameDataCatalog "!snapshot\.Version\.NativeReady" "AuraGameDataShared must reject incomplete native generations at the atomic publish boundary."
Require-Text $gameDataCatalog "AuraGameDataFieldPolicy\.IsScriptField" "AuraGameDataShared catalog must use the shared script-field policy."
Require-Text $gameDataHost "AuraGameDataFieldPolicy\.IsIdentityOrScriptField" "AuraGameDataShared Witch adapter must use the shared identity/script-field policy."
foreach ($required in @('LastIndexOf\("Script"', 'suffixStart', "fieldName\[index\] < '0'")) {
    Require-Text $gameDataFieldPolicy $required "AuraGameDataShared field policy must recognize Script columns and numbered Script suffixes without matching Description."
}
if ($gameDataCatalog -match 'IndexOf\("Script"' -or $gameDataHost -match 'IndexOf\("Script"') {
    throw "AuraGameDataShared must not classify fields by any Script substring because that blocks Description fields."
}
Require-Text $gameDataHost "if \(!request\.Source\.IsComplete" "AuraGameDataShared Witch adapter must not compile an unfinished cooperative capture."
if ($gameDataHost -match "public void Invalidate\(\)[\s\S]{0,500}cached\s*=\s*null") {
    throw "AuraGameDataShared native invalidation must preserve the last-good capture while a replacement is built."
}
if ($gameDataApplication -match "GameConfigManager|DataConfig|ScriptExecutor|FightCardManager|RoleTable") {
    throw "AuraGameDataShared Application must depend on ports and snapshots, not Witch runtime types."
}

$sunConfigIndex = Read-RepoText "Terrias-Dev\Mechanics\TerriasConfigIndex.cs"
$toolsRoleCatalog = Read-RepoText "AuraToolsExp-Dev\Infrastructure\RoleCatalog.cs"
$toolsStarterDeckCatalog = Read-RepoText "AuraToolsExp-Dev\Features\StarterDeck\StarterDeckCardCatalog.cs"
foreach ($consumer in @($sunConfigIndex, $toolsRoleCatalog, $toolsStarterDeckCatalog)) {
    Require-Text $consumer "AuraGameDataHostApi" "Shared game-data catalog consumers must delegate to AuraGameDataHostApi."
}
if ($sunConfigIndex -match "GameConfigManager" -or $toolsRoleCatalog -match "GameConfigManager") {
    throw "Shared game-data consumers must not restore private table scans."
}

foreach ($consumerRoot in @("Terrias-Dev", "AuraToolsExp-Dev", "SanGuoShaExp-Dev", "AuraJourneyShared", "AuraSkinShared")) {
    $consumerPath = Join-Path $repoRoot $consumerRoot
    foreach ($file in Get-ChildItem -LiteralPath $consumerPath -Recurse -Filter "*.cs" -File) {
        $text = Get-Content -Raw -LiteralPath $file.FullName
        $usesPrivateGameDataLookup =
            ($text -match "\bGetOne\s*\(|GetOneById\s*\(|GetTypeById\s*\(") -or
            ($text -match "GameConfigManager[^\r\n]{0,120}GetTable\s*\(") -or
            ($text -match "\.Instance\.GetTable\s*\(")
        if ($usesPrivateGameDataLookup) {
            throw "Main consumers must query game definitions through AuraGameDataShared: $($file.FullName)"
        }

        if ($text -match "new\s+DataConfig\s*\(") {
            throw "Main consumers must materialize IDataConfig instances through AuraGameDataShared GameApi: $($file.FullName)"
        }
    }
}

$resourceCacheText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedResourceCache.cs")
foreach ($required in @("MaximumEntries", "MaximumReferences", "MaximumEntriesPerOwner", "MaximumReferencesPerOwner", "LinkedList<string>", "EnforceLimitsNoLock", "RemoveEntryNoLock", "EstimatedBytes")) {
    Require-Text $resourceCacheText ([regex]::Escape($required)) "Shared resource cache must enforce bounded global/owner LRU retention: $required"
}

$modeRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraModeShared\AuraModeRuntime.cs")
foreach ($required in @("RegisterMode", "ActivateMode", "DeactivateMode", "EvaluateStarterDeckMutation", "expectedRevision", "PoliciesEquivalent")) {
    Require-Text $modeRuntimeText ([regex]::Escape($required)) "AuraMode shared runtime is missing its declarative policy/lifecycle contract: $required"
}

$rawWriteAllowed = @(
    "AuraSharedCore\AuraSharedStorageCoordinator.cs",
    "AuraSharedCore\AuraSharedPackageCoordinator.cs",
    "AuraSharedCore\AuraSharedRegistrationCoordinator.cs",
    "AuraSharedCore\AuraSharedEditableResourceCoordinator.cs",
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

$resourceV4 = Read-RepoText "docs\aura-shared-resource-v4-contract.md"
Require-Text $resourceV4 "registration-only protocol" "Shared v4 guidelines must require registration."
Require-Text $resourceV4 "moduleId/scopeType/canonicalScopeId/featureId/ownerModId/resourceId/content" "Shared v4 guidelines must define the canonical module-owned layout."
Require-Text $resourceV4 "History view" "Shared v4 guidelines must define independent history visibility."
Require-Text $resourceV4 "UserManual" "Shared v4 guidelines must define manual resource provenance."

$auditFile = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs\Terrias") -File -Filter "04-Aura*.md")
if ($auditFile.Count -ne 1) {
    throw "Expected exactly one Terrias Aura shared-layer audit document."
}
$audit = [System.IO.File]::ReadAllText($auditFile[0].FullName)
Require-Text $audit "AuraCgShared" "Shared architecture audit must include AuraCgShared."
Require-Text $audit "provider identity" "Shared architecture audit must include provider identity findings."

$journeyReadme = Read-RepoText "AuraJourneyShared\README.md"
Require-Text $journeyReadme "ownerModId:localJourneyId" "AuraJourneyShared README must document owner-qualified JourneyId."
Require-Text $journeyReadme "QualifyJourneyId" "AuraJourneyShared README must document JourneyId normalization."

Write-Host "Shared architecture guideline scan passed."
