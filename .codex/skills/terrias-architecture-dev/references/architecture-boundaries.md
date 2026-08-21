# Terrias Architecture Boundaries

Use this reference when deciding where a C# change belongs.

## Layers

- `Terrias-Dev/Scripting/*Scripts.cs`: stable public static methods called from
  CSV. Keep methods small and parameter lists stable.
- `Terrias-Dev/GameApi/*`: wrappers around game objects, status, buffs, cards,
  damage, vars, events, audio, flow facades, and compatibility dispatch.
- `Terrias-Dev/Mechanics/*`: reusable Terrias behavior that does not need to be
  CSV-callable directly.
- `Terrias-Dev/Features/*`: feature runtimes that are initialized by `Entry` or
  hooks but are not CSV-callable script surfaces, such as Skill CG integration.
- `Terrias-Dev/Hooks/*`: runtime hook registration, UI integration, map
  lifecycle, mode lifecycle, and listener attachment.
- `Terrias-Dev/Hooks/Ui/*`: reusable UI safety, modal, pooling, sprite, HUD, and
  tooltip helpers.
- `Terrias-Dev/Hooks/Visual/*`: Unity visual mutation, shader/material loading,
  VisualBundle access, card visual appliers, and visual animation helpers.
- `Terrias-Dev/Infrastructure/*`: ids, constants, logging, dictionary helpers,
  field ids, parsing, performance settings/counters, frame dispatch, and
  low-level support.
- `Terrias-Dev/Network/*`: explicit multiplayer RPC commands and Terrias RPC
  sender authority binding.

## Current Mechanics Layout

`Terrias-Dev/Mechanics` is currently a mostly flat directory of focused
services, models, registries, and catalogs. Prefer following the existing flat
file pattern and clear type names when adding a new mechanic. Create a new
subdirectory only when the current repository already has a stable sub-domain
layout or when several closely related files would otherwise become harder to
scan as flat siblings.

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
- `AuraCardPresentationRuntime`: shared owner-qualified card UI lifecycle used
  by sibling consumers. AuraToolsExp owns generic card themes/effects; Terrias
  may subscribe only for necessary content presentation.
- `TerriasResourceCache`: the central resource-loading choke point.
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

Battle semantics use one routed owner per native boundary. Features consume
`BattleInitializing/Materialized/Opening`, the real `FightStartSignaled` and
`BattleReady` barrier, `PlayerTurnEntering/PlayerRoundStarting/Ready`, and
`OutcomeEntering/BattleSettling/BattleEnded`. Do not restore the retired
`FightStarted` or `PlayerRoundStarted` aliases.

## Dependency Direction

`Scripting` may depend on `GameApi`, `Mechanics`, and `Infrastructure`.
`Mechanics` may depend on `GameApi` and `Infrastructure` when needed.
`Features` may depend on shared runtime APIs, `GameApi`, `Mechanics`,
`Infrastructure`, and hook-safe presentation helpers.
`Hooks` may call `GameApi`, `Mechanics`, and `Infrastructure`.
`GameApi` should not depend on concrete `Scripting` behavior.

Do not let `Scripting` import `Hooks`. If CSV needs hook-owned behavior, create
a `GameApi` facade and call that facade.

## Shared Candidate Boundary

Terrias architecture may contain local routers, factories, pools, caches, and
preloaders when they serve Terrias content. Do not let those local helpers become
the base framework for AuraToolsExp or other mods.

Promote the semantic-free part to an Aura shared runtime when the same
capability is needed by both content and tool mods:

- hook registration safety, routed lifecycle dispatch, owner diagnostics, and
  disposable registration handles;
- generic UI primitives, modal shells, row pools, raycast cleanup, and
  transition guards;
- resource registry access, preload planning, cache contracts, and package
  resolution;
- logging gates, InfoOnce/DebugOnce, and throttled diagnostics;
- multiplayer presentation event foundations. Put authority fields, duplicate
  suppression, and cleanup rules in the shared sync scenario model.

Keep Terrias-specific semantics, such as cards, modes, rewards, story, and
Terrias-owned trigger matching, in Terrias. Keep tool-local configuration,
preview, import/export, and overrides in AuraToolsExp. Shared components should
be sibling foundations for both, not Terrias internals exposed outward.

## Runtime Visual And UI Boundaries

Use `terrias-visual-runtime-dev` for visual registry, VisualBundle, shader,
card visual skin/effect, Skill CG, animated icon, Star Score HUD, Wuna orbit
fire, and map-node visual work.

Keep registry and rule matching in `Mechanics` or registry JSON. Keep Unity
object mutation in `Hooks/Visual` or `Hooks/Ui`. Do not add Unity visual
mutation to `Scripting`.

## Terrias RPC Authority

`TerriasRpcAuthorityRuntime` binds server-bound RPC commands to a sender derived
from the server receive context. Terrias remote commands that mutate shared or
authoritative state should implement the server-bound interface and validate the
bound sender before applying.

For Solar Memory role commit, reject remote submissions whose sender is missing,
outside the lobby, or mismatched with `Role.Id`. Local host direct paths should
use the same sender model through `CreateLocalServerSender`.

For synchronized event shapes, payload fields, timing, and duplicate
suppression, load
`terrias-shared-runtime-dev/references/sync-scenario-model.md`.
