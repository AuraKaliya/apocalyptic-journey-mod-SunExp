# CSV Schema Reference

Use this reference when adding or editing Data/Text rows. The first row is the schema and the second row is a comment row; preserve both.

## ID rules

- Game-loaded mod IDs are composed as `ModName_FileName_Id`.
- For SunExp rows in `sunexp.csv`, the full runtime ID is usually `SunExp_sunexp_<Id>`.
- Use full IDs when scripts reference SunExp content.
- An ID starting with `*` is normally excluded from random pools.

## Card

`SunExp/Data/Card/sunexp.csv` fields:

- `Id`: local card id.
- `Rarity`: rarity number.
- `Expend`: static cost.
- `Tag`: tags such as `Burnout`.
- `InitScript`: display/update setup. Must set `BaseScript`.
- `DrawScript`: runs when drawn.
- `UseScript`: runs when used.
- `DropScript`: runs when entering discard.
- `Icon`: resource path without file extension in current SunExp style.
- `Effects`: effect path.
- `Action`: usually `Attack` for attack cards.
- `PackBelong`: full card-pack id.

`SunExp/Text/Card/sunexp.csv` fields include `Type`, `Name`, localized names, and localized descriptions.

Required card checks:

- `AttackCardItem` for target cards, `CommonCardItem` for non-target cards.
- `Text/Card` description placeholders such as `{0}` must match `InitScript` dynamic descriptions.
- If `UseScript` computes a dynamic number, keep display logic and runtime logic in sync.
- If `PackBelong` changes, verify the pack exists in `Data/CardPack`.
- Do not duplicate auto-displayed tags such as `Burnout` in localized `Text/Card` descriptions.

## Buff

`SunExp/Data/Buff/sunexp.csv` fields:

- `InitScript`: display setup.
- `ApplyScript`: runs when Buff is applied.
- `ClearScript`: runs when Buff clears.
- `ReducePerTurn`, `ReducePerAttacked`, `ReducePerUse`: stack decay.
- `UpperBound`: stack cap.
- `Icon`, `Type`, `Rarity`, `Effects`, `SoundEffects`, `Action`, `CanZero`.

Use Buffs for persistent effects and event hooks. Pair event registration with cleanup or token gating when repeated application can duplicate listeners.

## Relic

`SunExp/Data/Relic/sunexp.csv` fields:

- `Rarity`
- `OwnScript`: when acquired.
- `FightScript`: combat setup and event hooks.
- `Icon`
- `PackBelong`

Relic scripts often need `self:UpdateRelicShow()` when variable state changes.

`SunExp/Text/Relic/sunexp.csv` includes display text fields for relic names, descriptions, and tags. `Text/Relic.Tag` can be appended by UI display paths; keep it blank unless a visible relic label is intentionally needed. It is not the same as `Data/Relic.PackBelong` or any logic script field.

## CardPack

SunExp currently uses three normal card packs:

- `SunExp_sunexp_cardpack_sunexp_base`
- `SunExp_sunexp_cardpack_sunexp_burst`
- `SunExp_sunexp_cardpack_sunexp_canopy`

Keep `Data/CardPack/sunexp.csv` and `Text/CardPack/sunexp.csv` synchronized.

## Future expansion tables

Role, dialogue, and event fields are summarized in `expansion-role-dialogue-event.md`.
