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
- Treat online de-duplication, relay, and multi-mod coordination as
  `AuraCgShared` responsibilities. SunExp should request playback; AuraTools
  should configure or override playback; neither should implement a private
  Skill CG multiplayer protocol.

## Multiplayer Playback Flow

Use this shape for synchronized Skill CG playback:

1. Only the local owner of the action may initiate playback. In multiplayer,
   skip and log if `OwnerInstanceId` is empty or if the observed action belongs
   to a remote owner/status.
2. The initiator creates a `SkillCgPlayId` from stable event parts such as
   `issuerPlayerId`, `ownerStatusId`, `cardId`, a local counter, and a
   run/fight token. The initiator inserts `(issuerPlayerId, SkillCgPlayId)` into
   the local playback pool and plays once immediately.
3. A non-host initiator sends a server-bound request to the host. It must not
   broadcast directly to all players.
4. The host binds the real sender from the receive context, validates that the
   sender owns the submitted owner/status, normalizes the issuer to the bound
   sender, and broadcasts an authorized playback event to all players.
5. Every client, including the original initiator, checks the global playback
   pool by `(issuerPlayerId, SkillCgPlayId)`. Already seen events are ignored;
   new events are inserted and played.
6. If multiple content/tool paths match the same local action, the shared layer
   should reuse the same play id within a short action window so imported
   AuraTools rules and SunExp declarations cannot produce duplicate playback.

Remote `FightUI.CallActionAnimation` observations are only observations. They
must not create fresh play ids or local broadcasts. Valid network playback comes
from the local owner or from a host-authorized relay.

## Validation

For Skill CG or shared resource protocol changes, run:

```powershell
tools\Build-SunExpDll.ps1
tools\Build-AuraToolsExpDll.ps1
tools\Test-AuraSharedCore.ps1
tools\Test-NetworkRpcAuthority.ps1
tools\Test-SharedArchitectureGuidelines.ps1
tools\Test-SharedReleaseGate.ps1
tools\Test-SharedDllPackaging.ps1
```

If only SunExp registry data changes, still run SunExp validation and inspect
shared resource paths.
