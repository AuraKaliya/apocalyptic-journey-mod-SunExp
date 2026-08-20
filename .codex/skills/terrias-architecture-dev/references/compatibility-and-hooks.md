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

Register hooks through shared wrappers such as `AuraSharedHooks.RegisterBefore`
or `AuraSharedHooks.RegisterAfter` when available. Log failures with a prefix
that identifies the subsystem.

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
`terrias-shared-runtime-dev`.

## Build Output

The project builds as internal assembly `Terrias.Aura` and copies the result to
`Terrias/Scripts/Entry.dll`. Source edits alone do not change shipped behavior.
Build and C# regression tests must run serially because both can write the same
DLL output.
