function SunExp_RegisterHook(self, hookKey, tokenKey)
    if self == nil or self.Vars == nil then
        return "0"
    end
    if self.Vars:ContainsKey(hookKey) and self.Vars:get_Item(hookKey) == "1" then
        return nil
    end
    local token = tonumber(SunExp_GetVar(self, tokenKey, "0")) or 0
    token = token + 1
    self.Vars:set_Item(hookKey, "1")
    self.Vars:set_Item(tokenKey, tostring(token))
    return tostring(token)
end

function SunExp_IsHookTokenActive(self, tokenKey, token)
    if self == nil or self.Vars == nil then
        return true
    end
    return SunExp_GetVar(self, tokenKey, "") == tostring(token)
end

function SunExp_ClearHook(self, hookKey, tokenKey)
    if self == nil or self.Vars == nil then
        return
    end
    self.Vars:set_Item(hookKey, "0")
    local token = tonumber(SunExp_GetVar(self, tokenKey, "0")) or 0
    self.Vars:set_Item(tokenKey, tostring(token + 1))
end
