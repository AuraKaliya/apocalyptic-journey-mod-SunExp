# ScriptExecutor API

`ScriptExecutor` is the main runtime context for CSV script columns. Most card,
buff, relic, event, dialogue, and role scripts eventually run through
`RunScript(scriptName)`.

Source anchors:

- `开发参考资料/反编译文件夹v1.0.23715745/Witch/ScriptExecutor.cs`
- `开发参考资料/反编译文件夹v1.0.23715745/Witch.Core/IScriptExecutor.cs`
- `开发参考资料/反编译文件夹v1.0.23715745/Witch/DataConfig.cs`

## Core Context

Important `IScriptExecutor` members:

- `dataConfig`: the row being executed.
- `Vars`: per-executor string dictionary.
- `Self`: the current actor.
- `Object`: current object target list.
- `Target`: primary target.
- `ScriptDict`: compiled script cache.
- `RunScript(scriptName)`: execute one script column.
- `SetStatus(filter)`: resolve status targets from a filter.
- `AddEvent(eventName, action)`: attach an event listener owned by this executor.
- `Clear()`: remove event listeners and property watchers owned by this executor.

For most fight scripts, `Self` must be non-null except during `InitScript`.

## Useful Host Operations

Frequently used `ScriptExecutor` operations include:

- health and resource changes: `SetHp`, `ChangeHp`, `SetPower`, `ChangePower`
- card operations: `AddCardById`, `AddCardToDeckById`, `DrawCount`, `BurnCard`
- buff operations: `AddBuff`, `RemoveBuff`, `RunImmediately`
- damage and defense: `Damage`, `ChangeDefence`
- target selection: `SetStatus`, `SetStatusById`
- descriptions: `AddDescription`, `GetDesValue`
- event binding: `AddEvent`, `AddTempEvent`, `AddEventWithVar`
- player rewards and event flow through `ScriptExecutor.PlayerInfo`

For C# projects, prefer wrapping these in a local `GameApi/` helper before using
them repeatedly.

## RunScript Behavior

`RunScript(scriptName)`:

1. checks whether the script was already compiled or imported
2. precompiles or resolves the script if needed
3. executes a Roslyn script runner, `Action`, or `Action<ScriptExecutor>`
4. logs the row ID, script name, exception, and script text if execution fails

This is why CSV script columns should stay small: the error points back to the
CSV cell, but the real behavior is much easier to debug in C# source.

## Dialogue Exception

`DataConfig.CreateExecutor()` returns `VisualScriptExecutor` for dialogue rows
and `ScriptExecutor` for other data. Dialogue still uses script columns, but it
is routed through a visual/dialogue-oriented executor.
