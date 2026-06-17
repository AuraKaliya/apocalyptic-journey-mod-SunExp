# SunExp C# 封装 API

SunExp 是本工作区当前最完整的 C# DLL MOD 示例。

发布面：

- `SunExp/`

C# 实现面：

- `SunExp-Dev/`

## DLL 入口

`SunExp-Dev/Entry.cs` 包含一个 `[ModInitialize]` 方法，它会：

1. 把 assembly 注册给 XLua
2. 导入 public `SunExp.Dll.Scripting.*` 类
3. 初始化运行时 Hook
4. 初始化特殊 Tag 行为

XLua 注册只是为了让 CSV 脚本列能调用 C# 方法的桥。它不意味着生产行为应该搬到
Lua 中。

## CSV 可调用层

`SunExp-Dev/Scripting/` 包含 CSV 行直接调用的 public static 入口：

- `CardScripts`
- `BuffScripts`
- `RelicScripts`
- `PartnerScripts`
- `EventScripts`
- `BossScripts`
- `WunaScripts`

CSV 调用应保持短：

```csv
CS.SunExp.Dll.Scripting.CardScripts.Init(self, "spark");
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "spark");
```

## Game API 封装

`SunExp-Dev/GameApi/` 封装宿主对象与危险操作：

- `ExecutorApi`：目标、描述、伤害、灼烧、场地状态、共享战斗状态。
- `PlayerApi`：游戏变量、奖励、字幕、事件结束。
- `BuffApi`：Buff 查询、负面 Buff 清理、余烬持久化。
- `CardConfigApi`：卡牌 ID、费用、临时标记。
- `GameCompatibilityApi`：兼容性守卫与大厅启动 helper。

实现新 SunExp 行为时，优先复用这些 wrapper。

## Hooks 与 Mechanics

`SunExp-Dev/Hooks/` 包含运行时补丁点和 UI/地图集成。

`SunExp-Dev/Mechanics/` 包含可复用逻辑，本身不是 Hook，也不是 CSV 入口。例如
Solar Memory 地图节点池生成与 Solar Radiance 逻辑。

## 编写检查表

- CSV 列需要新操作时，新增 `Scripting` 方法。
- 多个脚本需要同一宿主访问时，新增 `GameApi` wrapper。
- 重复字符串 ID 前，先加 `Infrastructure` 常量。
- 添加 Hook 前，先在反编译快照中验证目标方法。
- 保持 Data 与 Text 行同步。
- 行为变化时同步更新玩家可见文本。
