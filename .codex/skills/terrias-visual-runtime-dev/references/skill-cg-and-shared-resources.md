# CG And Shared Resources

Use this reference for Skill CG, card-use CG, Feast CG, shared playback, and
AuraTools resource ownership.

## Current Surfaces

- `AuraToolsExp/SharedResources/aura.registration.json` installs optional CG,
  voice, card-visual, and other tool-managed resources.
- `AuraToolsExp/SharedResources/cg.registry.json` declares Skill, card-use, and
  Feast CG entries owned by AuraToolsExp.
- `AuraToolsExp-Dev/Features/SkillCg/*` owns local configuration, provider
  requests, previews, per-entry presentation/effect overrides, activation, and
  CG VisualBundle registration.
- `AuraCgShared/*` owns registry, playback queue, overlay presentation, black
  keying, flash steps, session identity, networking, and duplicate suppression.
- `Terrias/SharedResources/aura.registration.json` is a bounded empty retirement
  manifest. It removes the former Terrias optional-media registrations and must
  not acquire new ones.
- `Terrias/SharedResources/cg.retire.registry.json` is the bounded empty
  `manifest` contribution that removes persisted legacy Terrias CG entries.

Terrias provides stable role/card ids and game mechanics only. It does not ship
or trigger optional Skill CG, card-use CG, Feast CG, role voice, or card-use
audio. Opening director animation is unaffected because it is required content
presentation rather than an externally configurable media extension.

## Blazing Crown Collapse

The card's successful action identity remains a Terrias content id. Generic
card-action observation, playback sequencing, black-key handling, flash
presentation, network authority, and deduplication belong to shared runtimes.
AuraTools owns the frame sequence, manifest entry, bundle/material resources,
configuration, and final presentation recipe.

## Migration

Preserve semantic CG ids while changing the provider owner from `Terrias` to
`AuraToolsExp`. Migrate retained local rules, activation entries, Feast
resource overrides, and replacement-skin selections once, then persist only the
new qualified identities. Do not keep a Terrias runtime fallback.

The Terrias empty retirement package and the higher AuraTools package version
provide the source-level cutover. Shipped Terrias assets and registries must be
deleted, not disabled.

## Playback Rules

- Only the local action owner initiates synchronized playback.
- The host binds and validates the real sender before broadcasting.
- All peers deduplicate by issuer and stable playback id.
- Raw media, local paths, bundles, and presentation parameters are never RPC
  payloads; peers resolve the same registered tool resource locally.
- Visual-only overlays are non-blocking and cleaned up on fight/session end.

## Validation

Run:

```powershell
tools\Build-AuraToolsVisualBundle.ps1
tools\Build-AuraToolsExpDll.ps1
tools\Test-AuraCgShared.ps1
tools\Test-AuraToolsExp.ps1
tools\Test-SharedReleaseGate.ps1 -Profile network
tools\Test-SharedDllPackaging.ps1
```
