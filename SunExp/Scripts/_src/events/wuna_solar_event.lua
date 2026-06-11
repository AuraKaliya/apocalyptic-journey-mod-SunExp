function SunExp_WunaProgressKey()
    return "SunExp_WunaEventProgressV2"
end

function SunExp_GetWunaProgress()
    return tonumber(SunExp_PlayerGetVar(SunExp_WunaProgressKey(), "0")) or 0
end

function SunExp_SetWunaProgress(progress)
    return SunExp_PlayerSetVar(SunExp_WunaProgressKey(), tostring(progress))
end

function SunExp_WunaLevelKey()
    return "SunExp_WunaEventLevelV2"
end

function SunExp_GetCurrentMapLevel()
    local level = nil
    pcall(function()
        if CS ~= nil and CS.MapManager ~= nil and CS.MapManager.Instance ~= nil then
            level = CS.MapManager.Instance.Level
        end
    end)
    return tostring(level or "unknown")
end

function SunExp_GetCurrentMapLevelNumber()
    local level = nil
    pcall(function()
        if CS ~= nil and CS.MapManager ~= nil and CS.MapManager.Instance ~= nil then
            level = CS.MapManager.Instance.Level
        end
    end)
    if level == nil then
        pcall(function()
            if CS ~= nil and CS.MapManager ~= nil and CS.MapManager.Instance ~= nil and CS.MapManager.Instance.ModeMapManager ~= nil then
                level = CS.MapManager.Instance.ModeMapManager.Level
            end
        end)
    end
    return tonumber(level) or 0
end

function SunExp_HasWunaEventInCurrentLevel()
    return false
end

function SunExp_MarkWunaEventInCurrentLevel()
    return true
end

function SunExp_AdvanceWunaEvent(progress)
    local current = SunExp_GetWunaProgress()
    local nextProgress = math.max(current, tonumber(progress) or current)
    SunExp_SetWunaProgress(nextProgress)
end

function SunExp_WunaFinish(progress)
    SunExp_AdvanceWunaEvent(progress)
    SunExp_EndEvent()
end

function SunExp_TrySetEventChoiceVar(vars, key, value)
    if vars == nil or key == nil then
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
    if ok then
        return true
    end
    ok = pcall(function()
        vars:Set(key, text)
    end)
    return ok
end

function SunExp_SetEventChoices(executor, choice1, choice2, choice3, choice4)
    local vars = nil
    if executor ~= nil then
        pcall(function()
            vars = executor.Vars
        end)
    end
    if vars == nil then
        pcall(function()
            if Vars ~= nil then
                vars = Vars
            end
        end)
    end
    if vars == nil then
        return false
    end
    local choices = { choice1, choice2, choice3, choice4 }
    local changed = false
    for i = 1, 4 do
        local value = "0"
        if choices[i] ~= nil and tostring(choices[i]) ~= "" then
            value = choices[i]
        end
        changed = SunExp_TrySetEventChoiceVar(vars, "Choice" .. tostring(i), value) or changed
    end
    return changed
end

function SunExp_BeginWunaRepeatEvent(executor)
    return SunExp_SetEventChoices(executor, "1")
end

function SunExp_BeginWunaEvent(executor, step)
    local expected = math.max(1, math.min(6, tonumber(step) or 1))
    local current = SunExp_GetWunaProgress()
    if current ~= expected - 1 then
        return SunExp_BeginWunaRepeatEvent(executor)
    end
    return SunExp_SetEventChoices(executor, "1", "1")
end

function SunExp_WunaRewardCard(progress, cardId)
    SunExp_GainGold(100)
    SunExp_AddCardReward(cardId)
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardRelic(progress, relicId)
    SunExp_GainGold(100)
    SunExp_AddRelicReward(relicId)
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardBless(progress, blessId)
    SunExp_GainGold(100)
    SunExp_AddBlessReward(blessId)
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardNone(progress)
    SunExp_GainGold(100)
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRepeatReward()
    SunExp_AddBlessReward("blessing_8")
    SunExp_EndEvent()
end

function SunExp_SolarEventMapId()
    return "SunExp_sunexp_solar_event"
end

function SunExp_SolarEventPlaceholderNodeId()
    return "SunExp_sunexp_solar_event"
end

function SunExp_IsSolarEventMapId(id)
    if id == nil then
        return false
    end
    local value = tostring(id)
    return value == "SunExp_sunexp_solar_event" or value == "solar_event"
end

function SunExp_IsSolarEventPlaceholderNodeId(id)
    return SunExp_IsSolarEventMapId(id)
end

