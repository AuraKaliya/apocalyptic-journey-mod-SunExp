---
name: sunexp-mod-dev
description: Project-local skill for developing the SunExp mod for Witch's Apocalyptic Journey. Use when editing or reviewing SunExp Lua scripts, card/buff/relic/card-pack CSV data and text, dynamic card descriptions, role data, dialogue, events, official ModTemplate examples, or C#-to-Lua translation risks in this repository.
---

# SunExp Mod Dev

Use this skill only inside this repository. Treat the current `SunExp/` folder as the truth for shipped behavior, and treat `apocalyptic-journey-mod-tutorial/ModTemplate/` as the local official reference for Lua APIs, type hints, and CSV schemas.

## Workflow

1. Inspect the current feature surface before editing:
   - `SunExp/Scripts/Entry.lua`
   - `SunExp/Data/**/sunexp.csv`
   - `SunExp/Text/**/sunexp.csv`
   - `SunExp/ModConfig.json`
   - release-facing docs only when behavior or counts change.
2. Load only the relevant reference:
   - Card, Buff, Relic, CardPack fields: `references/csv-schema.md`
   - Lua runtime and C# translation rules: `references/lua-runtime-api.md`
   - SunExp mechanic model and helper usage: `references/sunexp-domain-model.md`
   - Role, dialogue, and event expansion: `references/expansion-role-dialogue-event.md`
   - Validation expectations: `references/validation-rules.md`
3. Prefer existing `SunExp_` helpers over new inline logic. Add a new helper in `Entry.lua` only when multiple cards/relics need the same runtime behavior or nil-safe wrapper.
4. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, or event needs both config and localized text when the template has both sides.
5. Run validation before finishing:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```

## Hard Rules

- Do not copy C# snippets from `Scripts/Lib/DataConfigs` directly into Mod CSV script columns. Convert to Lua and verify.
- Use `self:Method(...)` for `ScriptExecutor` calls, not bare `Method(...)`.
- Use `dict:get_Item(key)`, `dict:set_Item(key, value)`, and `dict:ContainsKey(key)` for C# dictionaries exposed to Lua.
- Set card `BaseScript` in `InitScript`: `AttackCardItem` for target cards, `CommonCardItem` for non-target cards.
- Use full mod IDs when referencing SunExp-defined content, for example `SunExp_sunexp_solar_radiance`.
- For persistent or triggered behavior, prefer Buff `ApplyScript`/`ClearScript` events instead of putting all logic in a one-shot card `UseScript`.
- When changing numbers or behavior, update player-facing text and release-facing summaries if they mention the changed behavior.

## Useful Commands

Inventory current content:

```powershell
.codex\skills\sunexp-mod-dev\scripts\extract-sunexp-inventory.ps1
```

Lua syntax only:

```powershell
.codex\skills\sunexp-mod-dev\scripts\lint-lua-csv-snippets.ps1
```

Full local validation:

```powershell
.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1
```
