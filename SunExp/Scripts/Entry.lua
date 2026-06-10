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

function SunExp_GetEnemyTargets(self)
    local targets = {}
    if self == nil then
        return targets
    end
    pcall(function()
        self:SetStatus("AllTarget")
    end)
    if self.Object == nil then
        return targets
    end
    local count = SunExp_GetCollectionCount(self.Object)
    for i = 0, count - 1 do
        local target = SunExp_GetCollectionItem(self.Object, i)
        if target ~= nil and not SunExp_IsSelfStatus(self, target) then
            table.insert(targets, target)
        end
    end
    return targets
end

function SunExp_IsSelfStatus(self, target)
    return self ~= nil and self.Self ~= nil and target ~= nil and target.InstanceId ~= nil and self.Self.InstanceId == target.InstanceId
end

function SunExp_GetPrimaryTarget(self)
    if self == nil then
        return nil
    end
    if self.Target ~= nil and not SunExp_IsSelfStatus(self, self.Target) then
        return self.Target
    end
    pcall(function()
        self:SetStatus("Target")
    end)
    if self.Object == nil or self.Object.Count == nil or self.Object.Count <= 0 then
        return nil
    end
    local ok, target = pcall(function()
        return self.Object:get_Item(0)
    end)
    if ok and target ~= nil and not SunExp_IsSelfStatus(self, target) then
        return target
    end
    return nil
end

function SunExp_SetPrimaryTarget(self, target)
    if self == nil or target == nil then
        return false
    end
    if target.InstanceId ~= nil then
        local ok = pcall(function()
            self:SetStatusById(target.InstanceId)
        end)
        if ok then
            return true
        end
    end
    pcall(function()
        self:SetStatus("Target")
    end)
    return false
end

function SunExp_SetStatusForBuff(self, target, fallbackStatus)
    if self == nil then
        return false
    end
    if target ~= nil then
        if SunExp_IsSelfStatus(self, target) then
            self:SetStatus("Self")
            return true
        end
        return SunExp_SetPrimaryTarget(self, target)
    end
    if fallbackStatus ~= nil then
        self:SetStatus(fallbackStatus)
        return true
    end
    return false
end

function SunExp_GetStatusBuff(target, buffId)
    if target == nil or buffId == nil then
        return nil
    end
    local ok, buff = pcall(function()
        return target:GetBuff(buffId)
    end)
    if ok then
        return buff
    end
    return nil
end

function SunExp_GetStatusBuffLevel(target, buffId)
    local buff = SunExp_GetStatusBuff(target, buffId)
    if buff == nil or buff.buffConfig == nil or buff.buffConfig.Level == nil then
        return 0
    end
    return tonumber(buff.buffConfig.Level) or 0
end

function SunExp_RemoveStatusBuff(self, target, buffId, fallbackStatus)
    if self == nil or buffId == nil then
        return false
    end
    SunExp_SetStatusForBuff(self, target, fallbackStatus)
    local ok = pcall(function()
        self:RemoveBuff(buffId)
    end)
    if ok then
        return true
    end
    if target ~= nil then
        ok = pcall(function()
            target:RemoveBuff(buffId)
        end)
        return ok
    end
    return false
end

function SunExp_SetStatusBuffLevel(self, target, buffId, level)
    local nextLevel = tonumber(level) or 0
    if target == nil or buffId == nil then
        return false
    end
    if nextLevel <= 0 then
        return SunExp_RemoveStatusBuff(self, target, buffId)
    end
    local buff = SunExp_GetStatusBuff(target, buffId)
    if buff == nil or buff.buffConfig == nil then
        return false
    end
    buff.buffConfig.Level = nextLevel
    return true
end

function SunExp_AddStatusBuff(self, target, buffId, amount, fallbackStatus)
    if self == nil or buffId == nil or amount == nil then
        return false
    end
    local before = SunExp_GetStatusBuffLevel(target, buffId)
    if not SunExp_SetStatusForBuff(self, target, fallbackStatus) then
        return false
    end
    local ok = pcall(function()
        self:AddBuff(buffId, tostring(amount))
    end)
    if target == nil then
        return ok
    end
    local after = SunExp_GetStatusBuffLevel(target, buffId)
    if after > before then
        return true
    end
    ok = pcall(function()
        target:AddBuff(buffId, tonumber(amount) or amount)
    end)
    after = SunExp_GetStatusBuffLevel(target, buffId)
    if ok and after > before then
        return true
    end
    ok = pcall(function()
        target:AddBuff(buffId, tostring(amount))
    end)
    return ok and SunExp_GetStatusBuffLevel(target, buffId) > before
end

