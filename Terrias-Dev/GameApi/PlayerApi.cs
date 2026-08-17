using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Data.Save;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class PlayerApi
{
    private static readonly object TypeCacheSync = new();
    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);
    private static Type? playerInfoType;
    private static bool playerInfoResolved;

    private static object? PlayerInfo
    {
        get
        {
            if (playerInfoResolved)
            {
                return playerInfoType;
            }

            playerInfoType = typeof(ScriptExecutor).GetNestedType("PlayerInfo", BindingFlags.Public | BindingFlags.NonPublic);
            playerInfoResolved = true;
            return playerInfoType;
        }
    }

    public static int GetSkillTime(string key)
    {
        if (TryReadSkillTime(key, out var existing))
        {
            return existing;
        }

        var skillTime = GetStaticMember(PlayerInfo, "SkillTime");
        if (skillTime == null || string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        skillTime.GetType().GetMethod("set_Item")?.Invoke(skillTime, new object[] { key, 0 });
        return 0;
    }

    public static bool TryReadSkillTime(string key, out int value)
    {
        value = 0;
        var skillTime = GetStaticMember(PlayerInfo, "SkillTime");
        if (skillTime == null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            var contains = skillTime.GetType().GetMethod("ContainsKey")?.Invoke(skillTime, new object[] { key }) as bool?;
            if (contains != true)
            {
                return false;
            }

            var current = skillTime.GetType().GetMethod("get_Item")?.Invoke(skillTime, new object[] { key });
            value = current is int intValue ? intValue : DictionaryUtil.ParseInt(Convert.ToString(current));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetSkillTime(string key, int value)
    {
        var skillTime = GetStaticMember(PlayerInfo, "SkillTime");
        if (skillTime == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        skillTime.GetType().GetMethod("set_Item")?.Invoke(skillTime, new object[] { key, Math.Max(0, value) });
    }

    public static void SetGameVar(string key, string value)
    {
        InvokeStaticPlayerInfo("SetGameVar", key, value);
    }

    public static string GetGameVar(string key, string fallback = "")
    {
        var value = InvokeStaticPlayerInfo("GetGameVar", key);
        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    public static string ScopedGameVarKey(string key, IStatusManager? status)
    {
        return ScopedGameVarKeyForScope(key, status?.InstanceId);
    }

    public static string ScopedGameVarKeyForScope(string key, string? scopeId)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        var safeScopeId = SanitizeGameVarKeyPart(scopeId);
        return string.IsNullOrWhiteSpace(safeScopeId) ? key : key + "_" + safeScopeId;
    }

    public static string GetScopedGameVar(string key, IStatusManager? status, string fallback = "", bool migrateLegacyWhenSolo = false)
    {
        var scopedKey = ScopedGameVarKey(key, status);
        if (string.IsNullOrWhiteSpace(scopedKey))
        {
            return fallback;
        }

        if (scopedKey == key)
        {
            return GetGameVar(key, fallback);
        }

        var scopedValue = GetGameVar(scopedKey, "");
        if (!string.IsNullOrWhiteSpace(scopedValue))
        {
            return scopedValue;
        }

        if (migrateLegacyWhenSolo && !IsMultiplayerSession())
        {
            var legacyValue = GetGameVar(key, "");
            if (!string.IsNullOrWhiteSpace(legacyValue))
            {
                SetGameVar(scopedKey, legacyValue);
                return legacyValue;
            }
        }

        return fallback;
    }

    public static void SetScopedGameVar(string key, IStatusManager? status, string value)
    {
        var scopedKey = ScopedGameVarKey(key, status);
        if (string.IsNullOrWhiteSpace(scopedKey))
        {
            return;
        }

        SetGameVar(scopedKey, value);
    }

    public static string GetScopedGameVarForScope(string key, string? scopeId, string fallback = "")
    {
        var scopedKey = ScopedGameVarKeyForScope(key, scopeId);
        return string.IsNullOrWhiteSpace(scopedKey) ? fallback : GetGameVar(scopedKey, fallback);
    }

    public static void SetScopedGameVarForScope(string key, string? scopeId, string value)
    {
        var scopedKey = ScopedGameVarKeyForScope(key, scopeId);
        if (!string.IsNullOrWhiteSpace(scopedKey))
        {
            SetGameVar(scopedKey, value);
        }
    }

    public static void ShowCaption(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        InvokeStaticPlayerInfo("ShowCaption", text);
    }

    public static string GetCurrentCareerId()
    {
        try
        {
            return DictionaryUtil.Get(RoleTable.Instance?.Career?.data, "Id");
        }
        catch
        {
            return "";
        }
    }

    public static bool AddMoney(int amount)
    {
        if (amount == 0)
        {
            return true;
        }

        var next = Math.Max(0L, Math.Min(int.MaxValue, (long)GetMoney() + amount));
        return SetStaticMember(PlayerInfo, "Money", (int)next);
    }

    public static int GetMoney()
    {
        return Math.Max(0, DictionaryUtil.ParseInt(Convert.ToString(GetStaticMember(PlayerInfo, "Money"))));
    }

    public static bool TrySpendMoney(int amount)
    {
        var requested = Math.Max(0, amount);
        if (requested == 0)
        {
            return true;
        }

        var current = GetMoney();
        return current >= requested && SetStaticMember(PlayerInfo, "Money", current - requested);
    }

    public static int SpendMoneyUpTo(int amount)
    {
        var spent = Math.Min(GetMoney(), Math.Max(0, amount));
        return spent > 0 && !TrySpendMoney(spent) ? 0 : spent;
    }

    public static void AddCard(string cardId)
    {
        InvokeStaticPlayerInfo("AddCard", cardId);
    }

    public static string GetCareerIdForPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return "";
        }

        try
        {
            var roleTables = GameSaveManager.GetRoleTables();
            if (roleTables != null
                && roleTables.TryGetValue(playerId, out var roleTable)
                && roleTable?.Career != null)
            {
                return DictionaryUtil.Get(roleTable.Career.data, "Id");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[PlayerApi] player career lookup failed: player=" + playerId + "; error=" + ex.Message);
        }

        return "";
    }

    public static string GetSpecialVar(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var values = GetStaticMember(PlayerInfo, "SpecialVars") as IDictionary;
        if (values == null || !values.Contains(key))
        {
            return fallback;
        }

        return Convert.ToString(values[key]) ?? fallback;
    }

    public static void SetSpecialVar(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var values = GetStaticMember(PlayerInfo, "SpecialVars") as IDictionary;
        if (values == null)
        {
            return;
        }

        values[key] = value ?? "";
    }

    public static bool TryAddCardToDeck(string cardId, out string grantedCardId, out string message)
    {
        grantedCardId = "";
        message = "";
        var resolved = CardApi.ResolveCardId(cardId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            message = "unknown cardId=" + cardId;
            return false;
        }

        var candidates = new[]
            {
                resolved,
                (cardId ?? "").Trim()
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var candidate in candidates)
        {
            var before = OwnedCardSnapshot();
            var invoked = TryInvokeStaticPlayerInfo("AddCard", out var error, candidate);
            var after = OwnedCardSnapshot();
            if (after.Count > before.Count || after.Any(id => !before.Contains(id)))
            {
                grantedCardId = candidate;
                message = "";
                if (!invoked)
                {
                    TerriasLog.Warn("PlayerInfo.AddCard changed deck but returned failure: " + error);
                }

                return true;
            }

            message = invoked
                ? "deck did not change after AddCard(" + candidate + ")"
                : error;
        }

        TerriasLog.Warn("PlayerInfo.AddCard verification failed: " + message);
        return false;
    }

    public static void AddRelic(string relicId)
    {
        InvokeStaticPlayerInfo("AddRelic", relicId);
    }

    public static void AddBless(string blessId)
    {
        InvokeStaticPlayerInfo("AddBless", blessId);
    }

    public static void EndEvent()
    {
        InvokeStaticPlayerInfo("EndEvent");
    }

    public static void EventTryChangeMap()
    {
        InvokeStaticPlayerInfo("EventTryChangeMap");
    }

    private static object? InvokeStaticPlayerInfo(string methodName, params object[] args)
    {
        var playerInfo = PlayerInfo as Type;
        if (playerInfo == null)
        {
            return null;
        }

        try
        {
            return playerInfo.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, args);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("PlayerInfo." + methodName + " failed: " + ex.Message);
            return null;
        }
    }

    private static object? GetStaticMember(object? typeObject, string name)
    {
        var type = typeObject as Type;
        if (type == null)
        {
            return null;
        }

        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private static object? GetInstanceMember(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        var type = target.GetType();
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
    }

    public static bool IsMultiplayerSession()
    {
        try
        {
            var entryType = FindType("GameEntryUI");
            var playerCount = GetStaticMember(entryType, "playerCount");
            if (playerCount is int count && count > 1)
            {
                return true;
            }

            var server = GetStaticMember(FindType("GameServer"), "Instance");
            var lobby = GetInstanceMember(server, "LobbyInfo");
            var players = GetInstanceMember(lobby, "AddedPlayers") as ICollection;
            return players != null && players.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvokeStaticPlayerInfo(string methodName, out string error, params object[] args)
    {
        error = "";
        var playerInfo = PlayerInfo as Type;
        if (playerInfo == null)
        {
            error = "PlayerInfo unavailable";
            return false;
        }

        try
        {
            var method = playerInfo.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                error = "PlayerInfo." + methodName + " unavailable";
                return false;
            }

            method.Invoke(null, args);
            return true;
        }
        catch (Exception ex)
        {
            error = "PlayerInfo." + methodName + " failed: " + ex.Message;
            TerriasLog.Warn(error);
            return false;
        }
    }

    private static HashSet<string> OwnedCardSnapshot()
    {
        var snapshot = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            AddOwnedCards(snapshot, RoleTable.Instance?.cardList);
            AddOwnedCards(snapshot, RoleTable.Instance?.UnCardList);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Owned card snapshot failed: " + ex.Message);
        }

        return snapshot;
    }

    private static void AddOwnedCards(HashSet<string> snapshot, IEnumerable? cards)
    {
        if (cards == null)
        {
            return;
        }

        foreach (var card in cards)
        {
            if (card == null)
            {
                continue;
            }

            var id = card is IDataConfig dataConfig
                ? dataConfig.InstanceID
                : Convert.ToString(GetInstanceMember(card, "InstanceID"));
            if (!string.IsNullOrWhiteSpace(id))
            {
                snapshot.Add(id);
            }
        }
    }

    public static string LocalPlayerStatusId()
    {
        try
        {
            var fightPlayer = GetStaticMember(FindType("FightPlayer"), "Instance");
            var status = GetInstanceMember(fightPlayer, "Status");
            return Convert.ToString(GetInstanceMember(status, "InstanceId")) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static Type? FindType(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (TypeCacheSync)
        {
            if (TypeCache.TryGetValue(name, out var cached))
            {
                return cached;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name || type.FullName == name)
                    {
                        lock (TypeCacheSync)
                        {
                            TypeCache[name] = type;
                        }

                        return type;
                    }
                }
            }
            catch
            {
                // Some runtime assemblies can reject GetTypes; skip them.
            }
        }

        return null;
    }

    private static string SanitizeGameVarKeyPart(string? value)
    {
        if (value == null)
        {
            return "";
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        var chars = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            chars[i] = char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_';
        }

        return new string(chars);
    }

    private static bool SetStaticMember(object? typeObject, string name, object value)
    {
        var type = typeObject as Type;
        if (type == null)
        {
            return false;
        }

        try
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null)
            {
                property.SetValue(null, Convert.ChangeType(value, property.PropertyType));
                return true;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, Convert.ChangeType(value, field.FieldType));
                return true;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("PlayerInfo." + name + " set failed: " + ex.Message);
        }

        return false;
    }
}
