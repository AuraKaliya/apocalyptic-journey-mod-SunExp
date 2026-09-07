using System;

namespace Terrias.Dll.Mechanics;

// Mechanics reports lifecycle transitions; the composition root supplies the
// application owner that coordinates persistence, native state and transport.
public interface IProjectionLifecycle
{
    DataConfig CreateDataConfig(PolymorphRoleSpec role, CompanionStats? stats);
    void Register(ProjectionOtherObj projection, string source);
    void CommitAction(ProjectionOtherObj projection);
    void CompleteTurn(ProjectionOtherObj projection, string source);
    void RequestState(ProjectionOtherObj projection, string source);
    void Retire(ProjectionState state, string source);
}

public static class ProjectionLifecycle
{
    private static IProjectionLifecycle? current;
    public static IProjectionLifecycle Current => current
        ?? throw new InvalidOperationException("Projection lifecycle is not initialized.");
    public static void Configure(IProjectionLifecycle owner) => current = owner ?? throw new ArgumentNullException(nameof(owner));
}
