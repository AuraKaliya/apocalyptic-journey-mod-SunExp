using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuraToolsExp.Dll.Features.PresetLibrary;

internal sealed class AuraPresetModuleEntry
{
    [JsonProperty("moduleId")]
    public string ModuleId { get; set; } = "";

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("minimumReaderVersion")]
    public int MinimumReaderVersion { get; set; } = 1;

    [JsonProperty("payload")]
    public JObject Payload { get; set; } = new();
}

internal sealed class AuraPresetDocument
{
    internal const string FormatId = "AuraTools.Preset";
    internal const int CurrentSchemaVersion = 1;

    [JsonProperty("format")]
    public string Format { get; set; } = FormatId;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("minimumReaderVersion")]
    public int MinimumReaderVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("presetId")]
    public string PresetId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("createdUtc")]
    public string CreatedUtc { get; set; } = "";

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonProperty("modules")]
    public List<AuraPresetModuleEntry> Modules { get; set; } = new();
}

internal sealed class AuraPresetModuleInspection
{
    internal string ModuleId { get; set; } = "";
    internal string DisplayName { get; set; } = "";
    internal bool Compatible { get; set; }
    internal bool Changed { get; set; }
    internal JObject NormalizedPayload { get; set; } = new();
    internal List<string> Warnings { get; set; } = new();
    internal string Error { get; set; } = "";
}

internal sealed class AuraPresetInspection
{
    internal string Path { get; set; } = "";
    internal AuraPresetDocument Document { get; set; } = new();
    internal bool Compatible { get; set; }
    internal List<AuraPresetModuleInspection> Modules { get; set; } = new();
    internal List<string> Warnings { get; set; } = new();
    internal int ChangedCount => Modules.Count(module => module.Compatible && module.Changed);
}

internal sealed class AuraPresetSummary
{
    internal string DisplayName { get; set; } = "";
    internal int ModuleCount { get; set; }
    internal bool CompatibleFormat { get; set; }
    internal string Warning { get; set; } = "";
}

internal static class AuraPresetLibraryService
{
    private const long MaximumFileBytes = 2L * 1024L * 1024L;
    private static int cachedCount;

    internal static string DirectoryPath => Path.Combine(AuraToolsConfigService.DataRootDirectory, "PresetLibrary");
    internal static string BackupDirectory => Path.Combine(DirectoryPath, "Backups");
    internal static int CachedCount => cachedCount;

    internal static void RefreshCount()
    {
        _ = ListPresetFiles();
    }

