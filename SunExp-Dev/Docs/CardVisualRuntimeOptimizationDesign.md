# SunExp Card Visual Runtime Optimization Design

## Decision

This optimization replaces the previous multi-phase card visual fallback path
with a stricter primary path.

SunExp card frame skins and dynamic frame effects must be applied through the
official card style initialization path, primarily after `ICard.SetCardStyle`.
The runtime should not keep broad fallback hooks that silently retry through
`DataUpdate`, `DrawEffect`, `CreateCardItem`, or deck movement events.

If a visible card surface is not covered by the primary style initialization
path, the runtime should emit a clear diagnostic or error for that surface
instead of adding another fallback hook. The missing surface should then be
handled deliberately by extending the primary presentation contract.

## Goals

- Apply SunExp card frame skins and frame effects on every visible card surface.
- Return early for cards that do not match SunExp visual rules before touching
  Unity transforms or components.
- Apply visuals once per visible card instance and signature.
- Keep the visual pipeline predictable, measurable, and easy to audit.
- Expose unsupported card surfaces through diagnostics instead of masking them
  with fallback behavior.

## Non-Goals

- Do not preserve broad lifecycle fallback behavior for compatibility.
- Do not run full card visual application from `DataUpdate` or `DrawEffect`.
- Do not reapply all active combat cards after deck movement or card creation
  events.
- Do not let AuraShared/Core know SunExp card visual rule semantics.

## Primary Runtime Flow

```text
After ICard.SetCardStyle
  -> CardVisualInterestIndex.MayAffect(config)
  -> resolve visual root
  -> create or reuse CardVisualSkinMarker
  -> skip if same root and same visual signature already applied
  -> apply card frame skin
  -> install dynamic frame effect
  -> record applied signature
```

## Interest Gate

Add a lightweight `CardVisualInterestIndex` in `SunExp-Dev/Mechanics`.

The index should read only card data and vars, such as:

- card id
- pack id
- icon prefix
- runtime visual marker
- effect target

The index must not:

- resolve Unity transforms
- call `Find`
- call `GetComponent`
- create `CardVisualSkinMarker`
- schedule deferred work

Cards that miss the interest index must return without Unity object work.

## Instance Deduplication

The visual marker or equivalent instance cache should record:

- root instance id
- visual signature
- skin id
- frame effect id
- applied source stage

The same root and same visual signature should be skipped on repeated calls.
If the same UI root is reused for another card, the changed signature should
allow a new application.

## Diagnostics

Unsupported visible card surfaces are runtime defects, not compatibility cases.

When a card surface displays a card without passing through the primary style
initialization contract, log a clear diagnostic that includes:

- surface/source name
- card id if available
- whether a visual rule would have matched
- expected contract, such as `AfterSetCardStyle`

Diagnostics should guide the next deliberate contract extension. They should
not trigger an automatic fallback apply path.

## Shared/Core Boundary

AuraShared/Core owns semantic-free lifecycle hook de-duplication and phase
dispatch. SunExp owns visual interest matching and card-frame semantics.

Shared code must not contain SunExp card ids, pack ids, icon prefixes, skin
ids, or frame-effect ids.
