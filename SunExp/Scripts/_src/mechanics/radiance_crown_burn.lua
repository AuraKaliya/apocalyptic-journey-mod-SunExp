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

function SunExp_SolarCrownTierBuffId()
    return "SunExp_sunexp_solar_crown_tier"
end

function SunExp_CalcSolarCrownTierByRadiance(radiance)
    local level = math.floor(tonumber(radiance) or 0)
    if level >= 15 then
        return 5
    end
    if level >= 12 then
        return 4
    end
    if level >= 8 then
        return 3
    end
    if level >= 4 then
        return 2
    end
    if level >= 1 then
        return 1
    end
    return 0
end

function SunExp_CalcSolarCrownTier(self)
    return SunExp_CalcSolarCrownTierByRadiance(SunExp_GetRadianceLevel(self))
end

function SunExp_GetSolarCrownTier(self)
    if self == nil or self.Self == nil then
        return 0
    end
    return SunExp_GetStatusBuffLevel(self.Self, SunExp_SolarCrownTierBuffId())
end

function SunExp_SetSolarCrownTier(self, tier)
    if self == nil or self.Self == nil then
        return 0
    end
    local nextTier = math.max(0, math.min(5, math.floor(tonumber(tier) or 0)))
    self:SetStatus("Self")
    self:RemoveBuff(SunExp_SolarCrownTierBuffId())
    if nextTier > 0 then
        self:AddBuff(SunExp_SolarCrownTierBuffId(), tostring(nextTier))
    end
    return nextTier
end

function SunExp_ConsumeRadiance(self, amount)
    if self == nil or self.Self == nil then
        return 0
    end
    local count = math.floor(tonumber(amount) or 0)
    if count <= 0 then
        return 0
    end
    local current = SunExp_GetRadianceLevel(self)
    local consumed = math.min(current, count)
    if consumed <= 0 then
        return 0
    end
    local nextLevel = current - consumed
    if nextLevel <= 0 then
        SunExp_RemoveStatusBuff(self, self.Self, "SunExp_sunexp_solar_radiance", "Self")
    else
        SunExp_SetStatusBuffLevel(self, self.Self, "SunExp_sunexp_solar_radiance", nextLevel)
    end
    return consumed
end

function SunExp_OnSolarCrownApplied(self)
    if not SunExp_HasSolarCrown(self) then
        return 0
    end
    return SunExp_SetSolarCrownTier(self, SunExp_CalcSolarCrownTier(self))
end

function SunExp_OnSolarCrownCleared(self)
    local tier = SunExp_GetSolarCrownTier(self)
    if tier > 0 then
        SunExp_ConsumeRadiance(self, tier * 2)
    end
    self:SetStatus("Self")
    self:RemoveBuff(SunExp_SolarCrownTierBuffId())
    return tier
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
        SunExp_ConsumeEmberBeforeBurnSettlementForStatus(self, "All")
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
    local tier = SunExp_GetSolarCrownTier(self)
    local effectCount = 0
    if tier >= 1 then
        effectCount = effectCount + 1
        local total = SunExp_GetNegativeBuffTotal(self.Self)
        if total > 0 then
            SunExp_RemoveAllNegativeBuffs(self, self.Self)
            self:SetStatus("Self")
            self:AddBuff("buff_burn", tostring(total))
        end
    end
    if tier >= 2 then
        effectCount = effectCount + 1
        self:DrawCount("1")
    end
    if tier >= 3 then
        effectCount = effectCount + 1
        self:SetStatus("Self")
        self:ChangePower("1")
    end
    if tier >= 4 then
        effectCount = effectCount + 1
        local burn = SunExp_GetBuffLevel(self, "buff_burn")
        if burn > 0 then
            self:SetStatus("Self")
            self:RemoveBuff("buff_burn")
            self:AddBuff("SunExp_sunexp_gathered_flame", tostring(burn))
        end
    end
    if tier >= 5 then
        effectCount = effectCount + 1
        self:SetStatus("AllTarget")
        self:AddBuff("buff_burn", "5")
        SunExp_TriggerBurnAllEnemies(self, 1)
    end
    SunExp_DebugWhiteRadianceLog(
        "SolarCrown triggered effectCount=" .. tostring(effectCount)
        .. ", tier=" .. tostring(tier)
        .. ", radiance=" .. tostring(SunExp_GetRadianceLevel(self))
    )
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
