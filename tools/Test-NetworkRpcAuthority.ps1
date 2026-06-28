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
$auraPayloadGuard = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcPayloadGuard.cs"
$auraTransport = Read-RepoText "AuraToolsExp-Dev\Infrastructure\AuraToolsRpcTransport.cs"
$damageCommands = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterCommands.cs"
$damageRuntime = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterNetworkRuntime.cs"
$damagePolicy = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterAuthorityPolicy.cs"
$modSyncCommand = Read-RepoText "AuraToolsExp-Dev\Features\ModSync\AuraToolsModSyncManifestCommand.cs"
$modSyncRuntime = Read-RepoText "AuraToolsExp-Dev\Features\ModSync\AuraToolsModSyncRuntime.cs"
$loggingConfig = Read-RepoText "AuraToolsExp-Dev\Config\AuraToolsConfigModels.cs"
$fileLogging = Read-RepoText "AuraToolsExp-Dev\Features\Logging\AuraToolsFileLogRuntime.cs"
$sunEntry = Read-RepoText "SunExp-Dev\Entry.cs"
$sunAuthority = Read-RepoText "SunExp-Dev\Network\SunExpRpcAuthorityRuntime.cs"
$roleCommit = Read-RepoText "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"
$roleCommitApi = Read-RepoText "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"

Assert-Contains $auraEntry "AuraToolsRpcAuthorityRuntime.Initialize(modConfig)" "AuraToolsExp Entry must initialize RPC authority binding."
Assert-Contains $auraAuthority "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase" "AuraTools RPC authority must bind the user-code receive hook."
Assert-Contains $auraAuthority "PlayerManager.CmdReceiveRpcCommand" "AuraTools RPC authority must bind the generated receive hook."
Assert-Contains $auraAuthority "IAuraToolsServerBoundRpcCommand" "AuraTools RPC authority must only bind server-bound commands."
Assert-Contains $auraSender "public interface IAuraToolsServerBoundRpcCommand" "AuraTools server-bound command interface must be public for serializable public commands."

Assert-Contains $auraPayloadGuard "MirrorStringLimitBytes = 65534" "AuraTools payload guard must document Mirror's hard string byte limit."
Assert-Contains $auraPayloadGuard "DefaultSoftLimitBytes = 56000" "AuraTools payload guard must keep a soft budget below Mirror's hard limit."
Assert-Contains $auraPayloadGuard "Encoding.UTF8.GetByteCount" "AuraTools payload guard must measure payloads by UTF-8 bytes, not character count."
Assert-Contains $auraTransport "public static bool Send(" "AuraTools transport must expose a unified RPC send entry."
Assert-Contains $auraTransport "bytes > SoftLimitBytes" "AuraTools transport must block oversized payloads before Mirror serialization."
Assert-Contains $auraTransport "public static bool SendJsonChunksAsync" "AuraTools transport must expose a chunked JSON send path for large payloads."
Assert-Contains $auraTransport "ThreadPool.QueueUserWorkItem" "AuraTools chunk preparation should run off the main thread."
Assert-Contains $auraTransport "ConcurrentQueue<Action>" "AuraTools chunk sending must marshal work back to the main thread."
Assert-Contains $auraTransport "source=" "AuraTools transport logs must identify the sending source."
Assert-Contains $auraTransport "command=" "AuraTools transport logs must identify the RPC command type."

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
Assert-Contains $sunAuthority "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase" "SunExp RPC authority must bind the user-code receive hook."
Assert-Contains $sunAuthority "PlayerManager.CmdReceiveRpcCommand" "SunExp RPC authority must bind the generated receive hook."
Assert-Contains $sunAuthority "ISunExpServerBoundRpcCommand" "SunExp RPC authority must only bind server-bound commands."
Assert-Contains $sunAuthority "CreateLocalServerSender" "SunExp RPC authority must expose a local host sender for direct server paths."
Assert-Contains $sunAuthority "public interface ISunExpServerBoundRpcCommand" "SunExp server-bound command interface must be public for serializable public commands."

Assert-Matches $roleCommit "RpcSolarMemoryRoleCommit\s*:\s*RpcCommandBase,\s*ISunExpServerBoundRpcCommand" "Solar Memory role commit must receive server-bound sender context."
Assert-Contains $roleCommit "ApplyOnServer(Role, Source, serverSender, remoteRpc: true)" "Remote Solar Memory role commit must execute as a bound RPC."
Assert-Contains $roleCommit "ValidateSender(role, source, sender, remoteRpc)" "Solar Memory role commit must validate sender before committing."
Assert-Contains $roleCommit "string.Equals(role.Id, sender.PlayerId, StringComparison.Ordinal)" "Solar Memory role commit must reject sender/role mismatches."
Assert-Contains $roleCommitApi "SunExpRpcAuthorityRuntime.CreateLocalServerSender(source)" "Solar Memory local host commit must use the same sender authority model."

Write-Host "Network RPC authority assertions passed."
