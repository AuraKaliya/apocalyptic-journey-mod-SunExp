# Card-Pack CSV Reference

Use this reference when creating, reviewing, or repairing a Witch's Apocalyptic Journey card-pack Mod based on `apocalyptic-journey-mod-tutorial`.

## Global Rules

- Keep exact CSV headers and column order from the current project or official template.
- Row 1 is the header. Row 2 is a comment row and should usually stay.
- `Data/` stores mechanics, values, scripts, icons, and pack linkage.
- `Text/` stores names, descriptions, notes, and localization.
- Runtime Mod ids are `ModName_FileName_Id`. Use local `Id` inside matching `Data`/`Text` rows, but use full ids for cross-table references and `PackBelong`.
- A leading `*` on `Id` excludes the item from random pools.
- Script columns must be Lua. Convert original C# examples from `Scripts/Lib/DataConfigs` before use.
- In CSV script cells, quote the whole cell when needed and escape inner quotes as `""`.
- Mod resource paths normally look like `Mods/<ModName>/ModResource/...` and usually omit `.png`.

## Card Data

Header:

```csv
Id,Rarity,Expend,Tag,InitScript,DrawScript,UseScript,DropScript,Icon,Effects,Action,PackBelong
```

| Column | Fill / Format |
| --- | --- |
| `Id` | Local card id, usually lowercase English/number/underscore. May start with `*` to exclude from random pools. |
| `Rarity` | Integer string. Official card data shows `1`, `2`, `3`. |
| `Expend` | Energy cost as an integer string, e.g. `0`, `1`, `2`. |
| `Tag` | Optional comma-separated tokens. Observed: `Ability`, `Ascension`, `Burnout`, `Combo`, `Curse`, `Fission`, `Froze`, `Inherent`, `Instant`, `Nihility`, `Recycle`, `Retain`, `Ritual`, `SpellComponents`, `Unusable`. |
| `InitScript` | Lua executed for initialization/display refresh. Must set `BaseScript`. |
| `DrawScript` | Lua executed when drawn. Optional. |
| `UseScript` | Lua executed when used. Main card effect. |
| `DropScript` | Lua executed when entering discard. Optional. |
| `Icon` | Card art path, usually without extension. |
| `Effects` | Effect path. Optional. |
| `Action` | Optional presentation action. Observed: `Attack`, `Buff`, `Skill`, `Special`. |
| `PackBelong` | Full card-pack id, e.g. `SunExp_sunexp_cardpack_sunexp_base`. |

Required `InitScript` patterns:

```lua
self.Vars:set_Item("BaseScript", "AttackCardItem")
self.Vars:set_Item("CanSelf", "False")

self.Vars:set_Item("BaseScript", "CommonCardItem")
```

Observed target filters:

```text
Self, Target, All, AllTarget, AllExSelf,
AllFriends, AllFriendsExSelf,
AllRandomEnemy1, AllRandomTarget1, AllRandomTarget3
```

## Card Text

Header:

```csv
Id,是否完成,Type,Note,Name,Name_en,Name_zh-Hant,Name_ja,Description,Description_zh-Hant,Description_en,Description_ja
```

| Column | Fill / Format |
| --- | --- |
| `Id` | Local card id matching `Data/Card`. |
| `是否完成` | Usually `TRUE` for finished cards. |
| `Type` | Display type. Observed: `攻击牌`, `技能牌`, `能力牌`, `消耗攻击牌`, `消耗技能牌`, `诅咒`. |
| `Note` | Author note. Optional. |
| `Name`, `Name_en`, `Name_zh-Hant`, `Name_ja` | Localized names. |
| `Description`, localized descriptions | Rules text. Use `{full_or_original_id}` to reference buffs/keywords. |

## CardPack

Official reference shape in `Text/CardPack`:

```csv
Id,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_en,Description_ja,Icon,Type
```

- Official observed `Type`: `Basic`, `Expand`.
- Official rows may use `*` ids to keep packs out of random pools.

Current SunExp-style split:

```csv
Data/CardPack: Id,Type,Icon
Text/CardPack: Id,Note,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_ja,Description_en
```

For the split form:

| Column | Fill / Format |
| --- | --- |
| `Data.Id` | Local card-pack id such as `cardpack_sunexp_base`. |
| `Data.Type` | Current project uses `Normal`. Do not assume this equals official `Basic/Expand`. |
| `Data.Icon` | Pack icon path, usually without extension. |
| `Text.Id` | Local id matching `Data/CardPack`. |
| `Text.Name` and descriptions | Localized display text. |

