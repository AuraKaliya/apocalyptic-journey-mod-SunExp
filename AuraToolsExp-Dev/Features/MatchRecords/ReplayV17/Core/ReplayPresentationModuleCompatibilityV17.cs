using System;
using System.Collections.Generic;
using System.Linq;
using AuraReplay.Presentation.Shared;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayPresentationModuleCompatibilityV17
{
    internal static ReplayPresentationModuleRequirementV17? FindUnsatisfied(
        IEnumerable<ReplayPresentationModuleRequirementV17> requirements,
        IEnumerable<AuraReplayPresentationModuleDescriptor> available)
    {
        if (requirements == null) throw new ArgumentNullException(nameof(requirements));
        if (available == null) throw new ArgumentNullException(nameof(available));
        var modules = available.ToArray();
        foreach (var required in requirements)
        {
            if (!string.Equals(required.Portability,
                    AuraReplayPresentationPortability.ProviderRequired, StringComparison.Ordinal))
                continue;

            var contract = new AuraReplayPresentationModuleDescriptor
            {
                OwnerModId = required.OwnerModId,
                TypeId = required.TypeId,
                SchemaVersion = required.SchemaVersion,
                Portability = required.Portability,
                RendererCapability = required.RendererCapability,
                BuildIdentity = required.BuildIdentity
            };
            if (!modules.Any(module => module != null && module.MatchesContract(contract)))
                return required;
        }
        return null;
    }
}
