using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Modules.Contracts;

namespace AuraToolsExp.Dll.Modules;

public sealed class AuraToolModuleStateStore
{
    private readonly Dictionary<string, AuraToolModuleState> states =
        new(StringComparer.Ordinal);
    private long revision;

    public event Action<AuraToolModuleState>? Changed;

    public AuraToolModuleState Publish(AuraToolModuleState value)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ModuleId))
        {
            throw new ArgumentException("AuraTools module state requires a module id.", nameof(value));
        }

        if (states.TryGetValue(value.ModuleId, out var existing)
            && existing.SameVisibleState(value))
        {
            return existing;
        }

        var published = value.CloneWithRevision(++revision);
        states[value.ModuleId] = published;
        Changed?.Invoke(published);
        return published;
    }

    public bool TryGet(string moduleId, out AuraToolModuleState state)
    {
        return states.TryGetValue(moduleId ?? "", out state!);
    }
}
