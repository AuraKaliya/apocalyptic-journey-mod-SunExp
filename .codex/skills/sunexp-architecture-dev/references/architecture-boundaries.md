# SunExp Architecture Boundaries

Use this reference when deciding where a C# change belongs.

## Layers

- `SunExp-Dev/Scripting/*Scripts.cs`: stable public static methods called from
  CSV. Keep methods small and parameter lists stable.
- `SunExp-Dev/GameApi/*`: wrappers around game objects, status, buffs, cards,
  damage, vars, events, audio, flow facades, and compatibility dispatch.
- `SunExp-Dev/Mechanics/*`: reusable SunExp behavior that does not need to be
  CSV-callable directly.
- `SunExp-Dev/Hooks/*`: runtime hook registration, UI integration, map
  lifecycle, mode lifecycle, and listener attachment.
- `SunExp-Dev/Infrastructure/*`: ids, constants, logging, dictionary helpers,
  field ids, parsing, and low-level support.
- `SunExp-Dev/Network/*`: explicit multiplayer RPC commands.

## Current GameApi Split

`ExecutorApi` should remain a facade that delegates to focused wrappers. The
current architectural split includes:

- `ScriptVarApi`: script variable reads and writes.
- `CombatVarApi`: fight-scoped shared integer state.
- `ScriptEventApi`: safe event/listener registration.
- `TargetApi`: target selection and `ScriptExecutor.SetStatus` routing.
- `DamageApi`: damage, block, target damage, and descriptions.
- `SolarCombatApi`: solar keyword math and solar damage/block helpers.
- `FieldApi`: battlefield-wide field state and epoch sync.
- `BuffOverflowApi`: upper bounds and overflow conversions.
- `StatusApi`: status properties such as max HP and resurrection helpers.
- `SolarMemoryFlowApi`: CSV-callable facade into Solar Memory hook runtimes.
- `SolarMemoryRoleCommitApi`: final prepared role submission.

When adding a new cross-cutting game wrapper, add it as a focused `GameApi`
class first, then expose legacy convenience methods through `ExecutorApi` only
when existing scripts benefit from the facade.

## Dispatch Registries

Cards, buffs, and relics should use handler registries:

- `CardScripts`: `InitHandlers` and `UseHandlers`.
- `BuffScripts`: `ApplyHandlers` and `ClearHandlers`.
- `RelicScripts`: `FightHandlers`.

Prefer adding a handler entry and private implementation method over adding a
top-level `switch (id)`.

## Dependency Direction

`Scripting` may depend on `GameApi`, `Mechanics`, and `Infrastructure`.
`Mechanics` may depend on `GameApi` and `Infrastructure` when needed.
`Hooks` may call `GameApi`, `Mechanics`, and `Infrastructure`.
`GameApi` should not depend on concrete `Scripting` behavior.

Do not let `Scripting` import `Hooks`. If CSV needs hook-owned behavior, create
a `GameApi` facade and call that facade.
