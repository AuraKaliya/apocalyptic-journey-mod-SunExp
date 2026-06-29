using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public sealed class RuntimeCardAttachment
{
    public RuntimeCardAttachment(
        IEnumerable<string>? nativeTags = null,
        IEnumerable<string>? specialTags = null,
        IEnumerable<string>? markers = null,
        bool temporaryWhiteRadiance = false)
    {
        NativeTags = Normalize(nativeTags).ToArray();
        SpecialTags = Normalize(specialTags).ToArray();
        Markers = Normalize(markers).ToArray();
        TemporaryWhiteRadiance = temporaryWhiteRadiance;
    }

    public IReadOnlyList<string> NativeTags { get; }

    public IReadOnlyList<string> SpecialTags { get; }

    public IReadOnlyList<string> Markers { get; }

    public bool TemporaryWhiteRadiance { get; }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal);
    }
}

public sealed class RuntimeCardAttachmentResult
{
    public int UiCards { get; set; }

    public int ExecutorHandCards { get; set; }

    public int ExecutorWaitCards { get; set; }

    public int UiWaitCards { get; set; }

    public int ManagerFallbackCards { get; set; }

    public int TouchedCardItems { get; set; }

    public int TouchedConfigs { get; set; }

    public int Changed { get; set; }

    public string ToLogString()
    {
        return "uiCards=" + UiCards
            + ", executorHand=" + ExecutorHandCards
            + ", executorWait=" + ExecutorWaitCards
            + ", uiWait=" + UiWaitCards
            + ", managerFallback=" + ManagerFallbackCards
            + ", touchedCardItems=" + TouchedCardItems
            + ", touchedConfigs=" + TouchedConfigs
            + ", changed=" + Changed;
    }
}

public static class RuntimeCardAttachmentService
{
    private const string BurnoutTag = "Burnout";
    private const string FrozeTag = "Froze";
    private const string SnapshotPresentKey = "SunExpRuntimeAttachmentSnapshot";
    private const string SnapshotTagKey = "SunExpRuntimeAttachmentOriginalTag";
    private const string SnapshotSpecialTagKey = "SunExpRuntimeAttachmentOriginalSpecialTag";
    private const string SnapshotMarkersKey = "SunExpRuntimeAttachmentOriginalMarkers";
    private const string AddedVisibleTagsKey = "SunExpRuntimeAttachmentAddedVisibleTags";

    public static RuntimeCardAttachment WunaWhiteSunPrayerHandAttachment()
    {
        return new RuntimeCardAttachment(
            nativeTags: new[] { BurnoutTag },
            specialTags: new[] { SunExpIds.WhiteRadianceTag },
            markers: new[] { SunExpIds.TempWhiteRadiance },
            temporaryWhiteRadiance: true);
    }

    public static RuntimeCardAttachment WunaCoronationTokenAttachment()
    {
        return new RuntimeCardAttachment(
            nativeTags: new[] { BurnoutTag, FrozeTag },
            specialTags: new[] { SunExpIds.WhiteRadianceTag },
            markers: new[] { SunExpIds.TempWhiteRadiance },
            temporaryWhiteRadiance: true);
    }

    public static CardGrantMutation AttachMutation(RuntimeCardAttachment attachment)
    {
        return new CardGrantMutation("runtime-card-attachment", config => AttachToConfig(config, attachment));
    }

