function SunExp_TryAddMethodHookBefore(config, typeDotMethod, fn)
    if config == nil or typeDotMethod == nil or fn == nil then
        return false
    end
    local ok = pcall(function()
        config:AddMethodHookBefore(typeDotMethod, fn)
    end)
    return ok
end

function SunExp_TryAddMethodHookAfter(config, typeDotMethod, fn)
    if config == nil or typeDotMethod == nil or fn == nil then
        return false
    end
    local ok = pcall(function()
        config:AddMethodHookAfter(typeDotMethod, fn)
    end)
    return ok
end

function ModConfig:Setup()
    SunExp_RegisterDynamicMethods(self)
    SunExp_TryAddMethodHookBefore(self, "MapSelectUI.ReadyToSelect", SunExp_EnsureSolarEventInCurrentLayerFromHook)
    SunExp_TryAddMethodHookAfter(self, "NormalMapManager.RandomGenerate", SunExp_TryRepairSolarEventGeneratedMap)
    SunExp_TryAddMethodHookAfter(self, "NormalMapManager.GeneratrMap", SunExp_TryRepairSolarEventGeneratedMap)
    SunExp_TryAddMethodHookBefore(self, "MapManager.UserCode_CmdSelectMap__String[]__String[]__NetworkConnectionToClient", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "MapManager.UserCode_CmdSelectMapIncludeSender__String[]__String[]__NetworkConnectionToClient", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "MapManager.CmdSelectMap", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "MapManager.CmdSelectMapIncludeSender", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "MapManager.TargetUpdateMap", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "MapManager.RpcUpdateMap", SunExp_TryRepairSolarEventMapSelection)
    SunExp_TryAddMethodHookBefore(self, "ScriptExecutor.AddBuff", SunExp_OnScriptExecutorAddBuffBefore)
end
