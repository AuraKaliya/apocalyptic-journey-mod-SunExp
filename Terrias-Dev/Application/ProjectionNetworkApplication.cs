using Terrias.Dll.Contracts;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class ProjectionNetworkApplication
{
    public static int BattleEpoch => CompanionAuthorityService.BattleEpoch;

    public static void ApplyTurn(ProjectionSummonTurnSnapshot snapshot)
    {
        if (CompanionAuthorityService.IsAuthoritative()) return;
        if (snapshot.ProtocolVersion != TerriasProtocolContract.ProjectionVersion || snapshot.BattleEpoch != BattleEpoch)
        {
            TerriasLog.Warn("[PartnerTurn] incompatible summon-turn snapshot ignored: protocol=" + snapshot.ProtocolVersion + ", epoch=" + snapshot.BattleEpoch + ", localEpoch=" + BattleEpoch);
            return;
        }
        ProjectionTurnCoordinator.ApplyAuthoritativeTransaction(snapshot.ToTransaction(), "RpcProjectionSummonTurnState");
    }
}
