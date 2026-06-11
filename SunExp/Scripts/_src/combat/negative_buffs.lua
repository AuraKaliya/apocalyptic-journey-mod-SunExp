function SunExp_GetBuffTypeName(buff)
    if buff == nil or buff.buffConfig == nil then
        return nil
    end
    local ok, typeName = pcall(function()
        return buff.buffConfig.Type
    end)
    if ok and typeName ~= nil then
        return tostring(typeName)
    end
    if buff.buffConfig.dataConfig == nil or buff.buffConfig.dataConfig.data == nil then
        return nil
    end
    ok, typeName = pcall(function()
        local data = buff.buffConfig.dataConfig.data
        if data.ContainsKey ~= nil and not data:ContainsKey("Type") then
            return nil
        end
        return data:get_Item("Type")
    end)
    if ok and typeName ~= nil then
        return tostring(typeName)
    end
    return nil
end

SunExp_PositiveBuffExcludeIds = {
    solar_radiance = true,
    gathered_flame = true,
    scorching_canopy = true,
    ember_cloak = true,
    solar_crown = true,
    origin_core_radiance = true,
    cycle_gathered_flame = true,
    afterglow_omen = true
}

function SunExp_NormalizeBuffId(buffId)
    if buffId == nil then
        return nil
    end
    local id = tostring(buffId)
    local prefix = "SunExp_sunexp_"
    if string.sub(id, 1, string.len(prefix)) == prefix then
        return string.sub(id, string.len(prefix) + 1)
    end
    return id
end

function SunExp_IsPositiveBuffExcludedId(buffId)
    local id = SunExp_NormalizeBuffId(buffId)
    return id ~= nil and SunExp_PositiveBuffExcludeIds[id] == true
end

function SunExp_IsPositiveBuffExcludedItem(buff)
    return SunExp_IsPositiveBuffExcludedId(SunExp_GetBuffIdFromItem(buff))
end

function SunExp_IsNegativeBuffItem(buff)
    if SunExp_IsPositiveBuffExcludedItem(buff) then
        return false
    end
    local typeName = SunExp_GetBuffTypeName(buff)
    if typeName == nil then
        return false
    end
    return typeName == "负面" or typeName == "Negative" or string.find(typeName, "负面", 1, true) ~= nil
end

function SunExp_GetBuffIdFromItem(buff)
    if buff == nil or buff.buffConfig == nil then
        return nil
    end
    local ok, buffId = pcall(function()
        return buff.buffConfig.BuffId
    end)
    if ok and buffId ~= nil then
        return buffId
    end
    if buff.buffConfig.dataConfig == nil or buff.buffConfig.dataConfig.data == nil then
        return nil
    end
    ok, buffId = pcall(function()
        local data = buff.buffConfig.dataConfig.data
        if data.ContainsKey ~= nil and not data:ContainsKey("Id") then
            return nil
        end
        return data:get_Item("Id")
    end)
    if ok then
        return buffId
    end
    return nil
end

function SunExp_GetNegativeBuffSummary(target)
    local ids = {}
    local total = 0
    if target == nil then
        return ids, total
    end
    local ok, buffs = pcall(function()
        return target:GetBuffs()
    end)
    if not ok or buffs == nil then
        return ids, total
    end
    local count = SunExp_GetCollectionCount(buffs)
    for i = 0, count - 1 do
        local buff = SunExp_GetCollectionItem(buffs, i)
        if SunExp_IsNegativeBuffItem(buff) then
            local id = SunExp_GetBuffIdFromItem(buff)
            if id ~= nil then
                if buff.buffConfig ~= nil then
                    total = total + (tonumber(buff.buffConfig.Level) or 0)
                end
                table.insert(ids, id)
            end
        end
    end
    return ids, total
end

function SunExp_GetNegativeBuffTotal(target)
    local ids, total = SunExp_GetNegativeBuffSummary(target)
    return tonumber(total) or 0
end

function SunExp_RemoveAllNegativeBuffs(self, target)
    if self == nil then
        return false
    end
    local ids = SunExp_GetNegativeBuffSummary(target)
    for _, id in ipairs(ids) do
        SunExp_RemoveStatusBuff(self, target or self.Self, id)
    end
    return #ids > 0
end

function SunExp_HasNegativeBuff(target)
    if target == nil then
        return false
    end
    local ok, buffs = pcall(function()
        return target:GetBuffs()
    end)
    if not ok or buffs == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(buffs)
    for i = 0, count - 1 do
        if SunExp_IsNegativeBuffItem(SunExp_GetCollectionItem(buffs, i)) then
            return true
        end
    end
    return false
end
