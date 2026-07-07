# Compatibility And Hook Boundaries

Use this reference when a change touches Managed APIs, decompiled reference
findings, hook registration, or runtime lifecycle actions.

## Managed Compatibility

Treat repository `Managed/` assemblies as the compile contract. Use the
decompiled reference only to understand official behavior and likely signature
history.

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
registration. Prefer tokened registration when repeated apply/init paths can
otherwise duplicate listeners.

When a listener targets player, enemy, or field state, resolve the intended
status explicitly. Do not borrow unrelated active `ScriptExecutor` state for a
global or cross-status effect.

## RPC Sender Binding

Remote RPC authority must come from the server receive context, not from fields
inside the RPC payload. SunExp server-bound commands should bind a
`SunExpRpcSender` through `SunExpRpcAuthorityRuntime` and pass that sender into
server-side validation.

Host-local direct paths should create a local server sender and use the same
validation flow as remote RPC paths.

## RPC Shape Checklist

Before adding or changing `SunExp-Dev/Network/*`, classify the RPC shape:

- `command`: one requested action. Require a token or other idempotency key and
  validate the bound sender before applying.
- `snapshot`: authoritative state repair or replacement. Require state scope
  and sequence/version semantics; ignore stale state and request repair on gaps
  when possible.
- `presentation-event`: CG, visual, audio, projection, or temporary UI relay.
  Require an event/playback id, owner status, max age or duplicate window, and
  lifecycle cleanup. Do not let it advance progression.
- `bulk-transfer`: payload too large for normal RPC. Require payload budget,
  chunk metadata, checksum, expiration, and active-buffer caps.

Pick the field family deliberately: use `token` for idempotent commands,
`sequence` for ordered per-owner streams, `version` for state generations,
`hash` for payload/content consistency, and TTL/max-age fields for transient
events.

Remote observation hooks must not generate new authoritative event ids. If the
local owner/status cannot be resolved in multiplayer, skip the request and log a
diagnostic instead of broadcasting an ambiguous event.

## Build Output

The project builds as internal assembly `SunExp.Aura` and copies the result to
`SunExp/Scripts/Entry.dll`. Source edits alone do not change shipped behavior.
Build and C# regression tests must run serially because both can write the same
DLL output.
