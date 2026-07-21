using System;
using SunExp.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SolarMemorySettlementPresenter
{
    public static void Show()
    {
        try
        {
            UIManager.Instance?.CloseUI("MapSelectUI");
            UIManager.Instance?.CloseUI("EventUI");
            UIManager.Instance?.ShowUI<GameExitUI>("GameExitUI", true);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory settlement UI failed", ex);
        }
    }
}
