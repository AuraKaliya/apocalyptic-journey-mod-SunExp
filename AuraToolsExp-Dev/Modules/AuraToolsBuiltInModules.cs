using System;
using System.Collections.Generic;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Diagnostics;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Features.Skin;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;

namespace AuraToolsExp.Dll.Modules;

public static class AuraToolModuleIds
{
    public const string StarterDeck = "gameplay.starter-deck";
    public const string CardRefresh = "gameplay.card-refresh";
    public const string Feast = "gameplay.feast";
    public const string SafeBox = "gameplay.safe-box";
    public const string Skin = "presentation.skin";
    public const string BattleBgm = "presentation.battle-bgm";
    public const string CardUseAudio = "presentation.card-use-audio";
    public const string PixelEmoji = "presentation.pixel-emoji";
    public const string SkillCg = "presentation.skill-cg";
    public const string CardUseCg = "presentation.card-use-cg";
    public const string DamageStatistics = "records.damage-statistics";
    public const string BattleReplay = "records.battle-replay";
    public const string ModSync = "multiplayer.mod-sync";
    public const string AutoBattle = "intelligence.auto-battle";
    public const string FileLogging = "system.file-logging";
    internal const string Diagnostics = "system.card-ui-diagnostics";
}

internal static class AuraToolsBuiltInModules
{
    public static IReadOnlyList<IAuraToolModule> Create()
    {
        return new IAuraToolModule[]
        {
            FileLoggingModule(),
            SkinModule(),
            BattleBgmModule(),
            CardUseAudioModule(),
            StarterDeckModule(),
            FeastModule(),
            SafeBoxModule(),
            CardRefreshModule(),
            PixelEmojiModule(),
            AutoBattleModule(),
            ModSyncModule(),
            DamageStatisticsModule(),
            BattleReplayModule(),
            DiagnosticsModule(),
            SkillCgModule(),
            CardUseCgModule()
        };
    }

