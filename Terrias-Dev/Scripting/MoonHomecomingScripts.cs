using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class MoonHomecomingScripts
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> UseHandlers = new(StringComparer.Ordinal)
    {
        ["frostmoon_new_god"] = MoonHomecomingMechanics.UseFrostmoonNewGod,
        ["flower_sea_moon_night"] = MoonHomecomingMechanics.UseFlowerSeaMoonNight,
        ["moon_offering"] = MoonHomecomingMechanics.UseOffering,
        ["kuutar_morning_mist"] = MoonHomecomingMechanics.UseKuutarMorningMist,
        ["moon_homecoming_night"] = MoonHomecomingMechanics.UseHomecomingNight,
        ["new_moon_blessing"] = MoonHomecomingMechanics.UseNewMoonBlessing,
        ["luonnotar"] = MoonHomecomingMechanics.UseLuonnotar
    };

    private static readonly Dictionary<string, Action<ScriptExecutor>> DrawHandlers = new(StringComparer.Ordinal)
    {
        ["moon_chronicle_i"] = MoonHomecomingMechanics.DrawFirstChronicle,
        ["moon_chronicle_ii"] = MoonHomecomingMechanics.DrawSecondChronicle,
        ["moon_chronicle_iii"] = MoonHomecomingMechanics.DrawThirdChronicle
    };

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
            if (id == "moon_offering")
                DictionaryUtil.Set(self.Vars, "Usable", MoonHomecomingMechanics.HasOffering(self) ? "1" : "0");
            ScriptDelegateApi.BindParameterized(self, "InitScript", id, Init);
            ScriptDelegateApi.BindParameterized(self, "UseScript", id, Use);
            ScriptDelegateApi.BindParameterized(self, "DrawScript", id, Draw);
        }
        catch (Exception ex) { TerriasLog.Error("Moon Homecoming card init failed: " + id, ex); }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            if (MoonHomecomingMechanics.CanResolve(self) && UseHandlers.TryGetValue(id, out var use)) use(self);
        }
        catch (Exception ex) { TerriasLog.Error("Moon Homecoming card use failed: " + id, ex); }
    }

    public static void Draw(ScriptExecutor self, string id)
    {
        try
        {
            if (MoonHomecomingMechanics.CanResolve(self) && DrawHandlers.TryGetValue(id, out var draw)) draw(self);
        }
        catch (Exception ex) { TerriasLog.Error("Moon Homecoming card draw failed: " + id, ex); }
    }
}
