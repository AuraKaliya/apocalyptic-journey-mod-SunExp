using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public sealed class ProjectionLifecycleOwner : IProjectionLifecycle
{
    public DataConfig CreateDataConfig(PolymorphRoleSpec role, CompanionStats? stats) => ProjectionSummonService.CreateProjectionDataConfig(role, stats);
    public void Register(ProjectionOtherObj projection, string source) => ProjectionSummonService.RegisterFightState(projection, source);
    public void CommitAction(ProjectionOtherObj projection) => ProjectionSummonService.CommitAction(projection);
    public void CompleteTurn(ProjectionOtherObj projection, string source) => ProjectionSummonService.BroadcastTurnCompleted(projection, source);
    public void RequestState(ProjectionOtherObj projection, string source) => ProjectionSummonService.RequestRuntimeState(projection, source);
    public void Retire(ProjectionState state, string source) => ProjectionSummonService.BroadcastRetired(state, source);
}
