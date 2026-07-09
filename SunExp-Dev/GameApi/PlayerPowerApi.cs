using System;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class PlayerPowerApi
{
    public static bool TryChangeMaxPower(int delta)
    {
        if (delta == 0)
        {
            return true;
        }

        var player = FightPlayer.Instance;
        if (player == null)
        {
            return false;
        }

        try
        {
            var expected = Math.Max(0, player.MaxPowerCount + delta);
            player.MaxPowerCount = expected;
            return player.MaxPowerCount == expected;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("FightPlayer max power change failed: " + ex.Message);
            return false;
        }
    }
}
