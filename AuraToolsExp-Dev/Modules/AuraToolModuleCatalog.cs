using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Modules.Contracts;

namespace AuraToolsExp.Dll.Modules;

public sealed class AuraToolModuleCatalog
{
    private readonly object gate = new();
    private readonly IReadOnlyList<IAuraToolModule> builtIns;
    private IReadOnlyList<IAuraToolModule> external = Array.Empty<IAuraToolModule>();
    private IReadOnlyList<IAuraToolModule> modules = Array.Empty<IAuraToolModule>();
    private Dictionary<string, IAuraToolModule> byId =
        new(StringComparer.Ordinal);

    public AuraToolModuleCatalog(IEnumerable<IAuraToolModule> values)
    {
        builtIns = Validate(values ?? Array.Empty<IAuraToolModule>(), null);
        RebuildNoLock();
    }

    public event Action? Changed;

    public IReadOnlyList<IAuraToolModule> Modules
    {
        get
        {
            lock (gate)
            {
                return modules;
            }
        }
    }

    public IReadOnlyList<IAuraToolModule> VisibleModules
    {
        get
        {
            lock (gate)
            {
                return modules
                    .Where(module => module.Descriptor.Visible)
                    .OrderBy(module => module.Descriptor.Order)
                    .ThenBy(
                        module => module.Descriptor.DisplayName,
                        StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public void ReplaceExternal(IEnumerable<IAuraToolModule> values)
    {
        lock (gate)
        {
            external = Validate(
                values ?? Array.Empty<IAuraToolModule>(),
                builtIns.Select(module => module.Descriptor.ModuleId));
            RebuildNoLock();
        }
        PublishChanged();
    }

    public bool TryGet(string moduleId, out IAuraToolModule module)
    {
        lock (gate)
        {
            return byId.TryGetValue(moduleId ?? "", out module!);
        }
    }

    private void RebuildNoLock()
    {
        modules = builtIns
            .Concat(external)
            .OrderBy(module => module.Descriptor.InitializationOrder)
            .ThenBy(module => module.Descriptor.ModuleId, StringComparer.Ordinal)
            .ToArray();
        byId = modules.ToDictionary(
            module => module.Descriptor.ModuleId,
            module => module,
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<IAuraToolModule> Validate(
        IEnumerable<IAuraToolModule> values,
        IEnumerable<string>? reservedIds)
    {
        var result = new List<IAuraToolModule>();
        var ids = new HashSet<string>(
            reservedIds ?? Array.Empty<string>(),
            StringComparer.Ordinal);
        foreach (var module in values)
        {
            if (module == null)
            {
                throw new InvalidOperationException(
                    "AuraTools module catalog contains a null module.");
            }

            var descriptor = module.Descriptor
                             ?? throw new InvalidOperationException(
                                 "AuraTools module descriptor is missing.");
            if (string.IsNullOrWhiteSpace(descriptor.ModuleId)
                || string.IsNullOrWhiteSpace(descriptor.CategoryId)
                || string.IsNullOrWhiteSpace(descriptor.DisplayName))
            {
                throw new InvalidOperationException(
                    "AuraTools module descriptor requires moduleId, categoryId and displayName.");
            }
            if (!ids.Add(descriptor.ModuleId))
            {
                throw new InvalidOperationException(
                    "AuraTools module id is duplicated: " + descriptor.ModuleId);
            }
            result.Add(module);
        }

        return result.ToArray();
    }

    private void PublishChanged()
    {
        var handlers = Changed;
        if (handlers == null)
        {
            return;
        }
        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
                // Catalog ownership must survive a faulty presentation subscriber.
            }
        }
    }
}