function SunExp_GetCurrentSolarEventId()
    if SunExp_GetWunaProgress() >= 6 then
        return "SunExp_sunexp_Sub_wuna_event_repeat"
    end
    local nextEvent = math.min(6, math.max(1, SunExp_GetWunaProgress() + 1))
    return string.format("SunExp_sunexp_Sub_wuna_event_%02d", nextEvent)
end

function SunExp_TryGetHookField(obj, fieldName)
    if obj == nil or fieldName == nil then
        return nil
    end
    local ok, value = pcall(function()
        return obj[fieldName]
    end)
    if ok and value ~= nil then
        return value
    end
    ok, value = pcall(function()
        return obj:get_Item(fieldName)
    end)
    if ok and value ~= nil then
        return value
    end
    return nil
end

function SunExp_TryGetHookArguments(obj)
    return SunExp_TryGetHookField(obj, "Arguments")
end

function SunExp_TryGetHookTarget(obj)
    return SunExp_TryGetHookField(obj, "Target")
end

function SunExp_TrySetNodeData(node, key, value)
    if node == nil or node.data == nil or key == nil then
        return false
    end
    local text = tostring(value)
    local ok = pcall(function()
        node.data:set_Item(key, text)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        node.data[key] = text
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        node.data:Set(key, text)
    end)
    return ok
end

function SunExp_TryGetNodeDataValue(node, key)
    if node == nil or node.data == nil or key == nil then
        return nil
    end
    local ok, value = pcall(function()
        if node.data.ContainsKey ~= nil and not node.data:ContainsKey(key) then
            return nil
        end
        return node.data:get_Item(key)
    end)
    if ok and value ~= nil then
        return value
    end
    ok, value = pcall(function()
        return node.data[key]
    end)
    if ok then
        return value
    end
    return nil
end

function SunExp_IsSolarEventNode(node)
    return SunExp_IsSolarEventMapId(SunExp_TryGetNodeDataValue(node, "Id"))
end

function SunExp_IsEventNode(node)
    local typeName = SunExp_TryGetNodeDataValue(node, "Type")
    if typeName ~= nil and tostring(typeName) == "Event" then
        return true
    end
    local nodeType = nil
    pcall(function()
        nodeType = node.type
    end)
    return tostring(nodeType or "") == "Event"
end

function SunExp_GetMapTree(treeOrManager)
    if treeOrManager == nil then
        return nil
    end
    local tree = nil
    pcall(function()
        tree = treeOrManager.MapTree
    end)
    if tree ~= nil then
        return tree
    end
    pcall(function()
        tree = treeOrManager.mapTree
    end)
    if tree ~= nil then
        return tree
    end
    return treeOrManager
end

function SunExp_GetCurrentMapTree()
    local tree = nil
    pcall(function()
        if CS ~= nil and CS.MapManager ~= nil and CS.MapManager.Instance ~= nil then
            tree = CS.MapManager.Instance.MapTree
        end
    end)
    if tree ~= nil then
        return tree
    end
    pcall(function()
        if CS ~= nil and CS.MapManager ~= nil and CS.MapManager.Instance ~= nil and CS.MapManager.Instance.ModeMapManager ~= nil then
            tree = CS.MapManager.Instance.ModeMapManager.MapTree
        end
    end)
    return tree
end

function SunExp_GetSolarLayerSegmentSize()
    local exDelete = 0
    if SunExp_TestExDeleteDes ~= nil then
        exDelete = tonumber(SunExp_TestExDeleteDes) or 0
    else
        pcall(function()
            if CS ~= nil and CS.GameSaveManager ~= nil and CS.GameVar ~= nil then
                exDelete = tonumber(CS.GameSaveManager.GetValue(CS.GameVar.ExDeleteDes)) or exDelete
            end
        end)
        pcall(function()
            if CS ~= nil and CS.GameSaveManager ~= nil then
                exDelete = tonumber(CS.GameSaveManager.GetValue("ExDeleteDes")) or exDelete
            end
        end)
    end
    local size = 8 - exDelete
    if size < 1 then
        return 1
    end
    return size
end

function SunExp_GetSolarLayerStartIndex()
    local level = SunExp_GetCurrentMapLevelNumber()
    return math.floor(level / 6) * SunExp_GetSolarLayerSegmentSize()
end

function SunExp_GetSolarLayerRange()
    return SunExp_GetSolarLayerStartIndex(), SunExp_GetSolarLayerSegmentSize()
end

