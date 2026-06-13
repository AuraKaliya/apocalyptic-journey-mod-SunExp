function SunExp_GetVar(self, key, defaultValue)
    if self == nil or self.Vars == nil or not self.Vars:ContainsKey(key) then
        return defaultValue
    end
    return self.Vars:get_Item(key)
end

function SunExp_SetVar(self, key, value)
    if self == nil or self.Vars == nil then
        return
    end
    self.Vars:set_Item(key, tostring(value))
end

function SunExp_CombatVarsMap()
    if CS == nil or CS.FightManager == nil or CS.FightManager.Instance == nil then
        return nil
    end
    return CS.FightManager.Instance.TempVarsMap
end

function SunExp_CombatIntGet(key, defaultValue)
    local map = SunExp_CombatVarsMap()
    if map == nil or key == nil then
        return math.floor(tonumber(defaultValue) or 0)
    end
    local ok, hasKey = pcall(function()
        return map:ContainsKey(key)
    end)
    if not ok or not hasKey then
        return math.floor(tonumber(defaultValue) or 0)
    end
    local okValue, value = pcall(function()
        return map:get_Item(key)
    end)
    if okValue then
        return math.floor(tonumber(value) or 0)
    end
    return math.floor(tonumber(defaultValue) or 0)
end

function SunExp_CombatIntSet(key, value)
    local map = SunExp_CombatVarsMap()
    if map == nil or key == nil then
        return false
    end
    local nextValue = math.floor(tonumber(value) or 0)
    local ok = pcall(function()
        map:set_Item(key, nextValue)
    end)
    return ok
end

function SunExp_CombatIntAdd(key, amount)
    local nextValue = SunExp_CombatIntGet(key, 0) + math.floor(tonumber(amount) or 0)
    SunExp_CombatIntSet(key, nextValue)
    return nextValue
end
