# Lua Runtime API Notes

Use this reference when writing or reviewing `InitScript`, `DrawScript`, `UseScript`, `DropScript`, `ApplyScript`, `ClearScript`, `OwnScript`, `FightScript`, `SkillScript`, dialogue scripts, and event scripts.

## ScriptExecutor shape

In CSV script columns, `self` is usually a `CS.ScriptExecutor` instance. Official TypeHint methods include:

- Targeting: `self:SetStatus(status)`, `self:SetStatusById(instanceId)`
- Damage and defense: `self:Damage(val)`, `self:Damage(val, damageType)`, `self:ChangeDefence(val)`
- Buffs: `self:AddBuff(buffId, level)`, `self:RemoveBuff(buffId)`, `self:RunImmediately(buffId, eventName)`
- Cards and resources: `self:DrawCount(val)`, `self:ChangePower(val)`, `self:AddCard(id)`, `self:BurnCard(val, type)`
- Events: `self:AddEvent(eventName, fn)`, `self:AddTempEvent(eventName, fn)`
- Display: `self:AddDescription(index, type, value)`, `self:UpdateRelicShow()`

Use colon calls for instance methods. `self:AddBuff("buff_burn", "2")` is valid Lua. `AddBuff(...)`, `Self.AddBuff(...)`, or `self.AddBuff(...)` are suspect unless the surrounding API proves otherwise.

## Common status filters

- `Self`: the player/status owner.
- `Target`: the selected target.
- `All`: all combatants.
- `AllTarget`: all enemies in current SunExp helpers.
- `AllRandomEnemy1`, `AllRandomEnemy2`: random enemies.

For target-dependent cards, set `AttackCardItem` in `InitScript` and then use `self:SetStatus("Target")` before target damage or target Buff calls.

## Buff settlement and level-change events

Immediate buff settlement uses the current status selection. For all-enemy burn settlement, official template data uses the C# shape `SetStatus("AllTarget"); RunImmediately(DataId.buff_burn, "StartRound")`; in SunExp Lua CSV snippets, write:

```lua
self:SetStatus("AllTarget")
self:RunImmediately("buff_burn", "StartRound")
```

Reference examples:

- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Buff/buff.csv`
- `apocalyptic-journey-mod-tutorial/ModTemplate/Scripts/Lib/DataConfigs/Data/Card/elementscard.csv`

For selected-target settlement, set `Target` or `SetStatusById(...)` first. For all enemies, prefer the direct `AllTarget` pattern above unless a helper has already proved the same event dispatch behavior.

Buff stack-change effects should listen to the buff-specific level-change event:

```lua
self:AddEvent("buff_burnOnLevelChange", function()
    -- Read current buff_burn level and compare with a stored previous value.
end)
```

Use this pattern for effects phrased as "when a buff level changes" or "when a buff increases". `Action` polling misses changes caused by StartRound, enemy turns, and nested card/buff resolution. When only increases should count, store the previous level and update it on both increase and decrease paths.

Official references for level-change behavior include `ScriptSample.lua` and `DataConfigs/Data/Relic/relic.csv`, but many `DataConfigs` snippets are C# and must be translated to Lua before use.

## Dictionary and collection access

C# dictionaries exposed through XLua are not Lua tables:

```lua
if self.Vars ~= nil and self.Vars:ContainsKey("Key") then
    local value = self.Vars:get_Item("Key")
    self.Vars:set_Item("Key", tostring(tonumber(value) + 1))
end
```

Do not write `self.Vars["Key"] = value`.

For C# lists, use `.Count` plus `:get_Item(i)` with zero-based indexes unless existing helper code wraps the collection.

## C# to Lua translation traps

Translate these patterns before using official original data examples:

- `int.Parse(x)` -> `tonumber(x) or 0`
- `Math.Max(a, b)` -> `math.max(a, b)`
- `for (int i=0; i<n; i++)` -> `for i = 1, n do`
- `foreach (...)` -> iterate with `Count` and `get_Item`, or use an existing helper.
- `Vars["Key"] = "1"` -> `self.Vars:set_Item("Key", "1")`
- `AddDescription("1", "Damage", "10")` -> `self:AddDescription("1", "Damage", "10")`
- `Damage("10")` -> `self:Damage("10")`
- `new DataConfig(...)` generally has no direct CSV Lua equivalent; verify the TypeHint and prefer existing game Lua APIs.

## ModConfig entry points

Use `function ModConfig:Setup() ... end` for load-time registration.

Known official ModConfig methods include:

- `self:SetDataConfig(id, table)`
- `self:ModifyDataConfig(id, key, value)`
- `self:MergeDataConfig(source, target)`
- `self:RedirectSourcePath(originalPath, newPath)`
- `self:AddDynamicMethod(methodName, fn)`
- `self:AddMethodHookBefore(typeDotMethod, fn)`
- `self:AddMethodHookAfter(typeDotMethod, fn)`

SunExp currently uses `AddDynamicMethod` to expose helper functions to CSV script snippets.
