using System;
using Network.Command;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcSpiritEnemySuppressed : RpcCommandBase
{
    public string StatusId { get; set; } = "";

    public string EnemyId { get; set; } = "";

    public string Source { get; set; } = "";

    public RpcSpiritEnemySuppressed()
    {
    }

    public RpcSpiritEnemySuppressed(string statusId, string enemyId, string source)
    {
        StatusId = statusId ?? "";
        EnemyId = enemyId ?? "";
        Source = source ?? "";
    }

    public override void RpcExecute()
    {
        EnemyCaptureSettlementApi.ApplyNetworkSuppression(StatusId, EnemyId, Source);
    }
}
