---
name: aura-complete-solution-gate
description: Define and verify complete fixes for defects, compatibility changes, migrations and technical-debt cleanup in Aura products, shared runtimes and tooling. Apply to the affected capability before implementation; ordinary new content or editorial changes do not require a migration workflow.
---

# Aura Complete Solution Gate

Preserve the project's requirement to solve the root cause and finish the
affected cutover. Define scope from the user's intended result and the actual
failure chain. Unrelated debt is not added merely because it exists nearby.

## Required reasoning

1. State the observable final contract, its owner, and the relevant invariants.
2. Trace the earliest incorrect ownership, data, protocol or lifecycle boundary.
   Verify the host call chain and return semantics when native behavior matters.
3. Choose the final design at that boundary, including affected consumers.
   A small change is sufficient when it actually restores that contract.
4. Inventory the replaced surface: code, supported data, config, registrations,
   tests, docs, assets and shipped artifacts.
5. Complete required migration and removal in the same cutover. Retain a legacy
   reader only for an explicit supported-data requirement, with one-way
   migration and a stated exit condition.
6. Verify the failure boundary, recovery/cleanup and final postcondition using
   the [validation guide](../aura-project-dev/references/validation.md).

## Non-negotiable boundaries

- Do not present symptom-only mitigation, a bypass, silent loss of functionality
  or an indefinitely parallel implementation as a completed fix.
- Before adding a repair snapshot, relay or compensating writer, prove the
  existing authoritative path cannot represent the intended behavior.
- `deferred`, `skipped`, `sent` and `handled` are not proof of completion.
  A transfer needs a successor owner, durable pending state, a drain trigger
  and a final postcondition.
- Retries, timeouts and fallback behavior are acceptable only as explicit parts
  of a valid final contract. They must not conceal the broken dependency or
  replace the root-cause repair.
- Supported compatibility dispatch and optional-feature failure containment
  remain legitimate contracts. They must not evolve into competing writers or
  claim a required feature works when it does not.
- Phases must contribute production-quality parts of the declared final design,
  with an explicit cutover and no throwaway operational branch at completion.
- Resolve exact cleanup targets and preserve required user data. Existing
  authorization applies; the skill does not create an extra approval cycle.

## Completion evidence

Show that the affected root-cause path works, retained data remains usable,
replaced operational paths are removed, and deferred obligations have drained
or reached an explicit terminal cleanup. Align changed source, tests, docs,
configuration and package artifacts. Name relevant runtime acceptance that
could not be executed rather than declaring it passed.

A skill-only cutover is complete when canonical routes, references, helpers and
callers agree and validation is demonstrated. It does not require changing
unrelated gameplay implementations or rebuilding unchanged binaries.
