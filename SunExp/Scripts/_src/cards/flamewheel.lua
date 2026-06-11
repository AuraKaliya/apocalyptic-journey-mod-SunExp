function SunExp_FlamewheelKey()
    return "SunExp_flamewheel_recurrence_count"
end

function SunExp_GetFlamewheelSkillTime()
    local skillTime = nil
    pcall(function()
        if CS ~= nil and CS.ScriptExecutor ~= nil and CS.ScriptExecutor.PlayerInfo ~= nil then
            skillTime = CS.ScriptExecutor.PlayerInfo.SkillTime
        end
    end)
    return skillTime
end

function SunExp_GetFlamewheelUsed()
    local st = SunExp_GetFlamewheelSkillTime()
    if st == nil then
        return 0
    end
    local key = SunExp_FlamewheelKey()
    pcall(function()
        if st.ContainsKey ~= nil and not st:ContainsKey(key) then
            st:set_Item(key, 0)
        end
    end)
    local ok, value = pcall(function()
        return st:get_Item(key)
    end)
    if ok then
        return tonumber(value) or 0
    end
    return 0
end

function SunExp_SetFlamewheelUsed(value)
    local st = SunExp_GetFlamewheelSkillTime()
    if st == nil then
        return false
    end
    local ok = pcall(function()
        st:set_Item(SunExp_FlamewheelKey(), tonumber(value) or 0)
    end)
    return ok
end

function SunExp_SetFlamewheelCost(cardData, used)
    if cardData == nil then
        return false
    end
    local vars = nil
    pcall(function()
        vars = cardData.Vars
    end)
    if vars == nil then
        return false
    end
    local value = tostring(used or SunExp_GetFlamewheelUsed())
    local ok = pcall(function()
        vars:set_Item("ExCost", value)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        vars["ExCost"] = value
    end)
    return ok
end

function SunExp_UpdateFlamewheelCost(cardData)
    return SunExp_SetFlamewheelCost(cardData)
end

function SunExp_IsFlamewheelCardItem(item)
    if item == nil then
        return false
    end
    local id = nil
    if item.data ~= nil and item.data.ContainsKey ~= nil and item.data:ContainsKey("Id") then
        id = item.data:get_Item("Id")
    end
    if id == nil and item.dataConfig ~= nil and item.dataConfig.data ~= nil and item.dataConfig.data.ContainsKey ~= nil and item.dataConfig.data:ContainsKey("Id") then
        id = item.dataConfig.data:get_Item("Id")
    end
    if id == nil then
        return false
    end
    return string.find(tostring(id), "flamewheel_recurrence", 1, true) ~= nil
end

function SunExp_TrySetFlamewheelItemCost(item, used)
    if item == nil then
        return false
    end
    local value = tostring(used)
    local changed = false
    pcall(function()
        if item.Vars ~= nil then
            item.Vars:set_Item("ExCost", value)
            changed = true
        end
    end)
    pcall(function()
        if item.dataConfig ~= nil and item.dataConfig.Vars ~= nil then
            item.dataConfig.Vars:set_Item("ExCost", value)
            changed = true
        end
    end)
    if changed then
        pcall(function()
            if item.DataUpdate ~= nil then
                item:DataUpdate()
            end
        end)
    end
    return changed
end

function SunExp_RefreshFlamewheelHand(self, used)
    if self == nil then
        return false
    end
    if SunExp_GetVar(self, "SunExpFlamewheelRefreshBusy", "0") == "1" then
        return false
    end
    SunExp_SetVar(self, "SunExpFlamewheelRefreshBusy", "1")
    local changed = false
    pcall(function()
        local hand = nil
        pcall(function()
            hand = self.HandCard
        end)
        if hand == nil then
            return
        end
        local nextCost = used
        if nextCost == nil then
            nextCost = SunExp_GetFlamewheelUsed()
        end
        local count = SunExp_GetCollectionCount(hand)
        for i = 0, count - 1 do
            local item = SunExp_GetCollectionItem(hand, i)
            if SunExp_IsFlamewheelCardItem(item) and SunExp_TrySetFlamewheelItemCost(item, nextCost) then
                changed = true
            end
        end
    end)
    SunExp_SetVar(self, "SunExpFlamewheelRefreshBusy", "0")
    return changed
end

function SunExp_RefreshFlamewheelCosts(self, used)
    return SunExp_RefreshFlamewheelHand(self, used)
end
