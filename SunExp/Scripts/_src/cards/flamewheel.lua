function SunExp_FlamewheelKey()
    return "SunExp_flamewheel_recurrence_count"
end

function SunExp_GetFlamewheelUsed()
    local st = CS.ScriptExecutor.PlayerInfo.SkillTime
    if st == nil then
        return 0
    end
    local key = SunExp_FlamewheelKey()
    if not st:ContainsKey(key) then
        st:set_Item(key, 0)
    end
    return tonumber(st:get_Item(key)) or 0
end

function SunExp_SetFlamewheelUsed(value)
    local st = CS.ScriptExecutor.PlayerInfo.SkillTime
    if st == nil then
        return
    end
    st:set_Item(SunExp_FlamewheelKey(), value)
end

function SunExp_UpdateFlamewheelCost(cardData)
    if cardData == nil or cardData.Vars == nil then
        return
    end
    local used = SunExp_GetFlamewheelUsed()
    cardData.Vars:set_Item("ExCost", tostring(used))
    SunExp_RefreshFlamewheelCosts(cardData, used)
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

function SunExp_RefreshFlamewheelCosts(self, used)
    if self == nil or self.HandCard == nil then
        return
    end
    local count = self.HandCard.Count or 0
    for i = 0, count - 1 do
        local item = self.HandCard:get_Item(i)
        if SunExp_IsFlamewheelCardItem(item) then
            if item.Vars ~= nil then
                item.Vars:set_Item("ExCost", tostring(used))
            end
            if item.dataConfig ~= nil and item.dataConfig.Vars ~= nil then
                item.dataConfig.Vars:set_Item("ExCost", tostring(used))
            end
            if item.DataUpdate ~= nil then
                item:DataUpdate()
            end
        end
    end
end
