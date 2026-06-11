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

function SunExp_TrySetCollectionItem(collection, index, value)
    if collection == nil then
        return false
    end
    local ok = pcall(function()
        collection:set_Item(index, value)
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        collection[index] = value
    end)
    if ok then
        return true
    end
    ok = pcall(function()
        collection[index + 1] = value
    end)
    return ok
end
