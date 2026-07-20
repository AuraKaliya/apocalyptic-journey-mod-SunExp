using System;
using System.Collections.Generic;
using System.Linq;
using Data.Save;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SolarMemoryDeckIsolationRuntime
{
    private const string HookOwner = "SolarMemoryDeckIsolation";

    public static void Initialize(ModConfig modConfig)
    {
        SunExpHookRegistry.Before(
            modConfig,
            "GameConfigManager.CardPackCheck",
            FilterSolarMemoryCardPackCheck,
            HookOwner);
    }

    public static void OpenDeckWindow()
    {
        try
        {
            if (RoleTable.Instance == null)
            {
                return;
            }

            if (!SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryDeckConfiguredKey)
                || !SolarMemoryPlayerSetupState.IsSet(SunExpIds.SolarMemoryStarterDeckAppliedKey))
            {
                SunExpLog.Info("[SolarMemoryMode] deck window requested before starter deck completion; resuming preparation.");
                SolarMemoryPreparationRuntime.StartOrResume();
                return;
            }

            SanitizeSolarMemoryRoleCards(RoleTable.Instance, "OpenDeckWindow");
            var ui = UIManager.Instance.ShowUI<OutDeckUI>("OutDeckUI", true);
            ui.SetRole(new OutDeckUIData(RoleTable.Instance));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory deck window failed", ex);
        }
    }

    internal static HashSet<string> InitialPackSelection()
    {
        var visible = VisibleCardPacks().Select(pack => pack["Id"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(visible.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            selected.UnionWith(visible.Take(6));
        }

        return selected;
    }

    public static List<string> CurrentPackSelection()
    {
        var playerPacks = SolarMemoryPlayerSetupState.SelectedPacks()
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (playerPacks.Count > 0)
        {
            return playerPacks;
        }

        if (!PlayerApi.IsMultiplayerSession())
        {
            var saved = SolarMemoryModeRuntime.IsSolarMemoryRun()
                ? GameSaveManager.GetValue<string>(SunExpIds.SolarMemorySelectedPacksKey)
                : "";
            if (!string.IsNullOrWhiteSpace(saved))
            {
                var savedPacks = saved.Split('|')
                    .Where(IsValidPackForCurrentLobby)
                    .ToList();
                if (savedPacks.Count > 0)
                {
                    return savedPacks;
                }
            }
        }

        var selected = Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(IsValidPackForCurrentLobby)
            .ToList();
        if (selected.Count == 0)
        {
            selected.AddRange(VisibleCardPacks().Take(6).Select(pack => pack["Id"]));
        }

        return selected;
    }

    public static bool IsSolarMemoryEventCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        if (ContainsEventMarker(cardId))
        {
            return true;
        }

        try
        {
            var data = SunExpConfigIndex.Row(DataType.Card, cardId);
            if (data == null)
            {
                return false;
            }
            return IsSolarMemoryEventCard(data) || HasLocalizedEventCardType(cardId);
        }
        catch
        {
            return false;
        }
    }

    public static int SanitizeSolarMemoryRoleCards(RoleTable? role, string source)
    {
        if (role == null)
        {
            return 0;
        }

        var removed = new List<string>();
        RemoveEventConfigs(role.cardList, removed);
        RemoveEventConfigs(role.UnCardList, removed);
        NormalizeSolarMemoryCardCounts(role);

        if (removed.Count > 0)
        {
            SunExpLog.Info("[SolarMemoryMode] sanitized event cards from " + source + ": " + string.Join("|", removed));
        }

        return removed.Count;
    }

    public static void ClearSolarMemoryReservePool()
    {
        ClearSolarMemoryReservePool(RoleTable.Instance);
    }

    public static void ClearSolarMemoryReservePool(RoleTable? role)
    {
        if (role == null)
        {
            return;
        }

        SanitizeSolarMemoryRoleCards(role, "ClearSolarMemoryReservePool");
        role.UnCardList?.Clear();
        NormalizeSolarMemoryCardCounts(role);

        role.SpecialVarMap ??= new Dictionary<string, string>();
        role.SpecialVarMap[SunExpIds.SolarMemoryDeckConfiguredKey] = "1";
        if (ReferenceEquals(role, RoleTable.Instance))
        {
            SolarMemoryPlayerSetupState.SetFlag(SunExpIds.SolarMemoryDeckConfiguredKey, true);
        }

        UIManager.Instance?.ShowTip("\u65e5\u8000\u56de\u5fc6\u5907\u9009\u724c\u5df2\u6e05\u7a7a", null);
    }

    private static List<Dictionary<string, string>> VisibleCardPacks()
    {
        return SunExpConfigIndex.Rows(DataType.CardPack)
            .Where(pack => !Singleton<GameRuntimeData>.Instance.IsLocked(pack["Id"]) && pack["Id"] != "cardpack_13")
            .ToList();
    }

    private static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (!string.Equals(id, "cardpack_13", StringComparison.OrdinalIgnoreCase)
                || GameCompatibilityApi.ShouldEnableOnlineCardPack());
    }

    private static bool IsSolarMemoryEventCard(IDictionary<string, string> data)
    {
        var id = Field(data, "Id");
        if (ContainsEventCardIdMarker(id))
        {
            return true;
        }

        return ContainsEventTypeMarker(Field(data, "Type"))
            || ContainsEventTypeMarker(Field(data, "Note"))
            || HasLocalizedEventCardType(id)
            || ContainsSolarMemoryEventScriptMarker(Field(data, "Tag"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "Action"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "InitScript"))
            || ContainsSolarMemoryEventScriptMarker(Field(data, "UseScript"));
    }

    private static bool HasLocalizedEventCardType(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return false;
        }

        try
        {
            var data = SunExpConfigIndex.Row(DataType.Card, cardId);
            if (data == null)
            {
                return false;
            }
            return ContainsEventTypeMarker(data.Localize("Type"))
                || ContainsEventTypeMarker(data.Localize("Note"));
        }
        catch
        {
            return false;
        }
    }

    private static void FilterSolarMemoryCardPackCheck(ModHookContext context)
    {
        try
        {
            if (!SolarMemoryModeRuntime.IsSolarMemoryRun()
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<Dictionary<string, string>> cards)
            {
                return;
            }

            var removed = RemoveEventCardData(cards);
            if (removed.Count > 0)
            {
                SunExpLog.Info("[SolarMemoryMode] removed event cards from CardPackCheck: " + string.Join("|", removed));
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Solar memory CardPackCheck filter failed", ex);
        }
    }

    private static List<string> RemoveEventCardData(List<Dictionary<string, string>> cards)
    {
        var removed = new List<string>();
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var data = cards[i];
            if (data != null && IsSolarMemoryEventCard(data))
            {
                removed.Add(Field(data, "Id"));
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
        return removed;
    }

    private static void RemoveEventConfigs(IList<DataConfig> cards, List<string> removed)
    {
        for (var i = cards.Count - 1; i >= 0; i--)
        {
            var config = cards[i];
            var id = CardId(config);
            if (IsSolarMemoryEventCard(id))
            {
                removed.Add(id);
                cards.RemoveAt(i);
            }
        }

        removed.Reverse();
    }

    private static string CardId(DataConfig? config)
    {
        return config == null ? "" : Field(config.data, "Id");
    }

    private static void NormalizeSolarMemoryCardCounts(RoleTable role)
    {
        role.CardTopCount = Math.Max(role.CardTopCount, role.cardList.Count);
        role.CardBottomCount = Math.Min(role.CardBottomCount, role.cardList.Count);
        role.MaxAlCardCount = role.UnCardList == null ? 0 : Math.Min(role.MaxAlCardCount, role.UnCardList.Count);
    }

    private static string Field(IDictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out var value) ? value : "";
    }

    private static bool ContainsEventMarker(string value)
    {
        return ContainsEventCardIdMarker(value)
            || ContainsEventTypeMarker(value)
            || ContainsSolarMemoryEventScriptMarker(value);
    }

    private static bool ContainsEventCardIdMarker(string value)
    {
        return value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0
            || value.StartsWith("event_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("card_event", StringComparison.OrdinalIgnoreCase)
            || value.IndexOf("_event_", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件");
    }

    private static bool ContainsEventTypeMarker(string value)
    {
        return value.Equals("Event", StringComparison.OrdinalIgnoreCase)
            || value.Equals("事件", StringComparison.Ordinal)
            || value.Equals("事件牌", StringComparison.Ordinal)
            || value.Equals("事件卡", StringComparison.Ordinal)
            || value.IndexOf("EventCard", StringComparison.OrdinalIgnoreCase) >= 0
            || value.Contains("事件牌")
            || value.Contains("事件卡");
    }

    private static bool ContainsSolarMemoryEventScriptMarker(string value)
    {
        return value.IndexOf("solar_memory_event", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("SolarMemoryEvent", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
