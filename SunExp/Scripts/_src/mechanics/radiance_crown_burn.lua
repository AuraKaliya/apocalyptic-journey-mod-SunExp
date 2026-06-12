function SunExp_HasCrownPhase(self, threshold)
    if self == nil or self.Self == nil then
        return false
    end
    local crown = self.Self:GetBuff("SunExp_sunexp_solar_crown")
    return crown ~= nil and SunExp_GetRadianceLevel(self) >= threshold
end

function SunExp_HasSolarCrown(self)
    return self ~= nil and self.Self ~= nil and self.Self:GetBuff("SunExp_sunexp_solar_crown") ~= nil
end

function SunExp_TriggerBurnAll(self, times)
    if self == nil then
        return 0
    end
    local count = tonumber(times) or 1
    if count < 1 then
        count = 1
    end
    local triggered = 0
    for i = 1, count do
        self:SetStatus("All")
        local ok = pcall(function()
            self:RunImmediately("buff_burn", "StartRound")
        end)
        if ok then
            triggered = triggered + 1
        end
    end
    return triggered
end

function SunExp_TriggerSolarCrown(self)
    if not SunExp_HasSolarCrown(self) then
        return false
    end
    local radiance = SunExp_GetRadianceLevel(self)
    if radiance >= 1 then
        local total = SunExp_GetNegativeBuffTotal(self.Self)
        if total > 0 then
            SunExp_RemoveAllNegativeBuffs(self, self.Self)
            self:SetStatus("Self")
            self:AddBuff("buff_burn", tostring(total))
        end
    end
    if radiance >= 4 then
        self:DrawCount("1")
    end
    if radiance >= 8 then
        self:SetStatus("Self")
        self:ChangePower("1")
    end
    if radiance >= 12 then
        local burn = SunExp_GetBuffLevel(self, "buff_burn")
        if burn > 0 then
            self:SetStatus("Self")
            self:RemoveBuff("buff_burn")
            self:AddBuff("SunExp_sunexp_gathered_flame", tostring(burn))
        end
    end
    if radiance >= 15 then
        self:SetStatus("All")
        self:AddBuff("buff_burn", "5")
        SunExp_TriggerBurnAll(self, 1)
    end
    return true
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
    local ward = self.Self:GetBuff("SunExp_sunexp_ember_cloak")
    if ward ~= nil and ward.buffConfig ~= nil and ward.buffConfig.Level > 0 then
        return true
    end
    if includePending and SunExp_IsBurnWardPending(self) then
        return true
    end
    return false
end

function SunExp_ClearSelfBurnIfProtected(self, includePending)
    if self == nil or self.Self == nil then
        return false
    end
    if SunExp_IsSelfBurnProtected(self, includePending) then
        SunExp_RemoveStatusBuff(self, self.Self, "buff_burn")
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
            SunExp_RemoveStatusBuff(self, self.Self, "buff_burn")
        end
        return false
    end
    self:SetStatus("Self")
    self:AddBuff("buff_burn", tostring(amount))
    return true
end
