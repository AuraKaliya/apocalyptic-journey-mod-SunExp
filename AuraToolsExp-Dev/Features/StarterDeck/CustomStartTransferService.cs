using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraGameData.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.StarterDeck;

internal sealed class CustomStartContentReference
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "";
}

internal sealed class CustomStartTransferSource
{
    [JsonProperty("scope")]
    public string Scope { get; set; } = "Global";

    [JsonProperty("roleId")]
    public string RoleId { get; set; } = "";

    [JsonProperty("roleOwnerModId")]
    public string RoleOwnerModId { get; set; } = "";

    [JsonProperty("roleDisplayName")]
    public string RoleDisplayName { get; set; } = "";
}

internal sealed class CustomStartTransferDocument
{
    public const string FormatId = "AuraTools.CustomStart";
    public const int CurrentSchemaVersion = 1;

    [JsonProperty("format")]
    public string Format { get; set; } = FormatId;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("minimumReaderSchemaVersion")]
    public int MinimumReaderSchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonProperty("displayName")]
    public string DisplayName { get; set; } = "自定义开局";

    [JsonProperty("exportedUtc")]
    public string ExportedUtc { get; set; } = "";

    [JsonProperty("source")]
    public CustomStartTransferSource Source { get; set; } = new();

    [JsonProperty("cards")]
    public List<CustomStartContentReference> Cards { get; set; } = new();

    [JsonProperty("relics")]
    public List<CustomStartContentReference> Relics { get; set; } = new();
}

internal sealed class CustomStartImportPlan
{
    internal string SourcePath { get; set; } = "";

    internal string DisplayName { get; set; } = "";

    internal int SchemaVersion { get; set; }

    internal bool Compatible { get; set; }

    internal bool Legacy { get; set; }

    internal List<string> CardIds { get; set; } = new();

    internal List<string> RelicIds { get; set; } = new();

    internal List<string> Warnings { get; set; } = new();

    internal string Summary => "卡牌 " + CardIds.Count + "/" + StarterDeckSettings.MaximumCardCount
                               + "；遗物 " + RelicIds.Count + "/" + StarterDeckSettings.MaximumRelicCount;
}

internal static class CustomStartTransferService
{
    private const long MaximumFileBytes = 1024L * 1024L;

    internal static string Export(string roleId, bool global)
    {
        var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
        var settings = global
            ? AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile.Clone()
            : StarterDeckProfileResolver.EffectiveSettingsForExport(normalizedRole);
        var displayName = global
            ? "全局自定义开局"
            : RoleCatalog.GetDisplayName(normalizedRole) + " 自定义开局";
        var document = new CustomStartTransferDocument
        {
            DisplayName = displayName,
            ExportedUtc = DateTime.UtcNow.ToString("O"),
            Source = new CustomStartTransferSource
            {
                Scope = global ? "Global" : "Role",
                RoleId = global ? "" : normalizedRole,
                RoleOwnerModId = global ? "" : AuraGameDataHostApi.Resolve(DataType.Career, normalizedRole)?.OwnerModId ?? "",
                RoleDisplayName = global ? "" : RoleCatalog.GetDisplayName(normalizedRole)
            },
            Cards = settings.CardIds.Select(id => Reference(DataType.Card, id, StarterDeckCardPresentation.CardDisplayName(id))).ToList(),
            Relics = settings.RelicIds.Select(id => Reference(DataType.Relic, id, StarterRelicCatalog.DisplayName(id))).ToList()
        };

        var directory = Path.Combine(AuraToolsConfigService.DataRootDirectory, "Exports", "CustomStart");
        Directory.CreateDirectory(directory);
        var fileName = SafeFileName(displayName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".aurastart.json";
        var output = UniquePath(Path.Combine(directory, fileName));
        WriteAtomic(output, JsonConvert.SerializeObject(document, Formatting.Indented));
        return output;
    }

    internal static CustomStartImportPlan Inspect(string path)
    {
        var plan = new CustomStartImportPlan { SourcePath = path ?? "" };
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            plan.Warnings.Add("导入文件不存在。");
            return plan;
        }

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumFileBytes)
        {
            plan.Warnings.Add("导入文件大小不合法。");
            return plan;
        }

        if (!AuraGameDataHostApi.IsNativeCatalogReady)
        {
            plan.Warnings.Add("游戏内容目录尚未就绪，请稍后重试。");
            return plan;
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JObject.Parse(json);
            var format = root.Value<string>("format") ?? "";
            if (string.Equals(format, CustomStartTransferDocument.FormatId, StringComparison.Ordinal))
            {
                InspectCurrent(root.ToObject<CustomStartTransferDocument>() ?? new CustomStartTransferDocument(), plan);
                return plan;
            }

            if (root["profiles"] is JArray profiles)
            {
                InspectLegacyRegistry(root, profiles, plan);
                return plan;
            }

            plan.Warnings.Add("无法识别的自定义开局文件格式。");
        }
        catch (Exception ex)
        {
            plan.Warnings.Add("导入文件无法读取：" + ex.Message);
        }

