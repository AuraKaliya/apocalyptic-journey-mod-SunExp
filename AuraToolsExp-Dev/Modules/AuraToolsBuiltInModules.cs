using System;
using System.Collections.Generic;
using System.Linq;
using AuraCg.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.AdventureArchive;
using AuraToolsExp.Dll.Features.Audio;
using AuraToolsExp.Dll.Features.AutoBattle;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.CardVisual;
using AuraToolsExp.Dll.Features.Cg;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.Diagnostics;
using AuraToolsExp.Dll.Features.Feast;
using AuraToolsExp.Dll.Features.Logging;
using AuraToolsExp.Dll.Features.LobbyStatus;
using AuraToolsExp.Dll.Features.MatchRecords;
using AuraToolsExp.Dll.Features.ModHealth;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.PixelEmoji;
using AuraToolsExp.Dll.Features.PresetLibrary;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.SkillCg;
using AuraToolsExp.Dll.Features.Skin;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;

namespace AuraToolsExp.Dll.Modules;

internal static class AuraToolsBuiltInModules
{
    public static IReadOnlyList<IAuraToolModule> Create()
    {
        return new IAuraToolModule[]
        {
            FileLoggingModule(),
            SkinModule(),
            CardVisualModule(),
            BattleBgmModule(),
            CardUseAudioModule(),
            VoiceModule(),
            StarterDeckModule(),
            FeastModule(),
            FeastCgModule(),
            SafeBoxModule(),
            CardRefreshModule(),
            PixelEmojiModule(),
            AutoBattleModule(),
            StrategyLabModule(),
            ModSyncModule(),
            LobbyStatusModule(),
            DamageStatisticsModule(),
            BattleReplayModule(),
            AdventureArchiveModule(),
            DiagnosticsModule(),
            SkillCgModule(),
            CardUseCgModule(),
            EventCgModule(),
            PresetLibraryModule(),
            ModHealthModule()
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
            () => AuraToolsConfigService.Logging.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Logging.Enabled = enabled;
                AuraToolsConfigService.SaveLogging();
            },
            () => State(
                AuraToolModuleIds.FileLogging,
                AuraToolsConfigService.Logging.Enabled,
                AuraToolsConfigService.Logging.MinimumLevel + " 及以上"),
            AuraToolsLoggingSettingsPage.Show,
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
            () => AuraToolsConfigService.Skin.Enabled,
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
                    AuraToolsConfigService.Skin.Enabled,
                    "已启用 " + enabledCount + "/" + candidates.Count + " 个候选皮肤",
                    candidates.Count);
            },
            AuraToolsSkinEditor.Show,
            new[] { "皮肤", "角色", "外观" });
    }

    private static IAuraToolModule CardVisualModule()
    {
        return Module(
            AuraToolModuleIds.CardVisual,
            "presentation",
            215,
            21,
            "卡牌视觉",
            "按逐卡白名单配置卡框主题与动态效果。",
            context => AuraToolsCardVisualRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.CardVisual.Enabled,
            enabled =>
            {
                AuraToolsConfigService.CardVisual.Enabled = enabled;
                AuraToolsConfigService.SaveCardVisual();
                AuraToolsCardVisualRuntime.ApplyModuleActivation(enabled);
            },
            () => State(
                AuraToolModuleIds.CardVisual,
                AuraToolsConfigService.CardVisual.Enabled,
                "主题 " + AuraToolsCardVisualRegistry.Themes.Count
                + " 个 · 卡框 " + AuraToolsConfigService.CardVisual.Themes.Values.Sum(value => value.Cards.Count)
                + " 条 · 动效 " + AuraToolsCardVisualRuntime.EffectiveDynamicEffects().Count + " 条",
                AuraToolsConfigService.CardVisual.Themes.Values.Sum(value => value.Cards.Count)
                + AuraToolsCardVisualRuntime.EffectiveDynamicEffects().Count),
            AuraToolsCardVisualEditor.Show,
            new[] { "卡框", "卡面", "主题", "动态效果", "稀有度", "卡包" });
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
            () => AuraToolsConfigService.Audio.BattleBgm.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Audio.BattleBgm.Enabled = enabled;
                AuraToolsConfigService.SaveBattleBgm();
            },
            () => State(
                AuraToolModuleIds.BattleBgm,
                AuraToolsConfigService.Audio.BattleBgm.Enabled,
                AudioModeSummary(AuraToolsConfigService.Audio.BattleBgm)),
            AuraToolsAudioSettingsPage.ShowBattleBgm,
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
            () => AuraToolsConfigService.Audio.CardUse.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Audio.CardUse.Enabled = enabled;
                AuraToolsConfigService.SaveCardUseAudio();
            },
            () => State(
                AuraToolModuleIds.CardUseAudio,
                AuraToolsConfigService.Audio.CardUse.Enabled,
                AudioModeSummary(AuraToolsConfigService.Audio.CardUse)),
            AuraToolsAudioSettingsPage.ShowCardUse,
            new[] { "音效", "出牌", "音频" });
    }

    private static IAuraToolModule VoiceModule()
    {
        return Module(
            AuraToolModuleIds.Voice,
            "presentation",
            240,
            32,
            "角色语音",
            "按稳定信号、阶段和动作管理角色语音。",
            context => AuraToolsAudioRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.Audio.Voice.Enabled,
            enabled =>
            {
                AuraToolsConfigService.Audio.Voice.Enabled = enabled;
                AuraToolsConfigService.SaveVoice();
                AuraToolsAudioRuntime.RegisterProviders();
            },
            () => State(
                AuraToolModuleIds.Voice,
                AuraToolsConfigService.Audio.Voice.Enabled,
                "语音绑定 " + AuraToolsConfigService.Audio.Voice.Bindings.Count + " 条",
                AuraToolsConfigService.Audio.Voice.Bindings.Count),
            AuraToolsVoiceSettingsPage.Show,
            new[] { "语音", "角色", "技能", "低血量", "结算" });
    }

    private static IAuraToolModule StarterDeckModule()
    {
        return Module(
            AuraToolModuleIds.StarterDeck,
            "gameplay",
            110,
            40,
            "自定义开局",
            "为世界推演配置全局或按角色的开局卡牌与遗物。",
            context => AuraToolsStarterDeckRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.MatchExperience.StarterDeck.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.StarterDeck.Enabled = enabled;
                AuraToolsConfigService.SaveStarterDeck();
            },
            () =>
            {
                var settings = AuraToolsConfigService.MatchExperience.StarterDeck;
                var summary = settings.Mode == StarterDeckModes.RoleSpecific
                    ? "按角色覆盖 " + settings.Roles.Count + " 个"
                    : "全局 · 卡牌 " + settings.GlobalProfile.CardIds.Count
                      + "/15 · 遗物 " + settings.GlobalProfile.RelicIds.Count + "/6";
                return State(
                    AuraToolModuleIds.StarterDeck,
                    settings.Enabled,
                    summary,
                    settings.Roles.Count);
            },
            AuraToolsStarterDeckSettingsPage.Show,
            new[] { "自定义开局", "开局卡组", "遗物", "卡组", "世界推演" });
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
            () => AuraToolsConfigService.MatchExperience.CardRefresh.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.CardRefresh.Enabled = enabled;
                AuraToolsConfigService.SaveCardRefresh();
            },
            () => State(
                AuraToolModuleIds.CardRefresh,
                AuraToolsConfigService.MatchExperience.CardRefresh.Enabled,
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
            "进食一次后自动处理剩余食物。",
            context => AuraToolsFeastRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.MatchExperience.Feast.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.Feast.Enabled = enabled;
                AuraToolsConfigService.SaveFeast();
                AuraToolModuleHost.RefreshState(AuraToolModuleIds.FeastCg);
            },
            () => State(
                AuraToolModuleIds.Feast,
                AuraToolsConfigService.MatchExperience.Feast.Enabled,
                "单次最多处理 " + AuraToolsConfigService.MatchExperience.Feast.MaxBatchCount + " 份食物"),
            null,
            new[] { "食物", "美餐", "自动进食" });
    }

    private static IAuraToolModule FeastCgModule()
    {
        return Module(
            AuraToolModuleIds.FeastCg,
            "presentation",
            240,
            51,
            "美餐 CG",
            "在一键美餐完成后播放按角色配置的 CG。",
            null,
            () => AuraToolsConfigService.MatchExperience.Feast.Cg.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.Feast.Cg.Enabled = enabled;
                AuraToolsConfigService.SaveFeastCg();
            },
            FeastCgState,
            AuraToolsFeastRoleEditor.Show,
            new[] { "美餐", "CG", "食物", "角色", "表现资源" },
            visible: false);
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
            () => AuraToolsConfigService.MatchExperience.SafeBox.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.SafeBox.Enabled = enabled;
                AuraToolsConfigService.SaveSafeBox();
            },
            () => State(
                AuraToolModuleIds.SafeBox,
                AuraToolsConfigService.MatchExperience.SafeBox.Enabled,
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
            () => AuraToolsConfigService.PixelEmoji.Enabled,
            enabled =>
            {
                AuraToolsConfigService.PixelEmoji.Enabled = enabled;
                AuraToolsConfigService.SavePixelEmoji();
            },
            () =>
            {
                var items = PixelEmojiLibraryStore.GetItems();
                return NetworkState(
                    AuraToolModuleIds.PixelEmoji,
                    AuraToolsConfigService.PixelEmoji.Enabled,
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
            () => AuraToolsConfigService.MatchExperience.ModSync.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.ModSync.Enabled = enabled;
                AuraToolsConfigService.SaveModSync();
            },
            () => NetworkState(
                AuraToolModuleIds.ModSync,
                AuraToolsConfigService.MatchExperience.ModSync.Enabled,
                "联机大厅由房主发起同步"),
            null,
            new[] { "联机", "MOD", "同步", "房主" });
    }

    private static IAuraToolModule LobbyStatusModule()
    {
        return Module(
            AuraToolModuleIds.LobbyStatus,
            "multiplayer",
            520,
            101,
            "大厅状态面板",
            "集中查看玩家、角色、准备状态、游戏版本与 MOD 差异。",
            context => LobbyStatusRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.LobbyStatus.Enabled,
            enabled =>
            {
                AuraToolsConfigService.LobbyStatus.Enabled = enabled;
                AuraToolsConfigService.SaveLobbyStatus();
            },
            () => NetworkState(
                AuraToolModuleIds.LobbyStatus,
                AuraToolsConfigService.LobbyStatus.Enabled,
                LobbyStatusRuntime.Current.Players.Count == 0
                    ? "等待进入联机大厅"
                    : "大厅玩家 " + LobbyStatusRuntime.Current.Players.Count,
                LobbyStatusRuntime.Current.Players.Count),
            LobbyStatusRuntime.Show,
            new[] { "大厅", "玩家", "准备", "版本", "MOD" });
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
                AuraToolsConfigService.SaveDamageStatistics();
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
                return NetworkState(
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
                AuraToolsConfigService.SaveBattleReplay();
            },
            () =>
            {
                var replay = AuraToolsConfigService.MatchExperience.MatchRecords.Replay;
                return State(
                    AuraToolModuleIds.BattleReplay,
                    BattleReplayEnabled(),
                    "自动保存上限 " + replay.AutoRecordLimit + " 场");
            },
            AuraToolsReplaySettingsPage.Show,
            new[] { "回放", "录像", "对局", "视频" });
    }

    private static IAuraToolModule AdventureArchiveModule()
    {
        return Module(
            AuraToolModuleIds.AdventureArchive,
            "records",
            330,
            112,
            "冒险历程",
            "记录整轮冒险中的地点、选择、收藏变化与战斗。",
            context => AdventureArchiveRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.AdventureArchive.Enabled,
            enabled =>
            {
                AuraToolsConfigService.AdventureArchive.Enabled = enabled;
                AuraToolsConfigService.SaveAdventureArchive();
            },
            () => State(
                AuraToolModuleIds.AdventureArchive,
                AuraToolsConfigService.AdventureArchive.Enabled,
                AuraToolsConfigService.AdventureArchive.Enabled
                    ? ""
                    : "历程记录已关闭",
                AuraToolsConfigService.AdventureArchive.Enabled ? AdventureArchiveRuntime.Count : 0),
            AdventureArchivePage.Show,
            new[] { "冒险", "档案", "时间线", "快照", "对局" });
    }

    private static IAuraToolModule AutoBattleModule()
    {
        return Module(
            AuraToolModuleIds.AutoBattle,
            "intelligence",
            410,
            90,
            "自动战斗",
            "选择战斗策略并控制自动接管方式。",
            context => AuraToolsAutoBattleRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.MatchExperience.AutoBattle.Enabled,
            enabled =>
            {
                AuraToolsConfigService.MatchExperience.AutoBattle.Enabled = enabled;
                AuraToolsConfigService.SaveAutoBattle();
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
                    settings.Enabled,
                    summary,
                    attention: status.ModelIsolatedForBattle
                        ? status.Diagnostic
                        : "",
                    experimental: true);
            },
            AuraToolsAutoBattleSettingsPage.Show,
            new[] { "AI", "自动战斗", "策略", "接管" },
            experimental: true);
    }

    private static IAuraToolModule StrategyLabModule()
    {
        return Module(
            AuraToolModuleIds.StrategyLab,
            "intelligence",
            420,
            91,
            "策略模型实验室",
            "管理、训练和评估自动战斗模型。",
            null,
            () => true,
            _ => { },
            () => State(
                AuraToolModuleIds.StrategyLab,
                true,
                "",
                experimental: true),
            AuraToolsAutoBattleSettingsPage.ShowStrategyLab,
            new[] { "AI", "模型", "训练", "评估", "开发者" },
            experimental: true,
            showEnableControl: false,
            iconKey: AuraToolModuleIds.AutoBattle);
    }

    private static IAuraToolModule SkillCgModule()
    {
        return Module(
            AuraToolModuleIds.SkillCg,
            "presentation",
            250,
            130,
            "角色 CG",
            "管理角色的技能、美餐与低生命表现。",
            context => AuraToolsSkillCgRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.SkillCg.Enabled,
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
                    AuraToolsConfigService.SkillCg.Enabled,
                    "技能规则 " + count + " 条 · 低生命 "
                    + Math.Round(AuraToolsConfigService.SkillCg.LowHealthThreshold * 100f) + "% · 联机同步"
                    + (AuraToolsConfigService.SkillCg.SyncRemote ? "开启" : "关闭"),
                    count);
            },
            AuraToolsSkillCgEditor.Show,
            new[] { "技能", "美餐", "低生命", "CG", "角色", "特效" });
    }

    private static IAuraToolModule CardUseCgModule()
    {
        return Module(
            AuraToolModuleIds.CardUseCg,
            "presentation",
            260,
            131,
            "卡牌 CG",
            "管理按卡牌使用信号触发的注册表现。",
            context => AuraToolsSkillCgRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.SkillCg.CardUseCg.Enabled,
            enabled =>
            {
                AuraToolsConfigService.SkillCg.CardUseCg.Enabled = enabled;
                AuraToolsConfigService.SaveCardUseCg();
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
                    AuraToolsConfigService.SkillCg.CardUseCg.Enabled,
                    "已启用 " + enabledCount + "/" + entries.Count + " 个注册项",
                    entries.Count);
            },
            AuraToolsSkillCgManager.Show,
            new[] { "卡牌", "CG", "注册项", "特效" });
    }

    private static IAuraToolModule EventCgModule()
    {
        return Module(
            AuraToolModuleIds.EventCg,
            "presentation",
            270,
            132,
            "事件 CG",
            "管理战斗开场、胜负与冒险结算的队伍场景。",
            context => AuraToolsSkillCgRuntime.Initialize(context.ModConfig),
            () => AuraToolsConfigService.SkillCg.EventCg.Enabled,
            enabled =>
            {
                AuraToolsConfigService.SkillCg.EventCg.Enabled = enabled;
                AuraToolsConfigService.SaveEventCg();
            },
            () =>
            {
                var settings = AuraToolsConfigService.SkillCg.EventCg;
                var triggerCount = new[]
                {
                    settings.SpecialOpeningEnabled,
                    settings.SpecialVictoryEnabled,
                    settings.BattleDefeatEnabled,
                    settings.AdventureSettlementEnabled
                }.Count(value => value);
                return State(
                    AuraToolModuleIds.EventCg,
                    settings.Enabled,
                    "事件 " + triggerCount + "/4 · 特殊战斗 " + settings.SpecialBattleIds.Count + " 条",
                    triggerCount);
            },
            AuraToolsEventCgSettingsPage.Show,
            new[] { "事件", "CG", "战斗开场", "胜利", "失败", "冒险结算", "队伍" });
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

    private static IAuraToolModule PresetLibraryModule()
    {
        return Module(
            AuraToolModuleIds.PresetLibrary,
            "system",
            620,
            140,
            "妙妙方案库",
            "保存、预检并事务式应用跨模块配置方案。",
            _ => AuraPresetLibraryService.RefreshCount(),
            () => AuraToolsConfigService.PresetLibrary.Enabled,
            enabled =>
            {
                AuraToolsConfigService.PresetLibrary.Enabled = enabled;
                AuraToolsConfigService.SavePresetLibrary();
            },
            () => State(
                AuraToolModuleIds.PresetLibrary,
                AuraToolsConfigService.PresetLibrary.Enabled,
                "本地方案 " + AuraPresetLibraryService.CachedCount + " 个",
                AuraPresetLibraryService.CachedCount),
            AuraPresetLibraryPage.Show,
            new[] { "方案", "预设", "导入", "导出", "配置", "Codec" });
    }

    private static IAuraToolModule ModHealthModule()
    {
        return Module(
            AuraToolModuleIds.ModHealth,
            "system",
            630,
            150,
            "MOD 健康检查",
            "按游戏原生 MOD 加载契约检查依赖、入口与注册资源。",
            null,
            () => AuraToolsConfigService.ModHealth.Enabled,
            enabled =>
            {
                AuraToolsConfigService.ModHealth.Enabled = enabled;
                AuraToolsConfigService.SaveModHealth();
            },
            () =>
            {
                var report = ModHealthRuntime.Current;
                var summary = string.IsNullOrWhiteSpace(report.ScannedUtc)
                    ? "尚未扫描"
                    : report.Level + " · 问题 " + report.Issues.Count;
                return State(
                    AuraToolModuleIds.ModHealth,
                    AuraToolsConfigService.ModHealth.Enabled,
                    summary,
                    report.Issues.Count,
                    report.CriticalCount + report.ErrorCount > 0 ? "检测到需要处理的 MOD 加载问题。" : "");
            },
            ModHealthPage.Show,
            new[] { "MOD", "健康", "依赖", "加载", "版本", "资源" });
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
        bool visible = true,
        bool showEnableControl = true,
        string? iconKey = null)
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
                IconKey = string.IsNullOrWhiteSpace(iconKey) ? id : iconKey!,
                SearchTerms = searchTerms,
                HasSettingsPage = showSettings != null,
                ShowEnableControl = showEnableControl,
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
        var state = new AuraToolModuleState
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
        if (AuraToolsConfigService.IsModuleConfigReadOnly(moduleId))
        {
            state.Availability = AuraToolModuleAvailability.Degraded;
            state.Attention =
                "配置来自更新版本；当前使用安全默认值并保持原文件只读。";
        }
        return state;
    }

    private static AuraToolModuleState NetworkState(
        string moduleId,
        bool enabled,
        string summary,
        int? count = null)
    {
        var state = State(moduleId, enabled, summary, count);
        if (enabled && !AuraToolsRpcTransport.IsLobbyCompatible(out _))
        {
            state.Availability = AuraToolModuleAvailability.Degraded;
            var networkAttention =
                "当前房间有玩家未启用妙妙工具；联机部分已暂停，本地功能仍可使用。";
            state.Attention = string.IsNullOrWhiteSpace(state.Attention)
                ? networkAttention
                : state.Attention + " " + networkAttention;
        }
        return state;
    }

    private static AuraToolModuleState FeastCgState()
    {
        var feast = AuraToolsConfigService.MatchExperience.Feast;
        var configured = feast.Cg.Enabled;
        var effective = feast.IsCgEffective;
        var count = feast.Cg.Roles.Count;
        var state = new AuraToolModuleState
        {
            ModuleId = AuraToolModuleIds.FeastCg,
            ConfiguredEnabled = configured,
            EffectiveEnabled = effective,
            Availability = effective
                ? AuraToolModuleAvailability.Ready
                : AuraToolModuleAvailability.Disabled,
            Summary = feast.Enabled
                ? "已配置 " + count + " 个角色"
                : "随一键美餐暂停 · 已配置 " + count + " 个角色",
            ItemCount = count,
            EnableControlInteractable = feast.Enabled,
            SettingsControlInteractable = true
        };
        if (AuraToolsConfigService.IsModuleConfigReadOnly(AuraToolModuleIds.FeastCg))
        {
            state.Availability = AuraToolModuleAvailability.Degraded;
            state.Attention = "配置来自更新版本；当前保持只读。";
        }
        return state;
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
        return records.Enabled
               && records.Statistics.Enabled;
    }

    private static bool BattleReplayEnabled()
    {
        var records = AuraToolsConfigService.MatchExperience.MatchRecords;
        return records.Enabled
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
            "shadow" => "观察模式",
            "trial" => "试用",
            "full" => "正式接管",
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
    private IDisposable? activation;

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
        var shouldBeActive = enabled();
        if (shouldBeActive)
        {
            activation ??= AuraToolsModuleActivationPolicy.Activate(
                Descriptor.ModuleId);
            return;
        }

        if (activation != null)
        {
            activation.Dispose();
            activation = null;
        }
        else
        {
            AuraToolsModuleActivationPolicy.Deactivate(
                Descriptor.ModuleId);
        }
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
