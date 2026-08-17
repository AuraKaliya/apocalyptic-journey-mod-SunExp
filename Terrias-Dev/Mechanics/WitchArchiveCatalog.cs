using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class WitchArchiveCatalog
{
    private const int SupportedSchemaVersion = 2;
    private static readonly object SyncRoot = new();
    private static IReadOnlyList<WitchArchiveEntry> entries = Array.Empty<WitchArchiveEntry>();

    public static void Load(ModConfig modConfig)
    {
        var path = Path.Combine(modConfig.DirectoryName, TerriasIds.WitchArchiveRegistryFile);
        if (!File.Exists(path))
        {
            lock (SyncRoot)
            {
                entries = Array.Empty<WitchArchiveEntry>();
            }

            TerriasLog.Warn("[WitchArchive] registry missing: " + path);
            return;
        }

        try
        {
            var document = JsonConvert.DeserializeObject<WitchArchiveDocument>(File.ReadAllText(path))
                           ?? new WitchArchiveDocument();
            var normalized = Normalize(document);
            ApplyExternalBackgrounds(normalized, modConfig.DirectoryName);
            lock (SyncRoot)
            {
                entries = normalized;
            }

            TerriasLog.Info("[WitchArchive] registry loaded: entries=" + normalized.Count);
        }
        catch (Exception ex)
        {
            lock (SyncRoot)
            {
                entries = Array.Empty<WitchArchiveEntry>();
            }

            TerriasLog.Warn("[WitchArchive] registry load failed: " + ex.Message);
        }
    }

    public static IReadOnlyList<WitchArchiveDisplayEntry> DisplayEntries()
    {
        WitchArchiveEntry[] snapshot;
        lock (SyncRoot)
        {
            snapshot = entries.ToArray();
        }

        return snapshot.Select(ToDisplayEntry).ToArray();
    }

    internal static IReadOnlyList<WitchArchiveEntry> Normalize(WitchArchiveDocument document)
    {
        if (document.SchemaVersion <= 0 || document.SchemaVersion > SupportedSchemaVersion)
        {
            throw new InvalidDataException("unsupported schemaVersion=" + document.SchemaVersion);
        }

        if (!string.Equals(document.OwnerModId?.Trim(), TerriasIds.ModId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("ownerModId must be " + TerriasIds.ModId);
        }

        var byId = new Dictionary<string, WitchArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in document.Entries ?? new List<WitchArchiveEntry>())
        {
            if (entry == null || !entry.Enabled)
            {
                continue;
            }

            NormalizeEntry(entry);
            if (entry.Id.Length == 0
                || entry.RoleId.Length == 0
                || entry.AvatarPath.Length == 0
                || entry.PortraitPath.Length == 0)
            {
                TerriasLog.Warn("[WitchArchive] skipped incomplete entry: " + (entry.Id.Length == 0 ? "<empty>" : entry.Id));
                continue;
            }

            byId[entry.Id] = entry;
        }

        return byId.Values
            .OrderBy(entry => entry.Sort)
            .ThenBy(entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static WitchArchiveDisplayEntry ToDisplayEntry(WitchArchiveEntry entry)
    {
        var career = entry.CareerId.Length == 0 ? null : TerriasConfigIndex.Row(DataType.Career, entry.CareerId);
        var locale = TerriasLanguageApi.CurrentLocale;
        var name = LocalizedCareerField(career, "Name", entry.Name.Resolve(locale, entry.Id));
        var title = LocalizedCareerField(career, "Title", entry.Title.Resolve(locale));
        var summary = entry.Summary.Resolve(locale, LocalizedCareerField(career, "Description", ""));
        var background = entry.Background.Resolve(locale, summary);
        return new WitchArchiveDisplayEntry(
            entry.Id,
            entry.RoleId,
            name,
            title,
            summary,
            background,
            entry.AvatarPath,
            entry.PortraitPath,
            entry.PortraitOffsetX,
            entry.PortraitOffsetY);
    }

    private static string LocalizedCareerField(
        IDictionary<string, string>? career,
        string field,
        string fallback)
    {
        if (career != null)
        {
            try
            {
                var value = career.Localize(field);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            catch
            {
                // Use the manifest fallback while native tables are not ready.
            }
        }

        return fallback;
    }

    private static void NormalizeEntry(WitchArchiveEntry entry)
    {
        entry.Id = Clean(entry.Id);
        entry.RoleId = Clean(entry.RoleId);
        entry.CareerId = Clean(entry.CareerId);
        entry.AvatarPath = Clean(entry.AvatarPath);
        entry.PortraitPath = Clean(entry.PortraitPath);
        entry.Name ??= new TerriasLocalizedText();
        entry.Title ??= new TerriasLocalizedText();
        entry.Summary ??= new TerriasLocalizedText();
        entry.Background ??= new TerriasLocalizedText();
        entry.BackgroundFiles ??= new TerriasLocalizedText();
    }

    private static void ApplyExternalBackgrounds(
        IReadOnlyList<WitchArchiveEntry> normalized,
        string modDirectory)
    {
        foreach (var entry in normalized)
        {
            ApplyExternalBackground(entry, modDirectory, "zh-Hans", entry.BackgroundFiles.ZhHans, value => entry.Background.ZhHans = value);
            ApplyExternalBackground(entry, modDirectory, "zh-Hant", entry.BackgroundFiles.ZhHant, value => entry.Background.ZhHant = value);
            ApplyExternalBackground(entry, modDirectory, "en", entry.BackgroundFiles.English, value => entry.Background.English = value);
            ApplyExternalBackground(entry, modDirectory, "ja", entry.BackgroundFiles.Japanese, value => entry.Background.Japanese = value);
        }
    }

    private static void ApplyExternalBackground(
        WitchArchiveEntry entry,
        string modDirectory,
        string locale,
        string relativePath,
        Action<string> assign)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        if (WitchArchiveTextLoader.TryRead(modDirectory, relativePath, out var text, out var error))
        {
            assign(text);
            return;
        }

        TerriasLog.Warn(
            "[WitchArchive] external background fallback: entry=" + entry.Id
            + ", locale=" + locale
            + ", path=" + relativePath
            + ", reason=" + error);
    }

    private static string Clean(string? value)
    {
        return (value?.Trim() ?? "").Replace('\\', '/');
    }
}
