using Terrias.Dll.Application;
using Terrias.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class OlimyaRuntime
{
    public static void Initialize(ModConfig config)
    {
        TerriasHookRegistry.Before(config, "RoleTable.Init", context =>
        {
            if (context.Target is RoleTable role) OlimyaEconomyService.BeginInitialization(role);
        }, "Olimya.EconomyInit");
        TerriasHookRegistry.After(config, "RoleTable.Init", context =>
        {
            if (context.Target is RoleTable role) OlimyaEconomyService.EndInitialization(role);
        }, "Olimya.EconomyInit");
        TerriasHookRegistry.Before(config, "RoleTable.set_Money", context =>
        {
            if (context.Target is RoleTable role) OlimyaEconomyService.BeforeMoneyChange(role);
        }, "Olimya.Economy");
        TerriasHookRegistry.Before(config, "RoleTable.OnPropertyChanged", context =>
        {
            if (context.Target is RoleTable role && context.Arguments?.Length > 0 && context.Arguments[0] as string == "Money")
                OlimyaEconomyService.BeforeMoneyNotification(role);
        }, "Olimya.Economy");
        TerriasHookRegistry.After(config, "RoleTable.set_Money", context =>
        {
            if (context.Target is RoleTable role) OlimyaEconomyService.AfterMoneyChange(role);
        }, "Olimya.Economy");

        TerriasHookRegistry.Before(config, "CustomDamageType.ApplyDamage", BeforeDamage, "Olimya.Damage");
        TerriasHookRegistry.Before(config, "CustomDamageType.ShowDamage", OnDamageResolved, "Olimya.Damage");
        TerriasHookRegistry.After(config, "CustomDamageType.ApplyDamage", context =>
        {
            if (context.Arguments?.Length > 0 && context.Arguments[0] is IStatusManager target) OlimyaDamageService.End(target);
        }, "Olimya.Damage");

        TerriasBattleLifecycleRouter.Register("Olimya", new TerriasBattleLifecycleSubscription
        {
            PlayerTurnEntering = _ => OlimyaRoleApplication.BeginLocalTurn(),
            BattleOpening = _ => OlimyaRoleApplication.BeginBattle(),
            BattleInitializing = _ => OlimyaRoleApplication.EndBattle(),
            BattleRestarting = _ => OlimyaRoleApplication.EndBattle(),
            BattleEnded = _ => OlimyaRoleApplication.EndBattle()
        });
    }

    private static void BeforeDamage(ModHookContext context)
    {
        var args = context.Arguments;
        if (context.Target is CustomDamageType type && args?.Length >= 4 && args[0] is IStatusManager target)
            OlimyaDamageService.Begin(target, args[3] as IStatusManager, type.ignoreDefend);
    }

    private static void OnDamageResolved(ModHookContext context)
    {
        var args = context.Arguments;
        if (args?.Length >= 4 && args[0] is IStatusManager target && args[1] is int amount)
            OlimyaDamageService.ObserveResolvedDamage(target, args[3] as IStatusManager, amount);
    }
}
