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
