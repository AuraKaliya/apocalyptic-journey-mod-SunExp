# AuraGameDataShared

`AuraGameDataShared` is the shared game-data bounded context for Aura consumers.
It provides detached queries, owner-qualified v4 definition registration,
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
| Native game table | No direct write | Detached snapshot | No direct write | No |
| Registered definition | v4 registration | Catalog query | Owner/writer patch | Retire to history |
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

Only schema version 4 registrations are accepted. Manual definitions cannot
register script fields. Script and identity fields cannot be changed by Patch
or runtime `Vars` mutation.

The persisted JSON contract is defined by
`Schemas/aura-game-data-v4.schema.json` and is read on demand through
`AuraSharedConfigStore`.

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
