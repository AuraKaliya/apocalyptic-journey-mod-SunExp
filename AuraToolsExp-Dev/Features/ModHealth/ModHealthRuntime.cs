using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Witch;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.ModHealth;

internal static class ModHealthSeverities
{
    internal const string Critical = "critical";
    internal const string Error = "error";
    internal const string Warning = "warning";
    internal const string Info = "info";
}

internal sealed class ModHealthIssue
{
    public string Severity { get; set; } = ModHealthSeverities.Info;
    public string Code { get; set; } = "";
    public string ModId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Message { get; set; } = "";
    public string ExceptionType { get; set; } = "";
}

internal sealed class ModHealthModSnapshot
{
    public string ModId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string DirectoryName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Loaded { get; set; }
    public bool LoadedStateKnown { get; set; }
    public List<string> Dependencies { get; set; } = new();
}

internal sealed class ModHealthReport
{
    public int SchemaVersion { get; set; } = 1;
    public string GameVersion { get; set; } = "";
    public string ScannedUtc { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public List<ModHealthModSnapshot> Mods { get; set; } = new();
    public List<ModHealthIssue> Issues { get; set; } = new();

    [JsonIgnore]
    internal int CriticalCount => Issues.Count(issue => issue.Severity == ModHealthSeverities.Critical);
    [JsonIgnore]
    internal int ErrorCount => Issues.Count(issue => issue.Severity == ModHealthSeverities.Error);
    [JsonIgnore]
    internal int WarningCount => Issues.Count(issue => issue.Severity == ModHealthSeverities.Warning);
    [JsonIgnore]
    internal string Level => CriticalCount > 0 ? "严重" : ErrorCount > 0 ? "错误" : WarningCount > 0 ? "警告" : "正常";
}

internal static class ModHealthRuntime
{
    private static readonly string[] RecognizedTableFolders =
    {
        "EventList", "Map", "Card", "Enemy", "EnemyCard", "Level", "Partner", "PartnerCard",
        "FightPartner", "OutSideShop", "Hard", "Item", "Blessing", "Food", "Coin", "EnchTag",
        "Career", "Buff", "Destiny", "Relic", "KeyWordsDic", "Tutorial", "Announcement", "Dialogue",
        "Effect", "RoleData", "Task", "HouseDialogue", "HouseDialogueConfig", "Affection", "SlotCal",
        "SlotReward", "CardPack", "Achievement", "Narration"
    };
    private const int MaximumCsvFiles = 5000;
    private const long MaximumCsvBytes = 8L * 1024L * 1024L;

    internal static ModHealthReport Current { get; private set; } = new();
    internal static event Action? Changed;