function SunExp_CreateSolarEventNode(treeOrManager)
    local tree = SunExp_GetMapTree(treeOrManager)
    if tree == nil then
        tree = SunExp_GetCurrentMapTree()
    end
    local node = nil
    pcall(function()
        if tree ~= nil and tree.GetNodeByNodeId ~= nil then
            node = tree:GetNodeByNodeId(SunExp_SolarEventPlaceholderNodeId())
        end
    end)
    if node == nil then
        pcall(function()
            node = CS.MapTree.Node.New("Event")
        end)
    end
    if node == nil then
        return nil
    end
    if node.data == nil then
        return nil
    end
    pcall(function()
        node.type = "Event"
    end)
    SunExp_TrySetNodeData(node, "Id", SunExp_SolarEventMapId())
    SunExp_TrySetNodeData(node, "Type", "Event")
    SunExp_TrySetNodeData(node, "NodeId", SunExp_GetCurrentSolarEventId())
    SunExp_TrySetNodeData(node, "Level", "-1")
    return node
end

function SunExp_GetSelectNodes(treeOrManager)
    local tree = SunExp_GetMapTree(treeOrManager)
    if tree == nil then
        tree = SunExp_GetCurrentMapTree()
    end
    local nodes = nil
    pcall(function()
        if tree ~= nil then
            nodes = tree.SelectNode
        end
    end)
    return nodes
end

function SunExp_IsBreakNode(node)
    local nodeId = SunExp_TryGetNodeDataValue(node, "NodeId")
    if nodeId ~= nil and string.find(tostring(nodeId), "Breaks", 1, true) ~= nil then
        return true
    end
    local id = SunExp_TryGetNodeDataValue(node, "Id")
    if id ~= nil and string.find(tostring(id), "Breaks", 1, true) ~= nil then
        return true
    end
    return false
end

function SunExp_IsProtectedFixedEventNode(node)
    local nodeId = SunExp_TryGetNodeDataValue(node, "NodeId")
    if nodeId == nil then
        return false
    end
    local value = tostring(nodeId)
    return value == "event_2001"
        or value == "event_2002"
        or value == "event_2003"
        or value == "event_2004"
        or value == "event_2005"
        or value == "event_2006"
        or value == "event_2015"
        or value == "event_999"
end

function SunExp_TrySetSolarEventNode(node)
    if node == nil or node.data == nil then
        return false
    end
    local changed = false
    changed = SunExp_TrySetNodeData(node, "Id", SunExp_SolarEventMapId()) or changed
    changed = SunExp_TrySetNodeData(node, "Type", "Event") or changed
    changed = SunExp_TrySetNodeData(node, "NodeId", SunExp_GetCurrentSolarEventId()) or changed
    changed = SunExp_TrySetNodeData(node, "Level", "-1") or changed
    pcall(function()
        node.type = "Event"
    end)
    return changed
end

function SunExp_TryReplaceNodeAt(collection, index, replacement)
    if SunExp_TrySetCollectionItem(collection, index, replacement) then
        return true
    end
    return false
end

function SunExp_EnsureSolarEventInCurrentLayer(treeOrManager)
    local nodes = SunExp_GetSelectNodes(treeOrManager)
    if nodes == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(nodes)
    if count <= 0 then
        return false
    end
    local startIndex, segmentSize = SunExp_GetSolarLayerRange()
    if startIndex >= count then
        return false
    end
    local endIndex = math.min(count - 1, startIndex + segmentSize - 1)
    local firstEventIndex = nil
    local firstFallbackIndex = nil
    for i = startIndex, endIndex do
        local node = SunExp_GetCollectionItem(nodes, i)
        if SunExp_IsSolarEventNode(node) then
            return SunExp_TrySetSolarEventNode(node)
        end
        if node ~= nil and not SunExp_IsBreakNode(node) and not SunExp_IsProtectedFixedEventNode(node) then
            if firstFallbackIndex == nil then
                firstFallbackIndex = i
            end
            if firstEventIndex == nil and SunExp_IsEventNode(node) then
                firstEventIndex = i
            end
        end
    end
    local replaceIndex = firstEventIndex or firstFallbackIndex
    if replaceIndex == nil then
        return false
    end
    local node = SunExp_CreateSolarEventNode(treeOrManager)
    if node == nil then
        node = SunExp_GetCollectionItem(nodes, replaceIndex)
        if not SunExp_TrySetSolarEventNode(node) then
            return false
        end
        return true
    end
    if SunExp_TryReplaceNodeAt(nodes, replaceIndex, node) then
        return true
    end
    return SunExp_TrySetSolarEventNode(SunExp_GetCollectionItem(nodes, replaceIndex))
end

function SunExp_EnsureSolarEventInCurrentLayerFromHook(...)
    local args = {...}
    local changed = false
    pcall(function()
        for i = 1, #args do
            local context = args[i]
            local target = SunExp_TryGetHookTarget(context)
            if target == nil then
                target = context
            end
            if SunExp_EnsureSolarEventInCurrentLayer(target) then
                changed = true
            end
        end
    end)
    return changed
