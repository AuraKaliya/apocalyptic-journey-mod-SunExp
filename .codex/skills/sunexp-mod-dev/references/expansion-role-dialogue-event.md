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

Keep `SkillScript` as a short `CS.SunExp.Dll.Scripting.*` call. Put career and
role skill behavior in `SunExp/Dev/Scripting/WunaScripts.cs` or a supporting C#
helper.

## Dialogue

Official template fields:

- `Data/Dialogue`: `Id`, `BaseScript`, `EndScript`, `Roles`, `EventName`, `ChoiceCount`, `ChoiceScript1`, `ChoiceScript2`
- `Text/Dialogue`: localized text and localized choice text columns.

Use dialogue scripts for scene flow, rewards, state changes, and follow-up events. Keep `ChoiceCount` aligned with non-empty choice scripts and text columns.

## EventList

Official template fields:

- `Data/EventList`: `Id`, `1Script`, `2Script`, `3Script`, `4Script`, `InitScript`, `IsHighRisk`, `EntryScript`
- `Text/EventList`: localized event title, total description, option descriptions, and compare text.

Keep event scripts as short calls into `SunExp/Dev/Scripting/EventScripts.cs`.
Use the decompiled reference only to verify official event API names and argument
shape.

## Expansion workflow

1. Add Data rows.
2. Add matching Text rows.
3. Add or verify assets.
4. Add or reuse C# entry points for script columns.
5. Run validation and then test in game.
