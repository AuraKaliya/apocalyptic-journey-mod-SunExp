using System;
using System.IO;

namespace AuraCombatAi.Shared;

public static class CombatFoundationCaseArchiveProtocol
{
    public const string Version = "success-case-archive-worker-v4";

    public const int StorageVersion = 4;

    public const int CompatibilityKeyLength = 16;

    public const int EntryKeyLength = 24;

    public const string ExpertDirectoryName = "e";

    public const string CaseDirectoryName = "c";

    public const string ObservationDirectoryName = "o";

    public const string JsonExtension = ".json";

    public const string CompressedJsonExtension = ".json.gz";

    public const int MaximumExpertCasesPerCompatibility = 2048;

    public const int MaximumObservationsPerCompatibility = 8192;

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
            CompactIdentifier(entryId, identifierLength) + JsonExtension);
    }

    public static string CompressedEntryPath(
        string archiveRoot,
        string compatibilityKey,
        string directoryName,
        string entryId,
        int identifierLength = EntryKeyLength)
    {
        var legacyPath = EntryPath(
            archiveRoot,
            compatibilityKey,
            directoryName,
            entryId,
            identifierLength);
        return legacyPath + ".gz";
    }

    public static bool IsArchiveJsonFile(string path)
    {
        return path.EndsWith(JsonExtension, StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(
                   CompressedJsonExtension,
                   StringComparison.OrdinalIgnoreCase);
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

public sealed class CombatFoundationExpertCaseReference
{
    public string ProtocolVersion { get; set; } =
        CombatFoundationCaseArchiveProtocol.Version;

    public int StorageVersion { get; set; } =
        CombatFoundationCaseArchiveProtocol.StorageVersion;

    public string CompatibilityKey { get; set; } = "";

    public string CaseId { get; set; } = "";

    public string CanonicalFileName { get; set; } = "";
}
