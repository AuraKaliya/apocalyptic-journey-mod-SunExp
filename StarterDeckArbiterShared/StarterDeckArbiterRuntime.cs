using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Witch.Core;

namespace StarterDeckArbiter.Shared;

public sealed class StarterDeckClaim
{
    public string Owner { get; set; } = "";
    public string Scope { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string Source { get; set; } = "";
    public string State { get; set; } = StarterDeckArbiterRuntime.StatePending;
    public string AppliedKey { get; set; } = "";
    public string AppliedModeKey { get; set; } = "";
    public string AppliedMode { get; set; } = "";
    public string LegacyMode { get; set; } = "";
    public int DeckSize { get; set; } = 11;
    public string SourceName { get; set; } = "StarterDeck";
    public bool MarkLegacyCardPackApplied { get; set; } = true;
}

public static class StarterDeckArbiterRuntime
{
    public const string OwnerKey = "StarterDeck.Owner";
    public const string ScopeKey = "StarterDeck.Scope";
    public const string StateKey = "StarterDeck.State";
    public const string SourceKey = "StarterDeck.Source";
    public const string ModeKey = "StarterDeck.Mode";
    public const string CardsKey = "StarterDeck.Cards";
    public const string LegacyCardPackAppliedKey = "CardPackExp.StarterDeckApplied";
    public const string StatePending = "pending";
    public const string StateApplied = "applied";
    public const string StateOfficial = "official";

    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicOrPrivateStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PublicOrPrivateInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool ApplyDeck(
        RoleTable? roleTable,
        IEnumerable<string> cardIds,
        StarterDeckClaim claim,
        Func<string, bool>? rejectCard = null,
        bool sync = true)
    {
        if (roleTable == null)
        {
            Warn("ApplyDeck skipped: role table is null. owner=" + claim.Owner);
            return false;
        }

        if (roleTable.cardList == null)
        {
            Warn("ApplyDeck skipped: role card list is null. owner=" + claim.Owner);
            return false;
        }

        var cards = NormalizeDeck(cardIds, rejectCard);
        if (claim.DeckSize > 0 && cards.Count != claim.DeckSize)
        {
            Warn("ApplyDeck skipped: deck size mismatch. owner="
                + claim.Owner
                + ", expected="
                + claim.DeckSize
                + ", actual="
                + cards.Count);
            return false;
        }

        roleTable.cardList.Clear();
        foreach (var cardId in cards)
        {
            roleTable.cardList.Add(new DataConfig(cardId, DataType.Card));
        }

        NormalizeRoleCounts(roleTable);
        WriteClaim(roleTable, claim, StateApplied, string.Join("|", cards));
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".ApplyDeck");
        }