    internal static ModHealthReport Scan()
    {
        var report = new ModHealthReport
        {
            GameVersion = GameConfigManager.Version ?? "",
            ScannedUtc = DateTime.UtcNow.ToString("O")
        };
        var roots = Directory.Exists(Globals.ModsPath)
            ? Directory.GetDirectories(Globals.ModsPath)
            : Array.Empty<string>();
        var loaded = LoadedDirectories(out var loadedStateKnown);
        if (!loadedStateKnown)
        {
            Add(report, ModHealthSeverities.Warning, "loader.state-unavailable", "", "",
                "当前游戏版本未暴露可读取的已加载 MOD 集合；已跳过加载结果判定。");
        }
        var byId = new Dictionary<string, ModHealthModSnapshot>(StringComparer.OrdinalIgnoreCase);
        var registrations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var scannedCsv = 0;

        foreach (var root in roots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var directoryName = Path.GetFileName(root);
            var configPath = Path.Combine(root, "ModConfig.json");
            if (!File.Exists(configPath))
            {
                Add(report, ModHealthSeverities.Error, "modconfig.missing", "", directoryName + "/ModConfig.json", "MOD 目录缺少 ModConfig.json。");
                continue;
            }
            JObject config;
            try
            {
                config = JObject.Parse(File.ReadAllText(configPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Add(report, ModHealthSeverities.Error, "modconfig.invalid", "", directoryName + "/ModConfig.json", "ModConfig.json 无法解析：" + ex.Message, ex);
                continue;
            }
            var name = Text(config, "ModName");
            var author = Text(config, "ModAuthor");
            var modId = name + "." + author;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(author))
            {
                Add(report, ModHealthSeverities.Error, "modconfig.identity", modId, directoryName + "/ModConfig.json", "ModName 或 ModAuthor 为空，无法形成稳定 ModId。");
            }
            var snapshot = new ModHealthModSnapshot
            {
                ModId = modId,
                ModName = name,
                ModVersion = Text(config, "ModVersion"),
                DirectoryName = directoryName,
                Enabled = config.Value<bool?>("Enabled") ?? true,
                Loaded = loaded.Contains(Normalize(root)),
                LoadedStateKnown = loadedStateKnown,
                Dependencies = (config["Dependencies"] as JArray)?.Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    ?? new List<string>()
            };
            report.Mods.Add(snapshot);
            if (byId.ContainsKey(modId))
            {
                Add(report, ModHealthSeverities.Error, "modid.duplicate", modId, directoryName, "重复 ModId；游戏只会接受其中一个目录。");
            }
            else
            {
                byId[modId] = snapshot;
            }
            if (string.IsNullOrWhiteSpace(snapshot.ModVersion))
            {
                Add(report, ModHealthSeverities.Warning, "modconfig.version", modId, directoryName + "/ModConfig.json", "ModVersion 为空，无法判断发行版本。");
            }
            var icon = Text(config, "IconPath");
            if (!string.IsNullOrWhiteSpace(icon) && !ResourceExists(root, icon))
            {
                Add(report, ModHealthSeverities.Warning, "resource.icon", modId, directoryName + "/" + icon, "ModConfig.IconPath 无法解析。");
            }
            var ownConfig = Path.Combine(root, "Configuration.json");
            if (File.Exists(ownConfig))
            {
                try { _ = JObject.Parse(File.ReadAllText(ownConfig, Encoding.UTF8)); }
                catch (Exception ex) { Add(report, ModHealthSeverities.Warning, "configuration.invalid", modId, directoryName + "/Configuration.json", "Configuration.json 无法解析：" + ex.Message, ex); }
            }
            ValidateEntryDll(report, root, snapshot);
            foreach (var kind in new[] { "Data", "Text" })
            {
                if (scannedCsv >= MaximumCsvFiles) break;
                foreach (var folder in RecognizedTableFolders)
                {
                    if (scannedCsv >= MaximumCsvFiles) break;
                    var tableRoot = Path.Combine(root, kind, folder);
                    if (!Directory.Exists(tableRoot)) continue;
                    string[] csvFiles;
                    try { csvFiles = Directory.GetFiles(tableRoot, "*.csv", SearchOption.AllDirectories); }
                    catch (Exception ex)
                    {
                        Add(report, ModHealthSeverities.Error, "csv.enumerate", modId,
                            directoryName + "/" + kind + "/" + folder,
                            "游戏表目录无法枚举：" + ex.Message, ex);
                        continue;
                    }
                    foreach (var csv in csvFiles)
                    {
                        if (scannedCsv >= MaximumCsvFiles)
                        {
                            Add(report, ModHealthSeverities.Warning, "csv.limit", modId, directoryName, "CSV 数量超过健康检查上限，后续文件未扫描。");
                            break;
                        }
                        scannedCsv++;
                        ValidateCsv(report, root, snapshot, kind, folder, csv, registrations);
                    }
                }
            }
        }

        ValidateDependencies(report, byId);
        foreach (var duplicate in registrations.Where(pair => pair.Value.Count > 1))
        {
            Add(report, ModHealthSeverities.Warning, "registration.duplicate", "", "", "多个游戏表注册了相同 ID：" + duplicate.Key + "；" + string.Join("、", duplicate.Value));
        }
        report.Fingerprint = Fingerprint(report);
        Current = report;
        Changed?.Invoke();
        AuraToolModuleHost.RefreshState(AuraToolModuleIds.ModHealth);
        return report;
    }

    internal static string ExportReport()
    {
        var report = Current.ScannedUtc.Length == 0 ? Scan() : Current;
        var directory = Path.Combine(AuraToolsConfigService.DataRootDirectory, "Diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "mod-health-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented), Encoding.UTF8);
        return path;
    }

