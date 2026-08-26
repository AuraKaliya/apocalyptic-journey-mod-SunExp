using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using AuraGameData.Shared.GameApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Witch;

namespace AuraToolsExp.Dll.Features.PresetLibrary;

internal sealed class AuraToolConfigCodecAudit
{
    internal string ModuleId { get; set; } = "";
    internal string DisplayName { get; set; } = "";
    internal string Risk { get; set; } = "low";
    internal string ExportedSurface { get; set; } = "settings";
    internal string ExcludedSurface { get; set; } = "runtime state";
    internal string[] Dependencies { get; set; } = Array.Empty<string>();
}

internal sealed class AuraToolConfigCodecInspection
{
    internal bool Compatible { get; set; }
    internal JObject NormalizedPayload { get; set; } = new();
    internal List<string> Warnings { get; set; } = new();
    internal string Error { get; set; } = "";
}

internal interface IAuraToolConfigCodec
{
    string ModuleId { get; }
    int SchemaVersion { get; }
    int MinimumReaderVersion { get; }
    AuraToolConfigCodecAudit Audit { get; }
    JObject Export();
    AuraToolConfigCodecInspection Inspect(JObject payload, int schemaVersion, int minimumReaderVersion);
    void Commit(JObject normalizedPayload);
}

internal sealed class DelegateAuraToolConfigCodec<T> : IAuraToolConfigCodec where T : class, new()
{
    private readonly Func<T> capture;
    private readonly Action<T> normalize;
    private readonly Action<T> commit;
    private readonly Func<JObject, JObject>? sanitize;
    private readonly Func<JObject, JObject>? importSanitize;
    private readonly Func<T, IEnumerable<string>>? warnings;

    internal DelegateAuraToolConfigCodec(
        AuraToolConfigCodecAudit audit,
        int schemaVersion,
        Func<T> capture,
        Action<T> normalize,
        Action<T> commit,
        Func<JObject, JObject>? sanitize = null,
        Func<JObject, JObject>? importSanitize = null,
        Func<T, IEnumerable<string>>? warnings = null)
    {
        Audit = audit;
        SchemaVersion = Math.Max(1, schemaVersion);
        MinimumReaderVersion = 1;
        this.capture = capture;
        this.normalize = normalize;
        this.commit = commit;
        this.sanitize = sanitize;
        this.importSanitize = importSanitize;
        this.warnings = warnings;
    }

    public string ModuleId => Audit.ModuleId;
    public int SchemaVersion { get; }
    public int MinimumReaderVersion { get; }
    public AuraToolConfigCodecAudit Audit { get; }

    public JObject Export()
    {
        var value = Clone(capture());
        normalize(value);
        var payload = JObject.FromObject(value, JsonSerializer.CreateDefault());
        return sanitize?.Invoke(payload) ?? payload;
    }

    public AuraToolConfigCodecInspection Inspect(JObject payload, int schemaVersion, int minimumReaderVersion)
    {
        var result = new AuraToolConfigCodecInspection();
        if (minimumReaderVersion > SchemaVersion)
        {
            result.Error = "配置需要更新版本的模块 Codec。";
            return result;
        }
        if (schemaVersion > SchemaVersion)
        {
            result.Warnings.Add("文件来自更新 Schema；只读取当前版本识别的字段。");
        }
        try
        {
            var value = payload.ToObject<T>() ?? new T();
            normalize(value);
            result.NormalizedPayload = JObject.FromObject(value, JsonSerializer.CreateDefault());
            if (sanitize != null)
            {
                result.NormalizedPayload = sanitize(result.NormalizedPayload);
            }
            if (importSanitize != null)
            {
                result.NormalizedPayload = importSanitize(result.NormalizedPayload);
            }
            if (result.NormalizedPayload.Property("schemaVersion", StringComparison.OrdinalIgnoreCase) is { } schema)
            {
                schema.Value = SchemaVersion;
            }
            if (warnings != null)
            {
                result.Warnings.AddRange(warnings(value).Where(text => !string.IsNullOrWhiteSpace(text)));
            }
            result.Compatible = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    public void Commit(JObject normalizedPayload)
    {
        var value = normalizedPayload.ToObject<T>() ?? throw new InvalidDataException("模块配置无法反序列化：" + ModuleId);
        normalize(value);
        commit(value);
    }

    private static T Clone(T value)
    {
        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value)) ?? new T();
    }
}

