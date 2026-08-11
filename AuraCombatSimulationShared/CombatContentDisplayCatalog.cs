using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatSimulation.Shared;

public static class CombatContentDisplayCatalogProtocol
{
    public const int SchemaVersion = 1;

    public const string Locale = "zh-CN";
}

public sealed class CombatContentDisplayCatalogEntry
{
    public string OwnerModId { get; set; } = "";

    public string EntityType { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string Source { get; set; } = "";

    public string SourceHash { get; set; } = "";
}

public sealed class CombatContentDisplayCatalog
{
    public int SchemaVersion { get; set; } =
        CombatContentDisplayCatalogProtocol.SchemaVersion;

    public string CatalogId { get; set; } = "";

    public string Locale { get; set; } =
        CombatContentDisplayCatalogProtocol.Locale;

    public string GameBuild { get; set; } = "";

    public DateTime ExportedAtUtc { get; set; }

    public List<CombatContentDisplayCatalogEntry> Entries { get; set; } = new();

    public CombatContentDisplayCatalog Normalize()
    {
        SchemaVersion = CombatContentDisplayCatalogProtocol.SchemaVersion;
        Locale = string.IsNullOrWhiteSpace(Locale)
            ? CombatContentDisplayCatalogProtocol.Locale
            : Locale.Trim();
        Entries = (Entries ?? new List<CombatContentDisplayCatalogEntry>())
            .Where(item => item != null
                           && !string.IsNullOrWhiteSpace(item.EntityType)
                           && !string.IsNullOrWhiteSpace(item.EntityId)
                           && !string.IsNullOrWhiteSpace(item.DisplayName))
            .GroupBy(
                item => string.Join(
                    "|",
                    item.OwnerModId?.Trim() ?? "",
                    item.EntityType.Trim().ToLowerInvariant(),
                    item.EntityId.Trim()),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.EntityType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return this;
    }
}