    private static void ValidateDependencies(ModHealthReport report, Dictionary<string, ModHealthModSnapshot> byId)
    {
        foreach (var mod in report.Mods.Where(mod => mod.Enabled))
        {
            foreach (var dependency in mod.Dependencies)
            {
                if (!byId.TryGetValue(dependency, out var target))
                {
                    Add(report, ModHealthSeverities.Error, "dependency.missing", mod.ModId, mod.DirectoryName, "依赖不存在：" + dependency);
                }
                else if (!target.Enabled)
                {
                    Add(report, ModHealthSeverities.Error, "dependency.disabled", mod.ModId, mod.DirectoryName, "依赖未启用：" + dependency);
                }
            }
            if (mod.LoadedStateKnown && !mod.Loaded)
            {
                Add(report, ModHealthSeverities.Error, "loader.not-loaded", mod.ModId, mod.DirectoryName, "MOD 已启用但未进入游戏的已加载集合；请检查依赖和入口异常。");
            }
        }
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in report.Mods.Where(mod => mod.Enabled)) Visit(mod.ModId, new List<string>());

        void Visit(string id, List<string> path)
        {
            if (visited.Contains(id) || !byId.TryGetValue(id, out var mod)) return;
            if (!visiting.Add(id))
            {
                Add(report, ModHealthSeverities.Error, "dependency.cycle", id, mod.DirectoryName, "检测到循环依赖：" + string.Join(" -> ", path.Concat(new[] { id })));
                return;
            }
            path.Add(id);
            foreach (var dependency in mod.Dependencies) Visit(dependency, path);
            path.RemoveAt(path.Count - 1);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static void ValidateEntryDll(ModHealthReport report, string root, ModHealthModSnapshot mod)
    {
        var path = Path.Combine(root, "Scripts", "Entry.dll");
        if (!File.Exists(path)) return;
        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            if (mod.Enabled && mod.LoadedStateKnown && !mod.Loaded)
            {
                _ = Assembly.LoadFrom(path).GetTypes();
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            var details = string.Join(" | ", (ex.LoaderExceptions ?? Array.Empty<Exception>()).Select(error => error?.Message).Where(text => !string.IsNullOrWhiteSpace(text)));
            Add(report, ModHealthSeverities.Error, "dll.game-api-incompatible", mod.ModId, mod.DirectoryName + "/Scripts/Entry.dll",
                "DLL 类型加载失败，疑似与当前游戏 API 不兼容：" + details, ex);
        }
        catch (Exception ex)
        {
            Add(report, ModHealthSeverities.Error, "dll.load-failed", mod.ModId, mod.DirectoryName + "/Scripts/Entry.dll",
                "Entry.dll 无法加载：" + ex.Message, ex);
        }
    }

    private static void ValidateCsv(
        ModHealthReport report,
        string root,
        ModHealthModSnapshot mod,
        string kind,
        string folder,
        string csvPath,
        Dictionary<string, List<string>> registrations)
    {
        var relative = Relative(root, csvPath);
        var info = new FileInfo(csvPath);
        if (info.Length > MaximumCsvBytes)
        {
            Add(report, ModHealthSeverities.Warning, "csv.too-large", mod.ModId, mod.DirectoryName + "/" + relative, "CSV 超过健康检查大小上限。");
            return;
        }
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                DetectColumnCountChanges = true
            };
            using var stream = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var csv = new CsvReader(stream, config);
            if (!csv.Read() || !csv.ReadHeader())
            {
                Add(report, ModHealthSeverities.Error, "csv.header", mod.ModId, mod.DirectoryName + "/" + relative, "CSV 缺少表头。");
                return;
            }
            var headers = csv.HeaderRecord ?? Array.Empty<string>();
            var idIndex = Array.FindIndex(headers, header => string.Equals(header?.Trim(), "Id", StringComparison.OrdinalIgnoreCase));
            if (idIndex < 0)
            {
                Add(report, ModHealthSeverities.Error, "csv.id-column", mod.ModId, mod.DirectoryName + "/" + relative, "游戏表缺少 Id 列。");
                return;
            }
            var rowIndex = 0;
            while (csv.Read())
            {
                rowIndex++;
                var id = csv.GetField(idIndex)?.Trim() ?? "";
                if (rowIndex == 1 && LooksLikeDescriptionRow(id)) continue;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var key = kind + ":" + folder + ":" + id.Replace("*", "");
                if (!registrations.TryGetValue(key, out var sources)) registrations[key] = sources = new List<string>();
                sources.Add(mod.ModId + "/" + relative);
                for (var column = 0; column < headers.Length; column++)
                {
                    var value = csv.GetField(column) ?? "";
                    if (LooksLikeModResource(value) && !ResourceExists(root, value))
                    {
                        Add(report, ModHealthSeverities.Error, "resource.unresolved", mod.ModId,
                            mod.DirectoryName + "/" + relative + ":" + (rowIndex + 1),
                            "已注册的游戏表资源无法解析：" + value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Add(report, ModHealthSeverities.Error, "csv.parse", mod.ModId, mod.DirectoryName + "/" + relative,
                "CSV 无法按游戏表读取：" + ex.Message, ex);
        }
    }

    private static HashSet<string> LoadedDirectories(out bool available)
    {
        try
        {
            var field = typeof(GameConfigManager).GetField("loadedModDirectories", BindingFlags.Instance | BindingFlags.NonPublic);
            var values = field?.GetValue(Singleton<GameConfigManager>.Instance) as System.Collections.IEnumerable;
            available = values != null;
            return values == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(values.Cast<object>().Select(value => Normalize(Convert.ToString(value) ?? "")), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            available = false;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool ResourceExists(string modRoot, string declared)
    {
        var value = (declared ?? "").Trim().Trim('"').Replace('\\', '/');
        if (value.Length == 0) return true;
        string path;
        if (value.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(Globals.ModsPath, value.Substring("Mods/".Length).Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            path = Path.Combine(modRoot, value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
        if (File.Exists(path) || Directory.Exists(path)) return true;
        return new[] { ".png", ".jpg", ".jpeg", ".mp3", ".wav", ".ogg", ".json", ".asset", ".prefab" }
            .Any(extension => File.Exists(path + extension));
    }

    private static bool LooksLikeModResource(string value)
    {
        var text = (value ?? "").Trim().Replace('\\', '/');
        return text.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("ModResource/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDescriptionRow(string id)
    {
        return string.Equals(id, "id", StringComparison.OrdinalIgnoreCase)
               || id.IndexOf("唯一", StringComparison.OrdinalIgnoreCase) >= 0
               || id.IndexOf("标识", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Text(JObject json, string name)
    {
        return json.GetValue(name, StringComparison.OrdinalIgnoreCase)?.ToString().Trim() ?? "";
    }

    private static string Relative(string root, string path)
    {
        var rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return (path ?? "").Trim(); }
    }

    private static string Fingerprint(ModHealthReport report)
    {
        var text = report.GameVersion + "|" + string.Join("|", report.Mods.OrderBy(mod => mod.ModId).Select(mod => mod.ModId + ":" + mod.ModVersion + ":" + mod.Enabled + ":" + mod.Loaded))
                   + "|" + string.Join("|", report.Issues.OrderBy(issue => issue.Code).Select(issue => issue.Severity + ":" + issue.Code + ":" + issue.ModId));
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(value => value.ToString("x2")));
    }

    private static void Add(ModHealthReport report, string severity, string code, string modId, string relativePath, string message, Exception? exception = null)
    {
        report.Issues.Add(new ModHealthIssue
        {
            Severity = severity,
            Code = code,
            ModId = modId ?? "",
            RelativePath = (relativePath ?? "").Replace('\\', '/'),
            Message = RedactLocalRoots(message ?? ""),
            ExceptionType = exception?.GetType().Name ?? ""
        });
    }

    private static string RedactLocalRoots(string value)
    {
        var result = value ?? "";
        result = ReplaceInsensitive(result, Normalize(Globals.ModsPath), "<Mods>");
        result = ReplaceInsensitive(result, Normalize(Environment.CurrentDirectory), "<Game>");
        return result;
    }

    private static string ReplaceInsensitive(string value, string search, string replacement)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(search)) return value;
        var start = 0;
        while (true)
        {
            var index = value.IndexOf(search, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return value;
            value = value.Substring(0, index) + replacement + value.Substring(index + search.Length);
            start = index + replacement.Length;
        }
    }
}
