SunExpSolarKeyword = "白曜"
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

function SunExp_FieldCombatKey(fieldId, name)
    return "SunExpField_" .. tostring(fieldId) .. "_" .. tostring(name)
end

function SunExp_SetSharedFieldState(fieldId, stacks)
    local count = math.max(0, math.floor(tonumber(stacks) or 0))
    SunExp_CombatIntSet(SunExp_FieldCombatKey(fieldId, "Active"), count > 0 and 1 or 0)
    SunExp_CombatIntSet(SunExp_FieldCombatKey(fieldId, "Stacks"), count)
    if count <= 0 then
        SunExp_CombatIntSet(SunExp_FieldCombatKey(fieldId, "TriggerLock"), 0)
    end
end

function SunExp_ClearSharedFieldState(fieldId)
    SunExp_SetSharedFieldState(fieldId, 0)
end

function SunExp_IsSharedFieldActive(fieldId)
    return SunExp_CombatIntGet(SunExp_FieldCombatKey(fieldId, "Active"), 0) == 1
        and SunExp_CombatIntGet(SunExp_FieldCombatKey(fieldId, "Stacks"), 0) > 0
end

function SunExp_BeginSharedFieldStartRound(self, fieldId)
    local lockKey = SunExp_FieldCombatKey(fieldId, "TriggerLock")
    if SunExp_CombatIntGet(lockKey, 0) == 1 then
        return false
    end
    SunExp_CombatIntSet(lockKey, 1)
    local ok = false
    if self ~= nil then
        ok = pcall(function()
            self:AddTempEvent("StartRoundEnd", function()
                SunExp_CombatIntSet(lockKey, 0)
            end)
        end)
    end
    if not ok then
        SunExp_CombatIntSet(lockKey, 0)
    end
    return true
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
    local localActive = SunExp_GetActiveFieldId(self) == fieldId
    local sharedActive = SunExp_IsSharedFieldActive(fieldId)
    if epoch == nil and token == nil and sharedActive then
        return true
    end
    if epoch == nil and token == nil and localActive then
        return SunExp_SyncFieldStacks(self, fieldId) > 0
    end
    if not localActive then
        return false
    end
    if epoch ~= nil and SunExp_GetActiveFieldEpoch(self) ~= tonumber(epoch) then
        return false
    end
    if token ~= nil and not SunExp_IsHookTokenActive(self, SunExp_FieldStateKey(fieldId) .. "Token", token) then
        return false
    end
    if epoch ~= nil or token ~= nil then
        return sharedActive
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
        SunExp_SetSharedFieldState(fieldId, level)
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
    if not externalClear or stacks <= 0 then
        if wasActive then
            SunExp_ClearSharedFieldState(fieldId)
        end
        return
    end
    if not wasActive then
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
        count = SunExp_CombatIntGet(SunExp_FieldCombatKey(fieldId, "Stacks"), 0)
    end
    if count <= 0 then
        return false
    end
    if not SunExp_BeginSharedFieldStartRound(self, fieldId) then
        return false
    end
    self:SetStatus("All")
    self:AddBuff("buff_burn", tostring(count))
    SunExp_ClearSelfBurnIfProtected(self, true)
    return true
end

function SunExp_HandleSolarCardUsed(self, cost)
    if self == nil then
        SunExp_DebugWhiteRadianceLog("HandleSolarCardUsed skipped: self=nil, cost=" .. tostring(cost))
        return false
    end
    local gain = math.floor(tonumber(cost) or 0)
    local hasCrown = SunExp_HasSolarCrown(self)
    local beforeRadiance = SunExp_GetRadianceLevel(self)
    SunExp_DebugWhiteRadianceLog(
        "HandleSolarCardUsed enter cost=" .. tostring(cost)
        .. ", gain=" .. tostring(gain)
        .. ", hasCrown=" .. tostring(hasCrown)
        .. ", radianceBefore=" .. tostring(beforeRadiance)
        .. ", " .. SunExp_DebugCardLabel(self)
    )
    if SunExp_HasSolarCrown(self) then
        local triggered = SunExp_TriggerSolarCrown(self)
        SunExp_DebugWhiteRadianceLog(
            "HandleSolarCardUsed crown triggered=" .. tostring(triggered)
            .. ", radianceAfter=" .. tostring(SunExp_GetRadianceLevel(self))
        )
        return triggered
    end
    if gain <= 0 then
        SunExp_DebugWhiteRadianceLog("HandleSolarCardUsed skipped: gain<=0")
        return false
    end
    self:SetStatus("Self")
    self:AddBuff("SunExp_sunexp_solar_radiance", tostring(gain))
    SunExp_DebugWhiteRadianceLog(
        "HandleSolarCardUsed radiance added=" .. tostring(gain)
        .. ", radianceAfter=" .. tostring(SunExp_GetRadianceLevel(self))
    )
    return true