    private static IAuraToolModule FileLoggingModule()
    {
        return Module(
            AuraToolModuleIds.FileLogging,
            "system",
            610,
            10,
            "文件日志",
            "将工具运行信息写入独立日志文件。",
            context => AuraToolsFileLogRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.Logging.Enabled
                  && AuraToolsConfigService.Logging.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Logging.Enabled = enabled;
                AuraToolsConfigService.SaveLogging();
            },
            () => State(
                AuraToolModuleIds.FileLogging,
                AuraToolsConfigService.Root.Logging.Enabled
                && AuraToolsConfigService.Logging.Enabled,
                AuraToolsConfigService.Logging.MinimumLevel + " 及以上"),
            parent => AuraToolsSettingsRuntime.ShowLoggingSettings(parent),
            new[] { "日志", "log", "诊断" });
    }

    private static IAuraToolModule SkinModule()
    {
        return Module(
            AuraToolModuleIds.Skin,
            "presentation",
            210,
            20,
            "角色皮肤",
            "管理已注册皮肤并选择本地显示效果。",
            context => AuraToolsSkinRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.Skin.Enabled
                  && AuraToolsConfigService.Skin.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Skin.Enabled = enabled;
                AuraToolsConfigService.SaveSkin();
                if (enabled)
                {
                    AuraToolsSkinRuntime.RegisterBundledPackage();
                    AuraToolsSkinRuntime.Reload();
                }
            },
            () =>
            {
                var candidates = AuraToolsSkinRuntime.CandidateDefinitions();
                var enabledCount = candidates.Count(candidate =>
                    AuraToolsConfigService.Skin.IsCandidateEnabled(
                        candidate.QualifiedSkinId));
                return State(
                    AuraToolModuleIds.Skin,
                    AuraToolsConfigService.Root.Skin.Enabled
                    && AuraToolsConfigService.Skin.Enabled,
                    "已启用 " + enabledCount + "/" + candidates.Count + " 个候选皮肤",
                    candidates.Count);
            },
            AuraToolsSkinEditor.Show,
            new[] { "皮肤", "角色", "外观" });
    }

    private static IAuraToolModule BattleBgmModule()
    {
        return Module(
            AuraToolModuleIds.BattleBgm,
            "presentation",
            220,
            30,
            "战斗背景音乐",
            "替换战斗音乐，并可按角色设置不同曲目。",
            context => AuraToolsAudioRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.Audio.Enabled
                  && AuraToolsConfigService.Audio.BattleBgm.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Audio.BattleBgm.Enabled = enabled;
                AuraToolsConfigService.SaveAudio();
            },
            () => State(
                AuraToolModuleIds.BattleBgm,
                AuraToolsConfigService.Root.Audio.Enabled
                && AuraToolsConfigService.Audio.BattleBgm.Enabled,
                AudioModeSummary(AuraToolsConfigService.Audio.BattleBgm)),
            parent => AuraToolsSettingsRuntime.ShowAudioSettings(parent, true),
            new[] { "音乐", "BGM", "音频" });
    }

    private static IAuraToolModule CardUseAudioModule()
    {
        return Module(
            AuraToolModuleIds.CardUseAudio,
            "presentation",
            230,
            31,
            "出牌音效",
            "替换卡牌使用音效，并可按角色设置。",
            context => AuraToolsAudioRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.Audio.Enabled
                  && AuraToolsConfigService.Audio.CardUse.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Audio.CardUse.Enabled = enabled;
                AuraToolsConfigService.SaveAudio();
            },
            () => State(
                AuraToolModuleIds.CardUseAudio,
                AuraToolsConfigService.Root.Audio.Enabled
                && AuraToolsConfigService.Audio.CardUse.Enabled,
                AudioModeSummary(AuraToolsConfigService.Audio.CardUse)),
            parent => AuraToolsSettingsRuntime.ShowAudioSettings(parent, false),
            new[] { "音效", "出牌", "音频" });
    }

    private static IAuraToolModule StarterDeckModule()
    {
        return Module(
            AuraToolModuleIds.StarterDeck,
            "gameplay",
            110,
            40,
            "开局卡组",
            "为世界推演配置全局或按角色开局卡组。",
            context => AuraToolsStarterDeckRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.StarterDeck.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.StarterDeck.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
            },
            () =>
            {
                var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
                var summary = settings.Mode == StarterDeckModes.RoleSpecific
                    ? "按角色配置 " + settings.Roles.Count + " 个"
                    : "全局卡组 " + settings.GlobalProfile.CardIds.Count
                      + "/" + settings.GlobalProfile.DeckSize + " 张";
                return State(
                    AuraToolModuleIds.StarterDeck,
                    AuraToolsConfigService.Root.MatchExperience.Enabled
                    && settings.Enabled,
                    summary,
                    settings.Roles.Count);
            },
            AuraToolsSettingsRuntime.ShowStarterDeckSettings,
            new[] { "卡组", "开局", "世界推演" });
    }

    private static IAuraToolModule CardRefreshModule()
    {
        return Module(
            AuraToolModuleIds.CardRefresh,
            "gameplay",
            120,
            60,
            "卡牌刷新",
            "在战斗奖励选牌时提供一次重新抽取。",
            context => AuraToolsCardRefreshRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.CardRefresh.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.CardRefresh.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
            },
            () => State(
                AuraToolModuleIds.CardRefresh,
                AuraToolsConfigService.Root.MatchExperience.Enabled
                && AuraToolsConfigService.MatchExperience.CardRefresh.Enabled,
                "战斗奖励选牌可刷新"),
            null,
            new[] { "卡牌", "奖励", "刷新" });
    }

    private static IAuraToolModule FeastModule()
    {
        return Module(
            AuraToolModuleIds.Feast,
            "gameplay",
            130,
            50,
            "一键美餐",
            "进食一次后自动处理剩余食物，并播放角色表现。",
            context => AuraToolsFeastRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.Feast.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.Feast.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
            },
            () => State(
                AuraToolModuleIds.Feast,
                AuraToolsConfigService.Root.MatchExperience.Enabled
                && AuraToolsConfigService.MatchExperience.Feast.Enabled,
                "已配置 " + AuraToolsConfigService.MatchExperience.Feast.Roles.Count + " 个角色",
                AuraToolsConfigService.MatchExperience.Feast.Roles.Count),
            AuraToolsFeastRoleEditor.Show,
            new[] { "食物", "美餐", "CG" });
    }

    private static IAuraToolModule SafeBoxModule()
    {
        return Module(
            AuraToolModuleIds.SafeBox,
            "gameplay",
            140,
            70,
            "随身保险箱",
            "在冒险顶部栏直接打开保险箱。",
            context => AuraToolsSafeBoxRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.SafeBox.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.SafeBox.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
            },
            () => State(
                AuraToolModuleIds.SafeBox,
                AuraToolsConfigService.Root.MatchExperience.Enabled
                && AuraToolsConfigService.MatchExperience.SafeBox.Enabled,
                "冒险顶部栏显示入口"),
            null,
            new[] { "保险箱", "仓库", "冒险" });
    }

    private static IAuraToolModule PixelEmojiModule()
    {
        return Module(
            AuraToolModuleIds.PixelEmoji,
            "presentation",
            240,
            80,
            "像素表情",
            "制作并收藏可在冒险中使用的像素表情。",
            context => AuraToolsPixelEmojiRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.PixelEmoji.Enabled
                  && AuraToolsConfigService.PixelEmoji.Enabled,
            enabled =>
            {
                AuraToolsConfigService.PixelEmoji.Enabled = enabled;
                AuraToolsConfigService.SavePixelEmoji();
            },
            () =>
            {
                var items = PixelEmojiLibraryStore.GetItems();
                return State(
                    AuraToolModuleIds.PixelEmoji,
                    AuraToolsConfigService.Root.PixelEmoji.Enabled
                    && AuraToolsConfigService.PixelEmoji.Enabled,
                    "作品 " + items.Count + " · 收藏 "
                    + AuraToolsConfigService.PixelEmoji.FavoriteIds.Count,
                    items.Count);
            },
            PixelEmojiWorkshop.Show,
            new[] { "表情", "像素", "工坊" });
    }

    private static IAuraToolModule ModSyncModule()
    {
        return Module(
            AuraToolModuleIds.ModSync,
            "multiplayer",
            510,
            100,
            "MOD 配置同步",
            "在联机大厅同步房主的 MOD 启用状态。",
            context => AuraToolsModSyncRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.ModSync.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.ModSync.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
            },
            () => State(
                AuraToolModuleIds.ModSync,
                AuraToolsConfigService.Root.MatchExperience.Enabled
                && AuraToolsConfigService.MatchExperience.ModSync.Enabled,
                "联机大厅由房主发起同步"),
            null,
            new[] { "联机", "MOD", "同步", "房主" });
    }

    private static IAuraToolModule DamageStatisticsModule()
    {
        return Module(
            AuraToolModuleIds.DamageStatistics,
            "records",
            310,
            110,
            "DPT 统计",
            "统计本场和本轮冒险中的伤害与贡献。",
            context => AuraToolsMatchRecordsRuntime.Initialize(context.ModConfig),
            DamageStatisticsEnabled,
            enabled =>
            {
                var records = AuraToolsConfigService.MatchExperience.MatchRecords;
                AuraToolsMatchRecordModulePolicy.SetDamageStatistics(
                    records,
                    enabled);
                AuraToolsConfigService.SaveMatchExperience();
                if (!enabled)
                {
                    AuraToolsDamageMeterRuntime.SetVisible(false);
                }
            },
            () =>
            {
                var settings = AuraToolsConfigService.MatchExperience.MatchRecords.Statistics;
                var summary = DamageScopeLabel(settings.DisplayScope)
                              + " · " + DamageTeamLabel(settings.TeamFilter)
                              + " · " + DamageDisplayLabel(settings.DisplayMode);
                return State(
                    AuraToolModuleIds.DamageStatistics,
                    DamageStatisticsEnabled(),
                    summary);
            },
            null,
            new[] { "DPT", "伤害", "统计", "贡献" });
    }

    private static IAuraToolModule BattleReplayModule()
    {
        return Module(
            AuraToolModuleIds.BattleReplay,
            "records",
            320,
            111,
            "战斗回放",
            "自动保存结构化战斗回放并管理对局资料。",
            context => AuraToolsMatchRecordsRuntime.Initialize(context.ModConfig),
            BattleReplayEnabled,
            enabled =>
            {
                var records = AuraToolsConfigService.MatchExperience.MatchRecords;
                AuraToolsMatchRecordModulePolicy.SetBattleReplay(records, enabled);
                AuraToolsConfigService.SaveMatchExperience();
            },
            () =>
            {
                var replay = AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
                return State(
                    AuraToolModuleIds.BattleReplay,
                    BattleReplayEnabled(),
                    "自动保存上限 " + replay.AutoRecordLimit + " 场");
            },
            AuraToolsSettingsRuntime.ShowReplaySettings,
            new[] { "回放", "录像", "对局", "视频" });
    }

    private static IAuraToolModule AutoBattleModule()
    {
        return Module(
            AuraToolModuleIds.AutoBattle,
            "intelligence",
            410,
            90,
            "战斗策略实验室",
            "使用模型评估、学习并接管战斗决策。",
            context => AuraToolsAutoBattleRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.MatchExperience.Enabled
                  && AuraToolsConfigService.MatchExperience.AutoBattle.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.AutoBattle.Enabled = enabled;
                AuraToolsConfigService.SaveMatchExperience();
                if (!enabled)
                {
                    AuraToolsAutoBattleRuntime.SetActive(false);
                }
            },
            () =>
            {
                var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
                var status = AuraToolsAutoBattleRuntime.SnapshotModelApplicationStatus();
                var summary = string.IsNullOrWhiteSpace(settings.SelectedModelId)
                    ? "尚未选择模型"
                    : AutoBattleModeLabel(status.EffectiveMode)
                      + " · " + ShortModelId(settings.SelectedModelId);
                return State(
                    AuraToolModuleIds.AutoBattle,
                    AuraToolsConfigService.Root.MatchExperience.Enabled
                    && settings.Enabled,
                    summary,
                    attention: status.ModelIsolatedForBattle
                        ? status.Diagnostic
                        : "",
                    experimental: true);
            },
            AuraToolsSettingsRuntime.ShowAutoBattleSettings,
            new[] { "AI", "自动战斗", "模型", "训练", "评估" },
            experimental: true);
    }

    private static IAuraToolModule SkillCgModule()
    {
        return Module(
            AuraToolModuleIds.SkillCg,
            "presentation",
            250,
            130,
            "技能 CG",
            "按角色和技能播放自定义战斗 CG。",
            context => AuraToolsSkillCgRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.SkillCg.Enabled
                  && AuraToolsConfigService.SkillCg.Enabled,
            enabled =>
            {
                AuraToolsConfigService.SkillCg.Enabled = enabled;
                AuraToolsConfigService.SaveSkillCg();
            },
            () =>
            {
                var count = AuraToolsConfigService.SkillCg.Roles.Values
                    .Sum(role => role.Rules.Count);
                return State(
                    AuraToolModuleIds.SkillCg,
                    AuraToolsConfigService.Root.SkillCg.Enabled
                    && AuraToolsConfigService.SkillCg.Enabled,
                    "角色规则 " + count + " 条 · 联机同步"
                    + (AuraToolsConfigService.SkillCg.SyncRemote ? "开启" : "关闭"),
                    count);
            },
            AuraToolsSkillCgEditor.Show,
            new[] { "技能", "CG", "角色", "特效" });
    }

    private static IAuraToolModule CardUseCgModule()
    {
        return Module(
            AuraToolModuleIds.CardUseCg,
            "presentation",
            260,
            131,
            "卡牌使用 CG",
            "管理其他 MOD 注册的卡牌使用表现。",
            context => AuraToolsSkillCgRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Root.SkillCg.Enabled
                  && AuraToolsConfigService.SkillCg.CardUseCg.Enabled,
            enabled =>
            {
                AuraToolsConfigService.SkillCg.CardUseCg.Enabled = enabled;
                AuraToolsConfigService.SaveSkillCg();
            },
            () =>
            {
                var entries = SkillCgArbiterRuntime.GetRegisteredCardUseCgEntries();
                var enabledCount = entries.Count(entry =>
                    !AuraToolsConfigService.SkillCg.CardUseCg.RegisteredEntries
                        .TryGetValue(entry.QualifiedCgId, out var value)
                    || value);
                return State(
                    AuraToolModuleIds.CardUseCg,
                    AuraToolsConfigService.Root.SkillCg.Enabled
                    && AuraToolsConfigService.SkillCg.CardUseCg.Enabled,
                    "已启用 " + enabledCount + "/" + entries.Count + " 个注册项",
                    entries.Count);
            },
            AuraToolsSkillCgManager.Show,
            new[] { "卡牌", "CG", "注册项", "特效" });
    }

    private static IAuraToolModule DiagnosticsModule()
    {
        return Module(
            AuraToolModuleIds.Diagnostics,
            "system",
            990,
            120,
            "卡牌 UI 诊断",
            "内部性能诊断服务。",
            context => AuraToolsCardUiBenchmarkRuntime.Initialize(context.ModConfig),
            () => true,
            _ => { },
            () => State(AuraToolModuleIds.Diagnostics, true, "内部服务"),
            null,
            Array.Empty<string>(),
            visible: false);
    }

    private static IAuraToolModule Module(
        string id,
        string category,
        int order,
        int initializationOrder,
        string title,
        string description,
        Action<AuraToolModuleContext>? initialize,
        Func<bool> enabled,
        Action<bool> setEnabled,
        Func<AuraToolModuleState> state,
        Action<UnityEngine.Transform>? showSettings,
        IReadOnlyList<string> searchTerms,
        bool experimental = false,
        bool visible = true)
    {
        return new DelegateAuraToolModule(
            new AuraToolModuleDescriptor
            {
                ModuleId = id,
                CategoryId = category,
                Order = order,
                InitializationOrder = initializationOrder,
                DisplayName = title,
                Description = description,
                SearchTerms = searchTerms,
                HasSettingsPage = showSettings != null,
                Experimental = experimental,
                Visible = visible
            },
            context => initialize?.Invoke(context),
            enabled,
            setEnabled,
            state,
            showSettings);
    }

    private static AuraToolModuleState State(
        string moduleId,
        bool enabled,
        string summary,
        int? count = null,
        string attention = "",
        bool experimental = false)
    {
        return new AuraToolModuleState
        {
            ModuleId = moduleId,
            ConfiguredEnabled = enabled,
            EffectiveEnabled = enabled,
            Availability = enabled
                ? AuraToolModuleAvailability.Ready
                : AuraToolModuleAvailability.Disabled,
            Summary = summary ?? "",
            Attention = attention ?? "",
            ItemCount = count
        };
    }

    private static string AudioModeSummary(AudioFeatureSettings settings)
    {
        if (settings.Mode == AudioModes.Advanced)
        {
            return "按角色配置 " + settings.Roles.Count + " 个";
        }

        return string.IsNullOrWhiteSpace(settings.Common.RelativePath)
            ? "尚未设置通用音频"
            : "通用音频";
    }

    private static bool DamageStatisticsEnabled()
    {
        var records = AuraToolsConfigService.MatchExperience.MatchRecords;
        return AuraToolsConfigService.Root.MatchExperience.Enabled
               && records.Enabled
               && records.Statistics.Enabled;
    }

    private static bool BattleReplayEnabled()
    {
        var records = AuraToolsConfigService.MatchExperience.MatchRecords;
        return AuraToolsConfigService.Root.MatchExperience.Enabled
               && records.Enabled
               && records.Replay.Enabled;
    }

    private static string DamageDisplayLabel(string value)
    {
        return value == DamageMeterDisplayModes.Bars ? "进度条" : "表格";
    }

    private static string DamageScopeLabel(string value)
    {
        return value == DamageMeterDisplayScopes.Adventure ? "本轮" : "本场";
    }

    private static string DamageTeamLabel(string value)
    {
        return value == DamageMeterTeamFilters.Friendly
            ? "友方"
            : value == DamageMeterTeamFilters.Enemy ? "敌方" : "全部阵营";
    }

    private static string AutoBattleModeLabel(string value)
    {
        return value switch
        {
            "shadow" => "影子评估",
            "trial" => "实机试用",
            "full" => "完整应用",
            _ => "未应用"
        };
    }

    private static string ShortModelId(string value)
    {
        var text = value ?? "";
        return text.Length <= 28 ? text : text.Substring(0, 25) + "...";
    }
}

