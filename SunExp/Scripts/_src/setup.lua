function ModConfig:Setup()
    SunExp_RegisterDynamicMethods(self)
    self:AddMethodHookBefore("Witch.UI.Window.MapSelectUI.CreateMapItem", SunExp_TryInjectSolarEventMapCard)
end
