using System;
using System.IO;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCaseArchiveProtocol
{
    public const string Version = "success-case-archive-worker-v3";

    public const int StorageVersion = 3;

    public const int CompatibilityKeyLength = 16;

    public const int EntryKeyLength = 24;

    public const string ExpertDirectoryName = "e";

    public const string CaseDirectoryName = "c";

    public const string ObservationDirectoryName = "o";

    public static string CompatibilityDirectory(
        string archiveRoot,
        string compatibilityKey,
        int storageVersion = StorageVersion)
    {
        if (string.IsNullOrWhiteSpace(archiveRoot))
        {
            throw new ArgumentException(
                "Archive root is required.",
                nameof(archiveRoot));
        }
        if (string.IsNullOrWhiteSpace(compatibilityKey))
        {
            throw new ArgumentException(
                "Compatibility key is required.",
                nameof(compatibilityKey));
        }
        return Path.Combine(
            Path.GetFullPath(archiveRoot),
            "v" + storageVersion,
            CompactIdentifier(compatibilityKey, CompatibilityKeyLength));
    }

    public static string EntryPath(
        string archiveRoot,
        string compatibilityKey,
        string directoryName,
        string entryId,
        int identifierLength = EntryKeyLength)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            throw new ArgumentException(
                "Archive entry directory is required.",
                nameof(directoryName));
        }
        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new ArgumentException(
                "Archive entry id is required.",
                nameof(entryId));
        }
        return Path.Combine(
            CompatibilityDirectory(archiveRoot, compatibilityKey),
            directoryName,
            CompactIdentifier(entryId, identifierLength) + ".json");
    }

    public static string CompactIdentifier(string value, int maximumLength)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length == 0)
        {
            return "_";
        }
        var safe = normalized
            .Replace(Path.DirectorySeparatorChar, '_')
            .Replace(Path.AltDirectorySeparatorChar, '_');
        if (maximumLength <= 0 || safe.Length <= maximumLength)
        {
            return safe;
        }
        return safe.Substring(0, maximumLength);
    }
}
