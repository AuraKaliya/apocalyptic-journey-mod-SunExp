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
$damageCommands = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterCommands.cs"
$damageRuntime = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterNetworkRuntime.cs"
$damagePolicy = Read-RepoText "AuraToolsExp-Dev\Features\DamageMeter\Network\DamageMeterAuthorityPolicy.cs"
$sunEntry = Read-RepoText "SunExp-Dev\Entry.cs"
$sunAuthority = Read-RepoText "SunExp-Dev\Network\SunExpRpcAuthorityRuntime.cs"
$roleCommit = Read-RepoText "SunExp-Dev\Network\RpcSolarMemoryRoleCommit.cs"
$roleCommitApi = Read-RepoText "SunExp-Dev\GameApi\SolarMemoryRoleCommitApi.cs"

Assert-Contains $auraEntry "AuraToolsRpcAuthorityRuntime.Initialize(modConfig)" "AuraToolsExp Entry must initialize RPC authority binding."
Assert-Contains $auraAuthority "PlayerManager.UserCode_CmdReceiveRpcCommand__RpcCommandBase" "AuraTools RPC authority must bind the user-code receive hook."
Assert-Contains $auraAuthority "PlayerManager.CmdReceiveRpcCommand" "AuraTools RPC authority must bind the generated receive hook."
Assert-Contains $auraAuthority "IAuraToolsServerBoundRpcCommand" "AuraTools RPC authority must only bind server-bound commands."
Assert-Contains $auraSender "public interface IAuraToolsServerBoundRpcCommand" "AuraTools server-bound command interface must be public for serializable public commands."

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
