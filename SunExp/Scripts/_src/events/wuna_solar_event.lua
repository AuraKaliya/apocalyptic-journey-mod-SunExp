function SunExp_WunaProgressKey()
    return "SunExp_WunaEventProgress"
end

function SunExp_GetWunaProgress()
    return tonumber(SunExp_PlayerGetVar(SunExp_WunaProgressKey(), "0")) or 0
end

function SunExp_SetWunaProgress(progress)
    return SunExp_PlayerSetVar(SunExp_WunaProgressKey(), tostring(progress))
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

function SunExp_WunaRewardCard(progress, cardId)
    SunExp_GainGold(100)
    SunExp_AddCardReward(cardId)
    SunExp_ShowCaption("余烬中，一张日耀卡牌被保存下来。")
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardRelic(progress, relicId)
    SunExp_GainGold(100)
    SunExp_AddRelicReward(relicId)
    SunExp_ShowCaption("余烬中，一件日耀遗物被保存下来。")
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardBless(progress, blessId)
    SunExp_GainGold(100)
    SunExp_AddBlessReward(blessId)
    SunExp_ShowCaption("余烬中，一道旧日祝福回应了你。")
    SunExp_WunaFinish(progress)
end

function SunExp_WunaRewardNone(progress)
    SunExp_GainGold(100)
    SunExp_ShowCaption("余烬中，乌娜的名字被重新点亮。")
    SunExp_WunaFinish(progress)
end

function SunExp_TrySetNodeData(node, key, value)
    if node == nil or key == nil then
        return false
    end
    local ok = pcall(function()
        if node.data ~= nil and node.data.set_Item ~= nil then
            node.data:set_Item(key, tostring(value))
        elseif node.data ~= nil and node.data.Set ~= nil then
            node.data:Set(key, tostring(value))
        end
    end)
    return ok
end

function SunExp_CreateSolarEventNode()
    local node = nil
    pcall(function()
        node = CS.MapTree.Node.New("Event")
    end)
    if node == nil then
        return nil
    end
    local nextEvent = math.min(6, math.max(1, SunExp_GetWunaProgress() + 1))
    local eventId = string.format("SunExp_sunexp_wuna_event_%02d", nextEvent)
    pcall(function()
        node.type = "Event"
    end)
    SunExp_TrySetNodeData(node, "Id", "SunExp_sunexp_solar_event")
    SunExp_TrySetNodeData(node, "Type", "Event")
    SunExp_TrySetNodeData(node, "NodeId", eventId)
    SunExp_TrySetNodeData(node, "Level", "-1")
    return node
end

function SunExp_TryAppendSolarEventNode(nodes)
    if nodes == nil then
        return false
    end
    local count = SunExp_GetCollectionCount(nodes)
    for i = 0, count - 1 do
        local item = SunExp_GetCollectionItem(nodes, i)
        if item ~= nil and item.data ~= nil then
            local ok, id = pcall(function()
                if item.data.ContainsKey ~= nil and item.data:ContainsKey("Id") then
                    return item.data:get_Item("Id")
                end
                return nil
            end)
            if ok and id == "SunExp_sunexp_solar_event" then
                return true
            end
        end
    end
    local node = SunExp_CreateSolarEventNode()
    if node == nil then
        return false
    end
    local ok = pcall(function()
        nodes:Add(node)
    end)
    return ok
end

function SunExp_TryInjectSolarEventMapCard(...)
    local args = {...}
    pcall(function()
        if SunExp_GetWunaProgress() >= 6 then
            return
        end
        for i = 1, #args do
            local candidate = args[i]
            if candidate ~= nil then
                if SunExp_TryAppendSolarEventNode(candidate) then
                    return
                end
                if candidate.GetNodes ~= nil then
                    local ok, nodes = pcall(function()
                        return candidate:GetNodes()
                    end)
                    if ok and SunExp_TryAppendSolarEventNode(nodes) then
                        return
                    end
                end
            end
        end
    end)
end