    internal static IReadOnlyList<string> ListPresetFiles()
    {
        Directory.CreateDirectory(DirectoryPath);
        var files = Directory.GetFiles(DirectoryPath, "*.aurapreset.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        cachedCount = files.Length;
        return files;
    }

    internal static string CreateFromCurrent(
        string displayName,
        IEnumerable<string>? moduleIds = null,
        bool countAgainstLimit = true)
    {
        if (countAgainstLimit) EnsureCapacity();
        var selected = new HashSet<string>(
            moduleIds ?? AuraToolConfigCodecRegistry.All.Select(codec => codec.ModuleId),
            StringComparer.Ordinal);
        var now = DateTime.UtcNow.ToString("O");
        var document = new AuraPresetDocument
        {
            PresetId = Guid.NewGuid().ToString("N"),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "我的妙妙方案" : displayName.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now,
            Modules = AuraToolConfigCodecRegistry.All
                .Where(codec => selected.Contains(codec.ModuleId))
                .Select(codec => new AuraPresetModuleEntry
                {
                    ModuleId = codec.ModuleId,
                    SchemaVersion = codec.SchemaVersion,
                    MinimumReaderVersion = codec.MinimumReaderVersion,
                    Payload = codec.Export()
                })
                .ToList()
        };
        var path = UniquePath(Path.Combine(DirectoryPath, SafeName(document.DisplayName) + ".aurapreset.json"));
        WriteAtomic(path, JsonConvert.SerializeObject(document, Formatting.Indented));
        if (countAgainstLimit) RefreshCount();
        return path;
    }

    internal static AuraPresetSummary ReadSummary(string path)
    {
        var result = new AuraPresetSummary { DisplayName = Path.GetFileNameWithoutExtension(path ?? "") };
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumFileBytes)
            {
                result.Warning = "文件大小不合法";
                return result;
            }
            var document = JsonConvert.DeserializeObject<AuraPresetDocument>(File.ReadAllText(path));
            if (document == null)
            {
                result.Warning = "方案内容为空";
                return result;
            }
            result.DisplayName = string.IsNullOrWhiteSpace(document.DisplayName) ? result.DisplayName : document.DisplayName;
            result.ModuleCount = document.Modules?.Count ?? 0;
            result.CompatibleFormat = string.Equals(document.Format, AuraPresetDocument.FormatId, StringComparison.Ordinal)
                                      && document.MinimumReaderVersion <= AuraPresetDocument.CurrentSchemaVersion;
            if (!result.CompatibleFormat) result.Warning = "需要更新版本读取";
        }
        catch (Exception ex)
        {
            result.Warning = "无法读取：" + ex.Message;
        }
        return result;
    }

    internal static AuraPresetInspection Inspect(string path)
    {
        var result = new AuraPresetInspection { Path = path ?? "" };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            result.Warnings.Add("方案文件不存在。");
            return result;
        }
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumFileBytes)
        {
            result.Warnings.Add("方案文件大小不合法。");
            return result;
        }
        try
        {
            var document = JsonConvert.DeserializeObject<AuraPresetDocument>(File.ReadAllText(path))
                           ?? throw new InvalidDataException("方案内容为空。");
            result.Document = document;
            if (!string.Equals(document.Format, AuraPresetDocument.FormatId, StringComparison.Ordinal)
                || document.MinimumReaderVersion > AuraPresetDocument.CurrentSchemaVersion)
            {
                result.Warnings.Add("方案格式或最低读取版本不兼容。");
                return result;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.Modules ?? new List<AuraPresetModuleEntry>())
            {
                var moduleId = entry.ModuleId ?? "";
                if (!seen.Add(moduleId))
                {
                    result.Modules.Add(new AuraPresetModuleInspection
                    {
                        ModuleId = moduleId,
                        DisplayName = moduleId,
                        Error = "方案包含重复模块配置。"
                    });
                    continue;
                }
                if (!AuraToolConfigCodecRegistry.TryGet(moduleId, out var codec))
                {
                    result.Warnings.Add("当前版本没有模块 Codec：" + moduleId + "，已忽略。");
                    continue;
                }
                var inspected = codec.Inspect(entry.Payload ?? new JObject(), entry.SchemaVersion, entry.MinimumReaderVersion);
                if (AuraToolsConfigService.IsModuleConfigReadOnly(moduleId))
                {
                    inspected.Compatible = false;
                    inspected.Error = "模块配置来自更新版本，当前为只读状态。";
                }
                result.Modules.Add(new AuraPresetModuleInspection
                {
                    ModuleId = moduleId,
                    DisplayName = codec.Audit.DisplayName,
                    Compatible = inspected.Compatible,
                    Changed = inspected.Compatible && !JToken.DeepEquals(codec.Export(), inspected.NormalizedPayload),
                    NormalizedPayload = inspected.NormalizedPayload,
                    Warnings = inspected.Warnings,
                    Error = inspected.Error
                });
            }
            result.Compatible = result.Modules.Count > 0 && result.Modules.All(module => module.Compatible);
        }
        catch (Exception ex)
        {
            result.Warnings.Add("方案无法读取：" + ex.Message);
        }
        return result;
    }

    internal static void Apply(AuraPresetInspection inspection)
    {
        if (inspection == null || !inspection.Compatible)
        {
            throw new InvalidOperationException("方案尚未通过兼容性预检。");
        }
        var applicable = inspection.Modules
            .Where(module => module.Compatible && module.Changed)
            .OrderBy(module => CodecOrder(module.ModuleId))
            .ToList();
        if (applicable.Count == 0) return;
        foreach (var module in applicable)
        {
            if (AuraToolsConfigService.IsModuleConfigReadOnly(module.ModuleId))
            {
                throw new InvalidOperationException("模块配置处于只读状态：" + module.ModuleId);
            }
        }
        Directory.CreateDirectory(BackupDirectory);
        var backupPath = CreateFromCurrent(
            "应用前备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            applicable.Select(module => module.ModuleId),
            countAgainstLimit: false);
        var movedBackup = Path.Combine(BackupDirectory, Path.GetFileName(backupPath));
        AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, backupPath, UniquePath(movedBackup));
        PruneBackups(20);

        var previous = applicable.ToDictionary(
            module => module.ModuleId,
            module => AuraToolConfigCodecRegistry.TryGet(module.ModuleId, out var codec) ? codec.Export() : new JObject(),
            StringComparer.Ordinal);
        var committed = new List<string>();
        using var batch = AuraToolConfigChangeBus.BeginBatch();
        try
        {
            foreach (var module in applicable)
            {
                if (!AuraToolConfigCodecRegistry.TryGet(module.ModuleId, out var codec))
                {
                    continue;
                }
                committed.Add(module.ModuleId);
                codec.Commit(module.NormalizedPayload);
            }
        }
        catch
        {
            foreach (var moduleId in committed.AsEnumerable().Reverse())
            {
                if (AuraToolConfigCodecRegistry.TryGet(moduleId, out var codec))
                {
                    try { codec.Commit(previous[moduleId]); } catch { }
                }
            }
            throw;
        }
    }

    internal static string Import(string sourcePath)
    {
        EnsureCapacity();
        var inspection = Inspect(sourcePath);
        if (!inspection.Compatible)
        {
            throw new InvalidDataException("导入方案未通过兼容性预检。");
        }
        Directory.CreateDirectory(DirectoryPath);
        var target = UniquePath(Path.Combine(DirectoryPath, SafeName(inspection.Document.DisplayName) + ".aurapreset.json"));
        File.Copy(sourcePath, target, overwrite: false);
        RefreshCount();
        return target;
    }

    internal static string Duplicate(string path)
    {
        EnsureCapacity();
        var inspection = Inspect(path);
        if (!inspection.Compatible) throw new InvalidDataException("方案无法复制。");
        inspection.Document.PresetId = Guid.NewGuid().ToString("N");
        inspection.Document.DisplayName += " 副本";
        inspection.Document.UpdatedUtc = DateTime.UtcNow.ToString("O");
        var target = UniquePath(Path.Combine(DirectoryPath, SafeName(inspection.Document.DisplayName) + ".aurapreset.json"));
        WriteAtomic(target, JsonConvert.SerializeObject(inspection.Document, Formatting.Indented));
        RefreshCount();
        return target;
    }

    internal static string Rename(string path, string displayName)
    {
        path = RequireLibraryFile(path);
        var inspection = Inspect(path);
        if (!inspection.Compatible) throw new InvalidDataException("方案无法重命名。");
        inspection.Document.DisplayName = string.IsNullOrWhiteSpace(displayName) ? inspection.Document.DisplayName : displayName.Trim();
        inspection.Document.UpdatedUtc = DateTime.UtcNow.ToString("O");
        var target = UniquePath(Path.Combine(DirectoryPath, SafeName(inspection.Document.DisplayName) + ".aurapreset.json"));
        WriteAtomic(target, JsonConvert.SerializeObject(inspection.Document, Formatting.Indented));
        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Delete(path);
        RefreshCount();
        return target;
    }

    internal static void Delete(string path)
    {
        var full = RequireLibraryFile(path);
        if (File.Exists(full)) File.Delete(full);
        RefreshCount();
    }

    private static string RequireLibraryFile(string path)
    {
        var full = Path.GetFullPath(path ?? "");
        var root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝修改方案库之外的文件。");
        }
        return full;
    }

    private static void WriteAtomic(string path, string text)
    {
        AuraSharedFileStore.WriteAllText(AuraToolsIds.ModId, path, text);
    }

    private static void EnsureCapacity()
    {
        var maximum = Math.Max(1, AuraToolsConfigService.PresetLibrary.MaximumPresets);
        if (ListPresetFiles().Count >= maximum)
        {
            throw new InvalidOperationException("方案库已达到上限 " + maximum + "。请先删除不需要的方案。");
        }
    }

    private static void PruneBackups(int maximum)
    {
        if (!Directory.Exists(BackupDirectory)) return;
        foreach (var path in Directory.GetFiles(BackupDirectory, "*.aurapreset.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(Math.Max(1, maximum)))
        {
            try { File.Delete(path); }
            catch (Exception ex) { AuraToolsLog.Warn("[PresetLibrary] stale backup cleanup failed: " + ex.Message); }
        }
    }

    private static int CodecOrder(string moduleId)
    {
        for (var index = 0; index < AuraToolConfigCodecRegistry.All.Count; index++)
        {
            if (string.Equals(AuraToolConfigCodecRegistry.All[index].ModuleId, moduleId, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return int.MaxValue;
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? "妙妙方案").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (safe.Length == 0) safe = "妙妙方案";
        return safe.Length > 64 ? safe.Substring(0, 64) : safe;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var file = Path.GetFileName(path);
        const string suffix = ".aurapreset.json";
        var name = file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? file.Substring(0, file.Length - suffix.Length)
            : Path.GetFileNameWithoutExtension(file);
        var extension = file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? suffix : Path.GetExtension(file);
        for (var index = 2; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, name + "-" + index + extension);
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
    }
}
