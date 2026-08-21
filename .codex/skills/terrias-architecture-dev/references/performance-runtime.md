# Performance Runtime

Use this reference when touching low-FPS, card-stutter, repeated listeners,
resource loads, repeated table scans, UI rebuilds, or visual hot paths.

## Existing Surfaces

- `TerriasPerformanceSettings`: quality tiers, feature toggles, throttles, and
  pool caps.
- `TerriasPerformanceCounters`: named counters for expensive operations.
- `TerriasFrameScheduler`: next-frame and throttled work scheduling.
- `TerriasFrameDispatcher`: main-thread frame dispatch support.
- `TerriasResourceCache`: canonical resource load and `LoadAll` cache.
- `TerriasConfigIndex`: cached data-table row and filtered-row lookup.
- `AuraCardActionTransactionRouter`: shared typed card-action transactions for
  `Action`, `ActionAfter`, successful presentation commit, completion, and abort.
- `AuraSkillActionTransactionRouter`: shared skill attempts that observe the
  native `UseScript` boundary and publish committed/completed versus aborted
  transactions.
- `TerriasActionPassiveRegistry`: phase-indexed Terrias Buff/relic/career
  passives behind the single shared native action lane.
- `TerriasCardInteractionRouter`, `TerriasCardExitRouter`,
  `TerriasScriptExecutionRouter`, `TerriasCombatActionRouter`, and
  `TerriasStatusLifecycleRouter`: lock-free semantic subscriber snapshots behind
  one routed native callback per combat boundary.
- `TerriasCardInvalidationService`: dirty-field card invalidation, config/view
  coalescing, derived-state projection, delta presentation, and the single
  guarded `DataUpdate` fallback.
- `ScriptDelegateApi`: replaces the first CSV/XLua card initialization bridge
  with a cached direct C# delegate for subsequent presentation refreshes.
- `TerriasBuffMutationRouter`: typed Add/Remove/level/check transactions with
  phase-specific lock-free subscriber snapshots.
- `TerriasFightPresentationInvalidationService`: converts safe native full
  refreshes into the complete Terrias Buff dependency plan.
- `TerriasResourcePreloader`: deferred startup preload through the frame
  scheduler.

## Rules

- Prefer extending these surfaces over adding ad hoc settings, frame loops,
  resource caches, or duplicate listener registrations.
- Route card action semantics through `AuraCardActionTransactionRouter`; do not
  restore Terrias-local Action/ActionAfter stacks.
- Submit explicit `TerriasCardDirtyFields` through
  `TerriasCardInvalidationService`; never call `CardItem.RefreshTag`, whose
  native contract also performs `DataUpdate`, and never issue repeated direct
  `DataUpdate` calls.
- Route repeated resource loads through `TerriasResourceCache`.
- Route repeated data-table scans through `TerriasConfigIndex` when a cached
  lookup is already available.
- Start non-critical preload work through `TerriasResourcePreloader` and
  schedule it with `TerriasFrameScheduler.RunOnceNextFrame`.
- Keep one forced initial sync when moving visual or UI code to cached/deferred
  paths.
- Keep combat-card diagnostics unregistered while the effective shared
  diagnostics feature is off. Runtime disable disposes routed subscribers; the
  game exposes no supported native hook-removal API, so a dispatcher installed
  earlier in the process is removed only by restart.

## Validation

`tools/Test-TerriasArchitecture.ps1` should guard cache choke points, raw hook
registration ownership, and direct `ResourceLoader.Load/LoadAll` bypasses
through the declarative architecture rule set. Give
`tools/Test-TerriasCSharp.ps1` a longer timeout for this repository.
