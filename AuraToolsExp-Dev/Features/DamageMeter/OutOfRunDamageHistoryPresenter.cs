using AuraToolsExp.Dll.Features.DamageMeter.Model;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class OutOfRunDamageHistoryPresenter
{
    internal static void Show(OutOfRunDamageHistoryStore history)
    {
        DamageHistoryWindowRenderer.ShowOutOfRunHistory(history);
    }
}
