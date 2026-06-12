SunExpSolarKeyword = "SunExpSolar"
SunExpBodyBurnBuffId = "SunExp_sunexp_body_burn"
SunExpBurnUpperBound = 49

function SunExp_FieldStateKey(name)
    return "SunExpField_" .. tostring(name)
end

function SunExp_FieldBuffId(fieldId)
    if fieldId == "scorching_canopy" then
        return "SunExp_sunexp_scorching_canopy"
    end
    return nil
end

function SunExp_GetActiveFieldId(self)
    return SunExp_GetVar(self, "SunExpActiveFieldId", "")
end

function SunExp_GetActiveFieldEpoch(self)
    return tonumber(SunExp_GetVar(self, "SunExpActiveFieldEpoch", "0")) or 0
end

function SunExp_SetActiveField(self, fieldId)
    local current = SunExp_GetActiveFieldId(self)
    if current == fieldId then
        return SunExp_GetActiveFieldEpoch(self)
    end
    local epoch = SunExp_GetActiveFieldEpoch(self) + 1
    SunExp_SetVar(self, "SunExpActiveFieldId", fieldId or "")
    SunExp_SetVar(self, "SunExpActiveFieldEpoch", epoch)
    SunExp_SetVar(self, "SunExpActiveFieldStacks", "0")
    return epoch
end

function SunExp_IsActiveField(self, fieldId, epoch, token)
    if self == nil or fieldId == nil then
        return false
    end
    if SunExp_GetActiveFieldId(self) ~= fieldId then
        return false
    end
    if epoch ~= nil and SunExp_GetActiveFieldEpoch(self) ~= tonumber(epoch) then
        return false
    end
    if token ~= nil and not SunExp_IsHookTokenActive(self, SunExp_FieldStateKey(fieldId) .. "Token", token) then
        return false
    end
    return true
end

function SunExp_SyncFieldStacks(self, fieldId)
    local buffId = SunExp_FieldBuffId(fieldId)
    if self == nil or self.Self == nil or buffId == nil then
        return 0
    end
    local level = SunExp_GetBuffLevel(self, buffId)
    if SunExp_GetActiveFieldId(self) == fieldId then
        SunExp_SetVar(self, "SunExpActiveFieldStacks", level)
    end
    return level
end

function SunExp_InternalClearFieldBuff(self, fieldId)
    local buffId = SunExp_FieldBuffId(fieldId)
    if self == nil or buffId == nil then
        return false
    end
    SunExp_SetVar(self, "SunExpFieldInternalClear", "1")
    self:SetStatus("Self")
    local ok = pcall(function()
        self:RemoveBuff(buffId)
    end)
    SunExp_SetVar(self, "SunExpFieldInternalClear", "0")
    return ok
end

function SunExp_ApplyFieldBuff(self, fieldId, amount)
    local count = math.floor(tonumber(amount) or 0)
    local buffId = SunExp_FieldBuffId(fieldId)
    if self == nil or buffId == nil or count <= 0 then
        return false
    end
    local active = SunExp_GetActiveFieldId(self)
    if active ~= "" and active ~= fieldId then
        SunExp_InternalClearFieldBuff(self, active)
    end
    SunExp_SetActiveField(self, fieldId)
    self:SetStatus("Self")
    self:AddBuff(buffId, tostring(count))
    SunExp_SyncFieldStacks(self, fieldId)
    return true
end

function SunExp_OnFieldBuffApplied(self, fieldId)
    if self == nil or fieldId == nil then
        return
    end
    if SunExp_GetActiveFieldId(self) == "" then
        SunExp_SetActiveField(self, fieldId)
    end
    local hookKey = SunExp_FieldStateKey(fieldId) .. "Hook"
    local tokenKey = SunExp_FieldStateKey(fieldId) .. "Token"
    local token = SunExp_RegisterHook(self, hookKey, tokenKey)
    SunExp_SyncFieldStacks(self, fieldId)
    if token == nil then
        return
    end
    local epoch = SunExp_GetActiveFieldEpoch(self)
    self:AddEvent("StartRound", function()
        if not SunExp_IsActiveField(self, fieldId, epoch, token) then
            return
        end
        SunExp_FieldStartRound(self, fieldId)
    end)
end

