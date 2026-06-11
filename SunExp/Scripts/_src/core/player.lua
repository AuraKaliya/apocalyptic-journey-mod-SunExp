function SunExp_PlayerInfo()
    if CS == nil or CS.ScriptExecutor == nil then
        return nil
    end
    return CS.ScriptExecutor.PlayerInfo
end

function SunExp_PlayerGetVar(key, defaultValue)
    local player = SunExp_PlayerInfo()
    if player == nil then
        return defaultValue
    end
    local ok, value = pcall(function()
        return player.GetGameVar(key)
    end)
    if ok and value ~= nil and tostring(value) ~= "" then
        return tostring(value)
    end
    return defaultValue
end

function SunExp_PlayerSetVar(key, value)
    local player = SunExp_PlayerInfo()
    if player == nil then
        return false
    end
    local ok = pcall(function()
        player.SetGameVar(key, tostring(value))
    end)
    return ok
end

function SunExp_GainGold(amount)
    local player = SunExp_PlayerInfo()
    if player == nil then
        return false
    end
    local value = math.floor(tonumber(amount) or 0)
    if value == 0 then
        return true
    end
    local ok = pcall(function()
        player.Money = (tonumber(player.Money) or 0) + value
    end)
    return ok
end

function SunExp_AddCardReward(cardId)
    local player = SunExp_PlayerInfo()
    if player == nil or cardId == nil then
        return false
    end
    local ok = pcall(function()
        player.AddCard(cardId)
    end)
    return ok
end

function SunExp_AddRelicReward(relicId)
    local player = SunExp_PlayerInfo()
    if player == nil or relicId == nil then
        return false
    end
    local ok = pcall(function()
        player.AddRelic(relicId)
    end)
    return ok
end

function SunExp_AddBlessReward(blessId)
    local player = SunExp_PlayerInfo()
    if player == nil or blessId == nil then
        return false
    end
    local ok = pcall(function()
        player.AddBless(blessId)
    end)
    return ok
end

function SunExp_EndEvent()
    local player = SunExp_PlayerInfo()
    if player == nil then
        return false
    end
    local ok = pcall(function()
        player.EndEvent()
    end)
    return ok
end

function SunExp_ShowCaption(text)
    local player = SunExp_PlayerInfo()
    if player == nil or text == nil then
        return false
    end
    local ok = pcall(function()
        player.ShowCaption(text)
    end)
    return ok
end
