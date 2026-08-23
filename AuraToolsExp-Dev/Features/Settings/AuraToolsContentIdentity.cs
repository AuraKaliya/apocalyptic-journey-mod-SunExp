using System;

namespace AuraToolsExp.Dll.Features.Settings;

internal readonly struct AuraToolsContentIdentity
{
    internal AuraToolsContentIdentity(string ownerModId, string contentId)
    {
        OwnerModId = ownerModId ?? "";
        ContentId = contentId ?? "";
    }

    internal string OwnerModId { get; }

    internal string ContentId { get; }

    internal bool IsQualified => OwnerModId.Length > 0;

    internal static AuraToolsContentIdentity Parse(string value)
    {
        var normalized = (value ?? "").Trim();
        var separator = normalized.IndexOf(':');
        if (separator <= 0 || separator >= normalized.Length - 1)
        {
            return new AuraToolsContentIdentity("", normalized);
        }

        var owner = normalized.Substring(0, separator).Trim();
        var content = normalized.Substring(separator + 1).Trim();
        return owner.Length == 0 || content.Length == 0
            ? new AuraToolsContentIdentity("", normalized)
            : new AuraToolsContentIdentity(owner, content);
    }
}
