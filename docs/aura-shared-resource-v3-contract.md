# AuraShared resource protocol v3

AuraShared v3 separates persistent declarations from current-process activity.
`AuraSharedCore` owns storage, identity, leases, revisions and atomic package
installation. CG, Audio, Skin and other domain runtimes own their scope and
fallback semantics. SunExp and AuraToolsExp remain sibling consumers.

## Canonical layout

The canonical path template is:

```text
module/scopeType/scopeId/featureId/ownerModId/resourceId
```

Concrete payloads live below the resource leaf as `content.<ext>` for files or
`content/` for directories. Core writes the following layered documents:

- `<module>/aura.module.json`: protocol and layout metadata.
- `<feature scope>/aura.feature.json`: scope, effect mode and missing policy.
- `<owner>/aura.defaults.json`: owner-qualified default profiles.
- `<resource>/aura.resource.json`: immutable declaration and canonical path.
- `<resource>/aura.state.json`: seed hash, current hash and customization state.

Mods never concatenate this layout themselves. They submit scope and resource
identities; `AuraSharedResourcePathPolicy` derives the path.

## Registration and Active lease

Each owner supplies `SharedResources/aura.registration.json` with
`schemaVersion: 3`, `participantKind`, package identity, resources and defaults.
The Core persists one document per owner package below `_Registry/V3/Owners/<owner>`
and creates an active lease below
`_Runtime/Leases/<sessionId>/<owner>/<packageId>.json`. Multiple packages from
one owner remain active together; re-registering the same package replaces only
that package. Persistent data without a lease is residual data and cannot become
an active candidate.

Registration is idempotent. Each changed scope receives a new revision and is
published independently. Late tool loading therefore re-resolves only affected
scopes and does not depend on a global “all mods loaded” phase. Runtime indices
below `_Runtime/Index` are rebuildable caches, not authority.

One missing declaration produces an `Unavailable` item result without failing
unrelated declarations from the same Mod. Additive features use `Skip`;
replacement features use `NativeFallback` and must not suppress the game before
the replacement resource is ready.

## Defaults and formal resources

Configuration precedence is:

```text
LocalUser > ToolDefault > ContentDefault > ModuleDefault
```

Formal resource selection is independent. A ToolDefault profile may win while
the selected payload remains owned by a content Mod. A tool cannot re-own or
rewrite a foreign declaration. A local override is sparse and changes only the
effective local result.

## User modifications

v3 installs use `preserveLocalChanges`. Core records the packaged seed hash and
the current content hash separately. If the live file differs from the previous
seed, registration returns `PreservedLocal` and does not repair or overwrite it.
Explicit imports use an owner-qualified `local-user` resource leaf. Resetting a
default may change the profile reference without deleting the old user file.

## Migration

Migration is dual-read and single-write:

```text
read:  v3 canonical path, then declared v2 legacyPaths
write: v3 canonical path only
```

For a legacy file that differs from the packaged seed, Core copies the user
version to the v3 leaf before package registration and records the decision in
`_Migration/V2ToV3/<owner>/journal.json`. Exact duplicates are not imported as
new custom resources. Legacy directories and ambiguous files remain untouched
and diagnosable; the first migration never deletes them.

## Runtime resolution

`AuraSharedResourceProtocol.Resolve` returns activity, canonical or legacy
selection, owner/resource identity, scope revision, outcome and fallback.
`ResolveEffective` additionally reports configuration source separately from
resource source. Network presentation events continue to carry registered
identities only; each peer resolves its own local payload.

## Observability

Registration exposes per-resource results such as `Installed`, `Updated`,
`PreservedLocal`, `Deduplicated`, `Unavailable`, `RejectedProtocol` and
`Invalid`. Core writes a `RegisterPackageV3` operation record and keeps the
scope revision in the runtime index. Audio decoding continues to report source
extension, probed container/codec, load result and fallback behavior.
