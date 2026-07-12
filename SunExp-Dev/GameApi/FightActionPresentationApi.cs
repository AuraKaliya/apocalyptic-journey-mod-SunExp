using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

/// <summary>
/// Presents an already resolved action through the game's native combat animation
/// without asking the native enemy action pipeline to select or execute targets.
/// </summary>
public static class FightActionPresentationApi
{
    public static void PresentCommittedAction(
        ScriptExecutor? executor,
        IStatusManager? actor,
        IReadOnlyList<IStatusManager>? targets,
        string source)
    {
        if (executor == null || actor == null)
        {
            return;
        }

        var previousSelf = executor.Self;
        var previousTarget = executor.Target;
        var previousObjects = executor.Object?.ToArray() ?? Array.Empty<IStatusManager>();
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            executor.Self = actor;
            executor.Object ??= new List<IStatusManager>();
            executor.Object.Clear();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target == null
                        || target.CurHp <= 0
                        || target.state == IStatusManager.State.Dead
                        || !seen.Add(target.InstanceId))
                    {
                        continue;
                    }

                    executor.Object.Add(target);
                }
            }

            executor.Target = executor.Object.Count == 0 ? null : executor.Object[0];
            var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
            if (fightUi == null)
            {
                SunExpPerformanceCounters.Record("ProjectionAction.NativeAnimationUnavailable");
                return;
            }

            fightUi.CallActionAnimation(executor);
            SunExpPerformanceCounters.Record("ProjectionAction.NativeAnimationPresented");
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[FightActionPresentation] native animation skipped from "
                + source
                + ": "
                + ex.Message);
        }
        finally
        {
            executor.Self = previousSelf;
            executor.Target = previousTarget;
            executor.Object ??= new List<IStatusManager>();
            executor.Object.Clear();
            executor.Object.AddRange(previousObjects);
            SunExpPerformanceCounters.RecordDuration("ProjectionAction.NativeAnimation", start);
        }
    }
}