function SunExp_TriggerStatusBuff(self, target, buffId, eventName, fallbackStatus)
    if self == nil or buffId == nil then
        return false
    end
    if target ~= nil and SunExp_GetStatusBuffLevel(target, buffId) <= 0 then
        return false
    end
    if not SunExp_SetStatusForBuff(self, target, fallbackStatus) then
        return false
    end
    local ok = pcall(function()
        self:RunImmediately(buffId, eventName or "StartRound")
    end)
    return ok
end

function SunExp_TriggerBurn(self, target, fallbackStatus)
    return SunExp_TriggerStatusBuff(self, target, "buff_burn", "StartRound", fallbackStatus)
end

function SunExp_TriggerBurnAllEnemies(self, times)
    local targets = SunExp_GetEnemyTargets(self)
    local count = tonumber(times) or 1
    if count < 1 then
        count = 1
    end
    local triggered = 0
    for n = 1, count do
        for i = 1, #targets do
            if SunExp_TriggerBurn(self, targets[i]) then
                triggered = triggered + 1
            end
        end
    end
    return triggered
end

function SunExp_GetRandomEnemyTarget(self, requireBurn)
    local targets = SunExp_GetEnemyTargets(self)
    local candidates = {}
    for i = 1, #targets do
        local target = targets[i]
        if not requireBurn or SunExp_GetStatusBuffLevel(target, "buff_burn") > 0 then
            table.insert(candidates, target)
        end
    end
    if #candidates == 0 then
        return nil
    end
    return candidates[math.random(1, #candidates)]
end

function SunExp_AddBurnToRandomEnemy(self, amount)
    local target = SunExp_GetRandomEnemyTarget(self, false)
    if target == nil then
        return false
    end
    return SunExp_AddStatusBuff(self, target, "buff_burn", amount)
end

function SunExp_RemoveBuffStacks(selfOrTarget, targetOrBuffId, buffIdOrAmount, amountOrNil)
    local executor = nil
    local target = selfOrTarget
    local buffId = targetOrBuffId
    local amount = buffIdOrAmount
    if amountOrNil ~= nil then
        executor = selfOrTarget
        target = targetOrBuffId
        buffId = buffIdOrAmount
        amount = amountOrNil
    end
    local count = tonumber(amount) or 0
    if target == nil or buffId == nil or count <= 0 then
        return 0
    end
    local buff = SunExp_GetStatusBuff(target, buffId)
    if buff == nil or buff.buffConfig == nil then
        return 0
    end
    local level = tonumber(buff.buffConfig.Level) or 0
    local removed = math.min(level, count)
    if removed <= 0 then
        return 0
    end
    local nextLevel = level - removed
    if nextLevel <= 0 then
        if executor ~= nil then
            SunExp_RemoveStatusBuff(executor, target, buffId)
        else
            pcall(function()
                target:RemoveBuff(buffId)
            end)
        end
    else
        buff.buffConfig.Level = nextLevel
    end
    return removed
end

function SunExp_GetCollectionCount(collection)
    if collection == nil then
        return 0
    end
    local ok, count = pcall(function()
        return collection.Count
    end)
    if ok and count ~= nil then
        return tonumber(count) or 0
    end
    ok, count = pcall(function()
        return collection:Count()
    end)
    if ok and count ~= nil then
        return tonumber(count) or 0
    end
    ok, count = pcall(function()
        return collection.Length
    end)
    if ok and count ~= nil then
        return tonumber(count) or 0
    end
    ok, count = pcall(function()
        return #collection
    end)
    if ok and count ~= nil then
        return tonumber(count) or 0
    end
    return 0
end

function SunExp_GetCollectionItem(collection, index)
    if collection == nil then
        return nil
    end
    local ok, item = pcall(function()
        return collection:get_Item(index)
    end)
    if ok and item ~= nil then
        return item
    end
    ok, item = pcall(function()
        return collection[index]
    end)
    if ok and item ~= nil then
        return item
    end
    ok, item = pcall(function()
        return collection[index + 1]
    end)
    if ok and item ~= nil then
        return item
    end
    return nil
end

function SunExp_GetBuffTypeName(buff)
    if buff == nil or buff.buffConfig == nil then
        return nil
    end
    local ok, typeName = pcall(function()
        return buff.buffConfig.Type
    end)
    if ok and typeName ~= nil then
        return tostring(typeName)
    end
    if buff.buffConfig.dataConfig == nil or buff.buffConfig.dataConfig.data == nil then
        return nil
    end
    ok, typeName = pcall(function()
        local data = buff.buffConfig.dataConfig.data
        if data.ContainsKey ~= nil and not data:ContainsKey("Type") then
            return nil
        end
        return data:get_Item("Type")
    end)
    if ok and typeName ~= nil then
        return tostring(typeName)
    end
    return nil
end

SunExp_PositiveBuffExcludeIds = {
    solar_radiance = true,
    gathered_flame = true,
    scorching_canopy = true,
    ember_cloak = true,
    solar_crown = true,
    origin_core_radiance = true,
    cycle_gathered_flame = true,
    afterglow_omen = true
}

function SunExp_NormalizeBuffId(buffId)
    if buffId == nil then
        return nil
    end
    local id = tostring(buffId)
    local prefix = "SunExp_sunexp_"
    if string.sub(id, 1, string.len(prefix)) == prefix then
        return string.sub(id, string.len(prefix) + 1)
    end
    return id
end

function SunExp_IsPositiveBuffExcludedId(buffId)
    local id = SunExp_NormalizeBuffId(buffId)
    return id ~= nil and SunExp_PositiveBuffExcludeIds[id] == true
end

function SunExp_IsPositiveBuffExcludedItem(buff)
    return SunExp_IsPositiveBuffExcludedId(SunExp_GetBuffIdFromItem(buff))
end

function SunExp_IsNegativeBuffItem(buff)
    if SunExp_IsPositiveBuffExcludedItem(buff) then
        return false
    end
    local typeName = SunExp_GetBuffTypeName(buff)
    if typeName == nil then
        return false
    end
    return typeName == "负面" or typeName == "Negative" or string.find(typeName, "负面", 1, true) ~= nil
end

function SunExp_GetBuffIdFromItem(buff)
    if buff == nil or buff.buffConfig == nil then
        return nil
    end
    local ok, buffId = pcall(function()
        return buff.buffConfig.BuffId
    end)
    if ok and buffId ~= nil then
        return buffId
    end
    if buff.buffConfig.dataConfig == nil or buff.buffConfig.dataConfig.data == nil then
        return nil
    end
    ok, buffId = pcall(function()
        local data = buff.buffConfig.dataConfig.data
        if data.ContainsKey ~= nil and not data:ContainsKey("Id") then
            return nil
        end
        return data:get_Item("Id")
    end)
    if ok then
        return buffId
    end
    return nil
end

function SunExp_GetNegativeBuffSummary(target)
    local ids = {}
    local total = 0
    if target == nil then
        return ids, total
    end
    local ok, buffs = pcall(function()
        return target:GetBuffs()
    end)
    if not ok or buffs == nil then
        return ids, total
    end
    local count = SunExp_GetCollectionCount(buffs)
    for i = 0, count - 1 do
        local buff = SunExp_GetCollectionItem(buffs, i)
        if SunExp_IsNegativeBuffItem(buff) then
            local id = SunExp_GetBuffIdFromItem(buff)
            if id ~= nil then
                if buff.buffConfig ~= nil then
                    total = total + (tonumber(buff.buffConfig.Level) or 0)
                end
                table.insert(ids, id)
            end
        end
    end
    return ids, total
end

function SunExp_GetNegativeBuffTotal(target)
    local ids, total = SunExp_GetNegativeBuffSummary(target)
    return tonumber(total) or 0
end

function SunExp_RemoveAllNegativeBuffs(self, target)
    if self == nil then
        return false
    end
    local ids = SunExp_GetNegativeBuffSummary(target)
    for _, id in ipairs(ids) do
        SunExp_RemoveStatusBuff(self, target or self.Self, id)
    end
    return #ids > 0
end

function SunExp_HasNegativeBuff(target)
    if target == nil then
        return false
    end
    local ok, buffs = pcall(function()
        return target:GetBuffs()
    end)
    if not ok or buffs == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(buffs)
    for i = 0, count - 1 do
        if SunExp_IsNegativeBuffItem(SunExp_GetCollectionItem(buffs, i)) then
            return true
        end
    end
    return false
end

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

function SunExp_RegisterDynamicMethod(config, name, fn)
    if config == nil or fn == nil then
        return
    end
    config:AddDynamicMethod(name, fn)
end

function SunExp_RegisterDynamicMethods(config)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetVar", SunExp_GetVar)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetVar", SunExp_SetVar)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetBuffLevel", SunExp_GetBuffLevel)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetRadianceLevel", SunExp_GetRadianceLevel)
    SunExp_RegisterDynamicMethod(config, "SunExp_DealDamage", SunExp_DealDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_AddDamageDescription", SunExp_AddDamageDescription)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcSparkDamage", SunExp_CalcSparkDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcFlareCutDamage", SunExp_CalcFlareCutDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcSolarSparkDamage", SunExp_CalcSolarSparkDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcCrownPressureDamage", SunExp_CalcCrownPressureDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcCrownCoreFlashDamage", SunExp_CalcCrownCoreFlashDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcFlamePierceDamage", SunExp_CalcFlamePierceDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_CalcSmokeErosionDamage", SunExp_CalcSmokeErosionDamage)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetEnemyTargets", SunExp_GetEnemyTargets)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsSelfStatus", SunExp_IsSelfStatus)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetPrimaryTarget", SunExp_GetPrimaryTarget)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetPrimaryTarget", SunExp_SetPrimaryTarget)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetStatusForBuff", SunExp_SetStatusForBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetStatusBuff", SunExp_GetStatusBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetStatusBuffLevel", SunExp_GetStatusBuffLevel)
    SunExp_RegisterDynamicMethod(config, "SunExp_RemoveStatusBuff", SunExp_RemoveStatusBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetStatusBuffLevel", SunExp_SetStatusBuffLevel)
    SunExp_RegisterDynamicMethod(config, "SunExp_AddStatusBuff", SunExp_AddStatusBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_TriggerStatusBuff", SunExp_TriggerStatusBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_TriggerBurn", SunExp_TriggerBurn)
    SunExp_RegisterDynamicMethod(config, "SunExp_TriggerBurnAllEnemies", SunExp_TriggerBurnAllEnemies)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetRandomEnemyTarget", SunExp_GetRandomEnemyTarget)
    SunExp_RegisterDynamicMethod(config, "SunExp_AddBurnToRandomEnemy", SunExp_AddBurnToRandomEnemy)
    SunExp_RegisterDynamicMethod(config, "SunExp_RemoveBuffStacks", SunExp_RemoveBuffStacks)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetCollectionCount", SunExp_GetCollectionCount)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetCollectionItem", SunExp_GetCollectionItem)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetBuffTypeName", SunExp_GetBuffTypeName)
    SunExp_RegisterDynamicMethod(config, "SunExp_NormalizeBuffId", SunExp_NormalizeBuffId)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsPositiveBuffExcludedId", SunExp_IsPositiveBuffExcludedId)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsPositiveBuffExcludedItem", SunExp_IsPositiveBuffExcludedItem)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsNegativeBuffItem", SunExp_IsNegativeBuffItem)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetBuffIdFromItem", SunExp_GetBuffIdFromItem)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetNegativeBuffSummary", SunExp_GetNegativeBuffSummary)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetNegativeBuffTotal", SunExp_GetNegativeBuffTotal)
    SunExp_RegisterDynamicMethod(config, "SunExp_RemoveAllNegativeBuffs", SunExp_RemoveAllNegativeBuffs)
    SunExp_RegisterDynamicMethod(config, "SunExp_HasNegativeBuff", SunExp_HasNegativeBuff)
    SunExp_RegisterDynamicMethod(config, "SunExp_HasCrownPhase", SunExp_HasCrownPhase)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsBurnWardPending", SunExp_IsBurnWardPending)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetBurnWardPending", SunExp_SetBurnWardPending)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsSelfBurnProtected", SunExp_IsSelfBurnProtected)
    SunExp_RegisterDynamicMethod(config, "SunExp_ClearSelfBurnIfProtected", SunExp_ClearSelfBurnIfProtected)
    SunExp_RegisterDynamicMethod(config, "SunExp_ApplySelfBurn", SunExp_ApplySelfBurn)
    SunExp_RegisterDynamicMethod(config, "SunExp_RegisterHook", SunExp_RegisterHook)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsHookTokenActive", SunExp_IsHookTokenActive)
    SunExp_RegisterDynamicMethod(config, "SunExp_ClearHook", SunExp_ClearHook)
    SunExp_RegisterDynamicMethod(config, "SunExp_FlamewheelKey", SunExp_FlamewheelKey)
    SunExp_RegisterDynamicMethod(config, "SunExp_GetFlamewheelUsed", SunExp_GetFlamewheelUsed)
    SunExp_RegisterDynamicMethod(config, "SunExp_SetFlamewheelUsed", SunExp_SetFlamewheelUsed)
    SunExp_RegisterDynamicMethod(config, "SunExp_UpdateFlamewheelCost", SunExp_UpdateFlamewheelCost)
    SunExp_RegisterDynamicMethod(config, "SunExp_IsFlamewheelCardItem", SunExp_IsFlamewheelCardItem)
    SunExp_RegisterDynamicMethod(config, "SunExp_RefreshFlamewheelCosts", SunExp_RefreshFlamewheelCosts)
end

function ModConfig:Setup()
    SunExp_RegisterDynamicMethods(self)
end
