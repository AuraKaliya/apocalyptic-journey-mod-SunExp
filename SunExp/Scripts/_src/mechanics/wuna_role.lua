function SunExp_WunaEmberBuffId()
    return SunExp_EmberBuffId()
end

function SunExp_WunaPersistentEmberKey()
    return "SunExpWunaPersistentEmber"
end

function SunExp_WunaGetPersistentEmber()
    local value = tonumber(SunExp_PlayerGetVar(SunExp_WunaPersistentEmberKey(), "0")) or 0
    return math.max(0, math.min(99, math.floor(value)))
end

function SunExp_WunaSetPersistentEmber(value)
    local level = math.max(0, math.min(99, math.floor(tonumber(value) or 0)))
    SunExp_PlayerSetVar(SunExp_WunaPersistentEmberKey(), tostring(level))
    return level
end

function SunExp_WunaClearPersistentEmber()
    return SunExp_WunaSetPersistentEmber(0)
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
        SunExp_WunaSyncEmberDamageBonus(self)
        SunExp_WunaSetPersistentEmber(99)
        return 99
    end
    if level <= 0 then
        SunExp_WunaClearEmberDamageBonus(self)
        self:SetStatus("Self")
        self:RemoveBuff(SunExp_WunaEmberBuffId())
        SunExp_WunaClearPersistentEmber()
        return 0
    end
    SunExp_WunaSetPersistentEmber(level)
    return level
end

function SunExp_WunaGetEmberLevel(self)
    if self == nil or self.Self == nil then
        return 0
    end
    return SunExp_GetStatusBuffLevel(self.Self, SunExp_WunaEmberBuffId())
end

function SunExp_WunaSaveEmberFromBuff(self)
    return SunExp_WunaSetPersistentEmber(SunExp_WunaGetEmberLevel(self))
end