    public static RuntimeCardAttachmentResult AttachToCurrentHand(ScriptExecutor? executor, RuntimeCardAttachment attachment)
    {
        var result = new RuntimeCardAttachmentResult();
        var seenCards = new HashSet<CardItem>();
        var seenConfigs = new HashSet<IDataConfig>();

        try
        {
            foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            {
                result.UiCards++;
                AttachToCardItem(card, attachment, result, seenCards, seenConfigs);
            }

            foreach (var card in FightUI.WaitCard ?? new List<CardItem>())
            {
                result.UiWaitCards++;
                AttachToCardItem(card, attachment, result, seenCards, seenConfigs);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment UI hand scan skipped: " + ex.Message);
        }

        if (executor != null)
        {
            try
            {
                foreach (var card in executor.HandCard ?? Enumerable.Empty<CardItem>())
                {
                    result.ExecutorHandCards++;
                    AttachToCardItem(card, attachment, result, seenCards, seenConfigs);
                }

                foreach (var card in executor.WaitCard ?? Enumerable.Empty<CardItem>())
                {
                    result.ExecutorWaitCards++;
                    AttachToCardItem(card, attachment, result, seenCards, seenConfigs);
                }
            }
            catch (Exception ex)
            {
                SunExpLog.Debug("Runtime card attachment executor hand scan skipped: " + ex.Message);
            }
        }

        try
        {
            var manager = FightCardManager.Instance;
            if (manager != null && result.TouchedCardItems == 0)
            {
                foreach (var config in manager.cardList ?? Enumerable.Empty<DataConfig>())
                {
                    result.ManagerFallbackCards++;
                    AttachToConfig(config, attachment, result, seenConfigs);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment manager fallback skipped: " + ex.Message);
        }

        return result;
    }

    public static int ClearTemporaryAttachments(string source)
    {
        var changed = 0;
        var seenCards = new HashSet<CardItem>();
        var seenConfigs = new HashSet<IDataConfig>();
        var seenVars = new HashSet<IDictionary<string, string>>();

        try
        {
            foreach (var card in FightUI.cardItemList ?? new List<CardItem>())
            {
                changed += ClearCardItem(card, seenCards, seenConfigs, seenVars);
            }

            foreach (var card in FightUI.WaitCard ?? new List<CardItem>())
            {
                changed += ClearCardItem(card, seenCards, seenConfigs, seenVars);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment UI cleanup skipped: " + ex.Message);
        }

        try
        {
            var manager = FightCardManager.Instance;
            if (manager != null)
            {
                foreach (var config in manager.cardList ?? Enumerable.Empty<DataConfig>())
                {
                    changed += ClearConfig(config, seenConfigs, seenVars);
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment fight deck cleanup skipped: " + ex.Message);
        }

        changed += ClearRoleTableCards(seenConfigs, seenVars);
        if (changed > 0)
        {
            SunExpLog.Debug("Runtime card temporary attachments cleared from " + source + ": changed=" + changed);
        }

        return changed;
    }

    public static int AttachToConfig(IDataConfig? config, RuntimeCardAttachment attachment)
    {
        if (config == null)
        {
            return 0;
        }

        var changed = 0;
        CaptureOriginalVars(config.Vars);
        if (AppendNativeTokens(config, attachment.NativeTags))
        {
            changed++;
        }

        if (AppendTokens(config.Vars, "SpecialTag", attachment.SpecialTags))
        {
            changed++;
        }

        if (AppendTokens(config.Vars, SunExpIds.RuntimeMarkersKey, attachment.Markers))
        {
            changed++;
        }

        if (attachment.TemporaryWhiteRadiance && MarkTemporaryWhiteRadiance(config.Vars))
        {
            changed++;
        }

        RefreshDataConfigTags(config);
        return changed;
    }

    private static void AttachToCardItem(
        CardItem? card,
        RuntimeCardAttachment attachment,
        RuntimeCardAttachmentResult result,
        HashSet<CardItem> seenCards,
        HashSet<IDataConfig> seenConfigs)
    {
        if (card == null || !seenCards.Add(card))
        {
            return;
        }

        result.TouchedCardItems++;
        if (card.dataConfig != null && seenConfigs.Add(card.dataConfig))
        {
            result.TouchedConfigs++;
        }

        CaptureOriginalVars(card.Vars);
        CaptureOriginalVars(card.dataConfig?.Vars);
        if (AppendNativeTokens(card.dataConfig, attachment.NativeTags))
        {
            result.Changed++;
        }

        if (AppendTokens(card.Vars, "SpecialTag", attachment.SpecialTags))
        {
            result.Changed++;
        }

        if (AppendTokens(card.dataConfig?.Vars, "SpecialTag", attachment.SpecialTags))
        {
            result.Changed++;
        }

        if (AppendTokens(card.Vars, SunExpIds.RuntimeMarkersKey, attachment.Markers))
        {
            result.Changed++;
        }

        if (AppendTokens(card.dataConfig?.Vars, SunExpIds.RuntimeMarkersKey, attachment.Markers))
        {
            result.Changed++;
        }

        foreach (var tag in attachment.NativeTags.Concat(attachment.SpecialTags))
        {
            if (card.Tags != null && !card.Tags.Contains(tag))
            {
                card.Tags.Add(tag);
                AppendTokens(card.Vars, AddedVisibleTagsKey, new[] { tag });
                result.Changed++;
            }
        }

        if (attachment.TemporaryWhiteRadiance)
        {
            var lockId = EnsureTemporaryWhiteRadianceLock(card.Vars);
            if (MarkTemporaryWhiteRadiance(card.Vars, lockId))
            {
                result.Changed++;
            }

            if (MarkTemporaryWhiteRadiance(card.dataConfig?.Vars, lockId))
            {
                result.Changed++;
            }
        }

        RefreshCardItem(card);
    }

    private static void AttachToConfig(
        IDataConfig? config,
        RuntimeCardAttachment attachment,
        RuntimeCardAttachmentResult result,
        HashSet<IDataConfig> seenConfigs)
    {
        if (config == null || !seenConfigs.Add(config))
        {
            return;
        }

        result.TouchedConfigs++;
        result.Changed += AttachToConfig(config, attachment);
    }

    private static int ClearRoleTableCards(HashSet<IDataConfig> seenConfigs, HashSet<IDictionary<string, string>> seenVars)
    {
        var changed = 0;
        try
        {
            var roleTableType = FindType("RoleTable");
            var roleTable = roleTableType == null ? null : ReadStaticMember(roleTableType, "Instance");
            if (roleTable == null)
            {
                return changed;
            }

            foreach (var name in new[] { "cardList", "UnCardList" })
            {
                if (ReadMember(roleTable, name) is not System.Collections.IEnumerable cards)
                {
                    continue;
                }

                foreach (var card in cards)
                {
                    if (card is IDataConfig config)
                    {
                        changed += ClearConfig(config, seenConfigs, seenVars);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment role-table cleanup skipped: " + ex.Message);
        }

        return changed;
    }

    private static int ClearCardItem(
        CardItem? card,
        HashSet<CardItem> seenCards,
        HashSet<IDataConfig> seenConfigs,
        HashSet<IDictionary<string, string>> seenVars)
    {
        if (card == null || !seenCards.Add(card))
        {
            return 0;
        }

        var hadTemporary = HasTemporaryAttachment(card.Vars) || HasTemporaryAttachment(card.dataConfig?.Vars);
        var addedVisibleTags = DictionaryUtil.Get(card.Vars, AddedVisibleTagsKey);
        var changed = 0;
        changed += ClearVars(card.Vars, card.data, seenVars);
        changed += ClearConfig(card.dataConfig, seenConfigs, seenVars);
        if (hadTemporary)
        {
            changed += RemoveCardTags(card, addedVisibleTags, card.dataConfig?.data ?? card.data);
        }

        RefreshCardItem(card);
        return changed;
    }

    private static int ClearConfig(
        IDataConfig? config,
        HashSet<IDataConfig> seenConfigs,
        HashSet<IDictionary<string, string>> seenVars)
    {
        if (config == null || !seenConfigs.Add(config))
        {
            return 0;
        }

        var changed = ClearVars(config.Vars, config.data, seenVars);
        RefreshDataConfigTags(config);
        return changed;
    }

    private static void CaptureOriginalVars(IDictionary<string, string>? vars)
    {
        if (vars == null || DictionaryUtil.Get(vars, SnapshotPresentKey) == "1")
        {
            return;
        }

        DictionaryUtil.Set(vars, SnapshotPresentKey, "1");
        DictionaryUtil.Set(vars, SnapshotTagKey, DictionaryUtil.Get(vars, "Tag"));
        DictionaryUtil.Set(vars, SnapshotSpecialTagKey, DictionaryUtil.Get(vars, "SpecialTag"));
        DictionaryUtil.Set(vars, SnapshotMarkersKey, DictionaryUtil.Get(vars, SunExpIds.RuntimeMarkersKey));
    }

    private static int ClearVars(
        IDictionary<string, string>? vars,
        IDictionary<string, string>? data,
        HashSet<IDictionary<string, string>> seenVars)
    {
        if (vars == null || !seenVars.Add(vars) || !HasTemporaryAttachment(vars))
        {
            return 0;
        }

        var changed = 0;
        if (DictionaryUtil.Get(vars, SnapshotPresentKey) == "1")
        {
            changed += RestoreValue(vars, "Tag", SnapshotTagKey);
            changed += RestoreValue(vars, "SpecialTag", SnapshotSpecialTagKey);
            changed += RestoreValue(vars, SunExpIds.RuntimeMarkersKey, SnapshotMarkersKey);
        }
        else
        {
            var nativeTagsToRemove = DictionaryUtil.ContainsToken(DictionaryUtil.Get(data, "Tag"), BurnoutTag)
                ? Array.Empty<string>()
                : new[] { BurnoutTag };
            changed += SetTokensRemoved(vars, "Tag", nativeTagsToRemove);
            changed += SetTokensRemoved(vars, "SpecialTag", new[] { SunExpIds.WhiteRadianceTag });
            changed += SetTokensRemoved(vars, SunExpIds.RuntimeMarkersKey, new[] { SunExpIds.TempWhiteRadiance });
        }

        changed += RemoveKey(vars, SunExpIds.TempWhiteRadiance);
        changed += RemoveKey(vars, SunExpIds.TempWhiteRadianceResolved);
        changed += RemoveKey(vars, SunExpIds.TempWhiteRadianceLockId);
        changed += RemoveKey(vars, SnapshotPresentKey);
        changed += RemoveKey(vars, SnapshotTagKey);
        changed += RemoveKey(vars, SnapshotSpecialTagKey);
        changed += RemoveKey(vars, SnapshotMarkersKey);
        changed += RemoveKey(vars, AddedVisibleTagsKey);
        return changed;
    }

    private static bool HasTemporaryAttachment(IDictionary<string, string>? vars)
    {
        return vars != null
            && (DictionaryUtil.Get(vars, SnapshotPresentKey) == "1"
                || DictionaryUtil.Get(vars, SunExpIds.TempWhiteRadiance, "0") == "1"
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(vars, SunExpIds.RuntimeMarkersKey), SunExpIds.TempWhiteRadiance));
    }

    private static int RestoreValue(IDictionary<string, string> vars, string key, string snapshotKey)
    {
        var original = DictionaryUtil.Get(vars, snapshotKey);
        if (DictionaryUtil.Get(vars, key) == original)
        {
            return 0;
        }

        DictionaryUtil.Set(vars, key, original);
        return 1;
    }

    private static int SetTokensRemoved(IDictionary<string, string> vars, string key, IEnumerable<string> tokens)
    {
        var existing = DictionaryUtil.Get(vars, key);
        var next = RemoveTokens(existing, tokens);
        if (existing == next)
        {
            return 0;
        }

        DictionaryUtil.Set(vars, key, next);
        return 1;
    }

    private static string RemoveTokens(string existing, IEnumerable<string> tokens)
    {
        var remove = new HashSet<string>(
            tokens.Where(token => !string.IsNullOrWhiteSpace(token)).Select(token => token.Trim()),
            StringComparer.Ordinal);
        if (remove.Count == 0 || string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return string.Join(",", existing.Split(',')
            .Select(token => token.Trim())
            .Where(token => token.Length > 0 && !remove.Contains(token)));
    }

    private static int RemoveCardTags(CardItem card, string addedVisibleTags, IDictionary<string, string>? data)
    {
        var changed = 0;
        if (card.Tags == null)
        {
            return changed;
        }

        if (DictionaryUtil.ContainsToken(addedVisibleTags, BurnoutTag) && card.Tags.Remove(BurnoutTag))
        {
            changed++;
        }

        if (DictionaryUtil.ContainsToken(addedVisibleTags, SunExpIds.WhiteRadianceTag) && card.Tags.Remove(SunExpIds.WhiteRadianceTag))
        {
            changed++;
        }

        return changed;
    }

    private static int RemoveKey(IDictionary<string, string> vars, string key)
    {
        return vars.Remove(key) ? 1 : 0;
    }

    private static bool MarkTemporaryWhiteRadiance(IDictionary<string, string>? vars, string? lockId = null)
    {
        if (vars == null)
        {
            return false;
        }

        var changed = false;
        if (SetIfDifferent(vars, SunExpIds.TempWhiteRadiance, "1"))
        {
            changed = true;
        }

        var resolved = DictionaryUtil.Get(vars, SunExpIds.TempWhiteRadianceResolved);
        if (string.IsNullOrWhiteSpace(resolved) || resolved != "0")
        {
            DictionaryUtil.Set(vars, SunExpIds.TempWhiteRadianceResolved, "0");
            changed = true;
        }

        var assignedLock = string.IsNullOrWhiteSpace(lockId)
            ? EnsureTemporaryWhiteRadianceLock(vars)
            : lockId ?? "";
        if (SetIfDifferent(vars, SunExpIds.TempWhiteRadianceLockId, assignedLock))
        {
            changed = true;
        }

        if (AppendTokens(vars, "SpecialTag", new[] { SunExpIds.WhiteRadianceTag }))
        {
            changed = true;
        }

        if (AppendTokens(vars, SunExpIds.RuntimeMarkersKey, new[] { SunExpIds.TempWhiteRadiance }))
        {
            changed = true;
        }

        return changed;
    }

    private static string EnsureTemporaryWhiteRadianceLock(IDictionary<string, string>? vars)
    {
        var lockId = DictionaryUtil.Get(vars, SunExpIds.TempWhiteRadianceLockId);
        if (!string.IsNullOrWhiteSpace(lockId) && lockId != "0")
        {
            return lockId;
        }

        return ExecutorApi.CombatIntAdd("SunExpTempWhiteRadianceLockSeq", 1).ToString();
    }

    private static Type? FindType(string name)
    {
        var direct = Type.GetType(name);
        if (direct != null)
        {
            return direct;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var exact = assembly.GetType(name);
                if (exact != null)
                {
                    return exact;
                }

                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == name || type.FullName == name)
                    {
                        return type;
                    }
                }
            }
            catch
            {
                // Best-effort compatibility scan across game assemblies.
            }
        }

        return null;
    }

    private static object? ReadMember(object source, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance;
        var type = source.GetType();
        var property = type.GetProperty(name, flags);
        if (property != null)
        {
            try
            {
                return property.GetValue(source);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(name, flags);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(source);
        }
        catch
        {
            return null;
        }
    }

    private static object? ReadStaticMember(Type type, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Static;
        var property = type.GetProperty(name, flags);
        if (property != null)
        {
            try
            {
                return property.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        var field = type.GetField(name, flags);
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static bool AppendTokens(IDictionary<string, string>? values, string key, IEnumerable<string> tokens)
    {
        if (values == null)
        {
            return false;
        }

        var changed = false;
        var existing = DictionaryUtil.Get(values, key);
        foreach (var token in tokens.Where(token => !string.IsNullOrWhiteSpace(token)).Select(token => token.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (DictionaryUtil.ContainsToken(existing, token))
            {
                continue;
            }

            existing = string.IsNullOrWhiteSpace(existing) ? token : existing + "," + token;
            changed = true;
        }

        if (changed)
        {
            DictionaryUtil.Set(values, key, existing);
        }

        return changed;
    }

    private static bool AppendNativeTokens(IDataConfig? config, IEnumerable<string> tokens)
    {
        if (config?.Vars == null)
        {
            return false;
        }

        var existing = DictionaryUtil.Get(config.Vars, "Tag");
        if (string.IsNullOrWhiteSpace(existing))
        {
            DictionaryUtil.Set(config.Vars, "Tag", DictionaryUtil.Get(config.data, "Tag"));
        }

        return AppendTokens(config.Vars, "Tag", tokens);
    }

    private static bool SetIfDifferent(IDictionary<string, string>? values, string key, string value)
    {
        if (values == null || DictionaryUtil.Get(values, key) == value)
        {
            return false;
        }

        DictionaryUtil.Set(values, key, value);
        return true;
    }

    private static void RefreshDataConfigTags(IDataConfig config)
    {
        try
        {
            FightCardManager.Instance?.RefreshTag(config);
        }
        catch
        {
            // Presentation-only refresh.
        }
    }

    private static void RefreshCardItem(CardItem card)
    {
        try
        {
            SunExpCardRefreshQueue.RequestFullRefresh(card, "RuntimeCardAttachment");
            if (card.dataConfig != null)
            {
                FightCardManager.Instance?.RefreshTag(card.dataConfig);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Runtime card attachment refresh skipped: " + ex.Message);
        }
    }
}
