using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CardPackExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace CardPackExp.Dll.Hooks;

public static class CardPackSelectionRuntime
{
    private const string OnlineCardPackId = "cardpack_13";
    private const int MaxCardPackCheckDiagnostics = 30;
    private const int DefaultCustomCardPackCount = 6;
    private static readonly FieldInfo? HiddenEnabledCardPackField = typeof(CardPackUI)
        .GetField("hiddenEnabledCardPack", BindingFlags.Instance | BindingFlags.NonPublic);
    private static int cardPackCheckDiagnosticCount;
    private static HashSet<string>? lastKnownValidCardPacks;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "GameConfigManager.CardPackCheck", LogCardPackCheckDiagnostic);
        RegisterAfter(modConfig, "NormalMapManager.InitRoleTable", ConfigureAndLogNormalRoleTable);
        RegisterBefore(modConfig, "CardPackUI.Init", EnsureRuntimePacksBeforeCardPackInit);
        RegisterAfter(modConfig, "CardPackUI.Init", SyncSelectedPacksFromCardPackUi);
        RegisterAfter(modConfig, "CardPackUI.SetPackEnabled", SyncSelectedPacksFromCardPackUi);
        RegisterAfter(modConfig, "CardPackUI.OnDisable", SyncSelectedPacksFromCardPackUi);
        RegisterAfter(modConfig, "CardPackUI.OnDestroy", SyncSelectedPacksFromRuntime);
        RegisterBefore(modConfig, "GameEntryUI.NormalGame", SyncSelectedPacksBeforeStart);
        RegisterBefore(modConfig, "GameEntryUI.StartGame", SyncSelectedPacksBeforeStart);
        StarterDeckRuntime.Initialize(modConfig);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            CardPackExpLog.Info("Hook registered: " + target);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Warn("Hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, action);
            CardPackExpLog.Info("Hook registered: " + target);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Warn("Hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void SyncSelectedPacksBeforeStart(ModHookContext context)
    {
        try
        {
            if (context.Target is not GameEntryUI gameEntry)
            {
                return;
            }

            var selected = ResolveSelectedCardPacks(ReadSelectedCardPacks(gameEntry.cardPackUI), "GameEntryUI.StartGame", true);
            ApplySelectedCardPacks(selected, "GameEntryUI.StartGame");
            StarterDeckRuntime.CaptureSelectedPacks(selected);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to sync selected card packs before game start", ex);
        }
    }

    private static void LogCardPackCheckDiagnostic(ModHookContext context)
    {
        try
        {
            EnsureRuntimeCardPacks("GameConfigManager.CardPackCheck");

            if (cardPackCheckDiagnosticCount >= MaxCardPackCheckDiagnostics
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not List<Dictionary<string, string>> cards)
            {
                return;
            }

            cardPackCheckDiagnosticCount++;
            var manager = context.Target as GameConfigManager ?? Singleton<GameConfigManager>.Instance;
            var activePacks = Singleton<GameRuntimeData>.Instance.UseCardPack.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!GameConfigManager.ShouldEnableOnlineCardPack())
            {
                activePacks.Remove(OnlineCardPackId);
            }

            var byPack = cards
                .GroupBy(card => manager.GetPackBelong(card), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key + "=" + group.Count())
                .Take(16)
                .ToList();
            var kept = cards
                .Where(card => activePacks.Contains(manager.GetPackBelong(card)))
                .ToList();
            var rejected = cards
                .Where(card => !activePacks.Contains(manager.GetPackBelong(card)))
                .ToList();

            CardPackExpLog.Info("[DIAG CardPackCheck #" + cardPackCheckDiagnosticCount + "] caller=" + GuessCaller()
                + "; input=" + cards.Count
                + "; kept=" + kept.Count
                + "; active=" + string.Join("|", activePacks.OrderBy(id => id))
                + "; poolByPack=" + string.Join(", ", byPack)
                + "; keptExamples=" + ExampleIds(kept)
                + "; rejectedExamples=" + ExampleIds(rejected));

            if (cardPackCheckDiagnosticCount == MaxCardPackCheckDiagnostics)
            {
                CardPackExpLog.Info("[DIAG CardPackCheck] reached log limit; further CardPackCheck diagnostics are suppressed.");
            }
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to log CardPackCheck diagnostic", ex);
        }
    }

    private static void ConfigureAndLogNormalRoleTable(ModHookContext context)
    {
        try
        {
            var roleTable = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as RoleTable
                : RoleTable.Instance;
            if (roleTable == null)
            {
                CardPackExpLog.Warn("[DIAG InitRoleTable] roleTable is null.");
                return;
            }

            var originalDeckCount = roleTable.cardList.Count;
            var originalReserveCount = roleTable.UnCardList.Count;
            var originalReserveCapacity = roleTable.MaxAlCardCount;

            StarterDeckRuntime.MarkPending(roleTable, "NormalMapManager.InitRoleTable");

            var activePacks = EnsureRuntimeCardPacks("NormalMapManager.InitRoleTable")
                .OrderBy(id => id)
                .ToList();

            CardPackExpLog.Info("[DIAG InitRoleTable] activePacks=" + string.Join("|", activePacks)
                + "; originalDeckCount=" + originalDeckCount
                + "; deckCount=" + roleTable.cardList.Count
                + "; cardLimits=" + roleTable.CardBottomCount + "/" + roleTable.CardTopCount
                + "; reserveCapacity=" + originalReserveCapacity + "->" + roleTable.MaxAlCardCount
                + "; reserveCountBefore=" + originalReserveCount
                + "; reserveCount=" + roleTable.UnCardList.Count);
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to configure NormalMapManager.InitRoleTable deck reserve", ex);
        }
    }

    private static HashSet<string> DefaultCustomCardPacks()
    {
        var visiblePacks = Singleton<GameConfigManager>.Instance.GetTable(DataType.CardPack)
            .Getlines()
            .Where(row => row.TryGetValue("Id", out var id)
                && IsValidPackForCurrentLobby(id)
                && !Singleton<GameRuntimeData>.Instance.IsLocked(id))
            .Select(row => row["Id"])
            .Take(DefaultCustomCardPackCount)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (visiblePacks.Count > 0)
        {
            return visiblePacks;
        }

        return Singleton<GameRuntimeData>.Instance.UseCardPack
            .Where(IsValidPackForCurrentLobby)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void SyncSelectedPacksFromCardPackUi(ModHookContext context)
    {
        try
        {
            if (context.Target is not CardPackUI cardPackUI)
            {
                return;
            }

            ApplySelectedCardPacks(ReadSelectedCardPacks(cardPackUI), "CardPackUI");
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to sync selected card packs from CardPackUI", ex);
        }
    }

    private static void EnsureRuntimePacksBeforeCardPackInit(ModHookContext context)
    {
        try
        {
            EnsureRuntimeCardPacks("CardPackUI.Init");
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to repair selected card packs before CardPackUI.Init", ex);
        }
    }

    private static void SyncSelectedPacksFromRuntime(ModHookContext context)
    {
        try
        {
            ApplySelectedCardPacks(ReadSelectedCardPacks(null), "Runtime");
        }
        catch (Exception ex)
        {
            CardPackExpLog.Error("Failed to sync selected card packs from runtime", ex);
        }
    }

    private static void ApplySelectedCardPacks(HashSet<string> selected, string source)
    {
        var resolved = ResolveSelectedCardPacks(selected, source, true);
        if (resolved.Count == 0)
        {
            CardPackExpLog.Warn("Skipped card-pack sync from " + source + " because no selected packs or fallback packs were found.");
            return;
        }

        WriteRuntimeCardPacks(resolved);
        CardPackExpLog.Info("Synced card packs from " + source + " (" + resolved.Count + "): "
            + string.Join(", ", resolved.OrderBy(id => id)));
    }

    private static HashSet<string> EnsureRuntimeCardPacks(string source)
    {
        var current = ReadSelectedCardPacks(null);
        if (current.Count > 0)
        {
            RememberValidCardPacks(current);
            return current;
        }

        var repaired = ResolveSelectedCardPacks(current, source, true);
        if (repaired.Count > 0)
        {
            WriteRuntimeCardPacks(repaired);
        }

        return repaired;
    }

    private static HashSet<string> ResolveSelectedCardPacks(HashSet<string> selected, string source, bool repairEmpty)
    {
        var valid = selected
            .Where(IsValidPackForCurrentLobby)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (valid.Count > 0)
        {
            RememberValidCardPacks(valid);
            return valid;
        }

        if (!repairEmpty)
        {
            return valid;
        }

        var fallback = lastKnownValidCardPacks != null && lastKnownValidCardPacks.Count > 0
            ? lastKnownValidCardPacks.Where(IsValidPackForCurrentLobby).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : DefaultCustomCardPacks();
        if (fallback.Count > 0)
        {
            RememberValidCardPacks(fallback);
            CardPackExpLog.Warn("[DIAG CardPackGuard] repaired empty card-pack selection from " + source
                + "; fallback=" + string.Join("|", fallback.OrderBy(id => id)));
        }

        return fallback;
    }

    private static void RememberValidCardPacks(HashSet<string> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        lastKnownValidCardPacks = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteRuntimeCardPacks(HashSet<string> selected)
    {
        Singleton<GameRuntimeData>.Instance.UseCardPack = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Singleton<GameRuntimeData>.Instance.Save();
        RememberValidCardPacks(selected);
    }

    private static HashSet<string> ReadSelectedCardPacks(CardPackUI? cardPackUI)
    {
        if (cardPackUI == null)
        {
            return Singleton<GameRuntimeData>.Instance.UseCardPack
                .Where(IsValidPackForCurrentLobby)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var selected = ReadHiddenEnabledCardPacks(cardPackUI);
        selected.UnionWith(cardPackUI.UseCardPack.Where(IsValidPackForCurrentLobby));
        if (GameConfigManager.ShouldEnableOnlineCardPack())
        {
            selected.Add(OnlineCardPackId);
        }
        else
        {
            selected.Remove(OnlineCardPackId);
        }

        return selected;
    }

    internal static List<string> CardIdsFromPacks(IEnumerable<string> packIds)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packIds
                     .Where(IsValidPackForCurrentLobby)
                     .OrderBy(id => id))
        {
            foreach (var pair in Singleton<GameConfigManager>.Instance.GetPackItems(packId))
            {
                if (pair.Key != DataType.Card)
                {
                    continue;
                }

                foreach (var card in pair.Value)
                {
                    if (card.TryGetValue("Id", out var id) && !string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
    }

    private static HashSet<string> ReadHiddenEnabledCardPacks(CardPackUI cardPackUI)
    {
        if (HiddenEnabledCardPackField?.GetValue(cardPackUI) is IEnumerable<string> hiddenPacks)
        {
            return hiddenPacks
                .Where(IsValidPackForCurrentLobby)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsValidPackForCurrentLobby(string id)
    {
        return !string.IsNullOrWhiteSpace(id)
            && (!string.Equals(id, OnlineCardPackId, StringComparison.OrdinalIgnoreCase) || GameConfigManager.ShouldEnableOnlineCardPack());
    }

    private static string ExampleIds(List<Dictionary<string, string>> cards)
    {
        if (cards.Count == 0)
        {
            return "-";
        }

        return string.Join("|", cards
            .Select(card => card.TryGetValue("Id", out var id) ? id : "?")
            .Take(8));
    }

    private static string GuessCaller()
    {
        try
        {
            var trace = new StackTrace(false);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                var type = method?.DeclaringType;
                if (method == null || type == null)
                {
                    continue;
                }

                var typeName = type.FullName ?? type.Name;
                if (typeName.Contains("CardPackExp")
                    || typeName.Contains("GameConfigManager")
                    || typeName.Contains("Rougamo"))
                {
                    continue;
                }

                return type.Name + "." + method.Name;
            }
        }
        catch
        {
            return "unknown";
        }

        return "unknown";
    }
}
