# Content, Tool, And Shared Boundary

Use this reference when deciding where reusable Terrias/AuraToolsExp runtime
behavior belongs.

## Design Intent

The durable project model is:

- `Terrias`: content mod. It owns gameplay rules, story, cards, buffs, relics,
  modes, rewards, stable content ids, required presentation, and may carry
  Terrias-owned optional voice/CG declarations and files.
- `AuraToolsExp`: tool mod. It owns configuration, inspection, preview,
  override, debugging, import/export, and player-facing tooling over shared
  declarations.
- Aura shared runtimes: common foundations for both content and tool mods. They
  own semantic-free infrastructure and domain protocols that multiple mods
  consume.

This split is compatible with internal Terrias architecture work, but only when
Terrias routers, UI factories, object pools, and preloaders remain Terrias-local
implementation details. If AuraToolsExp also needs the capability, extract the
semantic-free part to shared infrastructure.

## Non-Negotiable Dependency Model

Treat this as the highest-priority boundary rule:

- Core/shared layers are the foundation libraries. They provide common services,
  shared storage, registries, resource/package protocols, domain arbiters, UI
  safety, RPC authority, and other reusable features.
- AuraToolsExp is a tool mod. It depends on the core/shared layers and uses
  them to enable, disable, configure, inspect, import, preview, or override
  shared feature modules.
- Terrias is a content mod. It depends on core/shared layers and registers
  gameplay declarations. Optional Terrias voice/CG is carried under the fixed
  discovery contract; replacement skins, card frames, configurable effects,
  and generic tool media remain AuraTools resources.
- Tool mods and content mods do not depend on each other. They are sibling
  consumers of the shared foundation.
- A content mod must separate content it owns from shared feature declarations
  it consumes. Keep content semantics local; use shared protocols for reusable
  feature behavior.

Default configuration policy:

- Required content presentation stays enabled with its content. Optional media
  features use AuraTools declarations and local configuration.
- When a tool mod and a content mod both configure the same shared feature, the
  tool mod's local effective configuration wins for tool-managed behavior.
- A tool-local override changes only the effective tool state. It must not
  mutate the content mod's registration source or claim ownership of foreign
  artifacts.
- Apply that precedence at the feature's execution/receive entry, not only in
  an editor or import pass. A content declaration stays default-enabled when
  no tool is installed; with AuraToolsExp installed, its local effective state
  gates both local execution and synchronized presentation reception.
- Do not depend on a global "all mods loaded" phase. Registrations must be
  idempotent and revisioned; consumers refresh derived effective state when the
  shared registry snapshot changes, including after late loading.

## Keep In Terrias

Keep behavior in Terrias when it depends on Terrias-owned content semantics:

- Solar Memory, EndlessSea/EndlessAbyss, Terrias cards, buffs, relics, enemies,
  rewards, story, and run-state rules.
- Terrias-required art and effects that are inseparable from gameplay, including
  opening director animation, Wuna orbit fire, and Star Score feedback.
- Stable Terrias role/card/pack ids used by shared signals and AuraTools
  mappings. Optional media trigger matching does not live in Terrias.
- Terrias-only lifecycle routers when their subscribers are Terrias features and
  the target lifecycle is not needed by other mods.
- Content-owned use of shared feature declarations, keeping Terrias-specific
  rules separate from semantic-free shared machinery.

Terrias may wrap shared services with Terrias-specific facades, but the wrapper
must not become a dependency for AuraToolsExp.

## Keep In AuraToolsExp

Keep behavior in AuraToolsExp when it is tool-local:

- Local persistent configuration, UI editors, previews, diagnostics, import and
  export flows, and one-click management tools.
- Tool-owned providers, rules, and official-content extensions that have stable
  `ownerModId` identities.
- Discovery and local configuration of content-carried voice/CG; tool-owned
  official-content extensions, generic card-use audio, replacement skins,
  card-frame themes, and configurable per-card dynamic effects.
- Theme-bound mapping presets that seed an editable explicit-card whitelist.
- Effective-state overrides using the precedence:
  `registered default -> tool shipped default -> local persistent override`.
- Feature-module enablement and local configuration over shared declarations,
  without editing the declaration owner.

AuraToolsExp may target foreign content ids through shared/native catalogs. It
must not scan private folders or depend on content runtime helpers; content
packages opt in only through `SharedResources/aura.discovery.json`, and tool
overrides never change the foreign semantic owner.

## Promote To Shared

Promote a capability to shared infrastructure when both content and tool mods
need it and the core can be expressed without Terrias content meaning:

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
their domain schema, but not Terrias content rules.

## Architecture Smells

Treat these as drift:

- AuraToolsExp imports `Terrias-Dev` internals or assumes Terrias private folder
  layout.
- Terrias owns a generic runtime that AuraToolsExp must call to function.
- Terrias actively registers or plays optional media from `Entry`, or ships
  replacement-skin/card-visual tool resources outside the discovery contract.
- Shared components mention Terrias card ids, mode names, story state, or
  content-specific rewards.
- A tool override rewrites a foreign registered declaration instead of layering
  an effective local setting.
- Multiplayer presentation relay, duplicate suppression, or authority policy is
  implemented separately in each consumer for the same shared feature.
- AuraToolsExp emits a private provider for a foreign registered content
  artifact instead of applying a local effective override to that artifact.

## Review Questions

Before changing a reusable runtime, ask:

- Does this behavior require Terrias content semantics? If yes, keep it in
  Terrias.
- Is this only a local editor/tool concern? If yes, keep it in AuraToolsExp.
- Would another content/tool mod need the same semantic-free lifecycle,
  presentation, pooling, logging, or registry behavior? If yes, promote it to
  shared.
- Can the boundary be tested by forbidding dependency direction, raw folder
  scans, or registry mutation? If yes, add or update a shared release check.
