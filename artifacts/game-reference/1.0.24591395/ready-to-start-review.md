# FightManager.ReadyToStart Review

## Fingerprints

| Build | Witch.dll SHA-256 | Size |
|---|---|---:|
| `1.0.23816797` | `8D87696341625B19F63059B6D91262FF5738F3C0B5ABB7598A05C7640727790A` | 3,038,208 B |
| `1.0.24591395` | `88613CF3E1F0F4A493FE722FBFB63E36A6C97CBF098F9F406F6AC2A28C136F60` | 3,070,464 B |

## Structural Comparison

Both builds expose `FightManager.ReadyToStart()` as a public instance `void`
method with no arguments and `[Command(requiresAuthority = false)]`. Both use
the same Mirror command signature and command hash:

```text
System.Void FightManager::ReadyToStart()
-180028292
```

The decompiled command user-code remains semantically equivalent:

1. Increment `readyCount`.
2. Compare it with `NetworkServer.connections.Count`.
3. Call `CmdChangeType(FightType.Start)` when all clients are ready.
4. Reset `readyCount` and player turn completion state.

The visible call site still routes through `ReadyToStart()`. The new build adds
nearby disconnect recovery and fight reset behavior, but the reviewed
ReadyToStart command body itself does not consume those new APIs.

ILSpy reports an unsupported `LdMemberToken` representation in the new Rougamo
wrapper. This affects the decompiler's rendering of `MethodContext.Method`; it
does not justify treating the whole wrapper as byte-for-byte unchanged.

## Gate Decision

The signature and static method-body review passes. The AuraDirector production
hash gate has deliberately not been updated because this machine cannot run the
game smoke tests. `tools/Test-AuraDirectorDetour.ps1` verifies that the current
`Managed/` is rejected as `detour-target-build-unverified` and remains unpatched;
the test passes by proving the intended fail-closed behavior.

Before changing the verified hash, run the game-machine checks for single-player
fight start, multiplayer readiness, disconnect recovery, and detour release.
