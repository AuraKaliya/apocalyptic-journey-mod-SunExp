using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraTools.UnityUiPreview
{
    [Serializable]
    internal sealed class PreviewCategory
    {
        internal string Id;
        internal string Label;
        internal string Icon;
    }

    [Serializable]
    internal sealed class PreviewModule
    {
        internal string Id;
        internal string Category;
        internal string Name;
        internal string Description;
        internal string Summary;
        internal string Attention;
        internal string Icon;
        internal bool Enabled;
        internal bool HasSettings;
        internal bool Experimental;
        internal bool ShowEnableControl = true;
        internal string Availability;

        internal PreviewModule Clone()
        {
            return (PreviewModule)MemberwiseClone();
        }
    }

    internal static class PreviewCatalog
    {
        internal static readonly PreviewCategory[] Categories =
        {
            Category("all", "全部", "all"),
            Category("gameplay", "游戏体验", "gameplay"),
            Category("presentation", "表现资源", "presentation"),
            Category("records", "对局记录", "records"),
            Category("multiplayer", "联机工具", "multiplayer"),
            Category("intelligence", "智能战斗", "intelligence"),
            Category("extensions", "扩展工具", "extensions"),
            Category("system", "系统数据", "system")
        };

        private static readonly PreviewModule[] DefaultModules =
        {
            Module("gameplay.starter-deck", "gameplay", "自定义开局", "为世界推演配置全局或按角色的开局卡牌与遗物。", "全局 · 卡牌 0/15 · 遗物 0/6", "starter-deck", false, true),
            Module("gameplay.card-refresh", "gameplay", "卡牌刷新", "在战斗奖励选牌时提供一次重新抽取。", "战斗奖励选牌可刷新", "card-refresh", false, false),
            Module("gameplay.feast", "gameplay", "一键美餐", "进食一次后自动处理剩余食物。", "单次最多处理 64 份食物", "feast", true, false),
            Module("presentation.feast-cg", "presentation", "美餐 CG", "在一键美餐完成后播放按角色配置的 CG。", "已配置 16 个角色", "feast-cg", true, true),
            Module("gameplay.safe-box", "gameplay", "随身保险箱", "在冒险顶部栏直接打开保险箱。", "冒险顶部栏显示入口", "safe-box", false, false),
            Module("presentation.skin", "presentation", "角色皮肤", "管理已注册皮肤并选择本地显示效果。", "已启用 3/3 个候选皮肤", "skin", true, true),
            Module("presentation.battle-bgm", "presentation", "战斗背景音乐", "替换战斗音乐，并可按角色设置不同曲目。", "通用音频", "battle-bgm", false, true),
            Module("presentation.card-use-audio", "presentation", "出牌音效", "配置通用或按角色生效的出牌音效。", "通用音效", "card-use-audio", true, true),
            Module("presentation.character-voice", "presentation", "角色语音", "管理内容 MOD 注册的角色语音。", "", "card-use-audio", true, true),
            Module("presentation.pixel-emoji", "presentation", "像素表情", "制作、收藏并在联机中展示像素表情。", "作品 12 · 收藏 5", "pixel-emoji", true, true),
            Module("presentation.skill-cg", "presentation", "技能 CG", "管理技能触发的角色表现和联机同步。", "角色规则 6 条 · 联机同步开启", "skill-cg", true, true),
            Module("presentation.card-use-cg", "presentation", "卡牌使用 CG", "管理注册卡牌的使用演出。", "已启用 4/7 个注册项", "card-use-cg", true, true),
            Module("presentation.card-visual", "presentation", "卡牌视觉", "管理卡框主题与动态效果。", "", "skin", true, true),
            Module("records.damage-statistics", "records", "伤害统计", "记录本场伤害并提供局内和冒险统计。", "本场 · 全部阵营 · 表格", "damage-statistics", true, true),
            Module("records.battle-replay", "records", "战斗回放", "自动记录对局并提供回放与视频导出。", "自动保存上限 20", "battle-replay", true, true),
            Module("records.adventure-archive", "records", "冒险历程", "记录整轮冒险中的地点、选择、收藏变化与战斗。", "", "adventure-archive", true, true),
            Module("multiplayer.mod-sync", "multiplayer", "MOD 配置同步", "在联机大厅中检查并同步工具配置。", "当前不在联机大厅", "mod-sync", true, true),
            Module("multiplayer.lobby-status", "multiplayer", "大厅状态面板", "集中查看玩家、角色、准备状态、游戏版本与 MOD 差异。", "大厅玩家 3", "lobby-status", true, true),
            Module("intelligence.auto-battle", "intelligence", "自动战斗", "选择战斗策略并控制自动接管方式。", "", "auto-battle", false, true, true),
            Module("intelligence.strategy-model-lab", "intelligence", "策略模型实验室", "管理、训练和评估自动战斗模型。", "", "auto-battle", true, true, true, false),
            Module("system.file-logging", "system", "文件日志", "将工具运行信息写入独立日志文件。", "Info 及以上", "file-logging", true, true),
            Module("system.preset-library", "system", "妙妙方案库", "保存、预检并事务式应用跨模块配置方案。", "本地方案 6 个", "preset-library", true, true),
            Module("system.mod-health", "system", "MOD 健康检查", "按游戏原生 MOD 加载契约检查依赖、入口与注册资源。", "警告 · 问题 2", "mod-health", true, true)
        };

        internal static List<PreviewModule> ForScenario(string scenario)
        {
            var modules = DefaultModules.Select(module => module.Clone()).ToList();
            switch ((scenario ?? "default").Trim().ToLowerInvariant())
            {
                case "long-text":
                    var starter = modules.First(module => module.Id == "gameplay.starter-deck");
                    starter.Name = "自定义开局与按角色卡牌遗物配置管理";
                    starter.Summary = "全局 · 卡牌 15/15 · 遗物 6/6 · 已配置 12 个角色覆盖";
                    starter.Description = "为世界推演配置全局或按角色的开局卡牌与遗物，并支持版本兼容的配置导入导出。";
                    var auto = modules.First(module => module.Id == "intelligence.auto-battle");
                    auto.Summary = "完整应用";
                    auto.Attention = "候选模型尚未完成高级难度验证";
                    auto.Availability = "warning";
                    break;
                case "warning":
                    var feastCg = modules.First(module => module.Id == "presentation.feast-cg");
                    feastCg.Summary = "随一键美餐暂停 · 已配置 16 个角色";
                    feastCg.Attention = "开启一键美餐后恢复自动播放";
                    feastCg.Availability = "warning";
                    var skin = modules.First(module => module.Id == "presentation.skin");
                    skin.Summary = "已启用 2/3 个候选皮肤";
                    skin.Attention = "1 个资源目录缺失";
                    skin.Availability = "warning";
                    auto = modules.First(module => module.Id == "intelligence.auto-battle");
                    auto.Summary = "模型扫描中";
                    auto.Attention = "请等待当前索引任务完成";
                    auto.Availability = "busy";
                    var sync = modules.First(module => module.Id == "multiplayer.mod-sync");
                    sync.Summary = "联机协议不可用";
                    sync.Attention = "当前版本不兼容";
                    sync.Availability = "error";
                    break;
                case "extensions":
                    modules.Add(Module("extensions.resource-inspector", "extensions", "资源检查器", "检查已注册共享资源的可用状态。", "来自 AuroraExtension · 24 个注册项", "extensions", true, true));
                    modules.Add(Module("extensions.seed-notebook", "extensions", "种子笔记", "记录并筛选最近使用的世界种子。", "已收藏 8 个种子", "records", false, true));
                    break;
            }
            return modules;
        }

        private static PreviewCategory Category(string id, string label, string icon)
        {
            return new PreviewCategory { Id = id, Label = label, Icon = icon };
        }

        private static PreviewModule Module(
            string id,
            string category,
            string name,
            string description,
            string summary,
            string icon,
            bool enabled,
            bool settings,
            bool experimental = false,
            bool showEnableControl = true)
        {
            return new PreviewModule
            {
                Id = id,
                Category = category,
                Name = name,
                Description = description,
                Summary = summary,
                Attention = "",
                Icon = icon,
                Enabled = enabled,
                HasSettings = settings,
                Experimental = experimental,
                ShowEnableControl = showEnableControl,
                Availability = enabled ? "ready" : "disabled"
            };
        }
    }
}
