# SunExp Lua Source Modules

`SunExp/Scripts/Entry.lua` is the only Lua entry file the game runtime is expected to load. Files in this `_src` folder are development sources; they are concatenated into `Entry.lua` by:

```powershell
tools\Build-SunExpEntry.ps1
```

## Workflow

1. Edit the relevant module under `_src`.
2. Keep `manifest.txt` in load order if adding or moving modules.
3. Run `tools\Build-SunExpEntry.ps1`.
4. Run `tools\Test-SunExpEntryLoad.ps1`.
5. Run `.codex\skills\sunexp-mod-dev\scripts\validate-sunexp.ps1`.

## Runtime Rules

- Do not rely on the game to auto-load `_src` files.
- Keep CSV-callable helpers as `SunExp_*` globals and register them in `registry.lua`.
- Keep `ModConfig:Setup()` in `setup.lua`; it remains the load-time registration point.
- Each module is wrapped in `do ... end` in the generated entry file, so cross-module state must be global or stored under a shared table.

## Fix Notes

### Temporary White Radiance From WuNa Skill

`SunExp_WunaUseWhiteSunPrayer` adds `Burnout` and `白曜` to the current battle hand only. The display tag is stored in each current `CardItem`/`DataConfig` `Vars.SpecialTag`; it must not be written back to base card `Data/Card.Tag`, or it would become permanent for the whole run/game.

Runtime lesson: adding `SpecialTag=白曜` only changes the visible tag and keyword text. Ordinary cards need the temporary runtime bridge below; otherwise they do not automatically run the `白曜` effect.

The working runtime bridge is:

1. When `白曜` is added by `白曜圣祷`, set `Vars.SunExpTempWhiteRadiance = "1"` and a battle-local `Vars.SunExpTempWhiteRadianceLockId` on the current card instance and its `dataConfig`.
2. After play resolution, `CommonCardItem.TrueUse.after` or `AttackCardItem.TrueUse.after` observes the actual `CardItem`. This is the only trigger route for temporary `白曜`.
3. If the card has `SunExpTempWhiteRadiance=1`, `SpecialTag` contains `白曜`, and base `Tag` does not already contain `白曜`, claim `FightManager.Instance.TempVarsMap["SunExpTempWhiteRadianceResolved_" .. lockId]` before calling `SunExp_HandleSolarCardUsed` with the played card's current cost.

Known failed approaches:

- `CardItem.RunScript.after` did not fire in the tested battle path, even though `CardItem.RunScript("UseScript")` appears in decompiled code.
- `ScriptExecutor.RunScript.before` did not fire in the tested battle path; logs showed successful card effects followed by `UseScript not seen`.
- Patching current card instance `UseScript` to append `SunExp_MarkTemporaryWhiteRadianceUseScript(self)` did not trigger reliably in tested battles.
- `ActionAfter` can be registered with `self:AddEvent("ActionAfter", function(actionData) ... end)`, but in the tested Lua callback the `ActionData` did not expose a usable `DataConfig` (`dataConfig=nil` in logs). Do not keep it as a second trigger route for this mechanic; multiplayer can replay/sync action events separately from the local `TrueUse.after` path.
- `card.hasUse` is not a reliable success check inside the Lua `TrueUse.after` hook. In testing, gating on it prevented normally played cards from triggering.
