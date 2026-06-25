---
name: sunexp-architecture-dev
description: Project-local skill for refactoring or reviewing SunExp C# architecture boundaries, including Scripting entry points, GameApi facades and wrappers, Mechanics services, Hooks runtimes, Infrastructure ids, handler registries, Managed compatibility, event registration wrappers, architecture tests, and DLL validation for Witch's Apocalyptic Journey.
---

# SunExp Architecture Dev

Use this skill inside this repository when changing the shape of SunExp C# code,
not just adding one content row. Pair it with `sunexp-mod-dev` for normal
content workflow and validation.

## Workflow

1. Classify the architectural surface:
   - CSV-callable `Scripting` entry point.
   - Game-facing wrapper or compatibility facade under `GameApi`.
   - Reusable service under `Mechanics`.
   - Hook/runtime lifecycle code under `Hooks`.
   - IDs, logging, field ids, or parsing under `Infrastructure`.
   - Network/RPC code under `Network`.
2. Inspect the local architecture gate before editing:
   - `tools/Test-SunExpArchitecture.ps1`
   - `tools/Test-SunExpCSharp.ps1`
   - affected files under `SunExp-Dev/`
3. Load `references/architecture-boundaries.md` for placement and dependency
   rules. Load `references/compatibility-and-hooks.md` when Managed signatures,
   event registration, or lifecycle hooks are involved.
4. Add or adjust architecture assertions when the task creates a new boundary
   that future edits must preserve.

## Hard Rules

- CSV scripts may call only `CS.SunExp.Dll.Scripting.*`.
- `Scripting` must not import `SunExp.Dll.Hooks`.
- `Scripting` must register events through `ScriptEventApi` or `ExecutorApi`
  wrappers, not raw `AddEvent` or `AddTempEvent`.
- Keep `ExecutorApi` as a compatibility facade. Put implementation in focused
  `GameApi` classes.
- Use handler registries for Card, Buff, and Relic id dispatch. Do not restore
  top-level `switch (id)` routing.
- Put long behavior in `Mechanics` when multiple scripts or hooks need it.
- Put reflection for signature drift in one `GameApi` wrapper and provide a
  deterministic fallback.
- Use named/logged lifecycle steps so one failed setup action does not abort
  unrelated initialization.
- Rebuild `SunExp/Scripts/Entry.dll` after C# changes.

## Validation

Run these serially:

```powershell
tools\Build-SunExpDll.ps1
tools\Test-SunExpArchitecture.ps1
tools\Test-SunExpCSharp.ps1
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

Do not run `Build-SunExpDll.ps1` and `Test-SunExpCSharp.ps1` in parallel; they
can contend for the same `SunExp.Aura.dll` output.
