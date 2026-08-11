using System;
using Network.Command;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcSpiritEnemySuppressed : RpcCommandBase
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;

    public int BattleEpoch { get; set; }

    public string StatusId { get; set; } = "";

    public string EnemyRuntimeId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public string Token { get; set; } = "";

    public string Source { get; set; } = "";

    public RpcSpiritEnemySuppressed()
    {
    }

    public RpcSpiritEnemySuppressed(
        string statusId,
        string enemyRuntimeId,
        string enemyId,
        string token,
        string source)
    {
        BattleEpoch = CompanionAuthorityService.BattleEpoch;
        StatusId = statusId ?? "";
        EnemyRuntimeId = enemyRuntimeId ?? "";
        EnemyId = enemyId ?? "";
        Token = token ?? "";
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        EnemyCaptureSettlementApi.ApplyNetworkSuppression(this);
    }
}
