# Sync Scenario Model

Use this reference when a shared-runtime task touches multiplayer sync,
multi-mod sync, initialization registration, tool-local configuration
overrides, timing, payload limits, RPC authority, event-shape fields, or
duplicate suppression. Keep detailed RPC/authority/dedupe rules here instead
of duplicating them in top-level skills.

## Scenario Model

| Scenario | Examples | Authority | Sync shape |
| --- | --- | --- | --- |
| Initialization registration | Terrias registers mod-owned roles, CG, audio, skins, starter decks, and unique content extensions. AuraToolsExp registers official-content or tool-owned extensions. | Registering mod's `ownerModId` plus stable domain id. | Startup manifest/provider registration. Do not use gameplay RPC for registration. |
| Tool configuration | AuraToolsExp reads local persistent settings and forces or overrides effective tool behavior. | Tool-local config store. | Local effective-state overlay. Do not mutate or re-own foreign registrations. |
| Shared progression | Map state, route state, run counters, shared reward state, final role commit. | Host/server. | Client request -> server validate -> authoritative snapshot/result broadcast. |
| Player-scoped state | Player choices, Wuna ember, damage submit, role-owned presentation request. | Bound sender/player owner. | Sender-bound command. Server binds sender from receive context before validation. |
| Presentation event | CG playback, audio, skin visual, temporary overlay, UI cleanup, projection visual. | Local owner may request; host/server may relay in multiplayer. | Transient event with duplicate suppression and lifecycle cleanup. CG relay carries only registered owner/provider/CG ids plus action/session identity; each peer resolves local resources and no resource body crosses the network. It must not advance progression. |
| Bulk transfer or diagnostic | ModSync host manifest, large snapshots, logs, damage-meter snapshots/history. | Host/server or tool-local producer, depending on feature. | Payload guard, chunking, checksum, expiration, and active-buffer cap. |

For AuraTools ModSync in the lobby, authenticate the requester through the
server-bound RPC command path, then return an ordinary host manifest through a
connection-targeted native `RpcQuery` response. Register the client callback
without sending the game's role-table-gated native query command. If native
query metadata, the requester connection, or the targeted payload budget is
unavailable, fall back to the bounded broadcast/chunk transport. A targeted
timeout may retry broadcast once; a broadcast timeout must fall back to the
lobby summary and clear all pending callback state. Do not hook
`PlayerManager.UserCode_CmdQuery__QueryBase__NetworkConnectionToClient`; the
current Managed method is not Modifiable-wrapped.

## Current Terrias Consensus

Use this section as the durable routing rule for Terrias/AuraToolsExp sync
reviews. Put detailed feature rationale in project docs; keep this reference as
the short operational memory.

- Initialization registration is a startup phase, not a gameplay sync phase.
  AuraToolsExp may register official-content or tool-owned extensions. Terrias
  may register MOD roles and MOD-unique content extensions. Registered content
  keeps the registering mod's owner identity.
- Tool configuration is local effective state. Terrias-owned content declarations
  default to enabled when Terrias configures a shared feature by itself.
  AuraToolsExp reads its persistent local configuration and may override or
  force tool behavior when both Terrias and AuraToolsExp configure the same
  shared feature, without rewriting foreign registrations.
- Endless Abyss map, monster, route, and gameplay-level effects are
  host/server-authoritative in multiplayer. Clients may display the result but
  must not independently calculate or initiate shared progression mutations.
- Endless Abyss shock choice is selected by the host in multiplayer. Non-host
  players only need the final resolution prompt/result; they do not need a
  read-only selection panel.
- Endless Abyss monster injection after battle initialization should follow the
  game's dynamic enemy-add pattern: only the host/server calculates and starts
  the add; clients receive the game's native enemy sync. Terrias should still
  own an explicit wrapper/service for authority checks, planning, logging, and
  duplicate suppression instead of scattering direct native calls.
- Endless Abyss milestone rewards are player-scoped. Each player opens,
  chooses, and receives their own reward independently; Terrias should not add a
  custom cross-player synchronization layer for the reward panel.
- Ember is a generic player-scoped adventure state, not a Wuna-only state. It
  persists across the whole adventure, survives battle end, and is keyed by the
  owning player in multiplayer. Prefer generic names such as
  `EmberAdventureState` for services, snapshots, and RPCs; keep Wuna-named
  keys only as legacy read fallbacks.
- Wuna-specific passive benefits are still Wuna-gated. Persisting and syncing
  Ember ownership is generic; healing, max-HP growth, and other Wuna passive
  rewards are applied only when Wuna's activation condition is true.
- Card-pack behavior should follow the current repository files as source of
  truth during cleanup. Do not reconstruct package placement from memory-derived
  anchors without verifying the current files.
- Lonaire has no current sync/design conflict requiring special handling.

## Effective Configuration Model

Keep three layers separate:

