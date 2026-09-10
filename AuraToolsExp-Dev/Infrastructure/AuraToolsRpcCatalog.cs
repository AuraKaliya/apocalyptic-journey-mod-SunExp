using AuraShared.Core;
namespace AuraToolsExp.Dll.Infrastructure;
internal static class AuraToolsRpcCatalog
{
    internal static void Register()
    {
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.Skin.AuraSkinSelectionCommand>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.PixelEmoji.AuraToolsPixelEmojiCommand>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.ModSync.AuraToolsModSyncManifestCommand>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.ModSync.AuraToolsModSyncManifestChunkCommand>(AuraRpcPublication.HostOnly);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Network.ReplayCapabilityCommandV17>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Network.ReplayCanonicalChunkCommandV17>(AuraRpcPublication.HostOnly);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.DamageMeter.Network.DamageMeterSubmitBatchCommand>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.DamageMeter.Network.DamageMeterControlCommand>(AuraRpcPublication.HostOnly);
        AuraRpcAdmission.Register<AuraToolsExp.Dll.Features.DamageMeter.Network.DamageMeterSnapshotCommand>(AuraRpcPublication.MemberRequest);
    }
}
