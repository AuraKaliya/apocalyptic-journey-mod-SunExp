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

- `aura.shared.json`: root layout and on-demand read policy.
- `<module>/aura.module.json`: protocol and layout metadata.
- `<module>/<scopeType>/aura.scope-type.json`: module-defined granularity metadata.
- `<module>/<scopeType>/<scopeId>/aura.scope.json`: concrete scope metadata.
- `<feature scope>/aura.feature.json`: scope, effect mode and missing policy.
- `<owner>/aura.provider.json`: all active packages and resources from one MOD at this scope.
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

Core distinguishes two identities for every catalog entry:

```text
semanticResourceId = module:feature:scopeType:scopeId:resourceId
qualifiedResourceId = module/scopeType/scopeId/feature/ownerModId/resourceId
```

Different owners may publish the same semantic identity and remain visible as
independent candidates. The same owner cannot publish one qualified identity
from two active packages; Core returns `Invalid` for the conflicting declaration
and keeps the previously valid candidate active. Repeating one package is the
supported idempotent update path.
Identity segments must already be canonical filesystem segments; Core rejects
values that would be rewritten by path sanitization so two ids cannot collapse
onto one directory.

Registration is idempotent. Each changed scope receives a new revision and is
published independently. Late tool loading therefore re-resolves only affected
scopes and does not depend on a global “all mods loaded” phase. Runtime indices
below `_Runtime/Index` are rebuildable caches, not authority.

Consumers enumerate registrations through `QueryCatalogV3Json`; they do not scan
another MOD's private directory. By default the catalog returns only current-session
active and available resources. `includeInactive: true` is a diagnostic view of
persisted redundant declarations and never activates them.

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

When a feature enables multiple additive candidates, its domain shared layer owns
one list-based selector. `priority`, deterministic `random`, and `sequential` modes
all consume the same enabled candidate list; tools must not implement separate
single-choice side paths. Feature-local choices are persisted in the scope's
`aura.user.json` and read on demand. Tool-owned aggregate settings may retain a
compatibility copy, but they are not the shared scope authority.

Domain catalogs must not discard entries by a bare semantic key during scanning.
They preserve qualified entries, group them by semantic key, and apply an explicit
selection policy. Content hashes may deduplicate physical storage, but never erase
owner declarations or tool enablement records.

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
