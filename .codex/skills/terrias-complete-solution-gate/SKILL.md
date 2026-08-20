---
name: terrias-complete-solution-gate
description: Highest-priority project-local solution gate for Terrias, AuraToolsExp, and Aura shared/core defect repair, refactoring, migration, compatibility work, and technical-debt cleanup. Use before proposing or implementing a solution whenever a bug, failed workflow, architecture drift, legacy path, stale data/config/test/docs, or retired implementation must be resolved; require a root-cause final-state design and legacy cleanup, and reject stopgaps or indefinite dual paths.
---

# Terrias Complete Solution Gate

Apply this gate before the owning domain skill designs or implements a solution.
Within project instructions, it has higher decision priority than convenience,
delivery speed, patch size, or preserving a retired implementation. A later user
instruction may explicitly change this policy; do not infer an exception from
schedule pressure or implementation difficulty.

## Required Solution Shape

- Solve the root cause and deliver the intended final architecture. Do not
  present a symptom-only mitigation, temporary bypass, or partial restoration as
  a candidate solution.
- Define one authoritative runtime, data model, protocol, lifecycle, and release
  contract for the affected capability.
- Include legacy cleanup in the same plan: inventory and remove superseded code
  paths, adapters, fallbacks, feature flags, schema branches, configuration,
  generated artifacts, tests, documentation, assets, and release claims that no
  longer belong to the final design.
- Migrate retained user data and supported artifacts to the final contract. Keep
  a compatibility reader only when it is an explicit product requirement; make
  it a bounded one-way migration boundary, not a second permanent runtime.
- Keep diagnosis, implementation, migration, cleanup, validation, and release
  synchronization in one completion definition. The task is not complete while
  the old and new implementations both remain operational.

## Rejected Directions

Do not propose or implement any of these as the solution:

- increasing a timeout, adding retries, swallowing an exception, or weakening a
  readiness check without removing the underlying lifecycle dependency;
- adding another fallback, shim, special case, manual repair step, or hidden
  feature flag that leaves the failed architecture intact;
- patching only the downstream symptom when an upstream shared failure causes it;
- indefinitely supporting parallel old/new writers, players, schemas, protocols,
  media formats, or configuration sources;
- relabeling broken data as degraded or analysis-only when the product contract
  requires full functionality and a deterministic migration is possible;
- keeping obsolete tests or documentation merely to describe retired behavior;
- calling throwaway code a phased solution.

Phased delivery is allowed only when every phase is production-quality work that
directly forms part of the declared final architecture, has an explicit cutover,
and leaves no temporary branch behind at completion.

## Solution Workflow

1. State the user-visible final contract and non-negotiable invariants.
2. Trace the complete failure chain and identify the earliest incorrect owner.
3. Design the single target architecture at that owner and all affected
   boundaries; do not start from the smallest patch.
4. Inventory the legacy surface that the target replaces, including stored data,
   source, configuration, tests, documentation, shipped artifacts, and orphaned
   files.
5. Define a deterministic migration and cutover. For destructive data cleanup,
   resolve exact targets, preserve required data, and obtain any authorization
   not already granted by the task.
6. Delete the retired surface and add checks that prevent it from returning.
7. Validate the real integration boundary, failure paths, migration, cleanup,
   shipped artifacts, and source/manifest/documentation consistency.

## Completion Gate

A solution is complete only when all of the following are true:

- the root-cause path works at the real runtime boundary;
- retained old data has been migrated and remains usable under the final
  contract;
- superseded runtime and data paths have been removed rather than disabled;
- orphaned files, rows, settings, registrations, and generated artifacts have a
  deterministic reconciliation or cleanup result;
- tests exercise the real failure boundary and fail on contract drift;
- source, shipped binaries, manifests, configuration UI, and documentation
  describe the same final behavior;
- the handoff names what was migrated, what was deleted, and what evidence proves
  that no legacy operational path remains.
