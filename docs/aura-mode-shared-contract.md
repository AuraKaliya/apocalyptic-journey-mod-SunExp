# AuraMode Shared Contract

`AuraModeShared` models a semantic content mode independently from the host
game's native mode type. Solar Memory and Endless Abyss can therefore both use
the native `Normal` host while exposing different names, identities, policies,
and capabilities to tools.

## Ownership

- A content MOD owns and registers immutable mode definitions under an
  owner-qualified id such as `SunExp:solar-memory`.
- The authoritative run launcher activates a self-contained snapshot. It also
  deactivates that snapshot conditionally by owner, mode id, and optional run
  id.
- Shared code validates, normalizes, persists with revision checks, and
  evaluates typed policies. It contains no SunExp mode names or ids.
- Tool MODs read the active snapshot and ask the shared evaluator for a policy
  decision. They do not infer content semantics from native `Normal`, save
  variables, display names, or known MOD ids.

## Policy Boundary

Version 1 exposes one typed policy: `starterDeck.mutationAuthority`.

- `InheritHost`: external tools may follow normal host behavior.
- `ModeOwnerExclusive`: only the declared provider may mutate the starter
  deck.
- `OfficialOnly`: no external provider may mutate the starter deck.

Definitions declare defaults; activation resolves them into the run snapshot;
the shared evaluator returns a pure allow/deny decision with policy and
provider diagnostics. Policies do not execute content callbacks, inspect save
flags, or own UI. New domains should add typed policy records and evaluators
rather than accumulating content-specific booleans or arbitrary script hooks.

## Runtime Rules

The active snapshot is a single runtime document written with compare-and-swap
revision checks. Repeating the same mode/run/definition/policy activation is
idempotent. Deactivation cannot clear another owner or another run. Snapshots
copy display, host, policy, and capability data so tools do not need to join
against a content MOD at read time.

Solar Memory temporarily dual-publishes the legacy Journey active state while
existing consumers migrate. AuraMode is the authoritative generic semantic
mode contract for new tool behavior.
