# SunExp Architecture Boundaries

Use this reference when deciding where a C# change belongs.

## Layers

- `SunExp-Dev/Scripting/*Scripts.cs`: stable public static methods called from
  CSV. Keep methods small and parameter lists stable.
- `SunExp-Dev/GameApi/*`: wrappers around game objects, status, buffs, cards,
  damage, vars, events, audio, flow facades, and compatibility dispatch.
- `SunExp-Dev/Mechanics/*`: reusable SunExp behavior that does not need to be
  CSV-callable directly.
- `SunExp-Dev/Features/*`: feature runtimes that are initialized by `Entry` or
  hooks but are not CSV-callable script surfaces, such as Skill CG integration.
- `SunExp-Dev/Hooks/*`: runtime hook registration, UI integration, map
  lifecycle, mode lifecycle, and listener attachment.
- `SunExp-Dev/Hooks/Ui/*`: reusable UI safety, modal, pooling, sprite, HUD, and
  tooltip helpers.
- `SunExp-Dev/Hooks/Visual/*`: Unity visual mutation, shader/material loading,
  VisualBundle access, card visual appliers, and visual animation helpers.
- `SunExp-Dev/Infrastructure/*`: ids, constants, logging, dictionary helpers,
  field ids, parsing, performance settings/counters, frame dispatch, and
  low-level support.
- `SunExp-Dev/Network/*`: explicit multiplayer RPC commands and SunExp RPC
  sender authority binding.

## Current GameApi Split

`ExecutorApi` should remain a facade that delegates to focused wrappers. The
current architectural split includes:

- `ScriptVarApi`: script variable reads and writes.
- `CombatVarApi`: fight-scoped shared integer state.
- `ScriptEventApi`: safe event/listener registration.
- `TargetApi`: target selection and `ScriptExecutor.SetStatus` routing.
- `CardApi`: safe card operations and card lookup.
- `BuffApi`: safe buff operations and buff lookup.
- `DamageApi`: damage, block, target damage, and descriptions.
- `SolarCombatApi`: solar keyword math and solar damage/block helpers.
- `FieldApi`: battlefield-wide field state and epoch sync.
- `BuffOverflowApi`: upper bounds and overflow conversions.
- `StatusApi`: status properties such as max HP and resurrection helpers.
- `DialogueApi` and `DialogueUiApi`: dialogue flow and UI access wrappers.
- `MapItemApi`: map item lookup and map UI compatibility.
- `BattleRewardApi`: safe battle reward access and mutation.
- `CardVisualSkinApi` and `CardVisualEffectApi`: registration facades for
  runtime card skins and visual effects.
- `SunExpResourceCache`: the central resource-loading choke point.
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
`Features` may depend on shared runtime APIs, `GameApi`, `Mechanics`,
`Infrastructure`, and hook-safe presentation helpers.
`Hooks` may call `GameApi`, `Mechanics`, and `Infrastructure`.
`GameApi` should not depend on concrete `Scripting` behavior.

Do not let `Scripting` import `Hooks`. If CSV needs hook-owned behavior, create
a `GameApi` facade and call that facade.

## Runtime Visual And UI Boundaries

Use `sunexp-visual-runtime-dev` for visual registry, VisualBundle, shader,
card visual skin/effect, Skill CG, animated icon, Star Score HUD, Wuna orbit
fire, and map-node visual work.

Keep registry and rule matching in `Mechanics` or registry JSON. Keep Unity
object mutation in `Hooks/Visual` or `Hooks/Ui`. Do not add Unity visual
mutation to `Scripting`.

## SunExp RPC Authority

`SunExpRpcAuthorityRuntime` binds server-bound RPC commands to a sender derived
from the server receive context. SunExp remote commands that mutate shared or
authoritative state should implement the server-bound interface and validate the
bound sender before applying.

For Solar Memory role commit, reject remote submissions whose sender is missing,
outside the lobby, or mismatched with `Role.Id`. Local host direct paths should
use the same sender model through `CreateLocalServerSender`.