function SunExp_WunaRestorePersistentEmber(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local stored = SunExp_WunaGetPersistentEmber()
    self:SetStatus("Self")
    if SunExp_WunaGetEmberLevel(self) > 0 then
        self:RemoveBuff(SunExp_WunaEmberBuffId())
    end
    if stored > 0 then
        self:AddBuff(SunExp_WunaEmberBuffId(), tostring(stored))
    else
        SunExp_WunaClearEmberDamageBonus(self)
    end
    return stored
end

function SunExp_WunaGetAppliedEmberDamageBonus(self)
    return SunExp_GetEmberDamageBonusApplied(self ~= nil and self.Self or nil)
end

function SunExp_WunaSyncEmberDamageBonus(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local level = SunExp_SyncEmberDamageBonus(self, self.Self)
    SunExp_WunaSetPersistentEmber(level)
    return level
end

function SunExp_WunaClearEmberDamageBonus(self)
    if self == nil or self.Self == nil then
        return 0
    end
    return SunExp_ClearEmberDamageBonus(self, self.Self)
end

function SunExp_WunaApplyConsumedEmber(self, consumed)
    if self == nil or self.Self == nil then
        return 0
    end
    local count = math.floor(tonumber(consumed) or 0)
    if count <= 0 then
        return 0
    end
    local ok, maxHp = pcall(function()
        return self.Self.MaxHp
    end)
    if not ok then
        maxHp = 0
    end
    local heal = math.max(1, math.floor((tonumber(maxHp) or 0) * count / 100))
    self:SetStatus("Self")
    self:ChangeHp(tostring(heal))
    self:ChangeMaxHp(tostring(count))
    return heal
end

function SunExp_WunaRecoverFromRemovedEmber(self, removed)
    return SunExp_WunaApplyConsumedEmber(self, removed)
end

function SunExp_WunaAddEmber(self, amount)
    local gain = math.floor(tonumber(amount) or 0)
    if self == nil or gain <= 0 then
        return 0
    end
    self:SetStatus("Self")
    self:AddBuff(SunExp_WunaEmberBuffId(), tostring(gain))
    local level = SunExp_WunaClampEmber(self)
    SunExp_WunaSyncEmberDamageBonus(self)
    return level
end

function SunExp_WunaHalveEmber(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local ember = self.Self:GetBuff(SunExp_WunaEmberBuffId())
    if ember == nil or ember.buffConfig == nil then
        return 0
    end
    local current = math.floor(tonumber(ember.buffConfig.Level) or 0)
    local removed = math.ceil(current / 2)
    local nextLevel = current - removed
    self:SetStatus("Self")
    if nextLevel <= 0 then
        SunExp_WunaClearEmberDamageBonus(self)
        self:RemoveBuff(SunExp_WunaEmberBuffId())
        SunExp_WunaClearPersistentEmber()
        SunExp_OnEmberConsumed(self, self.Self, removed)
        return 0
    end
    ember.buffConfig.Level = nextLevel
    SunExp_WunaSyncEmberDamageBonus(self)
    SunExp_WunaSetPersistentEmber(nextLevel)
    SunExp_OnEmberConsumed(self, self.Self, removed)
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
    local emberGain = math.floor(SunExp_WunaGetAllBurnTotal(self) / 2)
    if emberGain > 0 then
        SunExp_WunaAddEmber(self, emberGain)
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
        SunExp_WunaRestorePersistentEmber(self)
        SunExp_SetVar(self, "SunExpWunaRadianceDone", "0")
        SunExp_SetVar(self, "SunExpWunaPrevEnemyBurn", tostring(SunExp_WunaGetEnemyBurnTotal(self)))
    end)
    self:AddEvent("StartRound", function()
        SunExp_WunaStartRound(self)
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

function SunExp_WunaTrimTag(tag)
    if tag == nil then
        return ""
    end
    return tostring(tag):gsub("^%s+", ""):gsub("%s+$", "")
end

function SunExp_WunaGetCardData(item)
    if item == nil then
        return nil
    end
    local ok, data = pcall(function()
        return item.data
    end)
    if ok and data ~= nil then
        return data
    end
    ok, data = pcall(function()
        if item.dataConfig ~= nil then
            return item.dataConfig.data
        end
        return nil
    end)
    if ok then
        return data
    end
    return nil
end

function SunExp_WunaGetCardVars(item)
    if item == nil then
        return nil
    end
    local ok, vars = pcall(function()
        return item.Vars
    end)
    if ok and vars ~= nil then
        return vars
    end
    ok, vars = pcall(function()
        if item.dataConfig ~= nil then
            return item.dataConfig.Vars
        end
        return nil
    end)
    if ok then
        return vars
    end
    return nil
end

function SunExp_WunaGetCardTagText(item)
    local data = SunExp_WunaGetCardData(item)
    if data == nil then
        return ""
    end
    local ok, value = pcall(function()
        if data.ContainsKey ~= nil and not data:ContainsKey("Tag") then
            return ""
        end
        return data:get_Item("Tag")
    end)
    if ok and value ~= nil then
        return tostring(value)
    end
    ok, value = pcall(function()
        return data["Tag"]
    end)
    if ok and value ~= nil then
        return tostring(value)
    end
    return ""
end

function SunExp_WunaGetCardSpecialTagText(item)
    local vars = SunExp_WunaGetCardVars(item)
    if vars == nil then
        return ""
    end
    local ok, value = pcall(function()
        if vars.ContainsKey ~= nil and not vars:ContainsKey("SpecialTag") then
            return ""
        end
        return vars:get_Item("SpecialTag")
    end)
    if ok and value ~= nil then
        return tostring(value)
    end
    ok, value = pcall(function()
        return vars["SpecialTag"]
    end)
    if ok and value ~= nil then
        return tostring(value)
    end
    return ""
end

function SunExp_WunaCardHasTag(item, tag)
    local needle = SunExp_WunaTrimTag(tag)
    if needle == "" then
        return true
    end
    for part in string.gmatch(SunExp_WunaGetCardTagText(item), "([^,]+)") do
        if SunExp_WunaTrimTag(part) == needle then
            return true
        end
    end
    for part in string.gmatch(SunExp_WunaGetCardSpecialTagText(item), "([^,]+)") do
        if SunExp_WunaTrimTag(part) == needle then
            return true
        end
    end
    return false
end

function SunExp_WunaSetCardSpecialTagText(item, text)
    local changed = false
    local function setVars(vars)
        if vars == nil then
            return
        end
        local ok = pcall(function()
            vars:set_Item("SpecialTag", text)
        end)
        if ok then
            changed = true
            return
        end
        pcall(function()
            vars["SpecialTag"] = text
            changed = true
        end)
    end
    setVars(SunExp_WunaGetCardVars(item))
    pcall(function()
        if item ~= nil and item.dataConfig ~= nil then
            setVars(item.dataConfig.Vars)
        end
    end)
    local refreshTagOk = false
    local dataUpdateOk = false
    local managerRefreshOk = false
    if changed then
        refreshTagOk = pcall(function()
            if item.RefreshTag ~= nil then
                item:RefreshTag()
            end
        end)
        dataUpdateOk = pcall(function()
            if item.DataUpdate ~= nil then
                item:DataUpdate()
            end
        end)
        managerRefreshOk = pcall(function()
            if CS ~= nil and CS.FightCardManager ~= nil and CS.FightCardManager.Instance ~= nil and item.dataConfig ~= nil then
                CS.FightCardManager.Instance:RefreshTag(item.dataConfig)
            end
        end)
    end
    SunExp_DebugWhiteRadianceLog(
        "SetSpecialTag changed=" .. tostring(changed)
        .. ", text=" .. tostring(text)
        .. ", refreshTag=" .. tostring(refreshTagOk)
        .. ", dataUpdate=" .. tostring(dataUpdateOk)
        .. ", managerRefresh=" .. tostring(managerRefreshOk)
        .. ", " .. SunExp_DebugCardLabel(item)
    )
    return changed
end

function SunExp_WunaSetCardTempWhiteRadiance(item)
    local vars = nil
    pcall(function()
        vars = item.Vars
    end)
    if vars == nil then
        SunExp_DebugWhiteRadianceLog("SetTempWhiteRadiance failed: vars=nil, " .. SunExp_DebugCardLabel(item))
        return false
    end
    local lockId = SunExp_CombatIntAdd("SunExpTempWhiteRadianceLockSeq", 1)
    local changed = false
    local ok = pcall(function()
        vars:set_Item("SunExpTempWhiteRadiance", "1")
        vars:set_Item("SunExpTempWhiteRadianceLockId", tostring(lockId))
        vars:set_Item("SunExpTempWhiteRadianceResolved", "0")
    end)
    if ok then
        changed = true
    else
        pcall(function()
            vars["SunExpTempWhiteRadiance"] = "1"
            vars["SunExpTempWhiteRadianceLockId"] = tostring(lockId)
            vars["SunExpTempWhiteRadianceResolved"] = "0"
            changed = true
        end)
    end
    SunExp_DebugWhiteRadianceLog(
        "SetTempWhiteRadiance changed=" .. tostring(changed)
        .. ", lockId=" .. tostring(lockId)
        .. ", " .. SunExp_DebugCardLabel(item)
    )
    return changed
end

function SunExp_WunaEnsureCardTag(item, tag)
    local nextTag = SunExp_WunaTrimTag(tag)
    if item == nil or nextTag == "" then
        SunExp_DebugWhiteRadianceLog("EnsureCardTag skipped: item/tag invalid, tag=" .. tostring(tag))
        return false
    end
    if SunExp_WunaCardHasTag(item, nextTag) then
        SunExp_DebugWhiteRadianceLog(
            "EnsureCardTag skipped: already has tag=" .. tostring(nextTag)
            .. ", " .. SunExp_DebugCardLabel(item)
        )
        return false
    end
    local text = SunExp_WunaGetCardSpecialTagText(item)
    if SunExp_WunaTrimTag(text) == "" then
        text = nextTag
    else
        text = text .. "," .. nextTag
    end
    pcall(function()
        if item.Tags ~= nil and item.Tags.Add ~= nil then
            item.Tags:Add(nextTag)
        end
    end)
    local changed = SunExp_WunaSetCardSpecialTagText(item, text)
    if changed and nextTag == "白曜" then
        SunExp_WunaSetCardTempWhiteRadiance(item)
    end
    SunExp_DebugWhiteRadianceLog(
        "EnsureCardTag tag=" .. tostring(nextTag)
        .. ", changed=" .. tostring(changed)
        .. ", " .. SunExp_DebugCardLabel(item)
    )
    return changed
end

function SunExp_WunaEnsureHandTags(self, tags)
    if self == nil or tags == nil then
        return 0
    end
    local hand = nil
    pcall(function()
        hand = self.HandCard
    end)
    local count = SunExp_GetCollectionCount(hand)
    local changed = 0
    for i = 0, count - 1 do
        local item = SunExp_GetCollectionItem(hand, i)
        for j = 1, #tags do
            if SunExp_WunaEnsureCardTag(item, tags[j]) then
                changed = changed + 1
            end
        end
    end
    SunExp_DebugWhiteRadianceLog(
        "EnsureHandTags handCount=" .. tostring(count)
        .. ", tagCount=" .. tostring(#tags)
        .. ", changed=" .. tostring(changed)
    )
    return changed
end

function SunExp_WunaUseWhiteSunPrayer(self)
    local key = SunExp_WunaWhiteSunPrayerCardId()
    local cooldown = SunExp_WunaGetSkillTime(key)
    SunExp_DebugWhiteRadianceLog("WhiteSunPrayer start cooldown=" .. tostring(cooldown))
    if cooldown > 0 then
        SunExp_DebugWhiteRadianceLog("WhiteSunPrayer blocked by cooldown=" .. tostring(cooldown))
        SunExp_ShowCaption("白曜圣祷尚未冷却。")
        return false
    end
    self:SetStatus("Self")
    local addedToken = SunExp_WunaTryAddCardToHand(self, SunExp_WunaCoronationTokenCardId())
    local changed = SunExp_WunaEnsureHandTags(self, {"Burnout", "白曜"})
    local cooldownSet = SunExp_WunaSetSkillTime(key, 5)
    SunExp_DebugWhiteRadianceLog(
        "WhiteSunPrayer finish addedToken=" .. tostring(addedToken)
        .. ", changed=" .. tostring(changed)
        .. ", cooldownSet=" .. tostring(cooldownSet)
    )
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
    local burn = math.floor(ember / 2)
    self:SetStatus("Self")
    SunExp_WunaClearEmberDamageBonus(self)
    self:RemoveBuff(SunExp_WunaEmberBuffId())
    SunExp_WunaClearPersistentEmber()
    SunExp_OnEmberConsumed(self, self.Self, ember)
    SunExp_WunaSetSkillTime(key, 4)
    if burn > 0 then
        self:SetStatus("All")
        self:AddBuff("buff_burn", tostring(burn))
    end
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_ember_cloak", "1")
    SunExp_TriggerBurnAll(self, 1)
    return true
end

function SunExp_ApplyWunaEmberDamageBonus(self, amount)
    local base = math.floor(tonumber(amount) or 0)
    return base
end
