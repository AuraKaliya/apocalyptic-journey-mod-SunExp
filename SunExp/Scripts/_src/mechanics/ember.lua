function SunExp_EmberBuffId()
    return "SunExp_sunexp_ember"
end

function SunExp_EmberDamageBonusKey(target)
    local id = "unknown"
    if target ~= nil and target.InstanceId ~= nil then
        id = tostring(target.InstanceId)
    end
    return "SunExpEmberDamageBonus_" .. id
end

function SunExp_GetEmberDamageBonusApplied(target)
    return SunExp_CombatIntGet(SunExp_EmberDamageBonusKey(target), 0)
end

function SunExp_SetEmberDamageBonusApplied(target, value)
    return SunExp_CombatIntSet(SunExp_EmberDamageBonusKey(target), value)
end

function SunExp_GetEmberLevel(target)
    return SunExp_GetStatusBuffLevel(target, SunExp_EmberBuffId())
end

function SunExp_SyncEmberDamageBonus(self, target)
    if self == nil then
        return 0
    end
    if target == nil then
        target = self.Self
    end
    if target == nil then
        return 0
    end
    local level = math.max(0, math.floor(tonumber(SunExp_GetEmberLevel(target)) or 0))
    local applied = SunExp_GetEmberDamageBonusApplied(target)
    local delta = level - applied
    if delta ~= 0 then
        SunExp_SetStatusForBuff(self, target, "Self")
        self:ChangeDynamicVarPercent("PercentDamage", tostring(delta))
        SunExp_SetEmberDamageBonusApplied(target, level)
    end
    return level
end

function SunExp_ClearEmberDamageBonus(self, target)
    if self == nil then
        return 0
    end
    if target == nil then
        target = self.Self
    end
    if target == nil then
        return 0
    end
    local applied = SunExp_GetEmberDamageBonusApplied(target)
    if applied ~= 0 then
        SunExp_SetStatusForBuff(self, target, "Self")
        self:ChangeDynamicVarPercent("PercentDamage", tostring(-applied))
        SunExp_SetEmberDamageBonusApplied(target, 0)
    end
    return applied
end

function SunExp_IsWunaSelfExecutor(self, target)
    return self ~= nil
        and target ~= nil
        and SunExp_IsSelfStatus(self, target)
        and SunExp_GetVar(self, "SunExpWunaRadianceDone", nil) ~= nil
end

function SunExp_OnEmberConsumed(self, target, consumed)
    local count = math.floor(tonumber(consumed) or 0)
    if count <= 0 then
        return 0
    end
    if SunExp_IsWunaSelfExecutor(self, target)
        and SunExp_WunaApplyConsumedEmber ~= nil
        and SunExp_WunaSetPersistentEmber ~= nil
        and SunExp_WunaGetEmberLevel ~= nil then
        SunExp_WunaApplyConsumedEmber(self, count)
        SunExp_WunaSetPersistentEmber(SunExp_WunaGetEmberLevel(self))
    end
    return count
end

function SunExp_ConsumeEmberBeforeBurnSettlement(self, target)
    if self == nil then
        return 0
    end
    if target == nil then
        target = self.Self
    end
    if target == nil then
        return 0
    end
    local ember = SunExp_GetStatusBuff(target, SunExp_EmberBuffId())
    local burn = SunExp_GetStatusBuff(target, "buff_burn")
    if ember == nil or burn == nil or ember.buffConfig == nil or burn.buffConfig == nil then
        return 0
    end
    local emberLevel = math.floor(tonumber(ember.buffConfig.Level) or 0)
    local burnLevel = math.floor(tonumber(burn.buffConfig.Level) or 0)
    local consumed = math.min(emberLevel, burnLevel)
    if consumed <= 0 then
        return 0
    end

    SunExp_SetStatusForBuff(self, target, "Self")

    local nextBurn = burnLevel - consumed
    if nextBurn <= 0 then
        SunExp_RemoveStatusBuff(self, target, "buff_burn", "Self")
    else
        burn.buffConfig.Level = nextBurn
    end

    local nextEmber = emberLevel - consumed
    if nextEmber <= 0 then
        SunExp_ClearEmberDamageBonus(self, target)
        SunExp_RemoveStatusBuff(self, target, SunExp_EmberBuffId(), "Self")
    else
        ember.buffConfig.Level = nextEmber
        SunExp_SyncEmberDamageBonus(self, target)
    end

    SunExp_OnEmberConsumed(self, target, consumed)
    return consumed
end

function SunExp_GetAllCombatTargets(self)
    local targets = {}
    local seen = {}
    local function add(target)
        if target == nil then
            return
        end
        local key = target
        if target.InstanceId ~= nil then
            key = tostring(target.InstanceId)
        end
        if seen[key] then
            return
        end
        seen[key] = true
        table.insert(targets, target)
    end
    add(self ~= nil and self.Self or nil)
    local enemies = SunExp_GetEnemyTargets(self)
    for i = 1, #enemies do
        add(enemies[i])
    end
    local friendlies = SunExp_GetFriendlyTargets(self, true)
    for i = 1, #friendlies do
        add(friendlies[i])
    end
    return targets
end

function SunExp_ConsumeEmberBeforeBurnSettlementForTargets(self, targets)
    if targets == nil then
        return 0
    end
    local consumed = 0
    for i = 1, #targets do
        consumed = consumed + SunExp_ConsumeEmberBeforeBurnSettlement(self, targets[i])
    end
    return consumed
end

function SunExp_ConsumeEmberBeforeBurnSettlementForStatus(self, status)
    if self == nil then
        return 0
    end
    if status == "Self" or status == nil then
        return SunExp_ConsumeEmberBeforeBurnSettlement(self, self.Self)
    end
    if status == "Target" then
        return SunExp_ConsumeEmberBeforeBurnSettlement(self, SunExp_GetPrimaryTarget(self))
    end
    if status == "AllTarget" then
        return SunExp_ConsumeEmberBeforeBurnSettlementForTargets(self, SunExp_GetEnemyTargets(self))
    end
    if status == "All" then
        return SunExp_ConsumeEmberBeforeBurnSettlementForTargets(self, SunExp_GetAllCombatTargets(self))
    end
    return 0
end

function SunExp_OnEmberApplied(self)
    SunExp_SyncEmberDamageBonus(self, self ~= nil and self.Self or nil)
    local token = SunExp_RegisterHook(self, "SunExpEmberHook", "SunExpEmberToken")
    if token == nil then
        return
    end
    local function sync()
        if not SunExp_IsHookTokenActive(self, "SunExpEmberToken", token) then
            return
        end
        SunExp_SyncEmberDamageBonus(self, self.Self)
    end
    self:AddEvent("SunExp_sunexp_emberOnLevelChange", sync)
    self:AddEvent("emberOnLevelChange", sync)
    self:AddEvent("StartRound", function()
        if not SunExp_IsHookTokenActive(self, "SunExpEmberToken", token) then
            return
        end
        SunExp_ConsumeEmberBeforeBurnSettlement(self, self.Self)
    end)
end

function SunExp_OnEmberCleared(self)
    SunExp_ClearEmberDamageBonus(self, self ~= nil and self.Self or nil)
    SunExp_ClearHook(self, "SunExpEmberHook", "SunExpEmberToken")
end