internal sealed class DelegateAuraToolModule : IAuraToolModule
{
    private readonly Action<AuraToolModuleContext> initialize;
    private readonly Func<bool> enabled;
    private readonly Action<bool> setEnabled;
    private readonly Func<AuraToolModuleState> state;
    private readonly Action<UnityEngine.Transform>? showSettings;

    public DelegateAuraToolModule(
        AuraToolModuleDescriptor descriptor,
        Action<AuraToolModuleContext> initialize,
        Func<bool> enabled,
        Action<bool> setEnabled,
        Func<AuraToolModuleState> state,
        Action<UnityEngine.Transform>? showSettings)
    {
        Descriptor = descriptor;
        this.initialize = initialize;
        this.enabled = enabled;
        this.setEnabled = setEnabled;
        this.state = state;
        this.showSettings = showSettings;
    }

    public AuraToolModuleDescriptor Descriptor { get; }

    public void Initialize(AuraToolModuleContext context) => initialize(context);

    public AuraToolModuleState SnapshotState() => state();

    public AuraToolOperationResult SetEnabled(bool value)
    {
        if (enabled() == value)
        {
            return AuraToolOperationResult.Ok();
        }

        setEnabled(value);
        return AuraToolOperationResult.Ok(value ? "已启用" : "已关闭");
    }

    public void ApplyCurrentConfiguration()
    {
    }

    public IAuraToolSettingsPage? CreateSettingsPage()
    {
        return showSettings == null
            ? null
            : new DelegateAuraToolSettingsPage(Descriptor.ModuleId, showSettings);
    }
}

internal sealed class DelegateAuraToolSettingsPage : IAuraToolSettingsPage
{
    private readonly Action<UnityEngine.Transform> show;

    public DelegateAuraToolSettingsPage(
        string moduleId,
        Action<UnityEngine.Transform> show)
    {
        ModuleId = moduleId;
        this.show = show;
    }

    public string ModuleId { get; }

    public void Build(AuraToolSettingsPageContext context) => show(context.Parent);

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }

    public void Dispose()
    {
    }
}
