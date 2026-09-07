using Terrias.Dll.Application;
using Terrias.Dll.Contracts;

namespace Terrias.Dll.Network;

public sealed class ProjectionNetworkAdapter : IProjectionNetworkPort
{
    public TerriasNetworkSendStatus RequestSummon(string roleId, string ownerStatusId, string token, string deckRecipeHash, string source) =>
        TerriasNetworkRuntime.TrySend(new RpcProjectionSummonRequest(roleId, ownerStatusId, token, deckRecipeHash), source);
    public bool SendResult(Contracts.ProjectionSummonResultSnapshot result, string source) => TerriasNetworkRuntime.Send(new RpcProjectionSummonResult(new ProjectionSummonResultSnapshot(result)), source);
    public bool SendState(Contracts.ProjectionCompanionSnapshot snapshot, string source) => TerriasNetworkRuntime.Send(new RpcProjectionCompanionState(new ProjectionCompanionSnapshot(snapshot)), source);
    public bool SendTurn(Contracts.ProjectionSummonTurnSnapshot snapshot, string source) => TerriasNetworkRuntime.Send(new RpcProjectionSummonTurnState(new ProjectionSummonTurnSnapshot(snapshot)), source);
    public bool RequestState(string statusId, string generation, string source) => TerriasNetworkRuntime.Send(new RpcProjectionStateRequest(statusId, generation), source);
}
