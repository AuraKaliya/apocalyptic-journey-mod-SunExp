# Performance Runtime

Use this reference when touching low-FPS, card-stutter, repeated listeners,
resource loads, repeated table scans, UI rebuilds, or visual hot paths.

## Existing Surfaces

- `SunExpPerformanceSettings`: quality tiers, feature toggles, throttles, and
  pool caps.
- `SunExpPerformanceCounters`: named counters for expensive operations.
- `SunExpFrameScheduler`: next-frame and throttled work scheduling.
- `SunExpFrameDispatcher`: main-thread frame dispatch support.
- `SunExpResourceCache`: canonical resource load and `LoadAll` cache.
- `SunExpConfigIndex`: cached data-table row and filtered-row lookup.
- `SunExpActionEventRouter`: shared native `Action` / `ActionAfter` event
  listener routing.
- `SunExpCardRefreshQueue`: debounced card refresh and `DataUpdate` work.
- `SunExpResourcePreloader`: deferred startup preload through the frame
  scheduler.

## Rules

- Prefer extending these surfaces over adding ad hoc settings, frame loops,
  resource caches, or duplicate listener registrations.
- Route repeated native action listeners through `SunExpActionEventRouter`.
- Queue repeated card UI refresh through `SunExpCardRefreshQueue` instead of
  issuing immediate repeated `DataUpdate` calls.
- Route repeated resource loads through `SunExpResourceCache`.
- Route repeated data-table scans through `SunExpConfigIndex` when a cached
  lookup is already available.
- Start non-critical preload work through `SunExpResourcePreloader` and
  schedule it with `SunExpFrameScheduler.RunOnceNextFrame`.
- Keep one forced initial sync when moving visual or UI code to cached/deferred
  paths.

## Validation

`tools/Test-SunExpArchitecture.ps1` should guard cache boundaries, router
ownership, and direct `ResourceLoader.Load/LoadAll` bypasses. Give
`tools/Test-SunExpCSharp.ps1` a longer timeout for this repository.