        Log("Applied deck. owner=" + claim.Owner + ", scope=" + claim.Scope + ", cards=" + cards.Count);
        return true;
    }

    public static void ClaimOwnership(RoleTable? roleTable, StarterDeckClaim claim, string state, bool sync)
    {
        if (roleTable == null)
        {
            return;
        }

        WriteClaim(roleTable, claim, string.IsNullOrWhiteSpace(state) ? StatePending : state, null);
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".ClaimOwnership");
        }
    }

    public static void KeepOfficialDeck(RoleTable? roleTable, StarterDeckClaim claim, bool sync = true)
    {
        if (roleTable == null)
        {
            return;
        }

        NormalizeRoleCounts(roleTable);
        WriteClaim(roleTable, claim, StateOfficial, null);
        if (sync)
        {
            SyncRoleTable(roleTable, claim.SourceName + ".KeepOfficialDeck");
        }

        Log("Kept official deck. owner=" + claim.Owner + ", scope=" + claim.Scope);
    }

    public static bool HasApplied(RoleTable? roleTable, string appliedKey, string owner)
    {
        if (roleTable?.SpecialVarMap == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(appliedKey)
            && roleTable.SpecialVarMap.TryGetValue(appliedKey, out var applied)
            && applied == "1")
        {
            return true;
        }

        return roleTable.SpecialVarMap.TryGetValue(OwnerKey, out var currentOwner)
            && string.Equals(currentOwner, owner, StringComparison.OrdinalIgnoreCase)
            && roleTable.SpecialVarMap.TryGetValue(StateKey, out var state)
            && IsFinishedState(state);
    }

    public static bool IsOwnedByOther(RoleTable? roleTable, string owner, out string otherOwner)
    {
        otherOwner = "";
        if (roleTable?.SpecialVarMap == null)
        {
            return false;
        }

        if (!roleTable.SpecialVarMap.TryGetValue(OwnerKey, out var currentOwner)
            || string.IsNullOrWhiteSpace(currentOwner)
            || string.Equals(currentOwner, owner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        otherOwner = currentOwner;
        return true;
    }

    public static void SyncRoleTable(RoleTable? roleTable, string source)
    {
        if (roleTable == null)
        {
            return;
        }

        TryUpdateSaveRole(roleTable, source);
        TryCmdSyncRoleTable(roleTable, source);
    }

    private static List<string> NormalizeDeck(IEnumerable<string> cardIds, Func<string, bool>? rejectCard)
    {
        var cards = new List<string>();
        foreach (var cardId in cardIds)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                continue;
            }

            var id = cardId.Trim();
            if (rejectCard != null && rejectCard(id))
            {
                continue;
            }

            cards.Add(id);
        }

        return cards;
    }

    private static void NormalizeRoleCounts(RoleTable roleTable)
    {
        if (roleTable.cardList == null)
        {
            return;
        }

        roleTable.CardTopCount = Math.Max(roleTable.CardTopCount, roleTable.cardList.Count);
        roleTable.CardBottomCount = Math.Min(roleTable.CardBottomCount, roleTable.cardList.Count);
    }

    private static void WriteClaim(RoleTable roleTable, StarterDeckClaim claim, string state, string? deckCards)
    {
        roleTable.SpecialVarMap ??= new Dictionary<string, string>();
        WriteIfNotEmpty(roleTable.SpecialVarMap, OwnerKey, claim.Owner);
        WriteIfNotEmpty(roleTable.SpecialVarMap, ScopeKey, claim.Scope);
        WriteIfNotEmpty(roleTable.SpecialVarMap, StateKey, state);
        WriteIfNotEmpty(roleTable.SpecialVarMap, SourceKey, claim.Source);
        WriteIfNotEmpty(roleTable.SpecialVarMap, ModeKey, claim.ModeId);

        if (!string.IsNullOrWhiteSpace(deckCards))
        {
            roleTable.SpecialVarMap[CardsKey] = deckCards;
        }

        if (IsFinishedState(state))
        {
            WriteIfNotEmpty(roleTable.SpecialVarMap, claim.AppliedKey, "1");
            WriteIfNotEmpty(roleTable.SpecialVarMap, claim.AppliedModeKey, claim.AppliedMode);
        }

        if (!claim.MarkLegacyCardPackApplied)
        {
            return;
        }

        roleTable.SpecialVarMap[LegacyCardPackAppliedKey] = "1";
        if (!string.IsNullOrWhiteSpace(claim.LegacyMode))
        {
            roleTable.SpecialVarMap[LegacyCardPackAppliedKey + ".Mode"] = claim.LegacyMode;
        }
    }

    private static void WriteIfNotEmpty(IDictionary<string, string> map, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            map[key] = value ?? "";
        }
    }

    private static bool IsFinishedState(string state)
    {
        return string.Equals(state, StateApplied, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, StateOfficial, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryUpdateSaveRole(RoleTable roleTable, string source)
    {
        try
        {
            var type = FindType("Data.Save.GameSaveManager") ?? FindType("GameSaveManager");
            if (IsClientOnlySession())
            {
                Log("Skipped GameSaveManager.UpdateRoles on client-only session. source=" + source);
                return;
            }

            if (!HasWritableSaveRoleTable(type))
            {
                Log("Skipped GameSaveManager.UpdateRoles before writable save role table. source=" + source);
                return;
            }

            var method = type?.GetMethod("UpdateRoles", PublicStatic, null, new[] { typeof(RoleTable) }, null);
            method?.Invoke(null, new object[] { roleTable });
        }
        catch (Exception ex)
        {
            Warn("GameSaveManager.UpdateRoles failed from " + source + ": " + RootMessage(ex));
        }
    }

    private static void TryCmdSyncRoleTable(RoleTable roleTable, string source)
    {
        try
        {
            if (!IsNetworkClientReady())
            {
                Log("Skipped CmdSyncRoleTable before client ready. source=" + source);
                return;
            }

            var playerManager = StaticMemberValue(FindType("PlayerManager"), "Instance");
            var method = playerManager?.GetType().GetMethod("CmdSyncRoleTable", PublicOrPrivateInstance, null, new[] { typeof(RoleTable) }, null);
            method?.Invoke(playerManager, new object[] { roleTable });
        }
        catch (Exception ex)
        {
            Warn("PlayerManager.CmdSyncRoleTable failed from " + source + ": " + RootMessage(ex));
        }
    }

    private static bool IsClientOnlySession()
    {
        try
        {
            var playerManager = StaticMemberValue(FindType("PlayerManager"), "Instance");
            if (playerManager == null)
            {
                return false;
            }

            if (InstanceMemberValue(playerManager, "isClientOnly") is bool isClientOnly && isClientOnly)
            {
                return true;
            }

            if (InstanceMemberValue(playerManager, "isServer") is bool isServer)
            {
                return !isServer;
            }
        }
        catch
        {
            // Fall back to the legacy local-save path when multiplayer state cannot be inspected.
        }

        return false;
    }

    private static bool HasWritableSaveRoleTable(Type? gameSaveManagerType)
    {
        if (gameSaveManagerType == null)
        {
            return false;
        }

        try
        {
            var getNowSave = gameSaveManagerType.GetMethod("GetNowSave", PublicStatic, null, Type.EmptyTypes, null);
            if (getNowSave == null)
            {
                return true;
            }

            var save = getNowSave.Invoke(null, Array.Empty<object>());
            return InstanceMemberValue(save, "roleTable") != null;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNetworkClientReady()
    {
        try
        {
            var networkClient = FindType("Mirror.NetworkClient");
            var value = StaticMemberValue(networkClient, "ready");
            return value is bool ready && ready;
        }
        catch
        {
            return false;
        }
    }

    private static object? StaticMemberValue(Type? type, string memberName)
    {
        if (type == null)
        {
            return null;
        }

        return type.GetProperty(memberName, PublicOrPrivateStatic)?.GetValue(null)
            ?? type.GetField(memberName, PublicOrPrivateStatic)?.GetValue(null);
    }

    private static object? InstanceMemberValue(object? source, string memberName)
    {
        if (source == null)
        {
            return null;
        }

        var type = source.GetType();
        return type.GetProperty(memberName, PublicOrPrivateInstance)?.GetValue(source)
            ?? type.GetField(memberName, PublicOrPrivateInstance)?.GetValue(source);
    }

    private static Type? FindType(string fullNameOrName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = null;
            try
            {
                type = assembly.GetType(fullNameOrName, false);
            }
            catch
            {
                // ignored
            }

            if (type != null)
            {
                return type;
            }

            foreach (var candidate in SafeTypes(assembly))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.FullName, fullNameOrName, StringComparison.Ordinal)
                    || string.Equals(candidate.Name, fullNameOrName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<Type?> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static string RootMessage(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: { } inner }
            ? inner.Message
            : ex.Message;
    }

    private static void Log(string message)
    {
        Debug.Log("[StarterDeckArbiter] " + message);
    }

    private static void Warn(string message)
    {
        Debug.LogWarning("[StarterDeckArbiter] " + message);
    }
}
