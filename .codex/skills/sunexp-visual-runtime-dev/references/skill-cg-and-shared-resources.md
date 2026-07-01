# Skill CG And Shared Resources

Use this reference when editing Skill CG declarations, CG playback, shared
resource manifests, or AuraTools consumption of SunExp CG entries.

## SunExp Surfaces

- `SunExp/SharedResources/package.json`: installs shared CG files and bundle
  resources into AuraShared.
- `SunExp/SharedResources/cg.registry.json`: declares CG entries, display
  names, target roles/cards, media resources, presentation, priority, and
  enabled state.
- `SunExp-Dev/Features/SkillCg/SunExpSkillCgRuntime.cs`: SunExp-side trigger
  and runtime integration.
- `AuraCgShared/*`: shared CG registry, activation, overlay playback, and
  runtime protocol.
- `AuraToolsExp-Dev/Features/SkillCg/*`: tool-side consumption and rule
  management.

## Content/Tool Split

SunExp is a content owner. It installs files, registers CG manifests, and
provides machine-readable semantics. AuraTools consumes the shared declarations
and may create local tool rules or overrides.

Do not make AuraTools guess SunExp folder layout, scan private content folders,
or copy foreign CG files into a tool-owned default directory unless the user is
explicitly creating a local override.

## CG Playback Rules

- Keep visual overlays independent from game UI canvases.
- Keep visual-only overlays non-blocking: no `GraphicRaycaster`, graphics with
  `raycastTarget = false`, and root canvas groups with raycasts disabled.
- Use shared CG registry display names for CG/rule names; role display names
  remain role names only.
- Keep bundled-frame metadata aligned between `package.json`,
  `cg.registry.json`, and the VisualBundle.

## Validation

For Skill CG or shared resource protocol changes, run:

```powershell
tools\Build-SunExpDll.ps1
tools\Build-AuraToolsExpDll.ps1
tools\Test-AuraSharedCore.ps1
tools\Test-SharedArchitectureGuidelines.ps1
tools\Test-SharedReleaseGate.ps1
tools\Test-SharedDllPackaging.ps1
```

If only SunExp registry data changes, still run SunExp validation and inspect
shared resource paths.
