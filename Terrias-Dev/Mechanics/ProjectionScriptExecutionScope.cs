using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Binds one projection-owned native script to its synthetic actor and target
/// set, then restores every transient executor field on exit. Native online
/// routing remains untouched so non-local targets still use the game's RPC.
/// </summary>
internal sealed class ProjectionScriptExecutionScope : IDisposable
{
    private readonly IScriptExecutor executor;
    private readonly IStatusManager? previousSelf;
    private readonly IStatusManager? previousTarget;
    private readonly IStatusManager? previousStatus;
    private readonly List<IStatusManager>? previousObjects;
    private bool disposed;

    private ProjectionScriptExecutionScope(
        IScriptExecutor executor,
        IStatusManager self,
        IReadOnlyList<IStatusManager>? targets)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        previousSelf = executor.Self;
        previousTarget = executor.Target;
        previousStatus = executor.status;
        previousObjects = executor.Object;

        try
        {
            executor.Self = self ?? throw new ArgumentNullException(nameof(self));
            executor.Target = targets?.FirstOrDefault()!;
            executor.status = null!;
            executor.Object = targets?.Where(value => value != null).ToList()
                              ?? new List<IStatusManager>();
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public static ProjectionScriptExecutionScope Enter(
        IScriptExecutor executor,
        IStatusManager self,
        IReadOnlyList<IStatusManager>? targets)
    {
        return new ProjectionScriptExecutionScope(executor, self, targets);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Restore();
    }

    private void Restore()
    {
        executor.Self = previousSelf!;
        executor.Target = previousTarget!;
        executor.status = previousStatus!;
        executor.Object = previousObjects!;
    }
}
