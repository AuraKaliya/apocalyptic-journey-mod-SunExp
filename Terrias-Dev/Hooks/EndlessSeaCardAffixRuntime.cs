using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class EndlessSeaCardAffixRuntime
{
    private static RoleTable? owner;
    private static ObservableCollection<DataConfig>? deck;
    private static ObservableCollection<DataConfig>? reserve;

    public static void Initialize(ModConfig modConfig)
    {
        // Bind before callers mutate the returned role's inventory. This callback
        // only binds collections; it never re-enters RoleTable.Instance.
        TerriasHookRegistry.Before(modConfig, "RoleTable.get_Instance", _ => BindCurrentRole(), "EndlessSeaCardAffix");
        TerriasHookRegistry.Before(modConfig, "GameSaveManager.UpdateRoles", _ => BindCurrentRole(), "EndlessSeaCardAffix");
        TerriasHookRegistry.Before(modConfig, "MapSelectUI.ShowMap", _ => Reconcile(), "EndlessSeaCardAffix");
        TerriasBattleLifecycleRouter.Register("EndlessSeaCardAffix", new TerriasBattleLifecycleSubscription
        {
            BattleInitializing = _ => Reconcile()
        });
        BindCurrentRole();
    }

    private static void BindCurrentRole()
    {
        var role = Singleton<GameRuntimeData>.Instance?.roleTable;
        if (ReferenceEquals(owner, role) && ReferenceEquals(deck, role?.cardList)
            && ReferenceEquals(reserve, role?.UnCardList)) return;
        if (deck != null) deck.CollectionChanged -= OnOwnedCardsChanged;
        if (reserve != null) reserve.CollectionChanged -= OnOwnedCardsChanged;
        owner = role;
        deck = role?.cardList;
        reserve = role?.UnCardList;
        if (deck != null) deck.CollectionChanged += OnOwnedCardsChanged;
        if (reserve != null) reserve.CollectionChanged += OnOwnedCardsChanged;
        TerriasFrameDispatcher.RunOnceNextFrame("EndlessSeaCardAffix.Reconcile", Reconcile);
    }

    private static void Reconcile()
    {
        BindCurrentRole();
        if (owner != null) EndlessSeaCardAffixService.NormalizeOwnedCards(owner, "OwnedInventory.Bind");
    }

    private static void OnOwnedCardsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (owner == null || args.NewItems == null || !EndlessSeaModeRuntime.IsEndlessSeaRun()) return;
        try
        {
            var changed = false;
            foreach (var item in args.NewItems)
                if (item is IDataConfig card) changed |= EndlessSeaCardAffixService.AttachOwnedReward(card);
            if (changed && !EndlessSeaCardAffixService.TryPersistRole(owner, "OwnedInventory.Add"))
                throw new InvalidOperationException("Could not persist newly acquired abyss cards.");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[EndlessSeaCardAffix] owned inventory update failed", ex);
        }
    }
}
