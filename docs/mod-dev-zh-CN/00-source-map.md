# 资料源地图

写 MOD 或维护本文档时，先用本页判断应该查哪个资料源。

## 官方教程层

路径：`apocalyptic-journey-mod-tutorial/`

适合用来确认：

- 官方目录结构
- `ModConfig.json` 字段
- Lua `ModConfig:Setup()` 示例
- `ModTemplate/Data` 与 `ModTemplate/Text` 下的 CSV 模板
- 发布与上传流程
- ID、资源路径和本地化后缀的官方规则

重要文件：

- `apocalyptic-journey-mod-tutorial/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/ModTemplate/README.zh-CN.md`
- `apocalyptic-journey-mod-tutorial/DllTemplate/readme.zh-CN.md`

## 反编译快照层

路径：`开发参考资料/反编译文件夹v1.0.23715745/`

适合用来确认：

- 真实方法名与签名
- `RunScript(...)` 生命周期调用点
- `ScriptExecutor`、`IScriptExecutor`、`IStatusManager`、`EventCenter`
- `ModConfig` 的 Lua 初始化、DLL 初始化与 Hook 注册
- 地图、事件、对话、卡牌、Buff、遗物、角色等运行时流程

高价值路径：

- `Witch/Mod/ModConfig.cs`
- `Witch/ScriptExecutor.cs`
- `Witch.Core/IScriptExecutor.cs`
- `Witch.Core/IStatusManager.cs`
- `Witch.Core/EventCenter.cs`
- `Witch/CardItem.cs`
- `Witch/CommonCardItem.cs`
- `Witch/AttackCardItem.cs`
- `Witch/BuffItem.cs`
- `Witch/BlessingRelic.cs`
- `Witch/UI/Window/EventUI.cs`
- `Witch/UI/Window/DialogueUI.cs`
- `Witch/NormalMapManager.cs`
- `Witch/MapManager.cs`
- `AllScripts/AllScripts.cs`

不要把大段反编译实现复制进 MOD 代码或文档。只抽取方法名、签名、流程关系
和实际约束。

## 运行时 MOD 层

路径：`SunExp/`、`GoldExp/`、`StarExp/`、`SanGuoShaExp/` 等

适合用来确认：

- 发布态的 `Data/`、`Text/`、`ModResource/`、`Scripts/Entry.dll`
- 真实 CSV 行与脚本列调用方式
- 资源路径约定
- Data/Text ID 同步情况
- 本地化文本和玩家可见术语

`SunExp/` 是当前最大的例子，包含卡牌、Buff、遗物、职业/角色数据、伙伴、
敌人/敌方卡、地图入口和 EventList 事件。

## C# 实现层

路径：`SunExp-Dev/`、`GoldExp-Dev/`、`StarExp-Dev/` 等

适合用来确认：

- DLL 入口初始化
- CSV 直接调用的 C# 入口
- 宿主 API 封装
- Hook 注册
- 可复用玩法机制
- 对 `Managed/` DLL 的编译引用

推荐职责划分：

- `Scripting/`：CSV 脚本列直接调用的 public static 方法。
- `GameApi/`：对游戏对象和宿主 API 的安全封装。
- `Hooks/`：方法 Hook、UI Hook 和运行时生命周期补丁。
- `Mechanics/`：可复用玩法逻辑。
- `Infrastructure/`：ID、日志、解析 helper 和底层工具。

## 生成参考

`docs/mod-dev/generated/` 下的英文生成文件可通过以下命令刷新：

```powershell
tools\Export-ModDevDocs.ps1
```

本中文目录中的 `generated/` 是对应备份。使用生成索引定位源文件后，仍应回到
上面的资料源验证精确行为。
