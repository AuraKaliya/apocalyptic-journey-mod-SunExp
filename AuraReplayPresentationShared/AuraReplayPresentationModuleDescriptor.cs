using System;

namespace AuraReplay.Presentation.Shared;

public static class AuraReplayPresentationPortability
{
    public const string Portable = "Portable";
    public const string ProviderRequired = "ProviderRequired";
}

public sealed class AuraReplayPresentationModuleDescriptor
{
    public string OwnerModId { get; set; } = "";
    public string TypeId { get; set; } = "";

    // Versions the event payload and its interpretation, not the containing DLL.
    public int SchemaVersion { get; set; } = 1;
    public string Portability { get; set; } = AuraReplayPresentationPortability.Portable;

    // Recording provenance only. Compiler/reference changes can alter an assembly
    // MVID without changing this module's data or renderer contract.
    public string BuildIdentity { get; set; } = "";
    public string RendererCapability { get; set; } = "";

    /// <summary>
    /// Checks the declared module contract without modifying recorded provenance.
    /// Breaking payload or rendering changes must version SchemaVersion or
    /// RendererCapability respectively; an assembly rebuild is not such a change.
    /// </summary>
    public bool MatchesContract(AuraReplayPresentationModuleDescriptor? required)
    {
        if (required == null
            || string.IsNullOrWhiteSpace(OwnerModId)
            || string.IsNullOrWhiteSpace(TypeId)
            || SchemaVersion <= 0
            || (Portability != AuraReplayPresentationPortability.Portable
                && Portability != AuraReplayPresentationPortability.ProviderRequired)
            || (Portability == AuraReplayPresentationPortability.ProviderRequired
                && string.IsNullOrWhiteSpace(RendererCapability)))
            return false;

        return string.Equals(OwnerModId, required.OwnerModId, StringComparison.Ordinal)
               && string.Equals(TypeId, required.TypeId, StringComparison.Ordinal)
               && SchemaVersion == required.SchemaVersion
               && string.Equals(Portability, required.Portability, StringComparison.Ordinal)
               && string.Equals(RendererCapability ?? "", required.RendererCapability ?? "", StringComparison.Ordinal);
    }
}