function SunExp_OnFieldBuffCleared(self, fieldId)
    if self == nil or fieldId == nil then
        return
    end
    local hookKey = SunExp_FieldStateKey(fieldId) .. "Hook"
    local tokenKey = SunExp_FieldStateKey(fieldId) .. "Token"
    local externalClear = SunExp_GetVar(self, "SunExpFieldInternalClear", "0") ~= "1"
    local wasActive = SunExp_GetActiveFieldId(self) == fieldId
    local stacks = tonumber(SunExp_GetVar(self, "SunExpActiveFieldStacks", "1")) or 1
    SunExp_ClearHook(self, hookKey, tokenKey)
    if not externalClear or not wasActive or stacks <= 0 then
        return
    end
    local buffId = SunExp_FieldBuffId(fieldId)
    if buffId == nil then
        return
    end
    self:SetStatus("Self")
    self:AddBuff(buffId, tostring(stacks))
end

function SunExp_FieldStartRound(self, fieldId)
    if fieldId ~= "scorching_canopy" then
        return false
    end
    local count = SunExp_SyncFieldStacks(self, fieldId)
    if count <= 0 then
        return false
    end
    self:SetStatus("All")
    self:AddBuff("buff_burn", tostring(count))
    SunExp_ClearSelfBurnIfProtected(self, true)
    return true
end

function SunExp_HandleSolarCardUsed(self, cost)
    if self == nil then
        return false
    end
    if SunExp_HasSolarCrown(self) then
        return SunExp_TriggerSolarCrown(self)
    end
    local gain = math.floor(tonumber(cost) or 0)
    if gain <= 0 then
        return false
    end
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_solar_radiance", tostring(gain))
    return true
end

function SunExp_GetDataValue(data, key)
    if data == nil or key == nil then
        return nil
    end
    local ok, value = pcall(function()
        if data.ContainsKey ~= nil and not data:ContainsKey(key) then
            return nil
        end
        return data:get_Item(key)
    end)
    if ok and value ~= nil then
        return value
    end
    ok, value = pcall(function()
        return data[key]
    end)
    if ok then
        return value
    end
    return nil
end

function SunExp_GetCardVar(card, key, defaultValue)
    if card == nil or key == nil then
        return defaultValue
    end
    local vars = card.Vars
    if vars == nil and card.dataConfig ~= nil then
        vars = card.dataConfig.Vars
    end
    if vars == nil then
        return defaultValue
    end
    local ok, value = pcall(function()
        if vars.ContainsKey ~= nil and not vars:ContainsKey(key) then
            return nil
        end
        return vars:get_Item(key)
    end)
    if ok and value ~= nil then
        return value
    end
    ok, value = pcall(function()
        return vars[key]
    end)
    if ok and value ~= nil then
        return value
    end
    return defaultValue
end

function SunExp_GetEnchCardCost(self)
    if self == nil then
        return 0
    end
    local ok, card = pcall(function()
        return self:EnchGetCard()
    end)
    if not ok or card == nil then
        ok, card = pcall(function()
            return EnchGetCard()
        end)
    end
    if not ok or card == nil then
        return 0
    end
    local data = card.data
    if data == nil and card.dataConfig ~= nil then
        data = card.dataConfig.data
    end
    local base = tonumber(SunExp_GetDataValue(data, "Expend")) or 0
    local exCost = tonumber(SunExp_GetCardVar(card, "ExCost", "0")) or 0
    local onceExCost = tonumber(SunExp_GetCardVar(card, "OnceExCost", "0")) or 0
    local totalExCost = tonumber(SunExp_GetCardVar(card, "TotalExCost", "0")) or 0
    local total = base + exCost + onceExCost + totalExCost
    if total < 0 then
        total = 0
    end
    return math.floor(total)
end

function SunExp_HandleSolarEnchCardUsed(self)
    return SunExp_HandleSolarCardUsed(self, SunExp_GetEnchCardCost(self))
end

function SunExp_CalcSolarKeywordBonusDamage(self, target, coefficientScale)
    local scale = tonumber(coefficientScale) or 1
    return math.floor(SunExp_CalcSolarCoefficient(self, target) * scale)
end

