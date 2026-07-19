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

    return (($files | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join [Environment]::NewLine)
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

    Assert-True $Text.Contains($Needle) $Message
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

$auraEntry = Read-RepoText "AuraToolsExp-Dev\Entry.cs"
$auraAuthority = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcAuthorityRuntime.cs"
$auraSender = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcSender.cs"
$sharedAuthority = Read-RepoText "AuraSharedCore\AuraRpcAuthorityRuntime.cs"
$auraPayloadGuard = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcPayloadGuard.cs"
$auraTransport = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcTransport.cs"
$damageCommands = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterCommands.cs"
$damageRuntime = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterNetworkRuntime.cs"
$damagePolicy = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterAuthorityPolicy.cs"
$modSyncCommand = Read-RepoText "AuraToolsExp-Dev\Features\ModSync\AuraToolsModSyncManifestCommand.cs"
$modSyncRuntime = Read-RepoText "AuraToolsExp-Dev\Features\ModSync\AuraToolsModSyncRuntime.cs"
$loggingConfig = Read-RepoText "AuraToolsExp-Dev\Config\AuraToolsLoggingSettings.cs"
$fileLogging = Read-RepoText "AuraToolsExp-Dev\Features\Logging\AuraToolsFileLogRuntime.cs"
$sunEntry = Read-RepoText "SunExp-Dev\Entry.cs"
$sunAuthority = Read-RepoText "SunExp-Dev\Network\SunExpRpcAuthorityRuntime.cs"
$fieldNetworkSync = Read-RepoText "SunExp-Dev\Network\FieldNetworkSync.cs"
$fieldActivationIntents = Read-RepoText "SunExp-Dev\Mechanics\FieldActivationIntentCatalog.cs"
$constellationRpc = Read-RepoText "SunExp-Dev\Network\RpcConstellationStateCommit.cs"
$constellationService = Read-RepoText "SunExp-Dev\Mechanics\ConstellationService.cs"
$statusOwnershipPolicy = Read-RepoText "SunExp-Dev\Network\SunExpStatusOwnershipPolicy.cs"
$projectionRpc = Read-RepoText "SunExp-Dev\Network\RpcProjectionCompanion.cs"
$projectionSummon = Read-RepoText "SunExp-Dev\Mechanics\ProjectionSummonService.cs"
$projectionOtherObj = Read-RepoText "SunExp-Dev\Mechanics\ProjectionOtherObj.cs"
$endlessSeaNetworkSync = Read-RepoText "SunExp-Dev\Network\EndlessSeaNetworkSync.cs"
$endlessAbyssEvacuationRpc = Read-RepoText "SunExp-Dev\Network\RpcEndlessAbyssEvacuation.cs"
$endlessSeaMapPresenter = Read-RepoText "SunExp-Dev\Hooks\Ui\EndlessSeaMapViewPresenter.cs"
$sharedPayloadBudget = Read-RepoText "AuraSharedCore\AuraSharedPayloadBudget.cs"
$roleCommit = Read-RepoText "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"
$roleCommitApi = Read-RepoText "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"
$sunSkillCgRuntime = Read-RepoText "SunExp-Dev\Features\SkillCg\SunExpSkillCgRuntime.cs"
$auraCgRuntime = Read-RepoSourceTree "AuraCgShared"
$audioArbiter = Read-RepoSourceTree "AudioArbiterShared"
$audioComponent = Read-RepoText "AudioArbiterShared\AudioArbiterRuntime.cs"
$audioNetworkPolicy = Read-RepoText "AudioArbiterShared\AudioNetworkPolicy.cs"
$audioNetworkSession = Read-RepoText "AudioArbiterShared\AudioNetworkSessionState.cs"
$audioNetworkRuntime = Read-RepoText "AudioArbiterShared\AudioNetworkRuntime.cs"

Assert-Contains $auraEntry "AuraToolsRpcAuthorityRuntime.Initialize(modConfig)" "AuraToolsExp Entry must initialize RPC authority binding."
Assert-Contains $sharedAuthority "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase" "Shared RPC authority must bind the user-code receive hook."
Assert-Contains $sharedAuthority "PlayerManager.CmdReceiveRpcCommand" "Shared RPC authority must bind the generated receive hook."
Assert-Contains $auraAuthority "AuraRpcAuthorityRuntime.Register" "AuraTools RPC authority must delegate hook binding to shared RPC authority."
Assert-Contains $auraAuthority "IAuraToolsServerBoundRpcCommand" "AuraTools RPC authority must only bind server-bound commands."
Assert-Contains $auraSender "public interface IAuraToolsServerBoundRpcCommand" "AuraTools server-bound command interface must be public for serializable public commands."

Assert-Contains $auraPayloadGuard "MirrorStringLimitBytes = 65534" "AuraTools payload guard must document Mirror's hard string byte limit."
Assert-Contains $auraPayloadGuard "DefaultSoftLimitBytes = 56000" "AuraTools payload guard must keep a soft budget below Mirror's hard limit."
Assert-Contains $auraPayloadGuard "Encoding.UTF8.GetByteCount" "AuraTools payload guard must measure payloads by UTF-8 bytes, not character count."
Assert-Contains $auraTransport "public static bool Send(" "AuraTools transport must expose a unified RPC send entry."
Assert-Contains $auraTransport "bytes > SoftLimitBytes" "AuraTools transport must block oversized payloads before Mirror serialization."
Assert-Contains $auraTransport "public static bool SendJsonChunksAsync" "AuraTools transport must expose a chunked JSON send path for large payloads."
Assert-Contains $auraTransport "AuraSharedBackgroundWorkScheduler.Queue" "AuraTools chunk preparation must use the bounded shared background scheduler."
Assert-Contains $auraTransport "AuraSharedFrameStepRunner.Run" "AuraTools chunk sends must return to the budgeted shared frame scheduler."
Assert-NotContains $auraTransport "ThreadPool.QueueUserWorkItem" "AuraTools must not bypass shared background-work concurrency limits."
Assert-NotContains $auraTransport "AuraToolsRpcTransportDispatcher" "AuraTools must not keep a retired private main-thread dispatcher."
Assert-Contains $auraTransport "source=" "AuraTools transport logs must identify the sending source."
Assert-Contains $auraTransport "command=" "AuraTools transport logs must identify the RPC command type."
Assert-Contains $audioArbiter "RpcAudioPresentationRequest" "Card-use audio must expose a client-to-host presentation request."
Assert-Contains $audioArbiter "IAudioArbiterServerBoundRpcCommand" "Card-use audio requests must bind the actual server sender."
Assert-Contains $audioArbiter "SenderOwnsStatus" "Card-use audio host relay must validate sender ownership of the submitted status."
Assert-Contains $audioArbiter "Client card-use presentation submitted to host" "Client-owned card use must be submitted instead of waiting for host observation."
Assert-Contains $audioArbiter "CreatedAtUtcTicks" "Card-use audio presentation events must carry an expiry timestamp."
Assert-Contains $audioNetworkPolicy "ValidateServerCardUsePresentation" "Card-use audio sender validation must stay in the network policy."
Assert-Contains $audioNetworkPolicy "senderOwnsStatus(sender.PlayerId, request.StatusInstanceId)" "Card-use audio validation must authorize the bound sender against the submitted status."
Assert-Contains $audioNetworkPolicy "ValidateLocalPresentationIdentity" "Card-use audio must reject ambiguous local issuer or owner identity in multiplayer."
Assert-Contains $audioNetworkPolicy "expired presentation" "Card-use audio host validation must reject expired client requests before renewing their timestamp."
Assert-Contains $audioNetworkSession "receivedEventIds.Clear" "Card-use audio presentation dedupe must clear at battle start."
Assert-Contains $audioNetworkSession "receivedEventOrder.Count > maximumPlaybackClaims" "Card-use audio presentation dedupe must remain bounded."
Assert-Contains $audioNetworkSession "ReuseOrCreateLocalPlayId" "Card-use audio local action identity must stay in fight-scoped session state."
Assert-Contains $audioNetworkRuntime "AudioNetworkSenderSnapshot" "Card-use audio host validation must project the actual bound sender."
Assert-Contains $audioNetworkRuntime "PlayerManager.Instance" "Audio RPC transport must stay in the dedicated network adapter."
Assert-Contains $audioNetworkRuntime "public void RegisterAuthority" "Audio RPC authority initialization must stay in the network adapter."
Assert-Contains $audioNetworkRuntime "AuraRpcAuthorityRuntime.Register" "Audio network adapter must bind sender authority through the shared runtime."
Assert-Contains $audioComponent "networkRuntime.RegisterAuthority" "Audio component must delegate sender-authority initialization."
Assert-NotContains $audioComponent "AuraRpcAuthorityRuntime.Register" "Audio component must not register RPC receive hooks directly."
Assert-Contains $audioComponent "networkRuntime.ApplyServerCardUsePresentation" "Audio component must delegate host authorization and relay."
Assert-NotContains $audioComponent "SendRpcCommand" "Audio component must not own RPC transport after network runtime extraction."
Assert-Contains $audioArbiter "AudioArbiterRuntime.ReceiveRemote(Event)" "Audio RPC playback must enter the remote dedupe boundary."
Assert-Contains $audioArbiter "RpcAudioFightSession" "Audio presentation claims must be isolated by a host-issued fight session."
Assert-Contains $audioNetworkRuntime "MaximumPlaybackClaims = 512" "Audio presentation dedupe must remain bounded."
Assert-Contains $audioArbiter "PlayRemoteReplacementFallback" "Remote replacement audio must have a bounded native-pairing fallback."
Assert-Contains $audioArbiter "AudioReplacementCoordinator" "Remote replacement state must use the dedicated coordinator."
Assert-Contains $audioArbiter "TryClaimPairedFallback" "Remote replacement fallback must consume an event-id pairing claim."
Assert-Contains $audioArbiter "fallback-original-suppressed" "A late native original must be suppressed after remote fallback playback."

$auraToolsSourceRoot = Join-Path $repoRoot "AuraToolsExp-Dev"
$repoRootFull = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$rawAuraSends = Get-ChildItem -LiteralPath $auraToolsSourceRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notlike "*\Infrastructure\AuraToolsRpcTransport.cs" } |
    ForEach-Object {
        $relative = $_.FullName
        if ($relative.StartsWith($repoRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $relative.Substring($repoRootFull.Length)
        }

        $text = Get-Content -Raw -LiteralPath $_.FullName
        if ([regex]::IsMatch($text, "\.SendRpcCommand(?:ExcludeOwner)?\s*\(")) {
            $relative
        }
    }
Assert-True (($rawAuraSends | Measure-Object).Count -eq 0) ("AuraToolsExp modules must not bypass AuraToolsRpcTransport. Raw senders: " + ($rawAuraSends -join ", "))

Assert-Matches $damageCommands "DamageMeterSubmitCommand\s*:\s*RpcCommandBase,\s*IAuraToolsServerBoundRpcCommand" "DamageMeter submit command must receive server-bound sender context."
Assert-Matches $damageCommands "DamageMeterControlCommand\s*:\s*RpcCommandBase,\s*IAuraToolsServerBoundRpcCommand" "DamageMeter control command must receive server-bound sender context."
Assert-Matches $damageCommands "DamageMeterSnapshotCommand\s*:\s*RpcCommandBase,\s*IAuraToolsServerBoundRpcCommand" "DamageMeter snapshot command must receive server-bound sender context."
Assert-Contains $damageCommands "AcceptOnServer(Candidate, serverSender" "DamageMeter submit must pass server-bound sender into server acceptance."
Assert-Contains $damageCommands "ApplyControlOnServer(this, serverSender" "DamageMeter control must pass server-bound sender into server control."
Assert-Contains $damageCommands "TryCreateServerSnapshot(serverSender" "DamageMeter snapshot must authorize snapshot requests by sender."

Assert-Contains $damagePolicy "TryBindReporter" "DamageMeter policy must bind reporter identity from sender."
Assert-Contains $damagePolicy "RequireHostControl" "DamageMeter policy must centralize host-control authorization."
Assert-Contains $damagePolicy "RequireLobbyMember" "DamageMeter policy must centralize lobby membership authorization."
Assert-Contains $damageRuntime "DamageMeterAuthorityPolicy.TryBindReporter" "DamageMeter runtime must bind reporter identity before validation."
Assert-Contains $damageRuntime "DamageMeterAuthorityPolicy.RequireHostControl" "DamageMeter runtime must require host sender for control commands."
Assert-Contains $damageRuntime "DamageMeterAuthorityPolicy.RequireLobbyMember" "DamageMeter runtime must require lobby sender for snapshots."
Assert-NotContains $damageRuntime "IsHostIdentity(command.IssuerPlayerId)" "DamageMeter runtime must not trust payload IssuerPlayerId for host authorization."
Assert-NotContains $damageRuntime "LobbyContains(value.ReporterPlayerId)" "DamageMeter runtime must not trust payload ReporterPlayerId for lobby authorization."
Assert-Contains $damageCommands "EnsureControlResponseFits(this)" "DamageMeter control responses must be budget-checked before broadcast."
Assert-Contains $damageCommands "EnsureSnapshotResponseFits(this)" "DamageMeter snapshot responses must be budget-checked before broadcast."
Assert-Contains $damageRuntime "snapshot.History = new List<DamageFightRecord>()" "DamageMeter network snapshots must not synchronize full fight history."
Assert-Contains $damageRuntime "CompactNetworkSnapshot" "DamageMeter network snapshots must support tiered compaction."
Assert-Contains $damageRuntime "MinimizeNetworkSnapshot" "DamageMeter network snapshots must have a minimal fallback."
Assert-Contains $damageRuntime "CreateStatusOnlySnapshot" "DamageMeter oversized responses must be able to fall back to status-only snapshots."

Assert-Contains $modSyncCommand "HostManifestChunked" "ModSync manifest command must announce chunked host manifests."
Assert-Contains $modSyncCommand "AuraToolsModSyncManifestChunkCommand" "ModSync must define a dedicated manifest chunk RPC."
Assert-Contains $modSyncCommand "TrySendHostModManifestChunks" "ModSync oversized host manifests must switch to chunked transfer."
Assert-Contains $modSyncRuntime "MaxManifestTransferBytes" "ModSync chunked transfers must have a total byte budget."
Assert-Contains $modSyncRuntime "MaxManifestActiveTransfers" "ModSync chunked transfers must bound active receiver buffers."
Assert-Contains $modSyncRuntime "ReceiveHostModManifestChunk" "ModSync must reassemble host manifest chunks."
Assert-Contains $modSyncRuntime "Sha256Hex(payload)" "ModSync reassembly must verify payload checksum."
Assert-Contains $modSyncRuntime "PruneExpiredManifestChunkBuffers" "ModSync must expire stale chunk buffers."

Assert-Contains $loggingConfig "MaxRetainedLogFiles" "AuraTools logging settings must expose log retention."
Assert-Contains $fileLogging "PruneOldLogFiles" "AuraTools file logging must prune old log files."

Assert-Contains $sunEntry "SunExpRpcAuthorityRuntime.Initialize(modConfig)" "SunExp Entry must initialize RPC authority binding."
Assert-Contains $sharedAuthority "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase" "Shared RPC authority must bind the user-code receive hook."
Assert-Contains $sharedAuthority "PlayerManager.CmdReceiveRpcCommand" "Shared RPC authority must bind the generated receive hook."
Assert-Contains $sunAuthority "AuraRpcAuthorityRuntime.Register" "SunExp RPC authority must delegate hook binding to shared RPC authority."
Assert-Contains $sunAuthority "ISunExpServerBoundRpcCommand" "SunExp RPC authority must only bind server-bound commands."
Assert-Contains $sunAuthority "CreateLocalServerSender" "SunExp RPC authority must expose a local host sender for direct server paths."
Assert-Contains $sunAuthority "public interface ISunExpServerBoundRpcCommand" "SunExp server-bound command interface must be public for serializable public commands."
Assert-Contains $constellationService "SunExpStatusOwnershipPolicy.SenderOwnsStatus" "Constellation light-up requests must use centralized sender-bound status ownership validation."
Assert-Contains $constellationService "SyncDomain.TryClaimToken(sender.PlayerId, token)" "Constellation light-up requests must suppress duplicate sender commands."
Assert-Contains $constellationRpc "ConstellationService.TryResolveLightUpRequest" "Constellation clients must submit an increment request instead of an absolute level snapshot."
Assert-NotContains $constellationRpc "snapshot.Level > ConstellationService.Level" "Constellation authority must not validate a client-provided absolute level."
Assert-Matches $constellationRpc "RpcConstellationRosterSnapshot\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Constellation roster snapshots must be server-bound."
Assert-Matches $constellationRpc "RpcConstellationRoundReward\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Constellation round rewards must be host-authorized."
Assert-Contains $constellationService "sender.IsLobbyHost" "Constellation team rewards must validate the lobby host publisher."
Assert-Contains $constellationService "ApplyRoundReward" "Constellation team rewards must land through the owner-local application path."
Assert-Contains $statusOwnershipPolicy "string.Equals(playerId, ownerStatusId, StringComparison.Ordinal)" "Status ownership must accept the native player-status identity used in multiplayer."
Assert-Contains $statusOwnershipPolicy "RoleStatusMap" "Status ownership must retain the native role-status mapping fallback."
Assert-Matches $fieldNetworkSync "RpcFieldStateRequest\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Field state requests must receive server-bound sender context."
Assert-Matches $fieldNetworkSync "RpcFieldStateSnapshot\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Field snapshots must receive server-bound host authority."
Assert-Contains $fieldNetworkSync "BindServerSender" "Field state requests must bind server sender before CmdExecute."
Assert-Contains $fieldNetworkSync "ValidateRequest" "Field state requests must validate host authority before applying field state."
Assert-Contains $fieldNetworkSync "AuraAuthoritativeSyncRuntime.RegisterDomain" "Field state sync must use shared authoritative sync services for tokens and sessions."
Assert-Contains $fieldNetworkSync "SyncDomain.TryClaimToken" "Field state sync must use shared duplicate suppression."
Assert-Contains $fieldNetworkSync "SyncDomain.TryClaimToken(sender.PlayerId, token)" "Field token suppression must be scoped by server-bound sender identity."
Assert-Matches $projectionRpc "RpcProjectionSummonRequest\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Projection summon requests must receive server-bound sender context."
Assert-Contains $projectionRpc "ProtocolVersion" "Projection synchronization must carry an explicit protocol version."
Assert-Contains $projectionRpc "BattleEpoch" "Projection synchronization must reject snapshots from another battle."
Assert-Contains $projectionRpc "RegistryHash" "Projection synchronization must verify the intent registry contract."
Assert-Contains $projectionSummon "preferredOwnerPlayerId: sender.PlayerId" "Projection ownership must bind to the actual network sender."
Assert-Contains $projectionSummon "ShowRejectionCaption(snapshot.RejectionReason)" "Projection rejection snapshots must localize protocol reasons at the presentation boundary."
Assert-NotContains $projectionSummon "+ snapshot.RejectionReason" "Projection protocol reasons must not be exposed directly to players."
Assert-NotContains $projectionSummon "ProjectionBuffCopyService.HydrateExact" "Owner-bound projections must not hydrate copied player buffs."
Assert-Contains $projectionRpc "CompanionAuthorityService.ProjectionProtocolVersion" "Projection attachment semantics must use the centralized versioned network contract."
Assert-Contains $projectionSummon "ProjectionStateStore.FindByOwner" "Projection snapshots must preserve the one-projection-per-player invariant on clients."
Assert-Contains $projectionSummon "CompanionThreatService.ApplyAuthoritative" "Projection clients must not recalculate authoritative threat."
Assert-Contains $projectionOtherObj "!CompanionAuthorityService.IsAuthoritative()" "Projection clients must not execute companion turns locally."
Assert-Contains $fieldNetworkSync "OwnerStatusId" "Field activation requests must identify the requesting status owner."
Assert-Contains $fieldNetworkSync "ValidateActivateIntent" "Field activation requests must use server-resolved capabilities."
Assert-Contains $fieldNetworkSync "BattleSessionId" "Field requests must carry a host-issued battle session identity."
Assert-Contains $fieldNetworkSync "hostBattleSessionId" "Field host must reject intents from an earlier fight session."
Assert-Contains $fieldNetworkSync "SunExpStatusOwnershipPolicy.SenderOwnsStatus" "Field activation must validate the bound sender through the centralized status ownership policy."
Assert-Contains $fieldNetworkSync "FieldActivationIntentCatalog.TryResolve" "Field activation must resolve a declared field-and-intent capability on the host."
Assert-Contains $fieldNetworkSync "definition.FixedAmount" "Field activation must use the host-declared amount instead of the client-submitted amount."
Assert-Contains $fieldNetworkSync "ActivationResolutionFailed" "Field activation must reject an intent whose authoritative amount cannot be resolved."
Assert-Contains $fieldNetworkSync "request rejected: code=" "Rejected field requests must emit a structured host diagnostic."
Assert-Contains $fieldNetworkSync "activation accepted: code=Accepted(0)" "Accepted field activations must emit a structured host diagnostic."
Assert-Contains $fieldActivationIntents "SunExpFieldId.MoonDomain, ColumbinaHomesicknessIntent, 1" "Columbina Homesickness must authorize exactly one Moon Domain stack."
Assert-Contains $fieldActivationIntents 'ColumbinaHomesicknessIntent = "Columbina.Homesickness"' "The Moon Domain capability id must match Columbina's skill source."
Assert-Contains $fieldActivationIntents "StringComparer.Ordinal" "Field activation intent ids must use ordinal identity matching."
Assert-NotContains $fieldNetworkSync 'field != SunExpFieldId.ScorchingCanopy' "Field authorization must not hard-code Scorching Canopy as the only client-activatable field."
Assert-NotContains $fieldNetworkSync "FieldNetworkCommandKind.Set" "Remote field requests must not expose arbitrary set-state commands."
Assert-NotContains $fieldNetworkSync "FieldNetworkCommandKind.Clear" "Remote field requests must not expose arbitrary clear-state commands."

Assert-Contains $sharedPayloadBudget "Encoding.UTF8.GetByteCount" "Shared payload budgets must measure UTF-8 serialized bytes."
Assert-Contains $sharedPayloadBudget "MirrorStringLimitBytes = 65534" "Shared payload budgets must document Mirror's hard string limit."
Assert-Matches $endlessSeaNetworkSync "RpcEndlessSeaStateSnapshot\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Endless Sea snapshots must receive server-bound sender context."
Assert-Matches $endlessSeaNetworkSync "RpcEndlessSeaStateSnapshotRequest\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Endless Sea repair requests must receive server-bound sender context."
Assert-Contains $endlessSeaNetworkSync "serverSender.IsLobbyHost" "Endless Sea snapshots must reject non-host senders."
Assert-Contains $endlessSeaNetworkSync "CaptureAuthoritative(includePlan)" "Endless Sea host must capture its own authoritative state rather than trust a client snapshot."
Assert-Contains $endlessSeaNetworkSync "AuraSharedPayloadBudget.FitsSoftLimit" "Endless Sea snapshots must respect transport payload budgets."
Assert-Contains $endlessSeaNetworkSync "TryGetCachedPlan" "Endless Sea clients must cache host-provided floor plans for projection only."
Assert-Contains $endlessSeaMapPresenter "SunExpNetworkRuntime.IsClientOnly()" "Endless Sea clients must not generate missing floor plans locally."
Assert-Contains $endlessSeaMapPresenter "EndlessSeaNetworkSync.RequestSnapshot" "Endless Sea clients must repair a missing host floor plan through a snapshot request."
Assert-Matches $endlessAbyssEvacuationRpc "RpcEndlessAbyssEvacuation\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Endless Abyss evacuation must receive server-bound sender context."
Assert-Contains $endlessAbyssEvacuationRpc "serverSender.IsLobbyHost" "Endless Abyss evacuation must reject non-host publishers."
Assert-Contains $endlessAbyssEvacuationRpc "TryCaptureStored(RequestedToken" "Endless Abyss evacuation must resolve the result from server-stored state."
Assert-Contains $endlessAbyssEvacuationRpc "SyncDomain.TryClaimToken(senderId, commandToken)" "Endless Abyss evacuation must suppress duplicate host commands through the shared authoritative domain."
Assert-Contains $endlessAbyssEvacuationRpc "AuraSharedPayloadBudget.FitsSoftLimit" "Endless Abyss evacuation responses must respect the shared RPC payload budget."
Assert-NotContains $endlessAbyssEvacuationRpc "Resolution = resolution" "Endless Abyss evacuation must not trust a payload-provided resolution."

Assert-Matches $roleCommit "RpcSolarMemoryRoleCommit\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Solar Memory role commit must receive server-bound sender context."
Assert-Contains $roleCommit "ApplyOnServer(Role, Source, serverSender, remoteRpc: true)" "Remote Solar Memory role commit must execute as a bound RPC."
Assert-Contains $roleCommit "ValidateSender(role, source, sender, remoteRpc)" "Solar Memory role commit must validate sender before committing."
Assert-Contains $roleCommit "string.Equals(role.Id, sender.PlayerId, StringComparison.Ordinal)" "Solar Memory role commit must reject sender/role mismatches."
Assert-Contains $roleCommitApi "SunExpRpcAuthorityRuntime.CreateLocalServerSender(source)" "Solar Memory local host commit must use the same sender authority model."
Assert-Matches $auraCgRuntime "RpcSkillCgPlaybackRequest\s*:\s*RpcCommandBase,\s*IAuraCgServerBoundRpcCommand" "Shared Skill CG playback requests must receive server-bound sender context."
Assert-Contains $auraCgRuntime "public interface IAuraCgServerBoundRpcCommand" "Shared Skill CG server-bound command interface must be public for serializable public commands."
Assert-Contains $auraCgRuntime "AuraCgRpcAuthorityRuntime.Initialize(modConfig)" "Shared Skill CG runtime must initialize RPC sender authority when a consumer config is available."
Assert-Contains $auraCgRuntime "BindServerSender" "Shared Skill CG playback request must bind server sender before CmdExecute."
Assert-Contains $auraCgRuntime "ApplyServerPlaybackRequest(Playback, serverSender)" "Shared Skill CG playback request must pass bound sender into server validation."
Assert-Contains $auraCgRuntime "SenderOwnsStatus(sender.PlayerId, playback.OwnerStatusId)" "Shared Skill CG host relay must validate that the sender owns the owner status."
Assert-Matches $auraCgRuntime "playback\s*\.\s*IssuerPlayerId\s*=\s*sender\.PlayerId" "Shared Skill CG host relay must bind issuer identity from sender instead of trusting payload identity."
Assert-NotContains $auraCgRuntime "RpcSkillCgEvent" "Retired raw Skill CG event RPC must be removed instead of remaining as a disabled compatibility path."
Assert-NotContains $auraCgRuntime "FromNetworkEvent" "Retired raw Skill CG event conversion must be removed."
Assert-Contains $auraCgRuntime "MaximumEventsPerPlayback" "Shared Skill CG playback must cap event count."
Assert-Contains $auraCgRuntime "MaximumPayloadBytes" "Shared Skill CG playback must cap serialized payload bytes."
Assert-Contains $auraCgRuntime "AuraCgRegisteredRequestResolver" "Shared Skill CG playback must resolve local registered resources through the dedicated resolver."
Assert-Contains $auraCgRuntime "ResolveNetworkRequest" "Shared Skill CG playback must resolve compact identities before local playback."
Assert-Contains $auraCgRuntime "public string CgId" "Shared Skill CG network events must carry a registry id rather than resource bodies."
Assert-Contains $auraCgRuntime "RpcSkillCgFightSession" "Shared Skill CG must synchronize a host-owned fight session token."
Assert-Contains $auraCgRuntime "stale fight session" "Shared Skill CG host relay must reject late commands from another fight session."
Assert-Contains $sunSkillCgRuntime "SkillCgArbiterRuntime.RequestCg(SunExpIds.ModId, request, syncRemote: true)" "SunExp Skill CG must delegate synchronized playback to the shared Skill CG runtime."
Write-Host "Network RPC authority assertions passed."
