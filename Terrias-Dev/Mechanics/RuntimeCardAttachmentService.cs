using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

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

public sealed class RuntimeHandAttachmentSpec
{
    public string[] NativeTags { get; set; } = Array.Empty<string>();

    public string[] SpecialTags { get; set; } = Array.Empty<string>();

    public string[] Markers { get; set; } = Array.Empty<string>();

    public bool TemporaryWhiteRadiance { get; set; }

    public string Token { get; set; } = "";

    public string Source { get; set; } = "";
}

public static class RuntimeCardAttachmentService
{
    private const string BurnoutTag = "Burnout";
    private const string FrozeTag = "Froze";
    private const string SnapshotPresentKey = "TerriasRuntimeAttachmentSnapshot";
    private const string SnapshotTagKey = "TerriasRuntimeAttachmentOriginalTag";
    private const string SnapshotSpecialTagKey = "TerriasRuntimeAttachmentOriginalSpecialTag";
    private const string SnapshotMarkersKey = "TerriasRuntimeAttachmentOriginalMarkers";
    private const string AddedVisibleTagsKey = "TerriasRuntimeAttachmentAddedVisibleTags";
    private static readonly object PendingAttachmentSync = new();
    private static readonly Dictionary<string, PendingHandAttachment> PendingHandAttachments = new(StringComparer.Ordinal);
    private static readonly HashSet<string> AppliedNetworkTokens = new(StringComparer.Ordinal);

    public static RuntimeCardAttachment WunaWhiteSunPrayerHandAttachment()
    {
        return new RuntimeCardAttachment(
            nativeTags: new[] { BurnoutTag },
            specialTags: new[] { TerriasIds.WhiteRadianceTag },
            markers: new[] { TerriasIds.TempWhiteRadiance },
            temporaryWhiteRadiance: true);
    }

    public static RuntimeCardAttachment WunaCoronationTokenAttachment()
    {
        return new RuntimeCardAttachment(
            nativeTags: new[] { BurnoutTag, FrozeTag },
            specialTags: new[] { TerriasIds.WhiteRadianceTag },
            markers: new[] { TerriasIds.TempWhiteRadiance },
            temporaryWhiteRadiance: true);
    }

    public static CardGrantMutation AttachMutation(RuntimeCardAttachment attachment)
    {
        return new CardGrantMutation("runtime-card-attachment", config => AttachToConfig(config, attachment));
    }

    public static bool RequestAttachToCurrentHand(ScriptExecutor? executor, RuntimeCardAttachment attachment, string source)
    {
        if (attachment == null)
        {
            return false;
        }

        var token = Guid.NewGuid().ToString("N");
        EnqueueLocalHandAttachment(executor, attachment, source, token);
        BroadcastHandAttachment(attachment, source, token);

        return true;
    }

    public static void ApplyNetworkHandAttachment(RuntimeHandAttachmentSpec? spec, string source)
    {
        if (spec == null)
        {
            return;
        }

        var token = (spec.Token ?? "").Trim();
        if (!MarkNetworkToken(token))
        {
            return;
        }

        var attachment = new RuntimeCardAttachment(
            spec.NativeTags,
            spec.SpecialTags,
            spec.Markers,
            spec.TemporaryWhiteRadiance);
        EnqueueLocalHandAttachment(null, attachment, string.IsNullOrWhiteSpace(spec.Source) ? source : spec.Source, token);
    }

    public static RuntimeCardAttachmentResult AttachToCurrentHand(ScriptExecutor? executor, RuntimeCardAttachment attachment)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        var result = new RuntimeCardAttachmentResult();
        var seenCards = new HashSet<CardItem>();
        var seenConfigs = new HashSet<IDataConfig>();

