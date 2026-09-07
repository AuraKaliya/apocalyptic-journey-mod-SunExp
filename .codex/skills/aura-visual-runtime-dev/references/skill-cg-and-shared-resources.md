# Unified CG and Shared Resources

## Authoritative contract

Use the [unified CG contract](../../../../docs/AuraToolsExp/unified-cg-system-contract.md),
[current registry source](../../../../AuraCgShared/AuraCgRegistry.cs) and
[network runtime](../../../../AuraCgShared/AuraCgRuntime.cs). Query current
schema/protocol values with `tools/Get-AuraProjectContext.ps1`; do not copy
them into another operational guide.

CG registration describes subjectType, subjectIds, signals, optional match,
scene and presentation. Role, card and event subjects share one matching and
playback model. Old skill/card fields are migration inputs only; new
registrations follow the current source/schema.

## Owners

- Terrias SharedResources/aura.discovery.json declares Terrias-owned media.
  The referenced registration, audio and CG manifests preserve that owner.
- AuraTools discovery consumes actually loaded MODs and applies local effective
  configuration. Terrias does not actively install or trigger optional media.
- AuraToolsExp-Dev/Features/Cg owns role/event settings, signal adapters and
  scene source resolution. Features/SkillCg contains the remaining integration
  and visual bootstrap; it is not the complete configuration subsystem.
- AuraCgShared owns matching, planning, media repositories, playback and network
  validation. All subjects share the coordinator and overlay lifecycle.

Use owner-qualified logical resource IDs for media. Keep raw paths, bytes,
cache keys, runtime callbacks and unprocessed damage/history data out of RPCs.

## Scene and signal contracts

A local rich scene source is resolved to a bounded scene plan before transport.
The authority selects entries and layout; receivers validate identity, sender
and bounds and resolve resources locally.

Role low-health CG follows the native Dying signal and the owning per-battle
latch. Do not recreate a player-configurable health threshold. Role resource
selection belongs to the role/type/skill context; resource presentation
overrides are separate from that selection.

Terminal scenes freeze the adventure team independently of DamageMeter and
use one exclusive composition. Distinguish victory reasons, ordinary escape
and defeat. The shared planner and current theme renderer own layout for both
embedded preview and full playback.

## Lifetime and migration

Use the shared media/cache lease and overlay. Release replaced/canceled
requests and asynchronous UI previews. Preserve the declared settling/drain
contract for committed playback; clearing one feature must not destroy another
consumer's resources.

Migrate supported old registration/configuration data at the owning reader.
Keep only the current writer and runtime model. Do not restore historical
per-feature canvases, private caches, damage-settlement RPCs or a Terrias
playback fallback.

## Validation

Run focused CG and tool behavior checks. Add network behavior when authority or
transport changes, bundle builds when bundle sources change, and a coherent
product transaction when publishing C# changes. Use the
[impact guide](../../aura-project-dev/references/validation.md).

Verify role/card/event matching, contextual selection, shared preview/playback,
owner-qualified resource resolution, disable/replace/teardown and next reuse.
Unity media/render/layout changes need runtime evidence in addition to .NET
tests.
