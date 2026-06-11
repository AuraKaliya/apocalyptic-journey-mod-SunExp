function SunExp_WunaEmberBuffId()
    return "SunExp_wuna_wuna_ember"
end

function SunExp_WunaWhiteSunPrayerCardId()
    return "SunExp_wuna_wuna_white_sun_prayer"
end

function SunExp_WunaGraveSongCardId()
    return "SunExp_wuna_wuna_grave_song"
end

function SunExp_WunaCoronationTokenCardId()
    return "SunExp_wuna_wuna_coronation_token"
end

function SunExp_WunaGetSkillTime(key)
    local player = SunExp_PlayerInfo()
    if player == nil or player.SkillTime == nil or key == nil then
        return 0
    end
    local ok, hasKey = pcall(function()
        return player.SkillTime:ContainsKey(key)
    end)
    if not ok then
        return 0
    end
    if not hasKey then
        pcall(function()
            player.SkillTime:set_Item(key, 0)
        end)
        return 0
    end
    local okValue, value = pcall(function()
        return player.SkillTime:get_Item(key)
    end)
    if okValue then
        return tonumber(value) or 0
    end
    return 0
end

function SunExp_WunaSetSkillTime(key, value)
    local player = SunExp_PlayerInfo()
    if player == nil or player.SkillTime == nil or key == nil then
        return false
    end
    local nextValue = math.max(0, math.floor(tonumber(value) or 0))
    local ok = pcall(function()
        player.SkillTime:set_Item(key, nextValue)
    end)
    return ok
end

function SunExp_WunaTickSkillTimes()
    local keys = {
        SunExp_WunaWhiteSunPrayerCardId(),
        SunExp_WunaGraveSongCardId()
    }
    for i = 1, #keys do
        local current = SunExp_WunaGetSkillTime(keys[i])
        if current > 0 then
            SunExp_WunaSetSkillTime(keys[i], current - 1)
        end
    end
end

function SunExp_WunaGetEnemyBurnTotal(self)
    local total = 0
    local targets = SunExp_GetEnemyTargets(self)
    for i = 1, #targets do
        total = total + SunExp_GetStatusBuffLevel(targets[i], "buff_burn")
    end
    return total
end

function SunExp_WunaGetAllBurnTotal(self)
    local total = SunExp_WunaGetEnemyBurnTotal(self)
    if self ~= nil and self.Self ~= nil then
        total = total + SunExp_GetStatusBuffLevel(self.Self, "buff_burn")
    end
    return total
end

