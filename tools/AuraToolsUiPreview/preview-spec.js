window.AURA_TOOLBOX_PREVIEW_SPEC = Object.freeze({
  colors: {
    background: "#08043a",
    panel: "#10143a",
    control: "#1b1f46",
    controlHighlighted: "#272b58",
    categorySelected: "#20244b",
    accent: "#c2a462",
    auraAccent: "#aa92dc",
    text: "#eee6bd",
    mutedText: "#b5ae90",
    success: "#69c8a2",
    warning: "#ddaa58",
    error: "#d87373"
  },
  metrics: {
    categoryWidth: 168,
    headerHeight: 60,
    moduleRowHeight: 96,
    spacing: 8,
    switchWidth: 52,
    switchHeight: 30
  },
  categories: [
    { id: "all", label: "全部", icon: "all" },
    { id: "gameplay", label: "游戏体验", icon: "gameplay" },
    { id: "presentation", label: "表现资源", icon: "presentation" },
    { id: "records", label: "对局记录", icon: "records" },
    { id: "multiplayer", label: "联机工具", icon: "multiplayer" },
    { id: "intelligence", label: "智能战斗", icon: "intelligence" },
    { id: "extensions", label: "扩展工具", icon: "extensions" },
    { id: "system", label: "系统数据", icon: "system" }
  ],
  modules: [
    { id: "gameplay.starter-deck", category: "gameplay", name: "自定义开局", description: "为世界推演配置全局或按角色的开局卡牌与遗物。", summary: "全局 · 卡牌 0/15 · 遗物 0/6", icon: "starter-deck", enabled: false, settings: true },
    { id: "gameplay.card-refresh", category: "gameplay", name: "卡牌刷新", description: "在战斗奖励选牌时提供一次重新抽取。", summary: "战斗奖励选牌可刷新", icon: "card-refresh", enabled: false, settings: false },
    { id: "gameplay.feast", category: "gameplay", name: "一键美餐", description: "进食一次后自动处理剩余食物，并播放角色表现。", summary: "已配置 16 个角色", icon: "feast", enabled: true, settings: true },
    { id: "gameplay.safe-box", category: "gameplay", name: "随身保险箱", description: "在冒险顶部栏直接打开保险箱。", summary: "冒险顶部栏显示入口", icon: "safe-box", enabled: false, settings: false },
    { id: "presentation.skin", category: "presentation", name: "角色皮肤", description: "管理已注册皮肤并选择本地显示效果。", summary: "已启用 3/3 个候选皮肤", icon: "skin", enabled: true, settings: true },
    { id: "presentation.battle-bgm", category: "presentation", name: "战斗背景音乐", description: "替换战斗音乐，并可按角色设置不同曲目。", summary: "通用音频", icon: "battle-bgm", enabled: false, settings: true },
    { id: "presentation.card-use-audio", category: "presentation", name: "出牌音效", description: "配置通用或按角色生效的出牌音效。", summary: "通用音效", icon: "card-use-audio", enabled: true, settings: true },
    { id: "presentation.pixel-emoji", category: "presentation", name: "像素表情", description: "制作、收藏并在联机中展示像素表情。", summary: "作品 12 · 收藏 5", icon: "pixel-emoji", enabled: true, settings: true },
    { id: "presentation.skill-cg", category: "presentation", name: "技能 CG", description: "管理技能触发的角色表现和联机同步。", summary: "角色规则 6 条 · 联机同步开启", icon: "skill-cg", enabled: true, settings: true },
    { id: "presentation.card-use-cg", category: "presentation", name: "卡牌使用 CG", description: "管理注册卡牌的使用演出。", summary: "已启用 4/7 个注册项", icon: "card-use-cg", enabled: true, settings: true },
    { id: "records.damage-statistics", category: "records", name: "伤害统计", description: "记录本场伤害并提供局内和冒险统计。", summary: "本场 · 全部阵营 · 表格", icon: "damage-statistics", enabled: true, settings: true },
    { id: "records.battle-replay", category: "records", name: "战斗回放", description: "自动记录对局并提供回放与视频导出。", summary: "自动保存上限 20", icon: "battle-replay", enabled: true, settings: true },
    { id: "multiplayer.mod-sync", category: "multiplayer", name: "MOD 配置同步", description: "在联机大厅中检查并同步工具配置。", summary: "当前不在联机大厅", icon: "mod-sync", enabled: true, settings: true },
    { id: "intelligence.auto-battle", category: "intelligence", name: "战斗策略实验室", description: "管理模型、训练、评估和实机验证。", summary: "未选择模型", icon: "auto-battle", enabled: false, settings: true, experimental: true },
    { id: "system.file-logging", category: "system", name: "文件日志", description: "将工具运行信息写入独立日志文件。", summary: "Info 及以上", icon: "file-logging", enabled: true, settings: true }
  ]
});
