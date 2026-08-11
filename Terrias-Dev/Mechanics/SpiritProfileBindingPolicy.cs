using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrias.Dll.GameApi;

namespace Terrias.Dll.Mechanics;

/// <summary>
/// Keeps Spirit persistence bound to a durable player identity. Save-slot names
/// are accepted only as legacy recovery candidates and never as live owners.
/// </summary>
public static class SpiritProfileBindingPolicy
{
    public static string ResolveStableProfileKey(string networkPlayerId, string runtimePlayerId)
    {
        var candidate = string.IsNullOrWhiteSpace(networkPlayerId)
            ? runtimePlayerId
            : networkPlayerId;
        return string.IsNullOrWhiteSpace(candidate) ? "" : FamiliarId.Sanitize(candidate.Trim());
    }

    public static IReadOnlyList<string> LegacyProfileKeys(string savePath)
    {
        var keys = new List<string>();
        AddKey(keys, Path.GetFileNameWithoutExtension(savePath ?? ""));
        AddKey(keys, "SaveData");
        AddKey(keys, "local");
        return keys;
    }

    public static bool HasRecoverableContent(SpiritCollectionDocument? document)
    {
        return document != null
               && ((document.Instances?.Count ?? 0) > 0
                   || (document.DefaultPartySlots?.Any(uid => !string.IsNullOrWhiteSpace(uid)) ?? false)
                   || !string.IsNullOrWhiteSpace(document.DefaultActiveSpiritUid)
                   || (document.ProcessedCaptureTokens?.Count ?? 0) > 0
                   || (document.ProcessedBattleTokens?.Count ?? 0) > 0);
    }

    public static bool ShouldRecoverLegacy(bool stableProfileExists, SpiritCollectionDocument? legacyDocument)
    {
        return !stableProfileExists && HasRecoverableContent(legacyDocument);
    }

    private static void AddKey(ICollection<string> keys, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = FamiliarId.Sanitize(value.Trim());
        if (!keys.Contains(normalized))
        {
            keys.Add(normalized);
        }
    }
}
