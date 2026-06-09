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

function SunExp_GetBuffLevel(self, buffId)
    if self == nil or self.Self == nil then
        return 0
    end
    local buff = self.Self:GetBuff(buffId)
    if buff == nil or buff.buffConfig == nil or buff.buffConfig.Level == nil then
        return 0
    end
    return buff.buffConfig.Level
end

function SunExp_GetRadianceLevel(self)
    return SunExp_GetBuffLevel(self, "SunExp_sunexp_solar_radiance")
end

function SunExp_GetEnemyTargets(self)
    local targets = {}
    if self == nil then
        return targets
    end
    self:SetStatus("AllTarget")
    if self.Object == nil then
        return targets
    end
    for i = 0, self.Object.Count - 1 do
        local target = self.Object:get_Item(i)
        if target ~= nil then
            table.insert(targets, target)
        end
    end
    return targets
end

function SunExp_HasNegativeBuff(target)
    if target == nil or target.GetBuffs == nil then
        return false
    end
    local buffs = target:GetBuffs()
    if buffs == nil then
        return false
    end
    for i = 0, buffs.Count - 1 do
        local buff = buffs:get_Item(i)
        if buff ~= nil and buff.buffConfig ~= nil and buff.buffConfig.dataConfig ~= nil and buff.buffConfig.dataConfig.data ~= nil then
            local typeName = buff.buffConfig.dataConfig.data:get_Item("Type")
            if typeName == "负面" then
                return true
            end
        end
    end
    return false
end

function SunExp_HasCrownPhase(self, threshold)
    if self == nil or self.Self == nil then
        return false
    end
    local crown = self.Self:GetBuff("SunExp_sunexp_solar_crown_state")
    return crown ~= nil and SunExp_GetRadianceLevel(self) >= threshold
end

function SunExp_IsBurnWardPending(self)
    return SunExp_GetVar(self, "SunExpBurnWardPending", "0") == "1"
end

function SunExp_SetBurnWardPending(self, value)
    SunExp_SetVar(self, "SunExpBurnWardPending", value and "1" or "0")
end

function SunExp_IsSelfBurnProtected(self, includePending)
    if self == nil or self.Self == nil then
        return false
    end
    local ward = self.Self:GetBuff("SunExp_sunexp_burn_ward")
    if ward ~= nil and ward.buffConfig ~= nil and ward.buffConfig.Level > 0 then
        return true
    end
    if includePending and SunExp_IsBurnWardPending(self) then
        return true
    end
    return SunExp_HasCrownPhase(self, 12)
end

function SunExp_ClearSelfBurnIfProtected(self, includePending)
    if self == nil or self.Self == nil then
        return false
    end
    if SunExp_IsSelfBurnProtected(self, includePending) then
        self.Self:RemoveBuff("buff_burn")
        return true
    end
    return false
end

function SunExp_ApplySelfBurn(self, amount, includePending)
    if self == nil or amount == nil or amount <= 0 then
        return false
    end
    if SunExp_IsSelfBurnProtected(self, includePending) then
        if self.Self ~= nil then
            self.Self:RemoveBuff("buff_burn")
        end
        return false
    end
    self:SetStatus("Self")
    self:AddBuff("buff_burn", tostring(amount))
    return true
end

function SunExp_RegisterHook(self, hookKey, tokenKey)
    if self == nil or self.Vars == nil then
        return "0"
    end
    if self.Vars:ContainsKey(hookKey) and self.Vars:get_Item(hookKey) == "1" then
        return nil
    end
    local token = tonumber(SunExp_GetVar(self, tokenKey, "0")) or 0
    token = token + 1
    self.Vars:set_Item(hookKey, "1")
    self.Vars:set_Item(tokenKey, tostring(token))
    return tostring(token)
end

function SunExp_IsHookTokenActive(self, tokenKey, token)
    if self == nil or self.Vars == nil then
        return true
    end
    return SunExp_GetVar(self, tokenKey, "") == tostring(token)
end

function SunExp_ClearHook(self, hookKey, tokenKey)
    if self == nil or self.Vars == nil then
        return
    end
    self.Vars:set_Item(hookKey, "0")
    local token = tonumber(SunExp_GetVar(self, tokenKey, "0")) or 0
    self.Vars:set_Item(tokenKey, tostring(token + 1))
end

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
