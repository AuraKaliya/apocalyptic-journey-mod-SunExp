function SunExp_GetBuffLevel(self, buffId)
    if self == nil or self.Self == nil then
        return 0
    end
    local buff = self.Self:GetBuff(buffId)
    if buff == nil or buff.buffConfig == nil or buff.buffConfig.Level == nil then
        return 0
    end
    return tonumber(buff.buffConfig.Level) or buff.buffConfig.Level or 0
end

function SunExp_GetRadianceLevel(self)
    return SunExp_GetBuffLevel(self, "SunExp_sunexp_solar_radiance")
end

function SunExp_GetSolarMultiplier(self)
    if self ~= nil and self.Self ~= nil and self.Self:GetBuff("SunExp_sunexp_solar_crown") ~= nil then
        return 2
    end
    return 1
end

function SunExp_CalcSolarCoefficient(self, target)
    local radiance = SunExp_GetRadianceLevel(self)
    local flame = SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame")
    local burn = SunExp_GetStatusBuffLevel(target, "buff_burn")
    return SunExp_GetSolarMultiplier(self) * (radiance * 2 + math.floor(flame / 3) + math.floor(burn / 2))
end

function SunExp_DealDamage(self, amount)
    if self == nil then
        return false
    end
    local damage = math.floor(tonumber(amount) or 0)
    if damage <= 0 then
        return false
    end
    damage = SunExp_ApplyWunaEmberDamageBonus(self, damage)
    self:Damage(tostring(damage))
    return true
end

function SunExp_AddDamageDescription(self, index, amount)
    if self == nil then
        return 0
    end
    local damage = math.floor(tonumber(amount) or 0)
    if damage < 0 then
        damage = 0
    end
    pcall(function()
        self:AddDescription(tostring(index), "Damage", tostring(damage))
    end)
    return damage
end

function SunExp_CalcSolarKeywordDamage(self, base, target, coefficientScale)
    local scale = tonumber(coefficientScale) or 1
    return math.floor((tonumber(base) or 0) + SunExp_CalcSolarCoefficient(self, target) * scale)
end

function SunExp_CalcSolarKeywordBlock(self, base)
    return math.floor((tonumber(base) or 0) + SunExp_CalcSolarCoefficient(self, nil))
end

function SunExp_DealSolarKeywordDamage(self, base, target, fallbackStatus, coefficientScale)
    if target ~= nil then
        SunExp_SetStatusForBuff(self, target, fallbackStatus or "Target")
    elseif fallbackStatus ~= nil then
        self:SetStatus(fallbackStatus)
    end
    return SunExp_DealDamage(self, SunExp_CalcSolarKeywordDamage(self, base, target, coefficientScale))
end

function SunExp_DealSolarKeywordDamageAllEnemies(self, base, coefficientScale)
    local targets = SunExp_GetEnemyTargets(self)
    local maxDamage = 0
    for i = 1, #targets do
        local target = targets[i]
        local damage = SunExp_CalcSolarKeywordDamage(self, base, target, coefficientScale)
        if damage > maxDamage then
            maxDamage = damage
        end
        SunExp_SetPrimaryTarget(self, target)
        SunExp_DealDamage(self, damage)
    end
    return maxDamage
end

function SunExp_ApplySolarKeywordSkill(self, baseBlock)
    local block = SunExp_CalcSolarKeywordBlock(self, baseBlock)
    if block > 0 then
        self:SetStatus("Self")
        self:ChangeDefence(tostring(block))
    end
    return block
end

function SunExp_CalcSparkDamage(self)
    return 5
end

function SunExp_CalcFlareCutDamage(self)
    return 10
end

function SunExp_CalcFlareCutBonusDamage(self)
    return SunExp_CalcSolarKeywordBonusDamage(self, SunExp_GetPrimaryTarget(self))
end

function SunExp_CalcSolarSparkBaseDamage(self)
    local useFlame = math.min(5, SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame"))
    return 8 + useFlame * 2
end

function SunExp_CalcSolarSparkDamage(self)
    return SunExp_CalcSolarSparkBaseDamage(self)
end

function SunExp_CalcSolarSparkBonusDamage(self)
    return SunExp_CalcSolarKeywordBonusDamage(self, SunExp_GetPrimaryTarget(self))
end

function SunExp_CalcCrownPressureDamage(self)
    return 0
end

function SunExp_CalcCrownCoreFlashDamage(self)
    return SunExp_CalcSolarKeywordDamage(self, 40, SunExp_GetPrimaryTarget(self), 3)
end

function SunExp_CalcFlamePierceDamage(self)
    local target = SunExp_GetPrimaryTarget(self)
    local burnLevel = SunExp_GetStatusBuffLevel(target, "buff_burn")
    local flameLevel = SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame")
    local mult = math.max(1, math.floor(flameLevel / 4))
    return 8 + burnLevel * mult
end

function SunExp_CalcSmokeErosionDamage(self)
    local target = SunExp_GetPrimaryTarget(self)
    return 7 + SunExp_GetStatusBuffLevel(target, "buff_burn")
end
