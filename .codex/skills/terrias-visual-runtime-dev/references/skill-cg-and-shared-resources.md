# CG And Shared Resources

Use this reference for Skill CG, card-use CG, Feast CG, shared playback, and
AuraTools resource ownership.

## Current Surfaces

- `Terrias/SharedResources/aura.discovery.json` declares the resource, Audio,
  and CG contributions carried by Terrias.
- `Terrias/SharedResources/aura.registration.json`, `audio.registry.json`, and
  `cg.registry.json` own Terrias voice and Skill/card-use/Feast CG declarations.
- `AuraToolsExp/SharedResources/*` retains tool defaults, official-content
  extensions, skins, and card-visual resources.
- `AuraToolsExp-Dev/Features/SkillCg/*` owns local configuration, provider
  requests, previews, per-entry presentation/effect overrides, activation, and
  CG VisualBundle registration.
- `AuraCgShared/*` owns registry, playback queue, overlay presentation, black
  keying, flash steps, session identity, networking, and duplicate suppression.
Terrias does not actively install or trigger optional media. AuraToolsExp scans
the fixed discovery entry after MOD loading, registers declarations through the
shared domains, and applies player configuration. Shared playback remains
owner-qualified and independent of Terrias runtime helpers.

## Blazing Crown Collapse

The card's successful action identity remains a Terrias content id. Generic
card-action observation, playback sequencing, black-key handling, flash
presentation, network authority, and deduplication belong to shared runtimes.
Terrias carries the frame sequence and declaration. AuraTools owns local
configuration and final presentation overrides; shared runtime owns playback,
materials, authority, and de-duplication.

## Migration

Preserve semantic CG ids while migrating Terrias media owner-qualified settings
from `AuraToolsExp` back to `Terrias`. Delete AuraTools copies and completed
retirement manifests; do not restore a Terrias playback/runtime fallback.

## Playback Rules

- Only the local action owner initiates synchronized playback.
- The host binds and validates the real sender before broadcasting.
- All peers deduplicate by issuer and stable playback id.
- Raw media, local paths, bundles, and presentation parameters are never RPC
  payloads; peers resolve the same registered tool resource locally.
- Skill entries use schema-v3 `skillIds`; card-use entries use `cardIds`. Typed
  skill and card transactions cannot trigger one another.
- AuraCg network protocol 11 carries an explicit normalized `TriggerKind` in
  each event. A remote `skill` event resolves against `skillIds`; a remote
  `card` event resolves against `cardIds`. Never infer this distinction from a
  shared CardId-shaped payload field or fall back from skill matching to card
  matching.
- The shared CG runtime opens the fight session at `BattleOpening`. At
  `BattleSettling` it stops accepting new battle requests but drains already
  committed/current playback through `BattleEnded` up to the bounded timeout.
  A final-action CG therefore remains visible over settlement instead of being
  cleared immediately. Restart, module disable, or leaving the preparation /
  adventure context still performs an immediate clear.

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