        var handSnapshot = AuraCombatCardZoneSnapshot.Capture(executor, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = executor != null,
            IncludeExecutorWait = executor != null
        });

        result.UiCards = handSnapshot.Count(AuraCombatCardZoneKind.FightUiActive);
        result.UiWaitCards = handSnapshot.Count(AuraCombatCardZoneKind.FightUiWait);
        result.ExecutorHandCards = handSnapshot.Count(AuraCombatCardZoneKind.ExecutorHand);
        result.ExecutorWaitCards = handSnapshot.Count(AuraCombatCardZoneKind.ExecutorWait);

        foreach (var reference in handSnapshot.Cards)
        {
            if (reference.Card != null)
            {
                AttachToCardItem(reference.Card, attachment, result, seenCards, seenConfigs);
            }
        }

        if (result.TouchedCardItems == 0)
        {
            var managerSnapshot = AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
            {
                IncludeFightUiActive = false,
                IncludeFightUiWait = false,
                IncludeExecutorHand = false,
                IncludeExecutorWait = false,
                IncludeManagerDraw = true
            });
            foreach (var reference in managerSnapshot.Cards)
            {
                if (reference.Zone == AuraCombatCardZoneKind.ManagerDraw)
                {
                    result.ManagerFallbackCards++;
                    AttachToConfig(reference.Config, attachment, result, seenConfigs);
                }
            }
        }

        TerriasPerformanceCounters.RecordDuration("RuntimeCardAttachment.AttachToCurrentHand", start);
        return result;
    }

    public static int ClearTemporaryAttachments(string source)
    {
        var changed = 0;
        var seenCards = new HashSet<CardItem>();
        var seenConfigs = new HashSet<IDataConfig>();
        var seenVars = new HashSet<IDictionary<string, string>>();

        var uiSnapshot = AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = true,
            IncludeFightUiWait = true,
            IncludeExecutorHand = false,
            IncludeExecutorWait = false
        });

        foreach (var reference in uiSnapshot.Cards)
        {
            if (reference.Card != null)
            {
                changed += ClearCardItem(reference.Card, seenCards, seenConfigs, seenVars);
            }
        }

        var managerSnapshot = AuraCombatCardZoneSnapshot.Capture(null, new AuraCombatCardZoneSnapshotOptions
        {
            IncludeFightUiActive = false,
            IncludeFightUiWait = false,
            IncludeExecutorHand = false,
            IncludeExecutorWait = false,
            IncludeManagerDraw = true
        });
        foreach (var reference in managerSnapshot.Cards)
        {
            if (reference.Config != null)
            {
                changed += ClearConfig(reference.Config, seenConfigs, seenVars);
            }
        }

        changed += ClearRoleTableCards(seenConfigs, seenVars);
        if (changed > 0)
        {
            TerriasLog.Debug("Runtime card temporary attachments cleared from " + source + ": changed=" + changed);
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

        if (AppendTokens(config.Vars, TerriasIds.RuntimeMarkersKey, attachment.Markers))
        {
            changed++;
        }

        if (attachment.TemporaryWhiteRadiance && MarkTemporaryWhiteRadiance(config.Vars))
        {
            changed++;
        }

        if (changed > 0)
        {
            RefreshDataConfigTags(config);
        }

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

        var changedBefore = result.Changed;
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

        if (AppendTokens(card.Vars, TerriasIds.RuntimeMarkersKey, attachment.Markers))
        {
            result.Changed++;
        }

        if (AppendTokens(card.dataConfig?.Vars, TerriasIds.RuntimeMarkersKey, attachment.Markers))
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

        if (result.Changed > changedBefore)
        {
            RefreshCardItem(card);
        }
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
            TerriasLog.Debug("Runtime card attachment role-table cleanup skipped: " + ex.Message);
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
        if (changed > 0)
        {
            RefreshDataConfigTags(config);
        }

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
        DictionaryUtil.Set(vars, SnapshotMarkersKey, DictionaryUtil.Get(vars, TerriasIds.RuntimeMarkersKey));
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
            changed += RestoreValue(vars, TerriasIds.RuntimeMarkersKey, SnapshotMarkersKey);
        }
        else
        {
            var nativeTagsToRemove = DictionaryUtil.ContainsToken(DictionaryUtil.Get(data, "Tag"), BurnoutTag)
                ? Array.Empty<string>()
                : new[] { BurnoutTag };
            changed += SetTokensRemoved(vars, "Tag", nativeTagsToRemove);
            changed += SetTokensRemoved(vars, "SpecialTag", new[] { TerriasIds.WhiteRadianceTag });
            changed += SetTokensRemoved(vars, TerriasIds.RuntimeMarkersKey, new[] { TerriasIds.TempWhiteRadiance });
        }

        changed += RemoveKey(vars, TerriasIds.TempWhiteRadiance);
        changed += RemoveKey(vars, TerriasIds.TempWhiteRadianceResolved);
        changed += RemoveKey(vars, TerriasIds.TempWhiteRadianceLockId);
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
                || DictionaryUtil.Get(vars, TerriasIds.TempWhiteRadiance, "0") == "1"
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(vars, TerriasIds.RuntimeMarkersKey), TerriasIds.TempWhiteRadiance));
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

        if (DictionaryUtil.ContainsToken(addedVisibleTags, TerriasIds.WhiteRadianceTag) && card.Tags.Remove(TerriasIds.WhiteRadianceTag))
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
        if (SetIfDifferent(vars, TerriasIds.TempWhiteRadiance, "1"))
        {
            changed = true;
        }

        var resolved = DictionaryUtil.Get(vars, TerriasIds.TempWhiteRadianceResolved);
        if (string.IsNullOrWhiteSpace(resolved) || resolved != "0")
        {
            DictionaryUtil.Set(vars, TerriasIds.TempWhiteRadianceResolved, "0");
            changed = true;
        }

        var assignedLock = string.IsNullOrWhiteSpace(lockId)
            ? EnsureTemporaryWhiteRadianceLock(vars)
            : lockId ?? "";
        if (SetIfDifferent(vars, TerriasIds.TempWhiteRadianceLockId, assignedLock))
        {
            changed = true;
        }

        if (AppendTokens(vars, "SpecialTag", new[] { TerriasIds.WhiteRadianceTag }))
        {
            changed = true;
        }

        if (AppendTokens(vars, TerriasIds.RuntimeMarkersKey, new[] { TerriasIds.TempWhiteRadiance }))
        {
            changed = true;
        }

        return changed;
    }

    private static string EnsureTemporaryWhiteRadianceLock(IDictionary<string, string>? vars)
    {
        var lockId = DictionaryUtil.Get(vars, TerriasIds.TempWhiteRadianceLockId);
        if (!string.IsNullOrWhiteSpace(lockId) && lockId != "0")
        {
            return lockId;
        }

        return ExecutorApi.CombatIntAdd("TerriasTempWhiteRadianceLockSeq", 1).ToString();
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
        TerriasCardRefreshQueue.RequestConfigTagRefresh(config, "RuntimeCardAttachment");
    }

    private static void RefreshCardItem(CardItem card)
    {
        try
        {
            TerriasCardRefreshQueue.RequestFullRefresh(card, "RuntimeCardAttachment");
            if (card.dataConfig != null)
            {
                TerriasCardRefreshQueue.RequestConfigTagRefresh(card.dataConfig, "RuntimeCardAttachment.CardItem");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Runtime card attachment refresh skipped: " + ex.Message);
        }
    }

    private static void FlushPendingHandAttachment()
    {
        PendingHandAttachment[] pending;
        lock (PendingAttachmentSync)
        {
            if (PendingHandAttachments.Count == 0)
            {
                return;
            }

            pending = new PendingHandAttachment[PendingHandAttachments.Count];
            PendingHandAttachments.Values.CopyTo(pending, 0);
            PendingHandAttachments.Clear();
        }

        foreach (var item in pending)
        {
            var result = AttachToCurrentHand(item.Executor, item.Attachment);
            TerriasLog.Info("Runtime hand attachment applied from "
                + item.Source
                + ": "
                + result.ToLogString());
        }
    }

    private static void EnqueueLocalHandAttachment(ScriptExecutor? executor, RuntimeCardAttachment attachment, string source, string token)
    {
        MarkNetworkToken(token);
        lock (PendingAttachmentSync)
        {
            PendingHandAttachments[PendingHandAttachmentKey(attachment, source, token)] = new PendingHandAttachment(executor, attachment, source);
        }

        var enqueued = TerriasFrameDispatcher.RunOnceNextFrame("RuntimeCardAttachment.AttachToCurrentHand", FlushPendingHandAttachment);
        if (!enqueued)
        {
            TerriasPerformanceCounters.Record("RuntimeCardAttachment.HandAttachDeduped");
        }
    }

    private static void BroadcastHandAttachment(RuntimeCardAttachment attachment, string source, string token)
    {
        var runtimeType = FindRuntimeType("Terrias.Dll.Network.TerriasNetworkRuntime");
        if (runtimeType == null || InvokeBool(runtimeType, "IsMultiplayerSession") != true)
        {
            return;
        }

        var commandType = FindRuntimeType("Terrias.Dll.Network.RpcRuntimeHandAttachment");
        if (commandType == null)
        {
            return;
        }

        try
        {
            var command = Activator.CreateInstance(
                commandType,
                new RuntimeHandAttachmentSpec
                {
                    NativeTags = attachment.NativeTags.ToArray(),
                    SpecialTags = attachment.SpecialTags.ToArray(),
                    Markers = attachment.Markers.ToArray(),
                    TemporaryWhiteRadiance = attachment.TemporaryWhiteRadiance,
                    Token = token,
                    Source = source ?? ""
                });
            runtimeType.GetMethod("Send", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new[] { command, source ?? "RuntimeCardAttachment.BroadcastHandAttachment", true });
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("Runtime hand attachment network broadcast failed: " + ex.Message);
        }
    }

    private static bool? InvokeBool(Type type, string name)
    {
        try
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, Array.Empty<object>()) as bool?;
        }
        catch
        {
            return false;
        }
    }

    private static Type? FindRuntimeType(string name)
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
            }
            catch
            {
                // Best-effort runtime bridge.
            }
        }

        return null;
    }

    private static bool MarkNetworkToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        lock (PendingAttachmentSync)
        {
            return AppliedNetworkTokens.Add(token);
        }
    }

    private static string PendingHandAttachmentKey(RuntimeCardAttachment attachment, string source, string token)
    {
        return (source ?? "").Trim()
            + "|token="
            + (token ?? "").Trim()
            + "|native="
            + string.Join(",", attachment.NativeTags)
            + "|special="
            + string.Join(",", attachment.SpecialTags)
            + "|markers="
            + string.Join(",", attachment.Markers)
            + "|temp="
            + attachment.TemporaryWhiteRadiance;
    }

    private readonly struct PendingHandAttachment
    {
        public PendingHandAttachment(ScriptExecutor? executor, RuntimeCardAttachment attachment, string source)
        {
            Executor = executor;
            Attachment = attachment;
            Source = source;
        }

        public ScriptExecutor? Executor { get; }

        public RuntimeCardAttachment Attachment { get; }

        public string Source { get; }
    }
}
