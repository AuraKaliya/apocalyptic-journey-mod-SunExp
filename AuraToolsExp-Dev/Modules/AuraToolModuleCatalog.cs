using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Modules.Contracts;

namespace AuraToolsExp.Dll.Modules;

public sealed class AuraToolModuleCatalog
{
    private readonly IReadOnlyList<IAuraToolModule> modules;
    private readonly Dictionary<string, IAuraToolModule> byId;

    public AuraToolModuleCatalog(IEnumerable<IAuraToolModule> values)
    {
        var materialized = (values ?? Array.Empty<IAuraToolModule>()).ToList();
        byId = new Dictionary<string, IAuraToolModule>(StringComparer.Ordinal);
        foreach (var module in materialized)
        {
            if (module == null)
            {
                throw new InvalidOperationException("AuraTools module catalog contains a null module.");
            }

            var descriptor = module.Descriptor
                             ?? throw new InvalidOperationException("AuraTools module descriptor is missing.");
            if (string.IsNullOrWhiteSpace(descriptor.ModuleId)
                || string.IsNullOrWhiteSpace(descriptor.CategoryId)
                || string.IsNullOrWhiteSpace(descriptor.DisplayName))
            {
                throw new InvalidOperationException(
                    "AuraTools module descriptor requires moduleId, categoryId and displayName.");
            }
            if (byId.ContainsKey(descriptor.ModuleId))
            {
                throw new InvalidOperationException(
                    "AuraTools module id is duplicated: " + descriptor.ModuleId);
            }
            byId.Add(descriptor.ModuleId, module);
        }

        modules = materialized
            .OrderBy(module => module.Descriptor.InitializationOrder)
            .ThenBy(module => module.Descriptor.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<IAuraToolModule> Modules => modules;

    public IReadOnlyList<IAuraToolModule> VisibleModules => modules
        .Where(module => module.Descriptor.Visible)
        .OrderBy(module => module.Descriptor.Order)
        .ThenBy(module => module.Descriptor.DisplayName, StringComparer.Ordinal)
        .ToArray();

    public bool TryGet(string moduleId, out IAuraToolModule module)
    {
        return byId.TryGetValue(moduleId ?? "", out module!);
    }
}