end

function SunExp_GetCardItemScriptExecutor(card)
    if card == nil then
        return nil
    end
    local ok, executor = pcall(function()
        return card.scriptExecutor
    end)
    if ok and executor ~= nil then
        return executor
    end
    ok, executor = pcall(function()
        if card.dataConfig ~= nil then
            return card.dataConfig.scriptExecutor
        end
        return nil
    end)
    if ok then
        return executor
    end
    return nil
end

function SunExp_GetCardItemData(card)
    if card == nil then
        return nil
    end
    local ok, data = pcall(function()
        return card.data
    end)
    if ok and data ~= nil then
        return data
    end
    ok, data = pcall(function()
        if card.dataConfig ~= nil then
            return card.dataConfig.data
        end
        return nil
    end)
    if ok then
        return data
    end
    return nil
end

function SunExp_GetActionDataConfig(actionData)
    if actionData == nil then
        return nil
    end
    local fields = {"dataConfig", "DataConfig", "Data", "data", "Config", "config", "Source", "source"}
    for i = 1, #fields do
        local value = SunExp_TryGetHookField(actionData, fields[i])
        if value ~= nil then
            return value
        end
    end
    local ok, value = pcall(function()
        return actionData:get_Item(1)
    end)
    if ok and value ~= nil then
        return value
    end
    return nil
end

function SunExp_DataConfigHasTemporaryWhiteRadiance(dataConfig)
    if dataConfig == nil then
        return false
    end
    local vars = nil
    pcall(function()
        vars = dataConfig.Vars
    end)
    if vars == nil then
        return false
    end
    local value = SunExp_DebugDictValue(vars, "SunExpTempWhiteRadiance")
    if tostring(value or "0") ~= "1" then
        return false
    end
    local specialTag = tostring(SunExp_DebugDictValue(vars, "SpecialTag") or "")
    for part in string.gmatch(specialTag, "([^,]+)") do
        if SunExp_WunaTrimTag(part) == SunExpSolarKeyword then
            return true
        end
    end
    return false
end

function SunExp_DataConfigHasNativeWhiteRadiance(dataConfig)
    if dataConfig == nil then
        return false
    end
    local data = nil
    pcall(function()
        data = dataConfig.data
    end)
    local tag = tostring(SunExp_DebugDictValue(data, "Tag") or "")
    for part in string.gmatch(tag, "([^,]+)") do
        if SunExp_WunaTrimTag(part) == SunExpSolarKeyword then
            return true
        end
    end
    return false
end

function SunExp_DataConfigCost(dataConfig)
    if dataConfig == nil then
        return 0
    end
    local data = nil
    pcall(function()
        data = dataConfig.data
    end)
    local base = tonumber(SunExp_GetDataValue(data, "Expend")) or 0
    local exCost = tonumber(SunExp_DebugDictValue(dataConfig.Vars, "ExCost") or "0") or 0
    local onceExCost = tonumber(SunExp_DebugDictValue(dataConfig.Vars, "OnceExCost") or "0") or 0
    local totalExCost = tonumber(SunExp_DebugDictValue(dataConfig.Vars, "TotalExCost") or "0") or 0
    local total = base + exCost + onceExCost + totalExCost
    if total < 0 then
        total = 0
    end
    return math.floor(total)
end

function SunExp_DebugDataConfigLabel(dataConfig)
    if dataConfig == nil then
        return "dataConfig=nil"
    end
    local data = nil
    pcall(function()
        data = dataConfig.data
    end)
    local id = SunExp_DebugDictValue(data, "Id") or "?"
    local tag = SunExp_DebugDictValue(data, "Tag") or ""
    local specialTag = SunExp_DebugDictValue(dataConfig.Vars, "SpecialTag") or ""
    local temp = SunExp_DebugDictValue(dataConfig.Vars, "SunExpTempWhiteRadiance") or ""
    return "id=" .. tostring(id)
        .. ", tag=" .. tostring(tag)
        .. ", specialTag=" .. tostring(specialTag)
        .. ", tempWhiteRadiance=" .. tostring(temp)
end

function SunExp_GetCardItemCost(card)
    local data = SunExp_GetCardItemData(card)
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

