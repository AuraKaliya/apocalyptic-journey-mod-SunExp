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
    if buffId == "buff_burn" and (eventName == nil or eventName == "StartRound") then
        if target ~= nil then
            SunExp_ConsumeEmberBeforeBurnSettlement(self, target)
        else
            SunExp_ConsumeEmberBeforeBurnSettlementForStatus(self, fallbackStatus)
        end
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
    if self == nil then
        return 0
    end
    local count = tonumber(times) or 1
    if count < 1 then
        count = 1
    end
    local triggered = 0
    for n = 1, count do
        SunExp_ConsumeEmberBeforeBurnSettlementForStatus(self, "AllTarget")
        self:SetStatus("AllTarget")
        local ok = pcall(function()
            self:RunImmediately("buff_burn", "StartRound")
        end)
        if ok then
            triggered = triggered + 1
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

function SunExp_GetFriendlyTargets(self, includeSelf)
    local targets = {}
    if self == nil then
        return targets
    end
    local enemies = SunExp_GetEnemyTargets(self)
    local enemyIds = {}
    for i = 1, #enemies do
        local enemy = enemies[i]
        if enemy ~= nil and enemy.InstanceId ~= nil then
            enemyIds[tostring(enemy.InstanceId)] = true
        end
    end
    pcall(function()
        self:SetStatus("All")
    end)
    local seen = {}
    local function addTarget(target)
        if target == nil or target.InstanceId == nil then
            return
        end
        local id = tostring(target.InstanceId)
        if seen[id] or enemyIds[id] then
            return
        end
        if not includeSelf and SunExp_IsSelfStatus(self, target) then
            return
        end
        seen[id] = true
        table.insert(targets, target)
    end
    if self.Object ~= nil then
        local count = SunExp_GetCollectionCount(self.Object)
        for i = 0, count - 1 do
            addTarget(SunExp_GetCollectionItem(self.Object, i))
        end
    end
    if includeSelf and self.Self ~= nil then
        addTarget(self.Self)
    end
    return targets
end

function SunExp_GetRandomFriendlyTarget(self, includeSelf)
    local targets = SunExp_GetFriendlyTargets(self, includeSelf)
    if #targets == 0 and includeSelf and self ~= nil then
        return self.Self
    end
    if #targets == 0 then
        return nil
    end
    return targets[math.random(1, #targets)]
end

function SunExp_TransferSelfBurnToRandomFriendly(self)
    if self == nil or self.Self == nil then
        return 0
    end
    local burn = SunExp_GetStatusBuffLevel(self.Self, "buff_burn")
    if burn <= 0 then
        return 0
    end
    local target = SunExp_GetRandomFriendlyTarget(self, true)
    if target == nil then
        target = self.Self
    end
    SunExp_RemoveStatusBuff(self, self.Self, "buff_burn", "Self")
    SunExp_AddStatusBuff(self, target, "buff_burn", tostring(burn), "Self")
    return burn
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
