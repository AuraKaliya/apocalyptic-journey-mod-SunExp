# Solar Memory Mode Flow

Use this reference when editing Solar Memory entry, preparation, event scripts,
boss routing, finale routing, or old-save settlement.

## Core Files

- `SunExp-Dev/Hooks/SolarMemoryModeRuntime.cs`: mode hooks, map lifecycle,
  completion, fight abort/loss handling, and UI entry integration.
- `SunExp-Dev/Hooks/SolarMemoryRunLauncher.cs`: save creation and preparation
  state initialization.
- `SunExp-Dev/Hooks/SolarMemoryPreparationRuntime.cs`: explicit preparation
  state machine and legacy boolean-state inference.
- `SunExp-Dev/Hooks/SolarMemoryStarterDeckRuntime.cs`: starter deck picker and
  candidate filtering.
- `SunExp-Dev/Hooks/SolarMemorySetupFlowRuntime.cs`: origin allocation UI.
- `SunExp-Dev/Hooks/SolarMemoryBlessingPickerRuntime.cs`: quota blessing picker.
- `SunExp-Dev/GameApi/SolarMemoryFlowApi.cs`: event-script facade into hook
  runtimes.
- `SunExp-Dev/Scripting/EventScripts.cs`: CSV-callable event options.

## Event Script Boundary

`EventScripts` exposes stable CSV entry points only. It should call
`SolarMemoryFlowApi` for mode-level behavior such as preparation completion,
opening setup UI, starting boss rush, finale battle, or settlement. It must not
import `SunExp.Dll.Hooks` directly.

Keep `Data/EventList` option scripts aligned with localized option text. The
start event should offer preparation through C# before boss rush starts.

## Preparation State

The run starts with an explicit preparation step, normally deck selection, and
stores progress through `SunExpIds.SolarMemoryPrepStepKey`. The preparation
runtime must be resumable and must infer from legacy boolean keys for old saves.

Current preparation responsibilities:

- Starter deck selection captures selected packs and sanitizes event cards.
- Origin setup starts with 50 assignable points and clamps by current role caps.
- Blessing picker grants fixed quotas by tier and allows duplicate blessing ids.
- Completion submits the final role only after all steps are complete.

Avoid competing completion paths. Do not chain the native blessing picker from
Solar Memory setup.

## Boss And Finale Routing

Solar Memory settles immediately after the third-layer boss through native
settlement flow. Current tests intentionally reject:

- a dedicated finale map layer;
- pre-boss finale dialogue nodes in map generation;
- opening finale events from generic map transition hooks;
- forced finale map candidate arrays.

When old saves reach legacy terminal levels, settle them before native map item
initialization can index stale map lists.

## UI Lifecycle

Transient Solar Memory UI must be safe during fight abort, loss, and map
transition. Reuse `SunExpUiSafety.DisableRaycastsAndDestroyByName` for teardown
and `SunExpUiBuilder` for repeated panel construction. Close setup UI after
native fight reset and clear pending finale battle state on abort.
