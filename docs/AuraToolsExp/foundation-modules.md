# AuraToolsExp 基础工具模块

本文记录妙妙方案库、MOD 健康检查、大厅状态面板和冒险历程的生产边界。

## 妙妙方案库

- 稳定 ID：`system.preset-library`，文件格式 `AuraTools.Preset`，当前 Schema 1。
- 20 个可持久化模块各自提供显式 Codec；Codec 声明导出面、排除面、风险和依赖，不通过反射猜测配置归属。
- 只导出设置和资源引用，不复制音频、图片、模型、日志、训练样本或数据库。
- 应用前执行文档版本、模块版本、只读配置和内容引用预检；未知的新模块警告后忽略。
- 自定义开局只接受游戏当前已注册的角色、卡牌和遗物。未注册引用警告后忽略；技能类和隐藏卡牌继续采用自定义开局既有排除规则。锁定内容和未启用卡包只要仍在游戏注册表中即可使用。
- 应用按 Codec 顺序执行，变更总线在事务期间批处理；失败时逆序恢复已触碰模块。每次应用先在 `PresetLibrary/Backups` 写入回滚方案。

## MOD 健康检查

- 稳定 ID：`system.mod-health`。
- 只检查游戏主体会读取的契约：一级 MOD 目录、`ModConfig.json`、依赖、`Configuration.json`、`Scripts/Entry.dll`、游戏识别的 `Data`/`Text` CSV 和显式资源引用。
- 通过当前游戏 `loadedModDirectories` 判断启用但加载失败；反射入口不可用时明确降级，不把所有 MOD 误报为失败。
- DLL 仅在“启用但游戏未加载”时复现 `Assembly.LoadFrom(...).GetTypes()`，用于识别当前游戏 API 下的类型加载失败；不会执行初始化方法，也不提供自动修复。
- 报告只保存 MOD 标识、相对路径、异常类型和诊断信息，不导出绝对本机路径。

## 大厅状态面板

- 稳定 ID：`multiplayer.lobby-status`。
- 复用游戏原生 `LobbyInfo.PlayerInfo`、`GameEntryUI.Ready` 和现有 MOD 同步快照，显示玩家、角色同步、准备状态、游戏版本和相对房主的 MOD 差异。
- 面板提供现有 MOD 配置同步入口，但不建立第二套同步协议。
- MOD 健康摘要只显示本机结果；完整报告、文件路径和诊断内容不通过大厅网络广播。
- 准备大厅只放置一个“大厅状态”停靠按钮。MOD 配置在该面板内展开；DPT 使用独立悬浮按钮，详情/历史窗口的遮罩与内容面板是兄弟节点，点击内容不会冒泡到关闭遮罩。

## 冒险历程

- 稳定 ID：`records.adventure-archive`，默认关闭采集。
- 与 DPT/战斗回放共用 `MatchRecords.sqlite3` 物理数据库，但只拥有 `adventure_archives`、`adventure_archive_events` 和 `adventure_archive_snapshots` 三张表；当前冒险历程 Schema 为 2。
- 使用相同 `AdventureId` 关联 `battle_records`，不复制回放数据；删除档案不会删除战斗记录。
- 冒险初始化提交后绑定角色的卡牌、遗物和祝福集合；地图前进、事件与选择、商店、奖励、战斗开始/结束和冒险结束会生成结构化时间线。
- 快照同时保存稳定内容 ID、所有者、当时的本地化名称、所在区域、数量、金币、理智、层数与地图节点。界面按快照差异生成卡牌、遗物、祝福和金币变化。
- 旧 Schema 1 记录在数据库初始化时单向迁移到 Schema 2，并标记为“旧版简要记录”；不保留第二套读取或写入路径。
- 一级模块状态使用缓存计数，不在工具箱渲染路径查询数据库。

## 验证

- `AuraToolsExp-Dev.Tests` 覆盖模块清单、配置事件批处理，以及冒险档案的时间线、快照、`AdventureId` 关联和独立删除。
- `tools/Test-AuraToolsExp.ps1` 审计 20 个模块 Codec、敏感字段排除、事务回滚、模块设置页和图标清单。
- Unity UI Preview 使用与生产一致的模块目录和图标资源，并单独覆盖记录、联机、智能战斗和系统分类。
