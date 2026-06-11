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
   - SunExp solar map-event expansion: `references/solar-event-expansion.md`
   - Validation expectations: `references/validation-rules.md`
   - For ongoing EventList, Text/EventList, map-visible event, and event helper work, also use the project-local `sunexp-event-dev` skill.
3. Prefer existing `SunExp_` helpers over new inline logic. Add a new helper in `Entry.lua` only when multiple cards/relics need the same runtime behavior or nil-safe wrapper.
4. Keep Data and Text rows synchronized. Any new card, buff, relic, card pack, role, dialogue, or event needs both config and localized text when the template has both sides.
5. 检查当前的编辑是否会引起相关问题:
   - Compare edits against the Known Regression Checks below before validation.
   - If changes touch buff immediate settlement, buff stack-change triggers, card tags, relic text tags, or localized descriptions, run the targeted checks listed there.
6. Run validation before finishing:

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

## Known Regression Checks

- Buff immediate settlement: official all-target burn settlement uses `SetStatus("AllTarget"); RunImmediately(DataId.buff_burn, "StartRound")` in C# examples. In SunExp Lua CSV scripts, write this as `self:SetStatus("AllTarget"); self:RunImmediately("buff_burn", "StartRound")`. Do not replace this with per-target helper traversal unless the status selection and event dispatch have been tested.
- Buff stack-change triggers: effects that care about a buff level increasing, decreasing, or changing should usually listen to `self:AddEvent("buff_idOnLevelChange", function() ... end)`. `Action` polling can miss StartRound, enemy-turn, and card-resolution changes. Track the previous level when only increases should count.
- Card tags vs. descriptions: `Data/Card.Tag` values such as `Burnout` are display-facing semantic tags. Do not also hand-write their localized keyword text in `Text/Card` descriptions unless a deliberately different sentence is required.
- Relic text tags: `Text/Relic.Tag` can be appended by UI display paths. Leave SunExp relic text tags blank unless a visible relic label is intentionally needed; this is separate from `Data/Relic.PackBelong` and logic scripts.
- Official template language: files under `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs` often contain C# snippets, not Lua. Convert examples to Lua before using them in SunExp CSV columns: `self:Method(...)`, Lua `function() ... end`, `tonumber(...)`, and dictionary `get_Item`/`set_Item` calls.

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
