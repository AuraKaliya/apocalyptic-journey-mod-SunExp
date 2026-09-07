---
name: aura-shared-runtime-dev
description: Develop Aura shared/core contracts used by content and tool MODs, including storage, resource discovery, lifecycle ownership, multiplayer authority and shared DLL publication. Use for changes to the shared boundary, not every product-internal edit.
---

# Aura Shared Runtime Dev

Terrias and AuraToolsExp are sibling consumers. Core supplies semantic-free
coordination; domain components own domain rules; product adapters install,
observe and delegate. Discover compiled domains and product consumers with
`tools/Get-AuraProjectContext.ps1`.

## Choose the affected reference

- [Shared boundaries](references/shared-boundaries.md): Core/domain/adapter
  responsibilities, protocol identity and authority.
- [Content/tool boundary](references/content-tool-shared-boundary.md):
  resource discovery, registered defaults and local effective configuration.
- [Mutable runtime ownership](references/shared-mutable-runtime-ownership.md):
  nested temporary mutations, pooled generations and cleanup obligations.
- [Sync scenarios](references/sync-scenario-model.md): sender binding, payloads,
  ordering, deduplication, local overrides and initialization.
- [DLL and release](references/shared-dll-and-release-gates.md): canonical
  assembly, supported consumers, public ABI review and publication.
- [Terrias integration](references/terrias-shared-integration.md): only when
  the actual consumer being changed is Terrias.

Read the owner source and its focused behavior tests before selecting a design.
For a repair or cutover, use the
[complete-solution gate](../aura-complete-solution-gate/SKILL.md).

## Shared invariants

- Give registered artifacts stable owner-qualified identity. Preserve foreign
  ownership when a tool selects or overrides a registration.
- Core storage/registry/package writes use AuraSharedCore boundaries. Core
  does not acquire card, skin, CG or other product semantics.
- Content optional media remains declarative. AuraTools discovery scans loaded
  MODs through the fixed discovery contract, deduplicates physical packages by
  .modproj id and registers through shared domains.
- AuraTools local configuration can override effective behavior without
  rewriting the foreign registration source. Tool custom-start loadouts remain
  tool configuration; content modes use generic starter-deck ownership APIs.
- Shared multiplayer mutation requires the correct authority. Bind commands
  to the authenticated sender, not identity supplied in the payload.
- Adapters install and bridge; domain components decide identity, precedence,
  conflict handling, lifecycle and deterministic failure results.
- When consumers temporarily mutate the same property, one coordinator owns
  baseline, logical generation and release order. Persistent selection and
  additive aggregation require their own conflict models.
- Optional feature incompatibility must not abort unrelated initialization.
  Isolate independent setup steps and make failure observable.
- Shared source must not depend on product internals. Product projects consume
  one Aura.Shared assembly and the sole publisher updates their package copies.

## Validation

Use the [impact guide](../aura-project-dev/references/validation.md).
Internal domain work needs the domain suite and shared compilation; public
changes additionally need ABI review and affected consumer checks. Publishing
a shared fix requires a coherent product transaction.

The network profile combines domain behavior with a generic RPC registration
scan. A source scan cannot replace sender, dedupe or lifecycle behavior tests.
Keep archived TestMods outside product/shared release validation.
