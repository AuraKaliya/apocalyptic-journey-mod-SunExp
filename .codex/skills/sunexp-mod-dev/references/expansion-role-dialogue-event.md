# Role, Dialogue, and Event Expansion

Use this reference when SunExp grows beyond pure card packs.

## RoleData

Official template fields:

- `Data/RoleData`: `Id`, `Avatar`, `CharacterImage`, `HouseAvatar`
- `Text/RoleData`: localized `Name`, `Title`, and `Dia`

Role work is asset-sensitive. Verify every referenced image path exists under the mod resource tree or is a known original-game path.

## Career

Official and Defect sample fields include:

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`, `Skill2`
- `DollIcon`, `Character`, `Avatar`, `CareerImage`
- `ActionImage1`, `ActionImage2`
- `Dialogue`, `EmojiPath`, `FightWidget`

Treat `SkillScript` like combat Lua. Reuse `CS.ScriptExecutor.PlayerInfo.SkillTime` carefully for cooldown or once-per-fight state, and prefer nil checks before dictionary access.

## Dialogue

Official template fields:

- `Data/Dialogue`: `Id`, `BaseScript`, `EndScript`, `Roles`, `EventName`, `ChoiceCount`, `ChoiceScript1`, `ChoiceScript2`
- `Text/Dialogue`: localized text and localized choice text columns.

Use dialogue scripts for scene flow, rewards, state changes, and follow-up events. Keep `ChoiceCount` aligned with non-empty choice scripts and text columns.

## EventList

Official template fields:

- `Data/EventList`: `Id`, `1Script`, `2Script`, `3Script`, `4Script`, `InitScript`, `IsHighRisk`, `EntryScript`
- `Text/EventList`: localized event title, total description, option descriptions, and compare text.

Event scripts commonly use `CS.ScriptExecutor.PlayerInfo` methods such as `AddCard`, `AddRelic`, `ShowDialogue`, `ContinueEvent`, `SetGameVar`, `GetGameVar`, `EndEvent`, and `ShowCaption`.

## Expansion workflow

1. Add Data rows.
2. Add matching Text rows.
3. Add or verify assets.
4. Add helper functions only when scripts need shared nil-safe behavior.
5. Run validation and then test in game.
