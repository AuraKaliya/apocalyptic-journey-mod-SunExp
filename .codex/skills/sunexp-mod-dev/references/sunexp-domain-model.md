# SunExp Domain Model

Use this reference when reasoning about mechanics, balance, and helper reuse.

## Current content baseline

At skill creation time, the current repository contains:

- 30 cards
- 13 relics
- 8 Buffs
- 3 card packs

Recount before relying on these numbers.

## Core loop

SunExp is a high-risk burst card-pack mod built around this chain:

1. Gain `SunExp_sunexp_solar_radiance`.
2. Build `SunExp_sunexp_solar_field` and apply global `buff_burn`.
3. Convert or exploit burn into `SunExp_sunexp_gathered_flame`.
4. Enter `SunExp_sunexp_solar_crown_state`.
5. Spend the window on burst effects such as `crown_core_flash`, accepting backlash when used outside the intended window.

Balance changes should be reasoned about by chain position, not isolated DPS only.

## Existing helper preference

Before writing inline logic, search `SunExp/Scripts/Entry.lua` for a helper. Common helpers include:

- `SunExp_GetBuffLevel`
- `SunExp_GetRadianceLevel`
- `SunExp_DealDamage`
- `SunExp_AddDamageDescription`
- `SunExp_GetPrimaryTarget`
- `SunExp_GetEnemyTargets`
- `SunExp_GetStatusBuffLevel`
- `SunExp_AddStatusBuff`
- `SunExp_RemoveStatusBuff`
- `SunExp_TriggerBurn`
- `SunExp_TriggerBurnAllEnemies`
- `SunExp_RemoveBuffStacks`
- `SunExp_HasCrownPhase`
- `SunExp_ApplySelfBurn`
- `SunExp_RegisterHook`
- `SunExp_IsHookTokenActive`
- `SunExp_ClearHook`

If a helper is added, register it through `SunExp_RegisterDynamicMethods` so CSV snippets can call it.

## Text synchronization

Behavior-facing text lives in:

- `SunExp/Text/Card/sunexp.csv`
- `SunExp/Text/Buff/sunexp.csv`
- `SunExp/Text/Relic/sunexp.csv`
- `SunExp/Text/CardPack/sunexp.csv`

Release-facing text may also mention behavior or counts:

- `SunExp/README.md`
- `SunExp/WorkshopDescription_zh-en.md`
- `SunExp/WorkshopDescription_steam_bbcode.txt`
- `SunExp/ModConfig.json`

Update these only when the changed behavior or counts make them stale.