Use the full runtime card-pack id in card/relic `PackBelong`.

## Buff Data

Header:

```csv
Id,InitScript,ApplyScript,ClearScript,ReducePerTurn,ReducePerAttacked,ReducePerUse,UpperBound,Icon,Type,Rarity,Effects,SoundEffects,Action,CanZero
```

| Column | Fill / Format |
| --- | --- |
| `Id` | Local buff id. Cross-table references use full id. |
| `InitScript` | Optional Lua for display/init. |
| `ApplyScript` | Lua when buff applies. Use for persistent behavior and `AddEvent`. |
| `ClearScript` | Lua when cleared. Use to clean flags/tokens from event registration. |
| `ReducePerTurn` | Integer string. Observed: `0`, `1`, `2`, `10`, `99`, `999`. |
| `ReducePerAttacked` | Integer string. Observed: `0`, `1`. |
| `ReducePerUse` | Integer string. Observed: `0`. |
| `UpperBound` | Integer string stack cap. |
| `Icon` | Buff icon path. |
| `Type` | Observed: `正面`, `负面`, `能力`, `特性`, `契印`. |
| `Rarity` | Integer string. Observed: `1`, `2`, `3`, `4`. |
| `Effects`, `SoundEffects`, `Action` | Presentation fields. Usually optional. |
| `CanZero` | `TRUE` or `FALSE`; usually `FALSE`. |

Persistent card effects should be Buffs. Guard `ApplyScript` event registration against duplicates, and clear state in `ClearScript`.

## Buff Text

Header:

```csv
Id,Note,Name,Name_zh-Hant,Name_en,Name_ja,Description,Description_zh-Hant,Description_ja,Description_en
```

Use local `Id`, localized names, and localized descriptions. Descriptions can reference other ids with `{id}`.

## Relic Data

Header:

```csv
Id,Rarity,OwnScript,FightScript,Icon,PackBelong
```

| Column | Fill / Format |
| --- | --- |
| `Id` | Local relic id. |
| `Rarity` | Integer string. Observed: `1`, `2`, `3`, `4`. |
| `OwnScript` | Lua executed when obtained. Optional. |
| `FightScript` | Lua executed/registered for combat. Use `AddEvent` for triggers. |
| `Icon` | Relic image path without extension. |
| `PackBelong` | Full card-pack id. |

## Relic Text

Header:

```csv
Id,Note,Series,Tag,Name,Name_zh-Hant,Name_en,Name_ja,Tips,Tips_zh-Hant,Tips_en,Tips_ja,Description,Description_zh-Hant,Description_en,Description_ja
```

| Column | Fill / Format |
| --- | --- |
| `Id` | Local id matching `Data/Relic`. |
| `Note` | Optional author note. |
| `Series` | Display series/category, e.g. `日耀遗物`. |
| `Tag` | Display tags, e.g. `灼烧`, `防御`. |
| `Name` and localized names | Relic names. |
| `Tips` and localized tips | Flavor/lore text. Optional. |
| `Description` and localized descriptions | Mechanical rules text. |

## Common Script Methods

```lua
self:SetStatus("Self")
self:SetStatusById(target.InstanceId)
self:Damage("10")
self:ChangeHp("-5")
self:ChangeDefence("8")
self:AddBuff("buff_burn", "2")
self:RemoveBuff("buff_burn")
self:DrawCount("1")
self:ChangePower("1")
self:RunImmediately("buff_burn", "StartRound")
self:AddEvent("Action", function() ... end)
self:AddTempEvent("EndRound", function() ... end)
self:UpdateRelicShow()
```

Common events: `FightStart`, `StartRound`, `StartRoundEnd`, `Action`, `ActionAfter`, `Attack`, `AttackDone`, `Hurt`, `Heal`, `Damage`, `AddPower`, `CostPower`, `NoPowerWhenTry`, `BurnCard`, `Shuffle`, `Dead`, `Resurrection`, `EndRound`, `Win`, `Escape`, `CreateCardItem`, `ICreateCardItem`, `EndCreateCardItem`.

## Review Checklist

- Exact headers and column order match the target project.
- Data/Text local ids match.
- Cross-table Mod ids are full runtime ids.
- Card `InitScript` sets `BaseScript`.
- `PackBelong` points to an existing full card-pack id.
- Persistent effects live in Buffs.
- Buff event registration avoids duplicate listeners.
- `ClearScript` cleans flags/tokens when needed.
- Resource paths omit image extensions when the project does so.
- CSV quoting is valid after embedded Lua quotes.