internal sealed class FeastGameplayPortableSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = FeastSettings.CurrentSchemaVersion;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("maxBatchCount")]
    public int MaxBatchCount { get; set; } = 64;

    public void Normalize()
    {
        SchemaVersion = Math.Max(FeastSettings.CurrentSchemaVersion, SchemaVersion);
        MaxBatchCount = Math.Max(1, Math.Min(128, MaxBatchCount));
    }
}

internal static class AuraToolConfigCodecRegistry
{
    private static readonly IReadOnlyList<IAuraToolConfigCodec> Codecs = Build();
    private static readonly Dictionary<string, IAuraToolConfigCodec> ById = Codecs
        .ToDictionary(codec => codec.ModuleId, StringComparer.Ordinal);

    internal static IReadOnlyList<IAuraToolConfigCodec> All => Codecs;

    internal static bool TryGet(string moduleId, out IAuraToolConfigCodec codec)
    {
        return ById.TryGetValue(moduleId ?? "", out codec!);
    }

    private static IReadOnlyList<IAuraToolConfigCodec> Build()
    {
        var result = new List<IAuraToolConfigCodec>
        {
            Codec(Audit(AuraToolModuleIds.StarterDeck, "自定义开局", "resource", "卡牌、遗物、继承和模式", "运行时应用标记"),
                StarterDeckSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.MatchExperience.StarterDeck,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.StarterDeck, value,
                    item => AuraToolsConfigService.MatchExperience.StarterDeck = item, AuraToolsConfigService.SaveStarterDeck),
                importSanitize: SanitizeStarterDeck,
                warnings: StarterDeckWarnings),
            Codec(Audit(AuraToolModuleIds.CardRefresh, "卡牌刷新", "low"), 1,
                () => AuraToolsConfigService.MatchExperience.CardRefresh,
                _ => { },
                value => CommitSetting(AuraToolModuleIds.CardRefresh, value,
                    item => AuraToolsConfigService.MatchExperience.CardRefresh = item, AuraToolsConfigService.SaveCardRefresh)),
            Codec(Audit(AuraToolModuleIds.Feast, "一键美餐", "low", "主开关与批处理上限", "角色 CG 的美餐资源子配置"),
                FeastSettings.CurrentSchemaVersion,
                () => new FeastGameplayPortableSettings
                {
                    Enabled = AuraToolsConfigService.MatchExperience.Feast.Enabled,
                    MaxBatchCount = AuraToolsConfigService.MatchExperience.Feast.MaxBatchCount
                },
                value => value.Normalize(),
                value =>
                {
                    CommitSetting(AuraToolModuleIds.Feast, value, item =>
                    {
                        var current = AuraToolsConfigService.MatchExperience.Feast;
                        current.Enabled = item.Enabled;
                        current.MaxBatchCount = item.MaxBatchCount;
                    }, AuraToolsConfigService.SaveFeast);
                }),
            Codec(Audit(AuraToolModuleIds.FeastCg, "角色 CG · 美餐资源", "resource", "美餐角色规则、选择与相对资源引用", "图片文件正文", AuraToolModuleIds.SkillCg, AuraToolModuleIds.Feast),
                FeastCgSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.MatchExperience.Feast.Cg,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.FeastCg, value,
                    item => AuraToolsConfigService.MatchExperience.Feast.Cg = item, AuraToolsConfigService.SaveFeastCg)),
            Codec(Audit(AuraToolModuleIds.SafeBox, "随身保险箱", "low"), 1,
                () => AuraToolsConfigService.MatchExperience.SafeBox,
                _ => { },
                value => CommitSetting(AuraToolModuleIds.SafeBox, value,
                    item => AuraToolsConfigService.MatchExperience.SafeBox = item, AuraToolsConfigService.SaveSafeBox)),
            Codec(Audit(AuraToolModuleIds.Skin, "角色皮肤", "resource", "开关、选择与资源引用", "皮肤包文件"), 3,
                () => AuraToolsConfigService.Skin,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.Skin, value,
                    item => AuraToolsConfigService.Skin = item, AuraToolsConfigService.SaveSkin)),
            Codec(Audit(AuraToolModuleIds.BattleBgm, "战斗背景音乐", "resource", "模式、角色规则与相对音频路径", "音频文件正文"), 1,
                () => AuraToolsConfigService.Audio.BattleBgm,
                value => value.Normalize("Audio/Global/all/BattleBgm/AuraToolsExp/default-battle-bgm/content.mp3", -1000, false),
                value => CommitSetting(AuraToolModuleIds.BattleBgm, value,
                    item => AuraToolsConfigService.Audio.BattleBgm = item, AuraToolsConfigService.SaveBattleBgm),
                warnings: AudioWarnings),
            Codec(Audit(AuraToolModuleIds.CardUseAudio, "出牌音效", "resource", "模式、角色规则与相对音频路径", "音频文件正文"), 1,
                () => AuraToolsConfigService.Audio.CardUse,
                value => value.Normalize("Audio/Global/all/CardUse/AuraToolsExp/default-card-use/content.mp3", -1000, false),
                value => CommitSetting(AuraToolModuleIds.CardUseAudio, value,
                    item => AuraToolsConfigService.Audio.CardUse = item, AuraToolsConfigService.SaveCardUseAudio),
                warnings: AudioWarnings),
            Codec(Audit(AuraToolModuleIds.PixelEmoji, "像素表情", "data", "启停、同步和收藏引用", "作品库帧数据"), AuraToolsPixelEmojiSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.PixelEmoji,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.PixelEmoji, value,
                    item => AuraToolsConfigService.PixelEmoji = item, AuraToolsConfigService.SavePixelEmoji)),
            Codec(Audit(AuraToolModuleIds.SkillCg, "角色 CG", "resource", "技能、美餐与低生命规则和资源引用", "卡牌/事件 CG 子配置、图片正文"), AuraToolsSkillCgSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.SkillCg,
                value => value.Normalize(),
                value =>
                {
                    value.CardUseCg = AuraToolsConfigService.SkillCg.CardUseCg;
                    value.EventCg = AuraToolsConfigService.SkillCg.EventCg;
                    CommitSetting(AuraToolModuleIds.SkillCg, value,
                        item => AuraToolsConfigService.SkillCg = item, AuraToolsConfigService.SaveSkillCg);
                },
                payload =>
                {
                    payload.Remove("cardUseCg");
                    payload.Remove("eventCg");
                    return payload;
                }),
            Codec(Audit(AuraToolModuleIds.CardUseCg, "卡牌 CG", "resource", "注册项启停引用", "CG 图片正文"), AuraToolsCardUseCgSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.SkillCg.CardUseCg,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.CardUseCg, value,
                    item => AuraToolsConfigService.SkillCg.CardUseCg = item, AuraToolsConfigService.SaveCardUseCg)),
            Codec(Audit(AuraToolModuleIds.EventCg, "事件 CG", "resource", "事件触发、背景与队伍场景表现", "CG 图片正文"), AuraToolsEventCgSettings.CurrentSchemaVersion,
                () => AuraToolsConfigService.SkillCg.EventCg,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.EventCg, value,
                    item => AuraToolsConfigService.SkillCg.EventCg = item, AuraToolsConfigService.SaveEventCg)),
            Codec(Audit(AuraToolModuleIds.DamageStatistics, "DPT 统计", "data", "统计展示与采集设置", "伤害数据库"), 1,
                () => AuraToolsConfigService.MatchExperience.DamageMeter,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.DamageStatistics, value,
                    item => AuraToolsConfigService.MatchExperience.MatchRecords.Statistics = item, AuraToolsConfigService.SaveDamageStatistics)),
            Codec(Audit(AuraToolModuleIds.BattleReplay, "战斗回放", "data", "记录、保留与导出设置", "回放数据库、视频和临时帧"), 1,
                () => AuraToolsConfigService.MatchExperience.MatchRecords.Replay,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.BattleReplay, value,
                    item => AuraToolsConfigService.MatchExperience.MatchRecords.Replay = item, AuraToolsConfigService.SaveBattleReplay)),
            Codec(Audit(AuraToolModuleIds.ModSync, "MOD 配置同步", "low"), 1,
                () => AuraToolsConfigService.MatchExperience.ModSync,
                _ => { },
                value => CommitSetting(AuraToolModuleIds.ModSync, value,
                    item => AuraToolsConfigService.MatchExperience.ModSync = item, AuraToolsConfigService.SaveModSync)),
            Codec(Audit(AuraToolModuleIds.AutoBattle, "自动战斗与策略模型实验室", "high", "运行模式、模型引用和评估参数", "模型文件、训练样本、模型风险确认记录"), 1,
                () => AuraToolsConfigService.MatchExperience.AutoBattle,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.AutoBattle, value,
                    item => AuraToolsConfigService.MatchExperience.AutoBattle = item, AuraToolsConfigService.SaveAutoBattle),
                SanitizeAutoBattle),
            Codec(Audit(AuraToolModuleIds.FileLogging, "文件日志", "low", "日志等级、镜像与保留设置", "日志正文和机器路径"), 5,
                () => AuraToolsConfigService.Logging,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.FileLogging, value,
                    item => AuraToolsConfigService.Logging = item, AuraToolsConfigService.SaveLogging)),
            Codec(Audit(AuraToolModuleIds.PresetLibrary, "妙妙方案库", "low", "启停与方案数量上限", "方案文件与应用前备份"), 1,
                () => AuraToolsConfigService.PresetLibrary,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.PresetLibrary, value,
                    item => AuraToolsConfigService.PresetLibrary = item, AuraToolsConfigService.SavePresetLibrary)),
            Codec(Audit(AuraToolModuleIds.ModHealth, "MOD 健康检查", "low"), 1,
                () => AuraToolsConfigService.ModHealth,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.ModHealth, value,
                    item => AuraToolsConfigService.ModHealth = item, AuraToolsConfigService.SaveModHealth)),
            Codec(Audit(AuraToolModuleIds.LobbyStatus, "大厅状态面板", "low"), 1,
                () => AuraToolsConfigService.LobbyStatus,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.LobbyStatus, value,
                    item => AuraToolsConfigService.LobbyStatus = item, AuraToolsConfigService.SaveLobbyStatus)),
            Codec(Audit(AuraToolModuleIds.AdventureArchive, "冒险历程", "data", "记录开关与保留策略", "冒险数据库"), 1,
                () => AuraToolsConfigService.AdventureArchive,
                value => value.Normalize(),
                value => CommitSetting(AuraToolModuleIds.AdventureArchive, value,
                    item => AuraToolsConfigService.AdventureArchive = item, AuraToolsConfigService.SaveAdventureArchive))
        };
        return result;
    }

    private static DelegateAuraToolConfigCodec<T> Codec<T>(
        AuraToolConfigCodecAudit audit,
        int schemaVersion,
        Func<T> capture,
        Action<T> normalize,
        Action<T> commit,
        Func<JObject, JObject>? sanitize = null,
        Func<JObject, JObject>? importSanitize = null,
        Func<T, IEnumerable<string>>? warnings = null) where T : class, new()
    {
        return new DelegateAuraToolConfigCodec<T>(audit, schemaVersion, capture, normalize, commit, sanitize, importSanitize, warnings);
    }

    private static void CommitSetting<T>(string moduleId, T value, Action<T> assign, Action save)
    {
        assign(value);
        save();
        AuraToolsConfigService.RequireLastModuleSaveSucceeded(moduleId);
    }

    private static AuraToolConfigCodecAudit Audit(
        string moduleId,
        string displayName,
        string risk,
        string exported = "用户设置",
        string excluded = "运行态、缓存和生成数据",
        params string[] dependencies)
    {
        return new AuraToolConfigCodecAudit
        {
            ModuleId = moduleId,
            DisplayName = displayName,
            Risk = risk,
            ExportedSurface = exported,
            ExcludedSurface = excluded,
            Dependencies = dependencies ?? Array.Empty<string>()
        };
    }

    private static JObject SanitizeAutoBattle(JObject payload)
    {
        payload.Remove("experimentalModelAcknowledgement");
        payload["modelRiskAcknowledgements"] = new JArray();
        payload["captureTrainingSamples"] = false;
        RemoveResolvedFields(payload);
        return payload;
    }

    private static JObject SanitizeStarterDeck(JObject payload)
    {
        var settings = payload.ToObject<StarterDeckSettings>() ?? new StarterDeckSettings();
        settings.Normalize();
        FilterStarterProfile(settings.GlobalProfile);
        settings.Roles = settings.Roles
            .Where(pair => IsRegisteredRole(pair.Key))
            .ToDictionary(pair => pair.Key, pair =>
            {
                FilterStarterProfile(pair.Value);
                return pair.Value;
            }, StringComparer.OrdinalIgnoreCase);
        return JObject.FromObject(settings, JsonSerializer.CreateDefault());
    }

    private static void FilterStarterProfile(StarterDeckLocalProfileSettings profile)
    {
        profile.CardIds = (profile.CardIds ?? new List<string>())
            .Select(id => StarterDeckCardCatalog.ResolveCardId(id))
            .Where(id => StarterDeckCardCatalog.IsValidCard(id)
                         && !StarterDeckCardCatalog.IsStarterDeckExcludedCard(id))
            .Take(StarterDeckSettings.MaximumCardCount)
            .ToList();
        profile.RelicIds = (profile.RelicIds ?? new List<string>())
            .Select(id => StarterRelicCatalog.ResolveRelicId(id))
            .Where(StarterRelicCatalog.IsValidRelic)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(StarterDeckSettings.MaximumRelicCount)
            .ToList();
    }

    private static IEnumerable<string> StarterDeckWarnings(StarterDeckSettings settings)
    {
        var profiles = new[] { settings.GlobalProfile }
            .Concat((settings.Roles ?? new Dictionary<string, StarterDeckLocalProfileSettings>()).Values);
        foreach (var cardId in profiles.SelectMany(profile => profile?.CardIds ?? new List<string>())
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = StarterDeckCardCatalog.ResolveCardId(cardId);
            if (!StarterDeckCardCatalog.IsValidCard(resolved))
            {
                yield return "当前未注册卡牌，导入时已忽略：" + cardId;
            }
            else if (StarterDeckCardCatalog.IsStarterDeckExcludedCard(resolved))
            {
                yield return "卡牌按现有自定义开局规则不可选，导入时已忽略：" + cardId;
            }
        }
        foreach (var relicId in profiles.SelectMany(profile => profile?.RelicIds ?? new List<string>())
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = StarterRelicCatalog.ResolveRelicId(relicId);
            if (!StarterRelicCatalog.IsValidRelic(resolved))
            {
                yield return "当前未注册遗物，导入时已忽略：" + relicId;
            }
        }
        foreach (var roleId in (settings.Roles ?? new Dictionary<string, StarterDeckLocalProfileSettings>()).Keys
                     .Where(id => !IsRegisteredRole(id)))
        {
            yield return "当前未注册角色，其覆盖配置已忽略：" + roleId;
        }
    }

    private static bool IsRegisteredRole(string roleId)
    {
        try { return AuraGameDataHostApi.Resolve(DataType.Career, roleId) != null; }
        catch { return false; }
    }

    private static void RemoveResolvedFields(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if (property.Name.StartsWith("resolved", StringComparison.OrdinalIgnoreCase))
                {
                    property.Remove();
                }
                else
                {
                    RemoveResolvedFields(property.Value);
                }
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                RemoveResolvedFields(child);
            }
        }
    }

    private static IEnumerable<string> AudioWarnings(AudioFeatureSettings settings)
    {
        var paths = new[] { settings.Common?.RelativePath }
            .Concat((settings.Roles ?? new Dictionary<string, AudioRoleSettings>()).Values.Select(role => role?.RelativePath));
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = AuraToolsConfigService.ResolveConfiguredPath(path!);
            if (!string.IsNullOrWhiteSpace(resolved) && !File.Exists(resolved))
            {
                yield return "当前未找到音频引用：" + path;
            }
        }
    }
}
