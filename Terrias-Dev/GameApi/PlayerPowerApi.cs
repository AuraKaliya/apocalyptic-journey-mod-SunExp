using System;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

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
            TerriasLog.Warn("FightPlayer max power change failed: " + ex.Message);
            return false;
        }
    }

    public static bool TryRestoreToMax()
    {
        var player = FightPlayer.Instance;
        if (player == null)
        {
            return false;
        }

        try
        {
            player.CurPowerCount = Math.Max(0, player.MaxPowerCount);
            return player.CurPowerCount == Math.Max(0, player.MaxPowerCount);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("FightPlayer power restore failed: " + ex.Message);
            return false;
        }
    }

    public static bool TryGainPower(int amount)
    {
        var player = FightPlayer.Instance;
        if (player == null || amount <= 0)
        {
            return false;
        }

        try
        {
            player.CurPowerCount = Math.Max(0, player.CurPowerCount) + amount;
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("FightPlayer power gain failed: " + ex.Message);
            return false;
        }
    }
}