end

function SunExp_TryAddCollectionItem(collection, value)
    if collection == nil or value == nil then
        return false
    end
    local ok = pcall(function()
        collection:Add(value)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        table.insert(collection, value)
    end)
    return ok
end

function SunExp_TryReplaceFirstEventNode(nodes, replacement)
    if nodes == nil or replacement == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(nodes)
    for i = 0, count - 1 do
        local node = SunExp_GetCollectionItem(nodes, i)
        if SunExp_IsEventNode(node) then
            return SunExp_TrySetCollectionItem(nodes, i, replacement)
        end
    end
    return false
end

function SunExp_TryAppendSolarEventNode(nodes, treeOrManager)
    if nodes == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(nodes)
    for i = 0, count - 1 do
        if SunExp_IsSolarEventNode(SunExp_GetCollectionItem(nodes, i)) then
            return false
        end
    end
    local node = SunExp_CreateSolarEventNode(treeOrManager)
    if node == nil then
        return false
    end
    if SunExp_TryAddCollectionItem(nodes, node) then
        return true
    end
    return SunExp_TryReplaceFirstEventNode(nodes, node)
end

function SunExp_TryAppendSolarEventNodeFromHook(...)
    local args = {...}
    local changed = false
    pcall(function()
        for i = 1, #args do
            local context = args[i]
            local target = SunExp_TryGetHookTarget(context)
            local hookArgs = SunExp_TryGetHookArguments(context)
            local count = SunExp_GetCollectionCount(hookArgs)
            for j = 0, count - 1 do
                if SunExp_TryAppendSolarEventNode(SunExp_GetCollectionItem(hookArgs, j), target) then
                    changed = true
                    return
                end
            end
            if hookArgs == nil and SunExp_TryAppendSolarEventNode(context, target) then
                changed = true
                return
            end
        end
    end)
    return changed
end

function SunExp_TryRepairSolarEventNode(node)
    if SunExp_IsSolarEventNode(node) then
        return SunExp_TrySetSolarEventNode(node)
    end
    return false
end

function SunExp_TryRepairSolarEventNodes(nodes)
    if nodes == nil then
        return false
    end
    local changed = false
    local count = SunExp_GetCollectionCount(nodes)
    for i = 0, count - 1 do
        changed = SunExp_TryRepairSolarEventNode(SunExp_GetCollectionItem(nodes, i)) or changed
    end
    return changed
end

function SunExp_TryRepairSolarEventSyncedMapData()
    return false
end

function SunExp_TryRepairSolarEventSelectedNodesFromHook(...)
    return SunExp_EnsureSolarEventInCurrentLayerFromHook(...)
end

function SunExp_TryRepairSolarEventGeneratedMap(...)
    return SunExp_EnsureSolarEventInCurrentLayerFromHook(...)
end

function SunExp_GetMapSelectSolarChoiceCounts(ui)
    return 0, 0
end

function SunExp_TryCreateSolarEventChoiceUI(ui)
    return false
end

function SunExp_TryAppendSolarEventChoiceForUI(ui)
    return false
end

function SunExp_TryAppendSolarEventChoiceUI(...)
    return false
end

function SunExp_TryRepairSolarEventMapArrays(maps, mapdata)
    if maps == nil or mapdata == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(maps)
    if count <= 0 then
        return false
    end
    local changed = false
    for i = 0, count - 1 do
        local mapId = SunExp_GetCollectionItem(maps, i)
        if SunExp_IsSolarEventMapId(mapId) then
            SunExp_TrySetCollectionItem(maps, i, SunExp_SolarEventMapId())
            changed = SunExp_TrySetCollectionItem(mapdata, i, SunExp_GetCurrentSolarEventId()) or changed
        end
    end
    return changed
end

function SunExp_TryRepairSolarEventMapSelection(...)
    local args = {...}
    local changed = false
    pcall(function()
        for i = 1, #args do
            local context = args[i]
            local hookArgs = SunExp_TryGetHookArguments(context)
            if hookArgs == nil then
                hookArgs = context
            end
            local count = SunExp_GetCollectionCount(hookArgs)
            for j = 0, count - 2 do
                local maps = SunExp_GetCollectionItem(hookArgs, j)
                local mapdata = SunExp_GetCollectionItem(hookArgs, j + 1)
                if SunExp_TryRepairSolarEventMapArrays(maps, mapdata) then
                    changed = true
                end
            end
        end
    end)
    return changed
end

function SunExp_TryRepairSolarEventLoadArgumentList(arguments)
    return false
end

function SunExp_TryRepairSolarEventLoad(...)
    return false
end
