# Runtime Visual UI And Performance

Use this reference for animated icons, Star Score HUD visuals, Wuna orbit fire,
map-node visual replacement, and visual UI helpers.

## UI Visual Boundaries

- `Hooks/Ui/StarScoreHud*` owns Star Score HUD assets, shader materials,
  tooltip, hover probe, and view composition.
- `Hooks/Ui/TerriasModalHost.cs` owns modal parent resolution and close routing.
- `Hooks/Ui/TerriasUiSafety.cs` owns safe transient UI teardown.
- `Hooks/Ui/TerriasUiPool.cs` owns pooled repeated rows and listener scrubbing.
- `Hooks/Ui/TerriasUiSprites.cs` owns cached sprite and nine-slice creation.

Do not duplicate button sprite caches, nine-slice creation, modal close logic,
or pooled-list teardown in feature runtimes.

## Animated And Map Visuals

- Animated buff, blessing, and enemy dictionary icons should resolve frames
  through `VisualRegistry` and load through `TerriasResourceCache`.
- Map-node card art should keep fit logic in `MapNodeTextureFitService` and
  declarations in `MapNodeCardArtRegistry`.
- Solar Memory fixed map visuals belong in `SolarMemoryMapVisualRuntime` and
  related map animation runtimes, not in generic event scripts.

## Performance Controls

Use existing performance surfaces:

- `TerriasPerformanceSettings` for quality and enable/disable knobs.
- `TerriasPerformanceCounters` for expensive visual operations.
- `TerriasFrameScheduler` and `TerriasFrameDispatcher` for deferred or throttled
  work.
- `TerriasResourceCache` and `TerriasConfigIndex` for repeated resource/table
  access.

Resource prewarming must follow lifecycle: build manifests at initialization,
start per-resource shared-frame work after adventure setup is known, pause
nonessential loads during battle, and resume on safe map/reward idle frames.
Do not turn all registered visual resources into one synchronous startup
preload. Keep first-show visuals correct; only duplicate or post-native
reapply work may be deferred.

Wuna orbit fire must be quality-controlled and measurable. Geometry rebuilds,
shader/material updates, and per-frame scans should be throttled by settings
instead of running unconditionally.

## Runtime Checks

Automated tests cannot prove Unity presentation. For visual runtime changes,
reason through:

- Does the first frame render even when async/deferred work is used?
- Can the next native UI still be clicked after overlays or modal windows close?
- Are repeated windows/lists reusing cached sprites and pooled rows?
- Is there one forced initial sync when a cached visual path is introduced?
- Does the feature degrade cleanly when a bundle, shader, or material is
  missing?