1. Registered default: the registering mod declares the default state. Content
   mods should default their own shared-feature declarations to enabled unless
   the manifest says otherwise.
2. Tool shipped default: AuraToolsExp may provide tool-owned defaults for
   official content or tool-managed features.
3. Local persistent override: AuraToolsExp may force the effective state for
   local tool behavior.

The precedence is `registered default -> tool shipped default -> local
persistent override`. When a content mod is the only participant, its registered
default is the effective state. When AuraToolsExp also manages the same feature,
AuraToolsExp's local effective configuration wins for tool-managed behavior.
The override changes effective tool state only; it must not edit another mod's
manifest, package, or registry source.

## Sync Parameter Model

Classify each network/shared event before adding fields:

| Shape | Purpose | Required fields |
| --- | --- | --- |
| `command` | A request to apply one action once. | `protocolVersion`, `ownerModId` or feature id, command kind, `token`, bound sender, owner/target id. |
| `snapshot` | Authoritative state replacement or repair. | `protocolVersion`, state kind, owner/scope, `version` or `sequence`, source, payload hash when useful. |
| `presentation-event` | Transient playback or visual event. | `eventId` or play id, issuer, owner status, feature kind, created time or max age, duplicate key. |
| `bulk-transfer` | Payload too large for a normal RPC. | `transferId`, chunk index/count, total bytes, `sha256`, requester/target, TTL. |

Use `token` for idempotent commands, `sequence` for ordered per-owner event
streams, `version` for state generations, `hash` for content or payload
consistency, and `timestamp`/`ttl`/`maxAge` for transient expiration.

Never trust payload-provided sender, reporter, issuer, or owner fields for
authorization. Bind sender from the server receive context and pass the bound
sender into validation.

## Timing Model

Use this order unless a feature has a stronger local reason:

1. Startup: initialize shared core, install packages, register manifests and
   providers, then apply tool-local effective configuration. Do not freeze that
   result at startup: re-resolve it when a registry revision changes.
2. Lobby or entry: discover host/client state, request missing authoritative
   snapshots, and avoid client-side host misidentification.
3. Adventure or mode entry: initialize route, starter deck, role setup, map,
   and run state through owner-qualified ids.
4. Battle start: clear transient presentation queues, old hook tokens, battle
   ledgers, duplicate windows, and stale UI overlays.
5. Action or event hook: local owner may generate a command or presentation
   request; remote observation hooks must not create a new authoritative event.
6. Battle end or mode exit: commit authoritative state, broadcast snapshots,
   persist player-scoped results, and clear short-lived state.
7. Bulk transfer: expire incomplete transfers, enforce active-buffer caps, and
   validate checksum before applying the payload.

UI and Unity object work may need a frame scheduler. Do not assume a remote
packet arrives when the native UI tree or resource cache is ready.

### Client map-node projection repair

Native map/route progression remains host-authoritative. A non-host may repair
only its local `MapTree.currentNode` projection when the saved or cached node
identity exactly matches an already received native `mapList` / `mapData`
snapshot. The repair must not choose a route, rewrite the arrays, or send a
map command. If identity is ambiguous, retry for a bounded number of frames
and then log rather than selecting a fallback node.

### StarterDeck client submission

Content mods register starter-deck profiles; AuraToolsExp resolves the local
effective profile without changing its registration owner. In multiplayer each
client must apply that result to its own `RoleTable` immediately before the
native `CmdSyncRoleTable` serialization/submission path. Do not rely only on a
server RPC wrapper such as `RpcSyncRoleTables`, and do not introduce a parallel
deck gameplay RPC when the native role-table submission already carries it.

## Duplicate Suppression Model

| Layer | Key | Cleanup |
| --- | --- | --- |
| Registered identity | `ownerModId + resourceId/cgId/profileId` | Registry/package reload. |
| Command idempotency | `token` | Bounded TTL or lifecycle clear. |
| Ordered state | `owner + sequence/version` | Advance on newer state, ignore stale state, request snapshot on gaps. |
| Presentation playback | `issuer + playId/eventId + media/presentation key` | Short duplicate window, max pool, battle/UI lifecycle clear. |
| Bulk transfer | `transferId + chunkIndex + sha256` | TTL, active-transfer cap, remove after join or rejection. |
| Hook/listener registration | hook token, applied marker, frame key | Clear hook or reset at fight/mode boundary. |

If a dedupe set can grow across fights, modes, or lobbies, add an explicit
bounded retention policy before shipping the feature.

## Review Checklist

- Name the scenario before implementation.
- Name the event shape before choosing fields.
- Identify the authority writer and bound sender.
- Pick exactly the needed `token`, `sequence`, `version`, `hash`, and TTL
  fields.
- State how stale, duplicate, retried, oversized, and late-arriving payloads are
  handled.
- State the lifecycle cleanup point.
- Keep presentation events separate from progression state.
