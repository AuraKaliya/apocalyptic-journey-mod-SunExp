# ScriptExecutor API

`ScriptExecutor` 是 CSV 脚本列最主要的运行时上下文。多数卡牌、Buff、遗物、
事件、对话和角色脚本最终都会通过 `RunScript(scriptName)` 执行。

源码锚点：

- `开发参考资料/反编译文件夹v1.0.23693118/Witch/ScriptExecutor.cs`
- `开发参考资料/反编译文件夹v1.0.23693118/Witch.Core/IScriptExecutor.cs`
- `开发参考资料/反编译文件夹v1.0.23693118/Witch/DataConfig.cs`

## 核心上下文

重要 `IScriptExecutor` 成员：

- `dataConfig`：当前执行的数据行。
- `Vars`：当前 executor 的字符串字典。
- `Self`：当前行动者。
- `Object`：当前对象目标列表。
- `Target`：主目标。
- `ScriptDict`：已编译脚本缓存。
- `RunScript(scriptName)`：执行一个脚本列。
- `SetStatus(filter)`：通过过滤器解析目标。
- `AddEvent(eventName, action)`：为此 executor 挂接事件监听。
- `Clear()`：清理此 executor 拥有的事件监听与属性 watcher。

多数战斗脚本中，除 `InitScript` 外，`Self` 应非空。

## 常用宿主操作

常见 `ScriptExecutor` 操作包括：

- 血量与资源：`SetHp`、`ChangeHp`、`SetPower`、`ChangePower`
- 卡牌：`AddCardById`、`AddCardToDeckById`、`DrawCount`、`BurnCard`
- Buff：`AddBuff`、`RemoveBuff`、`RunImmediately`
- 伤害与防御：`Damage`、`ChangeDefence`
- 目标：`SetStatus`、`SetStatusById`
- 描述：`AddDescription`、`GetDesValue`
- 事件绑定：`AddEvent`、`AddTempEvent`、`AddEventWithVar`
- 奖励与事件流：通过 `ScriptExecutor.PlayerInfo`

C# 项目里，如果多处需要同一操作，优先封装到本地 `GameApi/` helper。

## RunScript 行为

`RunScript(scriptName)` 会：

1. 检查脚本是否已编译或已导入
2. 必要时预编译或解析脚本
3. 执行 Roslyn script runner、`Action` 或 `Action<ScriptExecutor>`
4. 如果执行失败，记录行 ID、脚本名、异常和脚本文本

这就是为什么 CSV 脚本列应尽量短：错误会指回 CSV 单元格，但真实行为放在 C#
源码里更容易调试。

## 对话例外

`DataConfig.CreateExecutor()` 对 Dialogue 行返回 `VisualScriptExecutor`，其他数据
返回 `ScriptExecutor`。对话也使用脚本列，但执行器更偏视觉/对话流程。
