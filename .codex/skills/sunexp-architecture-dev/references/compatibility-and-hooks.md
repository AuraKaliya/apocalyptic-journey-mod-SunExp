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

## Event Registration

In script behavior, use `ScriptEventApi` or `ExecutorApi` wrappers for event
registration. Prefer tokened registration when repeated apply/init paths can
otherwise duplicate listeners.

When a listener targets player, enemy, or field state, resolve the intended
status explicitly. Do not borrow unrelated active `ScriptExecutor` state for a
global or cross-status effect.

## Build Output

The project builds as internal assembly `SunExp.Aura` and copies the result to
`SunExp/Scripts/Entry.dll`. Source edits alone do not change shipped behavior.
Build and C# regression tests must run serially because both can write the same
DLL output.
