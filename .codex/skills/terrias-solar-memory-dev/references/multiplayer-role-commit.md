# Solar Memory Multiplayer Role Commit

Use this reference when editing starter deck setup, preparation state, role
sync, or multiplayer completion.

## Authority Model

Solar Memory preparation can be local and player-scoped, but the final prepared
role must be submitted once to server authority. Clients may update local UI and
player-scoped state; only the host/server may advance shared run state or write
authoritative role dictionaries.

## Intermediate Sync

Do not call the native RoleTable collector during Solar Memory preparation. The
custom starter deck and official-deck paths should suppress intermediate sync,
normally by passing `sync: false` to the shared starter deck runtime.

Sanitize Solar Memory event cards from:

- the active deck;
- the reserve or uncard pool;
- starter deck candidates;
- final role data before continuation.

## Final Commit

After preparation completion, call `SolarMemoryRoleCommitApi.CommitFinal`.
Clients submit a dedicated `RpcSolarMemoryRoleCommit`; the server applies the
role into the authoritative role dictionary and persists it with
`GameSaveManager.UpdateRoles`.

Reject unfinished preparation state. Use a per-run commit token to suppress
local re-entry and duplicate network delivery.

Remote commits must validate the server-bound sender supplied by
`TerriasRpcAuthorityRuntime`. Reject missing senders in multiplayer, senders
outside the lobby, and sender/`Role.Id` mismatches. Host-local direct commits
should create a local server sender and pass through the same server apply path.

## Legacy State

Preparation choices should live on the current role, such as
`RoleTable.SpecialVarMap`. Do not migrate legacy global preparation values
during multiplayer, because that can copy one player's setup into another
player's run.
