# SunExp C# Wrapper API

SunExp is the most complete current example of a C# DLL MOD in this workspace.

Published surface:

- `SunExp/`

C# implementation surface:

- `SunExp-Dev/`

## DLL Entry

`SunExp-Dev/Entry.cs` contains a `[ModInitialize]` method that:

1. registers the assembly with XLua
2. imports the public `SunExp.Dll.Scripting.*` classes
3. initializes runtime hooks
4. initializes special tag behavior

The XLua registration is a bridge that lets CSV script columns call C# methods.
It is not a signal to move production behavior into Lua.

## CSV-Callable Layer

`SunExp-Dev/Scripting/` contains the public static entry points that CSV rows
call directly:

- `CardScripts`
- `BuffScripts`
- `RelicScripts`
- `PartnerScripts`
- `EventScripts`
- `BossScripts`
- `WunaScripts`

CSV calls should remain short:

```csv
CS.SunExp.Dll.Scripting.CardScripts.Init(self, "spark");
CS.SunExp.Dll.Scripting.CardScripts.Use(self, "spark");
```

## Game API Wrappers

`SunExp-Dev/GameApi/` wraps host objects and unsafe operations:

- `ExecutorApi`: targets, descriptions, damage, burn, field state, shared combat state.
- `PlayerApi`: game vars, rewards, captions, event termination.
- `BuffApi`: buff lookup, negative buff removal, ember persistence.
- `CardConfigApi`: card IDs, costs, temporary flags.
- `GameCompatibilityApi`: compatibility guards and lobby startup helpers.

Prefer these wrappers over direct calls when implementing new SunExp behavior.

## Hooks and Mechanics

`SunExp-Dev/Hooks/` contains runtime patch points and UI/map integrations.

`SunExp-Dev/Mechanics/` contains reusable logic that is not itself a hook or CSV
entry point, such as Solar Memory map node-pool generation and Solar Radiance
logic.

## Authoring Checklist

- Add a `Scripting` method when a CSV column needs a new operation.
- Add a `GameApi` wrapper when several scripts need the same host access.
- Add an `Infrastructure` ID before repeating a string literal.
- Add hook code only after verifying the target method in the decompiled snapshot.
- Keep Data and Text rows synchronized.
- Update player-facing text when behavior changes.
