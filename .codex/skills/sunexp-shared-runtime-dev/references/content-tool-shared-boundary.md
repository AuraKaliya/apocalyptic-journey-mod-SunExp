# Content, Tool, And Shared Boundary

Use this reference when deciding where reusable SunExp/AuraToolsExp runtime
behavior belongs.

## Design Intent

The durable project model is:

- `SunExp`: content mod. It owns content resources, gameplay rules, story,
  cards, buffs, relics, modes, rewards, and content-specific trigger semantics.
- `AuraToolsExp`: tool mod. It owns configuration, inspection, preview,
  override, debugging, import/export, and player-facing tooling over shared
  declarations.
- Aura shared runtimes: common foundations for both content and tool mods. They
  own semantic-free infrastructure and domain protocols that multiple mods
  consume.

This split is compatible with internal SunExp architecture work, but only when
SunExp routers, UI factories, object pools, and preloaders remain SunExp-local
implementation details. If AuraToolsExp also needs the capability, extract the
semantic-free part to shared infrastructure.

## Keep In SunExp

Keep behavior in SunExp when it depends on SunExp-owned content semantics:

- Solar Memory, EndlessSea/EndlessAbyss, SunExp cards, buffs, relics, enemies,
  rewards, story, and run-state rules.
- SunExp-owned resource installation, registry entries, default declarations,
  and content-specific manifest semantics.
- Content trigger matching for SunExp cards, roles, Skill CG, BGM, skins, or
  visual effects.
- SunExp-only lifecycle routers when their subscribers are SunExp features and
  the target lifecycle is not needed by other mods.

SunExp may wrap shared services with SunExp-specific facades, but the wrapper
must not become a dependency for AuraToolsExp.

## Keep In AuraToolsExp

Keep behavior in AuraToolsExp when it is tool-local:

- Local persistent configuration, UI editors, previews, diagnostics, import and
  export flows, and one-click management tools.
- Tool-owned providers, rules, and official-content extensions that have stable
  `ownerModId` identities.
- Effective-state overrides using the precedence:
  `registered default -> tool shipped default -> local persistent override`.

AuraToolsExp may reference foreign registered resources by shared protocol. It
must not scan SunExp private folders as a substitute for registration, mutate a
foreign registry source, or copy foreign resources under tool ownership unless
the user explicitly creates a local override.

## Promote To Shared

Promote a capability to shared infrastructure when both content and tool mods
need it and the core can be expressed without SunExp content meaning:

- Hook registration safety, idempotency, owner diagnostics, lifecycle handles,
  and routed dispatch foundations.
- Debug switches, InfoOnce/DebugOnce, log throttling, and command-log mirroring.
- UI safety, modal primitives, scroll/text/button factories, object pooling,
  transition guards, and raycast cleanup.
- Resource registry access, owner-qualified ids, package installation, preload
  planning, and cache contracts.
- Cross-mod domain protocols such as Skill CG, Audio/BGM, StarterDeck, Journey,
  Skin, and shared presentation events.
- Multiplayer sender authority, duplicate suppression, sequence/hash semantics,
  and chunked/bounded payload transfer.

Shared Core must remain semantic-free. Domain shared components may understand
their domain schema, but not SunExp content rules.

## Architecture Smells

Treat these as drift:

- AuraToolsExp imports `SunExp-Dev` internals or assumes SunExp private folder
  layout.
- SunExp owns a generic runtime that AuraToolsExp must call to function.
- Shared components mention SunExp card ids, mode names, story state, or
  content-specific rewards.
- A tool override rewrites a foreign registered declaration instead of layering
  an effective local setting.
- Multiplayer presentation relay, duplicate suppression, or authority policy is
  implemented separately in each consumer for the same shared feature.

## Review Questions

Before changing a reusable runtime, ask:

- Does this behavior require SunExp content semantics? If yes, keep it in
  SunExp.
- Is this only a local editor/tool concern? If yes, keep it in AuraToolsExp.
- Would another content/tool mod need the same semantic-free lifecycle,
  presentation, pooling, logging, or registry behavior? If yes, promote it to
  shared.
- Can the boundary be tested by forbidding dependency direction, raw folder
  scans, or registry mutation? If yes, add or update a shared release check.
