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

function SunExp_CalcSparkDamage(self)
    return 5
end

function SunExp_CalcFlareCutDamage(self)
    local level = SunExp_GetRadianceLevel(self)
    local damage = 10 + level
    if SunExp_HasCrownPhase(self, 4) then
        damage = damage + level
    end
    return damage
end

function SunExp_CalcSolarSparkDamage(self)
    local useFlame = math.min(5, SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame"))
    local radLevel = SunExp_GetRadianceLevel(self)
    local damage = 6 + useFlame * 4
    if SunExp_HasCrownPhase(self, 4) then
        damage = damage + radLevel * 2
    end
    return damage
end

function SunExp_CalcCrownPressureDamage(self)
    if not SunExp_HasCrownPhase(self, 4) then
        return 0
    end
    local radLevel = SunExp_GetRadianceLevel(self)
    local fieldLevel = SunExp_GetBuffLevel(self, "SunExp_sunexp_scorching_canopy")
    return radLevel * 2 + fieldLevel * 5
end

function SunExp_CalcCrownCoreFlashDamage(self)
    local flameCount = SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame")
    local useRad = math.floor(SunExp_GetRadianceLevel(self) / 2)
    return 40 + flameCount * 5 + useRad * 10
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
