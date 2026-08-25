---
name: terrias-architecture-dev
description: Project-local skill for refactoring or reviewing Terrias C# architecture boundaries, including Scripting entry points, GameApi facades and wrappers, Mechanics services, Features runtimes, Hooks and UI/Visual runtimes, Infrastructure ids and performance surfaces, handler registries, Managed compatibility, synthetic Partner/status objects, ScriptExecutor local/remote routing, event registration wrappers, Terrias Network/RPC placement and local sender binding, architecture tests, DLL validation, and checks that Terrias internals do not become AuraToolsExp's implicit shared framework for Witch's Apocalyptic Journey.
---

# Terrias Architecture Dev

Use this skill inside this repository when changing the shape of Terrias C# code,
not just adding one content row. Pair it with `terrias-mod-dev` for normal
content workflow and validation.

## Workflow

1. Classify the architectural surface:
   - CSV-callable `Scripting` entry point.
   - Game-facing wrapper or compatibility facade under `GameApi`.
   - Reusable service under `Mechanics`.
   - Use-case transaction and adapter port under `Application`.
   - Non-CSV feature runtime under `Features`.
   - Hook/runtime lifecycle code under `Hooks`.
   - IDs, logging, field ids, or parsing under `Infrastructure`.
   - Network/RPC code under `Network`.
   - Synthetic native combat objects, owner/status indexes, or ScriptExecutor
     identity and locality routing.
2. Inspect the local architecture gate before editing:
   - `tools/Test-TerriasArchitecture.ps1`
   - `tools/architecture-boundary-rules.json`
   - `tools/Test-TerriasCSharp.ps1`
   - `Terrias-Dev.Tests/`
   - affected files under `Terrias-Dev/`
3. Load `references/architecture-boundaries.md` for placement and dependency
   rules. Load `references/compatibility-and-hooks.md` when Managed signatures,
   event registration, lifecycle hooks, or Terrias-local RPC sender binding are
   involved. Load `references/native-synthetic-runtime-objects.md` when a
   Projection, Spirit, Partner-derived status, or other synthetic combat object
   must participate in native ownership, queues, ScriptExecutor behavior, or
   local/remote routing. Load
   `terrias-shared-runtime-dev/references/sync-scenario-model.md`
   through `terrias-shared-runtime-dev` when event shape, RPC authority fields,
   timing, or duplicate suppression are involved.
   Load `references/performance-runtime.md` when touching frame scheduling,
   resource/config caches, repeated listeners, UI pools, or hot-path visuals.
4. Add or adjust a declarative rule when the task creates a namespace,
   dependency-direction, hook-registration, resource-loading, or CSV entry
   boundary. Put observable feature behavior in a C# test project, not in a
   PowerShell source-token assertion.

## Hard Rules

- CSV scripts may call only `CS.Terrias.Dll.Scripting.*`.
- `Scripting` must not import `Terrias.Dll.Hooks`.
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
- A synthetic native object must satisfy every owner map, manager, queue,
  executor route, presentation registration, and cleanup index used by its
  native object type. Creating the object or adding one manager entry is not
  sufficient.
- Scope temporary ScriptExecutor identity changes and restore the exact original
  object and collection references on success and exception. Do not invent or
  overwrite native executor-wide routing flags to compensate for one missing
  owner/status route.
- Use `TerriasRpcAuthorityRuntime` for server-bound Terrias RPC sender binding.
  Remote commands must not authorize from payload-provided identity.
- Keep `Application` between domain/game services and adapters. `Mechanics`
  returns decisions or domain events; it must not reference Application ports.
  `Network`, `Hooks`, `Features`, and `Scripting` may call Application but may
  not depend on one another without an explicit, expiring architecture-ledger
  entry.
- Run the Roslyn semantic architecture gate. Regex scans are supplemental and
  do not establish dependency direction.
- For Network event shape, authority fields, ordering, payload limits, and
  duplicate suppression, use the shared sync scenario reference instead of
  duplicating those rules here.
- Use the established performance surfaces before adding new knobs, frame
  loops, resource caches, or repeated listener registrations.
- Do not let Terrias internal architecture become the implicit shared framework
  for AuraToolsExp. If a hook lifecycle, UI primitive, resource preload,
  logging, object pool, or multiplayer presentation behavior is needed by both
  content and tool mods, mark the semantic-free part as a shared-runtime
  candidate and use `terrias-shared-runtime-dev`.
- Rebuild `Terrias/Scripts/Entry.dll` after C# changes.
- Keep `Test-TerriasArchitecture.ps1` free of private class, method-order, and
  feature-algorithm snapshots. The authoritative Terrias behavior harness is
  `Terrias-Dev.Tests/Terrias-Dev.Tests.csproj`.

## Validation

Run these serially:

```powershell
tools\Build-TerriasDll.ps1
pwsh -NoProfile -File tools\Test-TerriasArchitecture.ps1
tools\Test-TerriasCSharp.ps1
.codex\skills\terrias-mod-dev\scripts\validate-terrias.ps1
```

Do not run `Build-TerriasDll.ps1` and `Test-TerriasCSharp.ps1` in parallel; they
can contend for the same `Terrias.Aura.dll` output.