function SunExp_WunaClampEmber(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local ember = self.Self:GetBuff(SunExp_WunaEmberBuffId())
    if ember == nil or ember.buffConfig == nil then
        return 0
    end
    local level = math.floor(tonumber(ember.buffConfig.Level) or 0)
    if level > 99 then
        ember.buffConfig.Level = 99
        return 99
    end
    if level <= 0 then
        self:SetStatus("Self")
        self:RemoveBuff(SunExp_WunaEmberBuffId())
        return 0
    end
    return level
end

function SunExp_WunaGetEmberLevel(self)
    if self == nil or self.Self == nil then
        return 0
    end
    return SunExp_GetStatusBuffLevel(self.Self, SunExp_WunaEmberBuffId())
end

function SunExp_WunaAddEmber(self, amount)
    local gain = math.floor(tonumber(amount) or 0)
    if self == nil or gain <= 0 then
        return 0
    end
    self:SetStatus("Self")
    self:AddBuff(SunExp_WunaEmberBuffId(), tostring(gain))
    return SunExp_WunaClampEmber(self)
end

function SunExp_WunaHalveEmber(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local ember = self.Self:GetBuff(SunExp_WunaEmberBuffId())
    if ember == nil or ember.buffConfig == nil then
        return 0
    end
    local nextLevel = math.floor((tonumber(ember.buffConfig.Level) or 0) / 2)
    self:SetStatus("Self")
    if nextLevel <= 0 then
        self:RemoveBuff(SunExp_WunaEmberBuffId())
        return 0
    end
    ember.buffConfig.Level = nextLevel
    return nextLevel
end

function SunExp_WunaTryGainRadianceFromEnemyBurn(self)
    if self == nil or self.Self == nil then
        return false
    end
    local current = SunExp_WunaGetEnemyBurnTotal(self)
    local previous = tonumber(SunExp_GetVar(self, "SunExpWunaPrevEnemyBurn", tostring(current))) or current
    SunExp_SetVar(self, "SunExpWunaPrevEnemyBurn", tostring(current))
    if current <= previous then
        return false
    end
    if SunExp_GetVar(self, "SunExpWunaRadianceDone", "0") == "1" then
        return false
    end
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_solar_radiance", "1")
    SunExp_SetVar(self, "SunExpWunaRadianceDone", "1")
    return true
end

function SunExp_WunaStartRound(self)
    SunExp_WunaTickSkillTimes()
    SunExp_SetVar(self, "SunExpWunaRadianceDone", "0")
    local burnTotal = SunExp_WunaGetAllBurnTotal(self)
    if burnTotal > 0 then
        SunExp_WunaAddEmber(self, burnTotal)
    end
    SunExp_SetVar(self, "SunExpWunaPrevEnemyBurn", tostring(SunExp_WunaGetEnemyBurnTotal(self)))
end

function SunExp_WunaInitCareer(self)
    if self == nil then
        return
    end
    SunExp_WunaSetSkillTime(SunExp_WunaWhiteSunPrayerCardId(), 0)
    SunExp_WunaSetSkillTime(SunExp_WunaGraveSongCardId(), 0)
    SunExp_SetVar(self, "SunExpWunaRadianceDone", "0")
    SunExp_SetVar(self, "SunExpWunaPrevEnemyBurn", "0")
    self:AddEvent("FightStart", function()
        SunExp_SetVar(self, "SunExpWunaRadianceDone", "0")
        SunExp_SetVar(self, "SunExpWunaPrevEnemyBurn", tostring(SunExp_WunaGetEnemyBurnTotal(self)))
    end)
    self:AddEvent("StartRound", function()
        SunExp_WunaStartRound(self)
    end)
    self:AddEvent("EndRound", function()
        SunExp_WunaHalveEmber(self)
    end)
    self:AddEvent("buff_burnOnLevelChange", function()
        SunExp_WunaTryGainRadianceFromEnemyBurn(self)
    end)
end

function SunExp_WunaTryAddCardToHand(self, cardId)
    if self == nil or cardId == nil then
        return false
    end
    local ok = pcall(function()
        self:AddCardById(cardId)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        self:AddCardByData(cardId, "")
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        self:AddCard(cardId)
    end)
    return ok
end

function SunExp_WunaUseWhiteSunPrayer(self)
    local key = SunExp_WunaWhiteSunPrayerCardId()
    local cooldown = SunExp_WunaGetSkillTime(key)
    if cooldown > 0 then
        SunExp_ShowCaption("白曜圣祷尚未冷却。")
        return false
    end
    self:SetStatus("Self")
    SunExp_WunaTryAddCardToHand(self, SunExp_WunaCoronationTokenCardId())
    SunExp_WunaSetSkillTime(key, 5)
    return true
end

function SunExp_WunaIsAlive(self)
    if self == nil or self.Self == nil then
        return false
    end
    local ok, hp = pcall(function()
        return self.Self.CurHp
    end)
    return ok and (tonumber(hp) or 0) > 0
end

function SunExp_WunaHealPercent(self, ratio)
    if self == nil or self.Self == nil then
        return 0
    end
    local ok, maxHp = pcall(function()
        return self.Self.MaxHp
    end)
    if not ok then
        return 0
    end
    local heal = math.max(1, math.floor((tonumber(maxHp) or 0) * (tonumber(ratio) or 0)))
    self:SetStatus("Self")
    self:ChangeHp(tostring(heal))
    return heal
end

function SunExp_WunaUseGraveSong(self)
    local key = SunExp_WunaGraveSongCardId()
    local cooldown = SunExp_WunaGetSkillTime(key)
    if cooldown > 0 then
        SunExp_ShowCaption("圣庭墓曲尚未冷却。")
        return false
    end
    local ember = SunExp_WunaGetEmberLevel(self)
    if ember <= 30 then
        SunExp_ShowCaption("余烬不足。")
        return false
    end
    local flame = SunExp_GetBuffLevel(self, "SunExp_sunexp_gathered_flame")
    self:SetStatus("Self")
    self:RemoveBuff(SunExp_WunaEmberBuffId())
    self:RemoveBuff("SunExp_sunexp_ember_cloak")
    if flame > 0 then
        self:AddBuff("buff_burn", tostring(flame))
        self:RunImmediately("buff_burn", "StartRound")
    end
    SunExp_WunaSetSkillTime(key, 4)
    if not SunExp_WunaIsAlive(self) then
        return true
    end
    SunExp_WunaHealPercent(self, 0.3)
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_ember_cloak", "1")
    if flame > 0 then
        self:SetStatus("All")
        self:AddBuff("buff_burn", tostring(flame))
        self:RunImmediately("buff_burn", "StartRound")
    end
    return true
end

function SunExp_ApplyWunaEmberDamageBonus(self, amount)
    local base = math.floor(tonumber(amount) or 0)
    if base <= 0 then
        return base
    end
    local ember = SunExp_WunaGetEmberLevel(self)
    if ember <= 0 then
        return base
    end
    return math.floor(base * (100 + ember) / 100)
end