function SunExp_CardItemHasTagText(card, getter, tag)
    if getter == nil then
        return false
    end
    local needle = SunExp_WunaTrimTag(tag)
    if needle == "" then
        return false
    end
    local ok, text = pcall(function()
        return getter(card)
    end)
    if not ok or text == nil then
        return false
    end
    for part in string.gmatch(tostring(text), "([^,]+)") do
        if SunExp_WunaTrimTag(part) == needle then
            return true
        end
    end
    return false
end

function SunExp_IsTemporaryWhiteRadianceCard(card)
    if card == nil then
        return false
    end
    if SunExp_GetCardInstanceVar(card, "SunExpTempWhiteRadiance", "0") ~= "1" then
        return false
    end
    return SunExp_CardItemHasTagText(card, SunExp_WunaGetCardSpecialTagText, SunExpSolarKeyword)
end

function SunExp_CardItemHasNativeWhiteRadiance(card)
    return SunExp_CardItemHasTagText(card, SunExp_WunaGetCardTagText, SunExpSolarKeyword)
end

function SunExp_SetCardVar(card, key, value)
    if card == nil or key == nil then
        return false
    end
    local changed = false
    local text = tostring(value)
    local function setVars(vars)
        if vars == nil then
            return
        end
        local ok = pcall(function()
            vars:set_Item(key, text)
        end)
        if ok then
            changed = true
            return
        end
        pcall(function()
            vars[key] = text
            changed = true
        end)
    end
    pcall(function()
        setVars(card.Vars)
    end)
    pcall(function()
        if card.dataConfig ~= nil then
            setVars(card.dataConfig.Vars)
        end
    end)
    return changed
end

function SunExp_GetCardInstanceVar(card, key, defaultValue)
    if card == nil or key == nil then
        return defaultValue
    end
    local vars = nil
    pcall(function()
        vars = card.Vars
    end)
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

function SunExp_SetCardInstanceVar(card, key, value)
    if card == nil or key == nil then
        return false
    end
    local vars = nil
    pcall(function()
        vars = card.Vars
    end)
    if vars == nil then
        return false
    end
    local text = tostring(value)
    local ok = pcall(function()
        vars:set_Item(key, text)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        vars[key] = text
    end)
    return ok
end

function SunExp_AssignTemporaryWhiteRadianceLockId(card)
    local lockId = tostring(SunExp_CombatIntAdd("SunExpTempWhiteRadianceLockSeq", 1))
    SunExp_SetCardInstanceVar(card, "SunExpTempWhiteRadianceLockId", lockId)
    SunExp_SetCardInstanceVar(card, "SunExpTempWhiteRadianceResolved", "0")
    return lockId
end

function SunExp_EnsureTemporaryWhiteRadianceLockId(card)
    local lockId = tostring(SunExp_GetCardInstanceVar(card, "SunExpTempWhiteRadianceLockId", "") or "")
    if lockId ~= "" and lockId ~= "0" then
        return lockId
    end
    return SunExp_AssignTemporaryWhiteRadianceLockId(card)
end

function SunExp_TemporaryWhiteRadianceResolvedKey(lockId)
    return "SunExpTempWhiteRadianceResolved_" .. tostring(lockId)
end

