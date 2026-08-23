using System;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class CombatCardApi
{
    public static bool TryDrawPlayerCards(ScriptExecutor? executor, int count, string source)
    {
        var requested = Math.Max(0, count);
        if (requested <= 0 || executor == null)
        {
            return false;
        }

        if (!AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation)
        {
            return Skip(requested, source, "battle terminal barrier is closed");
        }

        try
        {
            executor.DrawCount(requested.ToString());
            return true;
        }
        catch (Exception ex)
        {
            return Fail(requested, source, ex.Message);
        }
    }

    public static bool TryDrawPlayerCards(int count, string source)
    {
        var requested = Math.Max(0, count);
        if (requested <= 0)
        {
            return false;
        }

        if (!AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation)
        {
            return Skip(requested, source, "battle terminal barrier is closed");
        }

        var manager = FightCardManager.Instance;
        if (manager == null)
        {
            return Fail(requested, source, "combat card manager unavailable");
        }

        FightUI? fightUi;
        try
        {
            fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        }
        catch (Exception ex)
        {
            return Fail(requested, source, "fight UI lookup failed: " + ex.Message);
        }

        if (fightUi == null)
        {
            return Fail(requested, source, "fight UI unavailable");
        }

        try
        {
            var available = manager.cardList?.Count ?? 0;
            if (!manager.HasCard() || available < requested)
            {
                manager.RandomIndex();
            }

            fightUi.CreateCardItem(requested);
            TerriasLog.Debug("[CombatCardApi] player draw applied: count="
                + requested
                + ", source="
                + NormalizeSource(source)
                + ".");
            return true;
        }
        catch (Exception ex)
        {
            return Fail(requested, source, ex.Message);
        }
    }

    private static bool Fail(int count, string source, string reason)
    {
        TerriasLog.Warn("[CombatCardApi] player draw failed: count="
            + count
            + ", source="
            + NormalizeSource(source)
            + ", reason="
            + reason
            + ".");
        return false;
    }

    private static bool Skip(int count, string source, string reason)
    {
        TerriasLog.Debug("[CombatCardApi] player draw skipped: count="
                         + count
                         + ", source="
                         + NormalizeSource(source)
                         + ", reason="
                         + reason
                         + ".");
        return false;
    }

    private static string NormalizeSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
    }
}
