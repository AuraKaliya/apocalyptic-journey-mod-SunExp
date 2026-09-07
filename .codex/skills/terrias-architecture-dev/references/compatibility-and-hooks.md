# Compatibility And Hook Boundaries

Use this reference when a change touches Managed APIs, game-reference findings,
hook registration, Terrias-local RPC sender binding, or runtime lifecycle
actions.

## Managed Compatibility

Treat repository `Managed/` assemblies as the compile contract. Use the
decompiled game reference index only to understand official behavior and likely
signature history.

For signature drift:

- locate every direct caller first;
- move reflection into one focused `GameApi` wrapper;
- support the current signature and known legacy signatures;
- provide a deterministic table/API fallback;
- log the fallback path without breaking UI or gameplay flow.

Do not scatter reflection or `GetType().GetProperty(...)` across scripting
files.

## Hook Containment

Register hooks through routed shared wrappers such as
`AuraSharedHooks.RegisterBeforeRouted` or
`AuraSharedHooks.RegisterAfterRouted`. Log failures with a prefix that
identifies the subsystem.

Combat features must subscribe to the established semantic routers rather than
owning native targets. `AuraBattleLifecycleRouter` derives exact battle phases
from native boundaries and EventCenter signals; `AuraCardActionTransactionRouter`
and `AuraSkillActionTransactionRouter` own card and skill use; Terrias card
interaction, exit, Buff mutation, status, script execution, and other-object
routers own their respective native targets. `TerriasHookRegistry.Before/After`
are routed compatibility entry points for non-combat adapters, not permission
to create a second combat lifecycle.

Independent startup or fight-start actions should run in separately named
steps. A failed HP adjustment, UI setup, listener, resource registration, or
tag setup must not block later unrelated actions.

Entry initialization should keep independent systems in named `RunStep` calls:
shared core, RPC authority, shared resources, visual registry, Skill CG,
Journey, audio, UI guard, performance runtime, gameplay hooks, and tags should
fail independently.

## Event Registration

In script behavior, use `ScriptEventApi` or `ExecutorApi` wrappers for event
registration. A single `TryAddEvent` receives an automatic battle lease;
multi-listener effects must use transactional `ScriptEventApi.BeginFightScope`.
Both paths keep registration state out of persistent `Vars` and allow the same
card, Buff, relic, blessing, or career executor to re-register in the next
battle session.

When a listener targets player, enemy, or field state, resolve the intended
status explicitly. Do not borrow unrelated active `ScriptExecutor` state for a
global or cross-status effect.

## RPC Sender Binding

Remote RPC authority must come from the server receive context, not from fields
inside the RPC payload. Terrias server-bound commands should bind a
`TerriasRpcSender` through `TerriasRpcAuthorityRuntime` and pass that sender into
server-side validation.

Host-local direct paths should create a local server sender and use the same
validation flow as remote RPC paths.

For event-shape classification, payload fields, timing, duplicate suppression,
and bulk-transfer requirements, load the shared sync scenario model through
`aura-shared-runtime-dev`.

## Build Output

The project builds as internal assembly `Terrias.Aura`. Product projects no
longer copy package DLLs from MSBuild targets. `Build-TerriasDll.ps1` runs the
main consumer transaction and `Publish-MainSharedConsumers.ps1` publishes the
validated Entry/Aura.Shared pair to Terrias and AuraToolsExp. Source edits or a
direct `.csproj` build alone do not change shipped behavior. Product builds and
C# regression tests remain serial because the product transaction writes the
same package outputs.
