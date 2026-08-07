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
- `TerriasActionEventRouter`: shared native `Action` / `ActionAfter` event
  listener routing.
- `TerriasCardRefreshQueue`: debounced card refresh and `DataUpdate` work.
- `TerriasResourcePreloader`: deferred startup preload through the frame
  scheduler.

## Rules

- Prefer extending these surfaces over adding ad hoc settings, frame loops,
  resource caches, or duplicate listener registrations.
- Route repeated native action listeners through `TerriasActionEventRouter`.
- Queue repeated card UI refresh through `TerriasCardRefreshQueue` instead of
  issuing immediate repeated `DataUpdate` calls.
- Route repeated resource loads through `TerriasResourceCache`.
- Route repeated data-table scans through `TerriasConfigIndex` when a cached
  lookup is already available.
- Start non-critical preload work through `TerriasResourcePreloader` and
  schedule it with `TerriasFrameScheduler.RunOnceNextFrame`.
- Keep one forced initial sync when moving visual or UI code to cached/deferred
  paths.

## Validation

`tools/Test-TerriasArchitecture.ps1` should guard cache choke points, raw hook
registration ownership, and direct `ResourceLoader.Load/LoadAll` bypasses
through the declarative architecture rule set. Give
`tools/Test-TerriasCSharp.ps1` a longer timeout for this repository.