function SunExp_TryResolveTemporaryWhiteRadianceLock(card, source)
    local lockId = SunExp_EnsureTemporaryWhiteRadianceLockId(card)
    local key = SunExp_TemporaryWhiteRadianceResolvedKey(lockId)
    local cardResolved = tostring(SunExp_GetCardInstanceVar(card, "SunExpTempWhiteRadianceResolved", "0") or "0") == "1"
    if SunExp_CombatIntGet(key, 0) == 1 then
        if not cardResolved then
            SunExp_DebugWhiteRadianceLog(
                "TempWhiteRadiance stale shared lock renewed source=" .. tostring(source)
                .. ", oldLockId=" .. tostring(lockId)
                .. ", " .. SunExp_DebugCardLabel(card)
            )
            lockId = SunExp_AssignTemporaryWhiteRadianceLockId(card)
            key = SunExp_TemporaryWhiteRadianceResolvedKey(lockId)
        else
            SunExp_DebugWhiteRadianceLog(
                "TempWhiteRadiance skipped: shared lock resolved source=" .. tostring(source)
                .. ", lockId=" .. tostring(lockId)
                .. ", " .. SunExp_DebugCardLabel(card)
            )
            return false
        end
    end
    if SunExp_CombatIntGet(key, 0) == 1 then
        SunExp_DebugWhiteRadianceLog(
            "TempWhiteRadiance skipped: shared lock resolved source=" .. tostring(source)
            .. ", lockId=" .. tostring(lockId)
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        return false
    end
    if SunExp_CombatIntSet(key, 1) then
        SunExp_SetCardInstanceVar(card, "SunExpTempWhiteRadianceResolved", "1")
        SunExp_DebugWhiteRadianceLog(
            "TempWhiteRadiance lock resolved source=" .. tostring(source)
            .. ", lockId=" .. tostring(lockId)
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        return true
    end
    if tostring(SunExp_GetCardInstanceVar(card, "SunExpTempWhiteRadianceResolved", "0") or "0") == "1" then
        SunExp_DebugWhiteRadianceLog(
            "TempWhiteRadiance skipped: card lock resolved source=" .. tostring(source)
            .. ", lockId=" .. tostring(lockId)
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        return false
    end
    SunExp_SetCardInstanceVar(card, "SunExpTempWhiteRadianceResolved", "1")
    SunExp_DebugWhiteRadianceLog(
        "TempWhiteRadiance card lock resolved source=" .. tostring(source)
        .. ", lockId=" .. tostring(lockId)
        .. ", " .. SunExp_DebugCardLabel(card)
    )
    return true
end

function SunExp_TriggerTemporaryWhiteRadianceCard(card, source)
    if card == nil then
        SunExp_DebugWhiteRadianceLog("TempWhiteRadiance skipped: card=nil, source=" .. tostring(source))
        return false
    end
    if not SunExp_IsTemporaryWhiteRadianceCard(card) then
        return false
    end
    if SunExp_CardItemHasNativeWhiteRadiance(card) then
        SunExp_DebugWhiteRadianceLog(
            "TempWhiteRadiance skipped: native white radiance card, source=" .. tostring(source)
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        return false
    end
    local executor = SunExp_GetCardItemScriptExecutor(card)
    if executor == nil then
        SunExp_DebugWhiteRadianceLog(
            "TempWhiteRadiance skipped: executor=nil, source=" .. tostring(source)
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        return false
    end
    if not SunExp_TryResolveTemporaryWhiteRadianceLock(card, source) then
        return false
    end
    local cost = SunExp_GetCardItemCost(card)
    SunExp_DebugWhiteRadianceLog(
        "TempWhiteRadiance trigger source=" .. tostring(source)
        .. ", cost=" .. tostring(cost)
        .. ", " .. SunExp_DebugCardLabel(card)
    )
    return SunExp_HandleSolarCardUsed(executor, cost)
end

function SunExp_GetHookCardItem(context)
    local card = SunExp_TryGetHookTarget(context)
    if SunExp_GetCardItemData(card) ~= nil or SunExp_GetCardItemScriptExecutor(card) ~= nil then
        return card
    end
    if SunExp_GetCardItemData(context) ~= nil or SunExp_GetCardItemScriptExecutor(context) ~= nil then
        return context
    end
    return card
end

function SunExp_OnCardTrueUseWithSource(source, ...)
    local contexts = {...}
    for i = 1, #contexts do
        local context = contexts[i]
        local card = SunExp_GetHookCardItem(context)
        SunExp_DebugWhiteRadianceLog(
            "TrueUse observed source=" .. tostring(source)
            .. ", temp=" .. tostring(SunExp_IsTemporaryWhiteRadianceCard(card))
            .. ", native=" .. tostring(SunExp_CardItemHasNativeWhiteRadiance(card))
            .. ", " .. SunExp_DebugCardLabel(card)
        )
        SunExp_TriggerTemporaryWhiteRadianceCard(card, source)
    end
end

function SunExp_OnCommonCardTrueUseBefore(...)
    return SunExp_OnCardTrueUseWithSource("CommonCardItem.TrueUse.before", ...)
end

function SunExp_OnAttackCardTrueUseBefore(...)
    return SunExp_OnCardTrueUseWithSource("AttackCardItem.TrueUse.before", ...)
end

function SunExp_OnCommonCardTrueUseAfter(...)
    return SunExp_OnCardTrueUseWithSource("CommonCardItem.TrueUse.after", ...)
end

function SunExp_OnAttackCardTrueUseAfter(...)
    return SunExp_OnCardTrueUseWithSource("AttackCardItem.TrueUse.after", ...)
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
    local function readVars(vars)
        if vars == nil then
            return nil
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
        return nil
    end
    local value = readVars(card.Vars)
    if value ~= nil then
        return value
    end
    pcall(function()
        if card.dataConfig ~= nil then
            value = readVars(card.dataConfig.Vars)
        end
    end)
    if value ~= nil then
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
