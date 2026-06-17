# 添加一个地图事件

这份清单覆盖 SunExp 风格项目中由 EventList 驱动的地图事件。

## 1. 添加 EventList Data

在以下位置添加行：

```text
<ModName>/Data/EventList/<file>.csv
```

典型字段：

- `Id`
- `1Script`
- `2Script`
- `3Script`
- `4Script`
- `InitScript`
- `IsHighRisk`
- `EntryScript`

保持选项脚本短：

```csv
CS.SunExp.Dll.Scripting.EventScripts.RewardRelic(1, "SunExp_sunexp_morning_shard");
```

## 2. 添加 EventList Text

在以下位置添加匹配行：

```text
<ModName>/Text/EventList/<file>.csv
```

填写标题、总描述、选项描述和本地化变体。只给实际可选的选项写选项文本。

## 3. 添加 Map 行

如果事件应出现在地图上，添加或更新：

```text
<ModName>/Data/Map/<file>.csv
<ModName>/Text/Map/<file>.csv
```

根据已有地图行和反编译地图流程确认 `Type`、`NodeId` 与 `Level`。

## 4. 添加 C# 行为

使用：

```text
<ModName>-Dev/Scripting/EventScripts.cs
```

奖励、字幕、游戏变量和事件结束优先通过 `PlayerApi` 或本项目等价 helper 完成。

## 5. 验证流程

自动检查应覆盖 Data/Text 同步和基础资源路径。手动检查应覆盖：

- 事件出现在预期地图层
- `InitScript` 在玩家看到选项前准备好文本/变量
- 选项脚本调用 `ContinueEvent`、`EndEvent` 或其他明确流转动作
- 重复事件不会重复发一次性奖励，除非这是设计目标
