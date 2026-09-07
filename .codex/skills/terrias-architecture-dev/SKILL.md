---
name: terrias-architecture-dev
description: Change or review Terrias C# boundaries, native hooks, Managed compatibility, synthetic Partner/status identity and ScriptExecutor locality. Use for architecture or native integration; ordinary Data/Text edits use the content skill.
---

# Terrias Architecture Dev

Classify the affected layer before editing: Infrastructure, GameApi,
Mechanics, Application, or an adapter (Scripting, Hooks, Features, Network).
Entry is the composition root.

Read [architecture rules](../../../tools/architecture-boundary-rules.json) and
the [exception ledger](../../../tools/architecture-boundary-exceptions.json)
for the actual allowed graph. Existing exceptions are bounded debt; new code
does not inherit them simply by belonging to the same feature.

## References

- [Layer boundaries](references/architecture-boundaries.md): placement,
  dependency direction and existing facades.
- [Compatibility and hooks](references/compatibility-and-hooks.md): Managed
  signature drift, event registration and sender binding.
- [Synthetic native objects](references/native-synthetic-runtime-objects.md):
  owner/index/queue participation, execution locality and exact scope restoration.
- [Performance](references/performance-runtime.md): established schedulers,
  caches, listener registration and hot-path instrumentation.
- [Sync scenarios](../aura-shared-runtime-dev/references/sync-scenario-model.md):
  only when custom RPC shape, authority, ordering or deduplication changes.

## Invariants

- CSV calls only `CS.Terrias.Dll.Scripting.*`. Scripting uses ScriptEventApi
  or ExecutorApi for event registration, not raw AddEvent/AddTempEvent.
- Keep ExecutorApi as a facade; focused GameApi classes implement game access
  and supported signature dispatch.
- Card/Buff/Relic dispatch uses handler registries, not restored top-level ID
  switches. Reusable behavior belongs to the appropriate domain service.
- Mechanics returns decisions or events and does not reference Application
  ports. Application owns use-case transactions; adapters do not directly
  depend on one another outside a registered, expiring exception.
- Synthetic objects must satisfy the native identity, indexes, managers,
  queues, locality, presentation and cleanup surfaces used by their type.
  Semantic owner, simulation authority and execution route are distinct.
- Temporary ScriptExecutor identity changes restore exact original objects and
  collection references on success and exception. Preserve native routing Vars;
  do not invent executor-wide overrides to fix one missing route.
- Server-bound Terrias commands use TerriasRpcAuthorityRuntime sender binding;
  payload-provided identity is not authorization.
- Isolate independent setup steps. Shared foundations needed by both products
  belong in Aura shared code, without Terrias semantics.

## Validation

Use [impact selection](../aura-project-dev/references/validation.md).
Run the Roslyn semantic architecture gate for layer/hook/entry changes;
regex scans are supplemental. Test observable feature behavior in the owning
C# suite, including native locality and failure cleanup when relevant.

Reconcile affected exception entries and report remaining debt honestly.
Do not grow the exception budget or refresh a baseline merely to turn a
failure green. Product publication uses the shared transaction once.
