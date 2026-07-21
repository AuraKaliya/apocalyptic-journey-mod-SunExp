# AuraGameDataShared

`AuraGameDataShared` is the shared game-data bounded context for Aura consumers.
It provides a revisioned immutable v5 catalog, owner-qualified registration,
controlled instance materialization, and aggregate-specific instance services.

## Boundaries

- `AuraSharedCore` remains semantic-free and only supplies storage and common
  infrastructure.
- The root domain models in `AuraGameDataShared` own identity, provenance,
  search policy, and mutation rules without referencing Witch runtime types.
- `AuraGameDataShared/Application` owns card-zone and relic-inventory use cases.
- `AuraGameDataShared/GameApi` is the Witch adapter. Only this adapter reads
  `GameConfigManager`, constructs `DataConfig`, or mutates native collections.
- SunExp and AuraToolsExp are sibling consumers. Neither is a framework for the
  other.

## Data Planes

| Plane | Create | Read | Update | Delete |
| --- | --- | --- | --- | --- |
| Native game table | No direct write | Captured once per native generation | No direct write | No |
| Registered definition | v5 registration | Compiled immutable catalog | Owner/writer patch | Retire to history |
| Runtime instance | Aggregate use case | Instance snapshot | Controlled `Vars` patch | Aggregate use case |

`IDataConfig.data` is always treated as read-only. Runtime changes are written
to `Vars`; a new `DataConfig` is materialized only when a writable presentation
snapshot is required.

## Identity And Provenance

The canonical key is `(dataType, fullId)`. Short ids and aliases are search
candidates, not primary keys. Every registered definition records both
`ownerModId` and `writerId`, plus one explicit provenance:

- `UserManual`
- `Registered`
- `Default`
- `Native`

The default search path is the ordered list above. Callers may provide a
different ordered source list through `AuraGameDataQuery`; search logic must not
be reimplemented in consumers.

Only schema version 5 registrations are accepted. Manual definitions cannot
register script fields. Script and identity fields cannot be changed by Patch
or runtime `Vars` mutation.

The persisted JSON contract is defined by
`Schemas/aura-game-data-v5.schema.json`. It is normalized when loaded or
mutated through `AuraSharedConfigStore`, never from a gameplay read.

## Runtime Catalog

Native rows are captured on the main thread once per explicit native
generation through cooperative slices with a 4 ms frame budget. Registry
definitions and captured native DTOs are compiled by the bounded background
work scheduler, generation-checked, and atomically published as a complete
immutable catalog. Point lookup, alias lookup, unique type resolution, table
views, and handle validation use prebuilt indexes and never read storage,
enumerate native tables, normalize documents, or clone the whole catalog.

An unfinished cooperative capture is represented by
`AuraGameDataSourceSnapshot.IsComplete == false` and is never published as a
ready catalog. `AuraGameDataCatalogVersion.NativeReady` is therefore the
consumer readiness contract. During a later invalidation/capture cycle the
runtime continues serving the last-good immutable snapshot; consumers key
derived caches by `Version.Epoch` and must not retain negative lookups observed
before the first native-ready publication.

Owner registrations persist prefix rules instead of copies of every native
row. Inline definitions carry complete fields; Overlay definitions carry only
field changes. The default effective-source order remains
`UserManual -> Registered -> Default -> Native`.

`AuraGameDataDiagnostics` exposes allocation-free hot-path counters plus named
operation spans for mode-selection skin application, map projection, battle
label resolution, native capture, catalog compilation, copied host-interop
rows, and materialization.

## History

Retiring a definition never removes its persisted record. Active queries omit
retired entries, while `QueryHistory` exposes an independent history view.

## Instance Use Cases

The shared application layer uses aggregate language instead of generic
`Create<IDataConfig>` or `Delete<IDataConfig>` operations:

- cards: grant to a card zone or remove through the owning zone;
- relics: grant to the relic inventory or remove by instance identity;
- roles: query and materialize definitions now; role replacement remains with
  the native role-selection/run lifecycle until its invariants are fully
  modeled.

Unsupported mutations fail explicitly. They must not fall back to direct list
mutation in a consumer.
