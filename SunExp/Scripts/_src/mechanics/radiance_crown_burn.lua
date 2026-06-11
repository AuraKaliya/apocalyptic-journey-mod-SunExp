function SunExp_HasCrownPhase(self, threshold)
    if self == nil or self.Self == nil then
        return false
    end
    local crown = self.Self:GetBuff("SunExp_sunexp_solar_crown")
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
    local ward = self.Self:GetBuff("SunExp_sunexp_ember_cloak")
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
