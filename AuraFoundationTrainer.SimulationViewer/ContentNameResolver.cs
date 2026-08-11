using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AuraFoundationTrainer.SimulationViewer;

internal sealed class ContentNameResolver
{
    private readonly Dictionary<string, string> names =
        new(StringComparer.OrdinalIgnoreCase);

    public string SourceDescription { get; private set; } = "未加载中文目录";

    public bool EmbeddedCatalogLoaded { get; private set; }

    public int Count => names.Count;

    public void Load(SqliteConnection connection, string databasePath)
    {
        names.Clear();
        EmbeddedCatalogLoaded = false;
        if (TableExists(connection, "content_entities"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT entity_type,entity_id,display_name
                FROM content_entities WHERE locale='zh-CN'
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                Add(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
            EmbeddedCatalogLoaded = names.Count > 0;
            if (EmbeddedCatalogLoaded)
            {
                SourceDescription = $"数据库内置中文目录（{names.Count} 项）";
            }
        }
        var external = FindExternalCatalog(databasePath);
        if (external != null)
        {
            LoadExternal(external);
            if (!EmbeddedCatalogLoaded)
            {
                SourceDescription = $"安装目录中文目录回退（{names.Count} 项）";
            }
        }
        if (names.Count == 0)
        {
            SourceDescription = "没有可用中文目录；未知 ID 将明确标记";
        }
    }

    public string Resolve(string entityType, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return "未选择";
        var normalized = entityId.Trim();
        return names.TryGetValue(Key(entityType, normalized), out var name)
            ? name
            : $"未识别内容（{normalized}）";
    }

    public string ResolveScenario(string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId)) return "未识别场景";
        var parts = scenarioId.Split(':');
        var encounterId = parts.Length == 0 ? scenarioId : parts[^1];
        return Resolve("encounter", encounterId);
    }

    public IReadOnlyList<ContentEntityItem> Items() => names
        .Select(pair =>
        {
            var separator = pair.Key.IndexOf('|');
            return new ContentEntityItem(
                ViewerText.EntityType(separator < 0
                    ? ""
                    : pair.Key[..separator]),
                pair.Value,
                separator < 0 ? pair.Key : pair.Key[(separator + 1)..]);
        })
        .OrderBy(item => item.Type, StringComparer.CurrentCulture)
        .ThenBy(item => item.Name, StringComparer.CurrentCulture)
        .ToList();

    public static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name=$name
            """;
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private void LoadExternal(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out var entries)) return;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryString(entry, "entityType", out var type)
                || !TryString(entry, "entityId", out var id)
                || !TryString(entry, "displayName", out var name))
            {
                continue;
            }
            Add(type, id, name);
        }
    }

    private void Add(string type, string id, string name)
    {
        if (string.IsNullOrWhiteSpace(type)
            || string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        names.TryAdd(Key(type, id), name.Trim());
    }

    private static string Key(string type, string id) =>
        type.Trim().ToLowerInvariant() + "|" + id.Trim();

    private static bool TryString(
        JsonElement value,
        string property,
        out string result)
    {
        result = "";
        if (!value.TryGetProperty(property, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        result = element.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(result);
    }

    private static string? FindExternalCatalog(string databasePath)
    {
        var direct = Path.Combine(
            Path.GetDirectoryName(databasePath) ?? "",
            "witch-content-display-catalog-v1.json");
        if (File.Exists(direct)) return direct;

        for (DirectoryInfo? current = new(AppContext.BaseDirectory);
             current != null;
             current = current.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(
                             current.FullName,
                             "Config",
                             "combat-simulation",
                             "witch-content-display-catalog-v1.json"),
                         Path.Combine(
                             current.FullName,
                             "AuraToolsExp",
                             "Config",
                             "combat-simulation",
                             "witch-content-display-catalog-v1.json")
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

internal sealed record ContentEntityItem(string Type, string Name, string Id);
