using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public enum AuraInitializationState { Ready, Failed, Blocked }

public sealed class AuraInitializationStep
{
    public string Name { get; set; } = "";
    public AuraInitializationState State { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class AuraSharedInitializationReport
{
    private readonly Dictionary<string, AuraInitializationStep> steps = new(StringComparer.Ordinal);
    public IReadOnlyList<AuraInitializationStep> Steps => steps.Values.ToArray();
    public bool Ready(string name) => steps.TryGetValue(name, out var step) && step.State == AuraInitializationState.Ready;
    public string Summary => "ready=" + steps.Values.Count(step => step.State == AuraInitializationState.Ready)
        + ", failed=" + steps.Values.Count(step => step.State == AuraInitializationState.Failed)
        + ", blocked=" + steps.Values.Count(step => step.State == AuraInitializationState.Blocked);
    public void Reset() => steps.Clear();
    public bool Run(string name, Action action, Action<string, Exception>? failed = null, params string[] dependencies)
    {
        var missing = dependencies.Where(dependency => !Ready(dependency)).ToArray();
        if (missing.Length > 0)
        {
            steps[name] = new AuraInitializationStep { Name = name, State = AuraInitializationState.Blocked, Detail = string.Join(", ", missing) };
            return false;
        }
        try
        {
            action();
            steps[name] = new AuraInitializationStep { Name = name, State = AuraInitializationState.Ready };
            return true;
        }
        catch (Exception ex)
        {
            steps[name] = new AuraInitializationStep { Name = name, State = AuraInitializationState.Failed, Detail = ex.Message };
            failed?.Invoke(name, ex); return false;
        }
    }
}