function SunExp_DealSolarKeywordBonusDamage(self, target, fallbackStatus, coefficientScale)
    local damage = SunExp_CalcSolarKeywordBonusDamage(self, target, coefficientScale)
    if damage <= 0 then
        return false
    end
    if target ~= nil then
        SunExp_SetStatusForBuff(self, target, fallbackStatus or "Target")
    elseif fallbackStatus ~= nil then
        self:SetStatus(fallbackStatus)
    end
    return SunExp_DealDamage(self, damage)
end

function SunExp_BodyBurnDamagePerStack(target)
    if target == nil then
        return 1
    end
    local ok, maxHp = pcall(function()
        return target.MaxHp
    end)
    if not ok or maxHp == nil then
        maxHp = 0
    end
    return math.floor((tonumber(maxHp) or 0) * 0.005) + 1
end

function SunExp_TriggerBodyBurn(self)
    local level = SunExp_GetBuffLevel(self, SunExpBodyBurnBuffId)
    if level <= 0 then
        return false
    end
    local damage = SunExp_BodyBurnDamagePerStack(self.Self) * level
    self:SetStatus("Self")
    if damage > 0 then
        self:Damage(tostring(damage), "True")
    end
    self:RemoveBuff(SunExpBodyBurnBuffId)
    return true
end

function SunExp_AddBodyBurn(self, target, amount)
    local count = math.floor(tonumber(amount) or 0)
    if count <= 0 then
        return false
    end
    return SunExp_AddStatusBuff(self, target, SunExpBodyBurnBuffId, tostring(count), "Target")
end

function SunExp_HandleBurnOverflow(self, target, amount)
    if self == nil or target == nil or not SunExp_IsActiveField(self, "scorching_canopy") then
        return false
    end
    if SunExp_IsSelfStatus(self, target) and SunExp_IsSelfBurnProtected(self, true) then
        return false
    end
    local add = math.floor(tonumber(amount) or 0)
    if add <= 0 then
        return false
    end
    local current = SunExp_GetStatusBuffLevel(target, "buff_burn")
    local overflow = current + add - SunExpBurnUpperBound
    if overflow <= 0 then
        return false
    end
    return SunExp_AddBodyBurn(self, target, overflow)
end

function SunExp_GetHookArg(args, index)
    if args == nil then
        return nil
    end
    local value = SunExp_GetCollectionItem(args, index - 1)
    if value ~= nil then
        return value
    end
    local ok
    ok, value = pcall(function()
        return args[index]
    end)
    if ok then
        return value
    end
    return nil
end

function SunExp_NormalizeHookBuffId(value)
    if value == nil then
        return nil
    end
    local text = tostring(value)
    local prefix = "DataId."
    if string.sub(text, 1, string.len(prefix)) == prefix then
        text = string.sub(text, string.len(prefix) + 1)
    end
    return text
end

function SunExp_GetHookExecutor(context)
    local target = SunExp_TryGetHookTarget(context)
    if target ~= nil and target.Self ~= nil then
        return target
    end
    if context ~= nil and context.Self ~= nil then
        return context
    end
    return nil
end

function SunExp_GetHookTargets(executor)
    local targets = {}
    if executor == nil then
        return targets
    end
    local seen = {}
    local function addTarget(target)
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
    local object = executor.Object
    local count = SunExp_GetCollectionCount(object)
    for i = 0, count - 1 do
        addTarget(SunExp_GetCollectionItem(object, i))
    end
    if #targets == 0 then
        addTarget(executor.Target)
    end
    if #targets == 0 then
        addTarget(executor.Self)
    end
    return targets
end

function SunExp_OnScriptExecutorAddBuffBefore(...)
    local contexts = {...}
    local handled = {}
    for i = 1, #contexts do
        local context = contexts[i]
        local executor = SunExp_GetHookExecutor(context)
        if executor ~= nil then
            local args = SunExp_TryGetHookArguments(context)
            local buffId = SunExp_NormalizeHookBuffId(SunExp_GetHookArg(args, 1))
            if buffId == "buff_burn" then
                local amount = tonumber(SunExp_GetHookArg(args, 2)) or 0
                local targets = SunExp_GetHookTargets(executor)
                for j = 1, #targets do
                    local target = targets[j]
                    local key = target
                    if target ~= nil and target.InstanceId ~= nil then
                        key = tostring(target.InstanceId)
                    end
                    if key ~= nil and not handled[key] then
                        handled[key] = true
                        SunExp_HandleBurnOverflow(executor, target, amount)
                    end
                end
            end
        end
    end
end
