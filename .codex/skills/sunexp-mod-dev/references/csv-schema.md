# CSV Schema Reference

Use this reference when adding or editing Data/Text rows. The first row is the schema and the second row is a comment row; preserve both.

## ID rules

- Game-loaded mod IDs are composed as `ModName_FileName_Id`.
- For SunExp rows in `sunexp.csv`, the full runtime ID is usually `SunExp_sunexp_<Id>`.
- For role-specific files such as `wuna.csv`, the full runtime ID uses the file stem, for example `SunExp_wuna_<Id>`.
- Use full IDs when scripts reference SunExp content.
- An ID starting with `*` is normally excluded from random pools. Prefer this
  for generated, removed, or hidden reward-pool rows that still need a data row.
- Do not hide Card, Relic, Buff, Blessing, or EnchTag rows by changing
  `Rarity`; these rows enter UI tooltip paths that expect display-valid rarity
  values.

## Card

`SunExp/Data/Card/*.csv` fields:

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

`SunExp/Text/Card/*.csv` fields include `Type`, `Name`, localized names, and localized descriptions.

Required card checks:

- `AttackCardItem` for target cards, `CommonCardItem` for non-target cards.
- `Text/Card` description placeholders such as `{0}` must match `InitScript` dynamic descriptions.
- If `UseScript` depends on a value shown in text, keep display setup and runtime behavior in sync.
- If `PackBelong` changes, verify the pack exists in `Data/CardPack`. Role skill cards may intentionally leave `PackBelong` blank when they are not reward-pool cards.
- Do not duplicate auto-displayed tags such as `Burnout` in localized `Text/Card` descriptions.

## Buff

`SunExp/Data/Buff/*.csv` fields:

- `InitScript`: display setup.
- `ApplyScript`: runs when Buff is applied.
- `ClearScript`: runs when Buff clears.
- `ReducePerTurn`, `ReducePerAttacked`, `ReducePerUse`: stack decay.
- `UpperBound`: stack cap.
- `Icon`, `Type`, `Rarity`, `Effects`, `SoundEffects`, `Action`, `CanZero`.

Use Buffs for persistent effects and event hooks. Pair event registration with cleanup or token gating when repeated application can duplicate listeners.

## Relic

`SunExp/Data/Relic/*.csv` fields:

- `Rarity`
- `OwnScript`: when acquired.
- `FightScript`: combat setup and event hooks.
- `Icon`
- `PackBelong`

When a relic's displayed state can change, expose that update through the C#
relic script or a supporting wrapper instead of inline CSV logic.

`SunExp/Text/Relic/*.csv` includes display text fields for relic names, descriptions, and tags. `Text/Relic.Tag` can be appended by UI display paths; keep it blank unless a visible relic label is intentionally needed. It is not the same as `Data/Relic.PackBelong` or any logic script field.

## CardPack

Keep `Data/CardPack/*.csv` and `Text/CardPack/*.csv` synchronized.

## Future expansion tables

Role, dialogue, and event fields are summarized in `expansion-role-dialogue-event.md`.
