using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class FightDamageHistoryPresenter
{
    internal static void Show(DamageHistoryStore history, DamageMeterSettings settings)
    {
        DamageHistoryWindowRenderer.ShowHistory(history, settings);
    }
}
