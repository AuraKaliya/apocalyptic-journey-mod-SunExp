# 状态与事件

本页覆盖 `ScriptExecutor` 周边两个最重要的战斗/运行时表面：
`IStatusManager` 与 `EventCenter`。

源码锚点：

- `开发参考资料/反编译文件夹v1.0.23715745/Witch.Core/IStatusManager.cs`
- `开发参考资料/反编译文件夹v1.0.23715745/Witch.Core/EventCenter.cs`

## IStatusManager

`IStatusManager` 表示战斗中的行动者或目标。

重要分组：

- 身份：`Name`、`InstanceId`、`fatherObject`
- 战斗值：`MaxHp`、`CurHp`、`Defend`
- 状态：`state`、`animatedState`
- Buff：`AddBuff`、`RemoveBuff`、`GetBuff`、`GetBuffs`、`ClearAllBuff`
- 伤害与恢复：`Hit`、`Heal`、`DamageCalculate`、`DefenceCalculate`
- 显示：`UpdateDisplay`、`UpdateStatus`、`SetSprite`
- 召唤物：`AddSummon`、`FindSummon`、`RemoveSummon`、`ShowSummon`

脚本临时改变目标上下文时，应恢复上下文，或使用能限定作用域的 wrapper。
当前多个 `GameApi/` helper 就是为此存在的。

## EventCenter

`EventCenter` 是全局事件总线。

常用方法：

- `AddEventListener(eventName, action, owner, dispose)`
- `AddEventListener<T>(eventName, action, owner, dispose)`
- `RemoveEventListener(eventName, owner)`
- `Clear(owner)`
- `Clear(disposeTypes)`
- `EventTrigger(eventName)`
- `EventTrigger<T>(eventName, param)`

owner 对象很重要。它决定重复注册行为和清理方式。战斗内监听应尽量使用战斗结束
自动清理的 dispose 模式，或在对应 `ClearScript` 中清理。

## 战斗事件名

官方教程列出的常见战斗事件包括：

- `Attack`
- `AttackDone`
- `CostPower`
- `NoPower`
- `AddPower`
- `Dead`
- `OnEnemyDead`
- `EndRound`
- `StartRound`
- `FightStart`
- `Hurt`
- `Heal`
- `SelectCardEnd`
- `OnTriggerEffect`
- `ScriptExecute`

许多战斗事件会拼接行动者 `InstanceId`。新增监听前，应在反编译调用点确认精确
字符串。

## 实用规则

- 持续性 Buff 效果在 `ApplyScript` 注册，在 `ClearScript` 清理。
- 重复注册要用 token 或 owner key 防重。
- 事件名和 owner 清理优先放到本地 wrapper。
- 多人/网络广播路径在进游戏验证前都应视为高风险。
