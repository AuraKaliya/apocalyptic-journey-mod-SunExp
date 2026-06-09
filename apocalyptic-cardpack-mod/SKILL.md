---
name: apocalyptic-cardpack-mod
description: Build, review, document, or repair Witch's Apocalyptic Journey card-pack Mods based on apocalyptic-journey-mod-tutorial. Use when working with Card, CardPack, Buff, or Relic CSV files, PackBelong/id wiring, Lua Script columns, ModResource paths, or when the user asks for card-pack Mod development guidance.
---

# Apocalyptic Cardpack Mod

## Overview

Use this skill to work on card-pack style Mods for Witch's Apocalyptic Journey. It captures the CSV schema, id rules, Lua script-column conventions, and practical review workflow from the official `apocalyptic-journey-mod-tutorial` repository.

## Quick Workflow

1. Locate the Mod root and `ModConfig.json`; record `ModName`.
2. Locate the CSV file names under `Data/` and `Text/`; remember runtime ids are `ModName_FileName_Id`.
3. Read `references/cardpack-csv-guide.md` before changing or explaining `Card`, `CardPack`, `Buff`, or `Relic` CSV columns.
4. Preserve exact headers and column order from the current project. If official templates and current project differ, follow the current project's verified shape and call out the difference.
5. Keep mechanics in `Data`, display/localization in `Text`, and make sure matching rows share the same local `Id`.
6. Use full runtime ids for cross-table references such as `PackBelong` and custom Buff references.
7. Treat all `*Script` columns as Lua, even when official reference examples are C#.

## When Editing CSV

- Use a real CSV parser or careful table-aware edits when possible; script cells often contain commas and escaped quotes.
- Do not rename CSV files casually; the file stem participates in runtime ids.
- Do not replace local ids in `Data`/`Text` rows with full runtime ids. Use full ids only when another table or script references the Mod item.
- Check every card `InitScript` for `BaseScript`: `AttackCardItem` for target-selecting cards, `CommonCardItem` for non-target cards.
- Put long-lived or event-driven effects into Buff rows rather than only in card `UseScript`.
- For Buffs that register events in `ApplyScript`, add duplicate-registration guards and clean state in `ClearScript`.

## Review Checklist

- Confirm `Data` and `Text` ids match for cards, packs, buffs, and relics.
- Confirm `PackBelong` points to an existing full card-pack id.
- Confirm custom Buff/card/relic references use full runtime ids.
- Confirm original ids such as `buff_burn` are valid in the official reference tables.
- Confirm resource paths use the same extension convention as the project, usually no `.png`.
- Confirm descriptions match actual script behavior and localization columns remain present.
- Confirm CSV quoting remains valid after Lua edits.

## Reference

Load [references/cardpack-csv-guide.md](references/cardpack-csv-guide.md) for detailed column-by-column fill rules, observed enum values, script examples, and card-pack-specific differences between official template shape and the current SunExp-style split.
