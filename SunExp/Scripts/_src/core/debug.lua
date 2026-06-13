SunExpDebugWhiteRadiance = true

function SunExp_DebugToString(value)
    if value == nil then
        return "nil"
    end
    return tostring(value)
end

function SunExp_DebugDictValue(data, key)
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
    if ok and value ~= nil then
        return value
    end
    return nil
end

function SunExp_DebugCardData(item)
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
    if ok and data ~= nil then
        return data
    end
    ok, data = pcall(function()
        if item.data ~= nil then
            return item.data
        end
        return nil
    end)
    if ok then
        return data
    end
    return nil
end

function SunExp_DebugCardVars(item)
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

function SunExp_DebugCardLabel(item)
    if item == nil then
        return "card=nil"
    end
    local data = SunExp_DebugCardData(item)
    local id = SunExp_DebugDictValue(data, "Id") or "?"
    local name = SunExp_DebugDictValue(data, "Name")
    local ok, localized = pcall(function()
        if data ~= nil and data.Localize ~= nil then
            return data:Localize("Name")
        end
        return nil
    end)
    if ok and localized ~= nil and tostring(localized) ~= "" then
        name = localized
    end
    if name == nil or tostring(name) == "" then
        name = "?"
    end
    local tag = SunExp_DebugDictValue(data, "Tag") or ""
    local specialTag = SunExp_DebugDictValue(SunExp_DebugCardVars(item), "SpecialTag") or ""
    if specialTag == "" and SunExp_WunaGetCardSpecialTagText ~= nil then
        local okSpecial, value = pcall(function()
            return SunExp_WunaGetCardSpecialTagText(item)
        end)
        if okSpecial and value ~= nil then
            specialTag = value
        end
    end
    return "id=" .. tostring(id)
        .. ", name=" .. tostring(name)
        .. ", tag=" .. tostring(tag)
        .. ", specialTag=" .. tostring(specialTag)
end

function SunExp_DebugLog(scope, message)
    if not SunExpDebugWhiteRadiance then
        return false
    end
    local title = tostring(scope or "SunExp")
    local text = tostring(message or "")
    local sent = false
    pcall(function()
        if CS ~= nil and CS.Commands ~= nil and CS.Commands.Log ~= nil then
            CS.Commands.Log(title, text)
            sent = true
        end
    end)
    if not sent then
        pcall(function()
            if CS ~= nil and CS.UnityEngine ~= nil and CS.UnityEngine.Debug ~= nil then
                CS.UnityEngine.Debug.Log("[" .. title .. "] " .. text)
                sent = true
            end
        end)
    end
    return sent
end

function SunExp_DebugWhiteRadianceLog(message)
    return SunExp_DebugLog("SunExp.WhiteRadiance", message)
end