        return plan;
    }

    internal static IReadOnlyList<CustomStartImportPlan> InspectAll(string path)
    {
        var first = Inspect(path);
        if (!first.Compatible || !first.Legacy)
        {
            return new[] { first };
        }

        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            if (root["profiles"] is not JArray profiles || profiles.Count <= 1)
            {
                return new[] { first };
            }

            return profiles
                .OfType<JObject>()
                .Select(profile =>
                {
                    var plan = new CustomStartImportPlan { SourcePath = path };
                    InspectLegacyProfile(root, profile, plan);
                    return plan;
                })
                .ToList();
        }
        catch
        {
            return new[] { first };
        }
    }

    internal static void Commit(CustomStartImportPlan plan, string roleId, bool global)
    {
        if (plan == null || !plan.Compatible)
        {
            throw new InvalidOperationException("导入计划不兼容，不能覆盖当前配置。");
        }
        if (AuraToolsConfigService.IsModuleConfigReadOnly(AuraToolModuleIds.StarterDeck))
        {
            throw new InvalidOperationException("当前自定义开局配置来自更新版本，处于只读状态。");
        }

        BackupCurrent(roleId, global);
        var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
        var previousGlobal = settings.GlobalProfile.Clone();
        var normalizedRoleForRollback = RoleCatalog.NormalizeRoleId(roleId);
        var hadPreviousRole = settings.Roles.TryGetValue(normalizedRoleForRollback, out var previousRoleValue);
        var previousRole = previousRoleValue?.Clone();
        if (global)
        {
            settings.GlobalProfile.CardIds = plan.CardIds.ToList();
            settings.GlobalProfile.RelicIds = plan.RelicIds.ToList();
            settings.GlobalProfile.InheritCards = false;
            settings.GlobalProfile.InheritRelics = false;
        }
        else
        {
            var normalizedRole = RoleCatalog.NormalizeRoleId(roleId);
            var role = StarterDeckLocalProfileStore.EnsureRoleSettings(normalizedRole, RoleCatalog.GetDisplayName(normalizedRole));
            role.InheritCards = false;
            role.InheritRelics = false;
            role.CardIds = plan.CardIds.ToList();
            role.RelicIds = plan.RelicIds.ToList();
        }

        bool saved;
        try
        {
            saved = AuraToolsConfigService.TrySaveStarterDeck();
        }
        catch
        {
            RestorePrevious();
            throw;
        }
        if (saved)
        {
            return;
        }

        RestorePrevious();
        throw new IOException("自定义开局配置写入失败，已恢复导入前配置。");

        void RestorePrevious()
        {
            settings.GlobalProfile = previousGlobal;
            if (hadPreviousRole && previousRole != null)
            {
                settings.Roles[normalizedRoleForRollback] = previousRole;
            }
            else
            {
                settings.Roles.Remove(normalizedRoleForRollback);
            }
        }
    }

    private static void InspectCurrent(CustomStartTransferDocument document, CustomStartImportPlan plan)
    {
        plan.DisplayName = string.IsNullOrWhiteSpace(document.DisplayName) ? "自定义开局" : document.DisplayName.Trim();
        plan.SchemaVersion = document.SchemaVersion;
        if (!string.Equals(document.Format, CustomStartTransferDocument.FormatId, StringComparison.Ordinal))
        {
            plan.Warnings.Add("自定义开局格式标识不匹配。");
            return;
        }

        if (document.MinimumReaderSchemaVersion > CustomStartTransferDocument.CurrentSchemaVersion)
        {
            plan.Warnings.Add("该文件需要更新版本的 AuraToolsExp。");
            return;
        }

        if (document.SchemaVersion > CustomStartTransferDocument.CurrentSchemaVersion)
        {
            plan.Warnings.Add("文件包含新版本扩展字段；本次只导入当前版本可识别的内容。");
        }

        ResolveReferences(document.Cards, DataType.Card, StarterDeckSettings.MaximumCardCount, preserveDuplicates: true, plan.CardIds, plan.Warnings);
        ResolveReferences(document.Relics, DataType.Relic, StarterDeckSettings.MaximumRelicCount, preserveDuplicates: false, plan.RelicIds, plan.Warnings);
        plan.Compatible = true;
    }

    private static void InspectLegacyRegistry(JObject root, JArray profiles, CustomStartImportPlan plan)
    {
        var profile = profiles.OfType<JObject>().FirstOrDefault();
        if (profile == null)
        {
            plan.Warnings.Add("旧版 Profile 文件不包含可导入项。");
            return;
        }

        InspectLegacyProfile(root, profile, plan);
    }

    private static void InspectLegacyProfile(JObject root, JObject profile, CustomStartImportPlan plan)
    {
        plan.Legacy = true;
        plan.SchemaVersion = root.Value<int?>("schemaVersion") ?? 1;
        plan.DisplayName = profile.Value<string>("displayName") ?? profile.Value<string>("profileId") ?? "旧版开局配置";
        var owner = root.Value<string>("ownerModId") ?? "";
        var cards = (profile["cardIds"] as JArray)?.Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new CustomStartContentReference { Id = value!, OwnerModId = owner })
            .ToList() ?? new List<CustomStartContentReference>();
        if ((profile["candidatePackIds"] as JArray)?.Count > 0)
        {
            plan.Warnings.Add("旧版候选卡包不会自动补牌，只导入显式 cardIds。");
        }

        ResolveReferences(cards, DataType.Card, StarterDeckSettings.MaximumCardCount, preserveDuplicates: true, plan.CardIds, plan.Warnings);
        plan.RelicIds.Clear();
        plan.Warnings.Add("旧版文件没有遗物字段；导入后开局遗物配置为空。");
        plan.Compatible = true;
    }

    private static void ResolveReferences(
        IEnumerable<CustomStartContentReference>? references,
        DataType dataType,
        int maximum,
        bool preserveDuplicates,
        ICollection<string> output,
        ICollection<string> warnings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var reference in references ?? Array.Empty<CustomStartContentReference>())
        {
            index++;
            if (output.Count >= maximum)
            {
                warnings.Add(dataType + " 超过上限，已忽略第 " + index + " 项及后续内容。");
                break;
            }

            var resolved = ResolveReference(dataType, reference);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                warnings.Add("未检索到 " + dataType + "：" + DisplayReference(reference) + "，已忽略。");
                continue;
            }

            if (dataType == DataType.Card && StarterDeckCardCatalog.IsStarterDeckExcludedCard(resolved))
            {
                warnings.Add("卡牌按当前开局过滤规则不可用：" + DisplayReference(reference) + "，已忽略。");
                continue;
            }

            if (!preserveDuplicates && !seen.Add(resolved))
            {
                warnings.Add("重复遗物已忽略：" + DisplayReference(reference) + "。");
                continue;
            }

            output.Add(resolved);
        }
    }

    private static string ResolveReference(DataType dataType, CustomStartContentReference reference)
    {
        if (reference == null)
        {
            return "";
        }

        var declared = (reference.Id ?? "").Trim();
        if (declared.Length == 0)
        {
            return "";
        }

        var exact = AuraGameDataHostApi.Resolve(dataType, declared);
        if (exact != null)
        {
            return exact.Id;
        }

        var ids = AuraGameDataHostApi.Table(dataType).Select(snapshot => snapshot.Id);
        var compatible = AuraSharedContentId.Resolve(declared, ids, reference.OwnerModId);
        return compatible.Success ? compatible.ResolvedId : "";
    }

    private static CustomStartContentReference Reference(DataType dataType, string id, string displayName)
    {
        var snapshot = AuraGameDataHostApi.Resolve(dataType, id);
        return new CustomStartContentReference
        {
            Id = snapshot?.Id ?? id,
            OwnerModId = snapshot?.OwnerModId ?? "",
            DisplayName = displayName ?? id
        };
    }

    private static string DisplayReference(CustomStartContentReference reference)
    {
        if (reference == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(reference.DisplayName))
        {
            return reference.DisplayName;
        }

        return "已失效内容";
    }

    private static void BackupCurrent(string roleId, bool global)
    {
        try
        {
            var directory = Path.Combine(AuraToolsConfigService.DataRootDirectory, "Backups", "CustomStart");
            Directory.CreateDirectory(directory);
            var settings = global
                ? AuraToolsConfigService.MatchExperience.StarterDeck.GlobalProfile.Clone()
                : StarterDeckProfileResolver.EffectiveSettingsForExport(roleId);
            var name = global ? "global" : SafeFileName(RoleCatalog.GetDisplayName(roleId));
            var path = UniquePath(Path.Combine(directory, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            WriteAtomic(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CustomStart] failed to back up current configuration: " + ex.Message);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        AuraSharedFileStore.WriteAllText(AuraToolsIds.ModId, path, text);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? "自定义开局").Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (safe.Length == 0) safe = "自定义开局";
        return safe.Length > 64 ? safe.Substring(0, 64) : safe;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var fileName = Path.GetFileName(path);
        var extension = fileName.EndsWith(".aurastart.json", StringComparison.OrdinalIgnoreCase)
            ? ".aurastart.json"
            : Path.GetExtension(fileName);
        var name = fileName.Substring(0, fileName.Length - extension.Length);
        for (var index = 2; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, name + "-" + index + extension);
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
    }
}
