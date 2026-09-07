using Terrias.Dll.Contracts;

namespace Terrias.Dll.Application;

public interface IProjectionNetworkPort
{
    TerriasNetworkSendStatus RequestSummon(string roleId, string ownerStatusId, string token, string deckRecipeHash, string source);
    bool SendResult(ProjectionSummonResultSnapshot result, string source);
    bool SendState(ProjectionCompanionSnapshot snapshot, string source);
    bool SendTurn(ProjectionSummonTurnSnapshot snapshot, string source);
    bool RequestState(string statusId, string generation, string source);
}
