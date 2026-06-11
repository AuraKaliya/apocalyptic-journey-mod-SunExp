function SunExp_GetVar(self, key, defaultValue)
    if self == nil or self.Vars == nil or not self.Vars:ContainsKey(key) then
        return defaultValue
    end
    return self.Vars:get_Item(key)
end

function SunExp_SetVar(self, key, value)
    if self == nil or self.Vars == nil then
        return
    end
    self.Vars:set_Item(key, tostring(value))
end
