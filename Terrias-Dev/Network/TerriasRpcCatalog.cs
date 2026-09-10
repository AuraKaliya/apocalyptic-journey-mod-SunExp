using AuraShared.Core;
namespace Terrias.Dll.Network;
internal static class TerriasRpcCatalog
{
    internal static void Register()
    {
        AuraRpcAdmission.Register<RpcSpiritEnemySuppressed>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritSummonRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritCompanionState>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritCompanionRemoved>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritWithdrawRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritCaptureRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcSpiritCaptureState>(AuraRpcPublication.HostOnly, battleScoped: true, allowSettledResult: true);
        AuraRpcAdmission.Register<RpcSolarMemoryRoleCommit>(AuraRpcPublication.MemberRequest, battleScoped: false);
        AuraRpcAdmission.Register<RpcRuntimeHandAttachment>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionSummonRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionCompanionState>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionSummonResult>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionSummonTurnState>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionStateRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcProjectionActionFrame>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcHeartChangeControlRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcHeartChangeControlState>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcPolymorphVisualState>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcOlimyaGoldenization>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcEndlessAbyssShockResolution>(AuraRpcPublication.HostOnly, battleScoped: false);
        AuraRpcAdmission.Register<RpcEndlessAbyssSettlementBarrier>(AuraRpcPublication.MemberRequest, battleScoped: false);
        AuraRpcAdmission.Register<RpcEndlessAbyssEvacuation>(AuraRpcPublication.HostOnly, battleScoped: false);
        AuraRpcAdmission.Register<RpcEmberAdventureStateCommit>(AuraRpcPublication.MemberRequest, battleScoped: false);
        AuraRpcAdmission.Register<RpcElementalEnemyMagicSnapshot>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcElementalCrystalSpawn>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcElementalCrystalCreateRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcElementalCrystalClaim>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcElementalCrystalResolution>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcConstellationStateCommit>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcConstellationRosterSnapshot>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcConstellationRoundReward>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcFieldStateSnapshot>(AuraRpcPublication.HostOnly, battleScoped: true);
        AuraRpcAdmission.Register<RpcFieldStateRequest>(AuraRpcPublication.MemberRequest, battleScoped: true);
        AuraRpcAdmission.Register<RpcEndlessSeaStateSnapshot>(AuraRpcPublication.HostOnly, battleScoped: false);
        AuraRpcAdmission.Register<RpcEndlessSeaStateSnapshotRequest>(AuraRpcPublication.MemberRequest, battleScoped: false);
    }
}
