# Status and Events

This page covers the two most important combat/runtime surfaces around
`ScriptExecutor`: `IStatusManager` and `EventCenter`.

Source anchors:

- `开发参考资料/反编译文件夹v1.0.23693118/Witch.Core/IStatusManager.cs`
- `开发参考资料/反编译文件夹v1.0.23693118/Witch.Core/EventCenter.cs`

## IStatusManager

`IStatusManager` represents an actor or target in combat.

Important groups:

- identity: `Name`, `InstanceId`, `fatherObject`
- combat values: `MaxHp`, `CurHp`, `Defend`
- state: `state`, `animatedState`
- buffs: `AddBuff`, `RemoveBuff`, `GetBuff`, `GetBuffs`, `ClearAllBuff`
- damage and recovery: `Hit`, `Heal`, `DamageCalculate`, `DefenceCalculate`
- display: `UpdateDisplay`, `UpdateStatus`, `SetSprite`
- summons: `AddSummon`, `FindSummon`, `RemoveSummon`, `ShowSummon`

When a script temporarily changes target context, restore it or use a wrapper
that scopes the change. Several current `GameApi/` helpers exist for this reason.

## EventCenter

`EventCenter` is the global event bus.

Common methods:

- `AddEventListener(eventName, action, owner, dispose)`
- `AddEventListener<T>(eventName, action, owner, dispose)`
- `RemoveEventListener(eventName, owner)`
- `Clear(owner)`
- `Clear(disposeTypes)`
- `EventTrigger(eventName)`
- `EventTrigger<T>(eventName, param)`

The owner object is important. It controls duplicate registration behavior and
cleanup. For fight-only listeners, use a fight-end disposal mode when available
or clear the listener in the matching `ClearScript`.

## Fight Event Names

The official tutorial lists common fight events such as:

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

Many fight events are suffixed with an actor `InstanceId`. Verify the exact
string in the decompiled call site before adding a new listener.

## Practical Rules

- Register persistent buff effects in `ApplyScript` and clean them in `ClearScript`.
- Guard repeated registration with a token or owner key.
- Prefer local wrappers for event names and owner cleanup.
- Treat multiplayer/network broadcast paths as risky until verified in game.
