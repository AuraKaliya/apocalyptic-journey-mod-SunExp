function ModConfig:Setup()
    if CS ~= nil and CS.UnityEngine ~= nil and CS.UnityEngine.Debug ~= nil then
        CS.UnityEngine.Debug.Log("[SunExp] Lua Entry.lua compatibility setup loaded; runtime logic is in Entry.dll.")
    end
end
