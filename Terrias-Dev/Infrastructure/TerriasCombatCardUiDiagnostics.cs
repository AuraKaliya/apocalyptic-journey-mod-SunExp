using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Infrastructure;

/// <summary>
/// Associates MOD-owned visual work with the native card-UI method currently
/// being measured.  Disabled performance counters keep this path allocation-free.
/// </summary>
public static class TerriasCombatCardUiDiagnostics
{
    private const int MaxPendingCauses = 24;
    private const int MaxCauseAgeFrames = 8;
    [ThreadStatic] private static Stack<Scope>? scopes;
    [ThreadStatic] private static Stack<BuffLevelChange>? buffLevelChanges;
    [ThreadStatic] private static List<RefreshCause>? pendingCauses;
    [ThreadStatic] private static RefreshBatch? refreshBatch;
    [ThreadStatic] private static CardBreakdown lastDataUpdateBreakdown;

    public static void Begin(string target, ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        scopes ??= new Stack<Scope>();
        scopes.Push(new Scope(target, CardId(context)));
    }

    public static string End(string target, double elapsedMilliseconds)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || scopes == null || scopes.Count == 0)
        {
            return "";
        }

        var scope = scopes.Pop();
        if (!string.Equals(scope.Target, target, StringComparison.Ordinal))
        {
            TerriasPerformanceCounters.Record("CombatCardUi.Diagnostics.StackMismatch");
        }

        if (IsDataUpdateTarget(target))
        {
            lastDataUpdateBreakdown = CardBreakdown.From(scope.Segments);
        }

        if (scopes.Count > 0 && elapsedMilliseconds >= 0d)
        {
            var prefix = "Nested." + target;
            RecordSegment(prefix, elapsedMilliseconds);
            if (scope.Segments != null)
            {
                foreach (var pair in scope.Segments)
                {
                    RecordSegment(prefix + "/" + pair.Key, pair.Value);
                }
            }
        }

        if (scope.Segments == null || scope.Segments.Count == 0)
        {
            return " card=" + scope.CardId
                + "; segments=<native-or-unattributed>"
                + NativeRemainder(target, elapsedMilliseconds, null);
        }

        var parts = new List<string>();
        foreach (var pair in scope.Segments)
        {
            parts.Add(pair.Key + "=" + pair.Value.ToString("0.###") + "ms");
            TerriasPerformanceCounters.Record("CombatCardUi." + target + ".Segment." + pair.Key);
        }

        return " card=" + scope.CardId
            + "; segments=" + string.Join(",", parts)
            + NativeRemainder(target, elapsedMilliseconds, scope.Segments);
    }

    private static string NativeRemainder(
        string target,
        double elapsedMilliseconds,
        Dictionary<string, double>? segments)
    {
        if (!string.Equals(target, "FightUI.CreateCardItemInternal", StringComparison.Ordinal))
        {
            return "";
        }

        var attributed = 0d;
        if (segments != null)
        {
            foreach (var pair in segments)
            {
                if (pair.Key.IndexOf('/') < 0)
                {
                    attributed += Math.Max(0d, pair.Value);
                }
            }
        }

        return "; attributedTopLevelMs="
            + attributed.ToString("0.###")
            + "; nativeOrStaticRemainderMs="
            + Math.Max(0d, elapsedMilliseconds - attributed).ToString("0.###");
    }

    public static void RecordCurrentSegment(string name, long startTimestamp)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || startTimestamp <= 0L || scopes == null || scopes.Count == 0)
        {
            return;
        }

        RecordSegment(name, TerriasPerformanceCounters.ElapsedMilliseconds(startTimestamp));
    }

    public static void BeginRefreshBatch(ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var frame = Time.frameCount;
        var causes = new List<string>();
        if (pendingCauses != null)
        {
            foreach (var cause in pendingCauses)
            {
                if (frame - cause.Frame <= MaxCauseAgeFrames)
                {
                    causes.Add("f" + cause.Frame + ":" + cause.Text);
                }
            }

            pendingCauses.Clear();
        }

        refreshBatch = new RefreshBatch(
            frame,
            FightUI.cardItemList?.Count ?? 0,
            FightUiDiagnosticsApi.SkillCount(context.Target),
            FightUiDiagnosticsApi.CurrentRoleId(),
            causes);
    }

    public static void RecordRefreshCard(ModHookContext context, double elapsedMilliseconds)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || refreshBatch == null)
        {
            return;
        }

        refreshBatch.CardUpdates++;
        var breakdown = lastDataUpdateBreakdown.WithSetCardMsgFallback(elapsedMilliseconds);
        refreshBatch.Cards.Add(new CardTiming(
            CardId(context),
            Math.Max(0d, elapsedMilliseconds),
            breakdown));
        lastDataUpdateBreakdown = default;
    }

    public static string EndRefreshBatch(double elapsedMilliseconds)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || refreshBatch == null)
        {
            return "";
        }

        var batch = refreshBatch;
        refreshBatch = null;
        TerriasPerformanceCounters.Record("CombatCardUi.UpdateCardMsg.Batch");
        var slowest = batch.Cards
            .OrderByDescending(card => card.ElapsedMilliseconds)
            .Take(3)
            .Select(card => card.Format());
        return " batchFrame="
            + batch.Frame
            + "; hand="
            + batch.HandCount
            + "; skills="
            + (batch.SkillCount < 0 ? "unknown" : batch.SkillCount.ToString())
            + "; role="
            + Safe(batch.RoleId)
            + "; cardUpdates="
            + batch.CardUpdates
            + "; causes="
            + (batch.Causes.Count == 0 ? "<unknown>" : string.Join(",", batch.Causes))
            + "; topCards="
            + (batch.Cards.Count == 0 ? "<none>" : string.Join(",", slowest))
            + "; batchMs="
            + Math.Max(0d, elapsedMilliseconds).ToString("0.###");
    }

    public static void BeginBuffLevelChange(ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || context.Target is not BuffItemConfig config)
        {
            return;
        }

        var requested = context.Arguments != null
            && context.Arguments.Length > 0
            && context.Arguments[0] is int value
                ? value
                : config.Level;
        buffLevelChanges ??= new Stack<BuffLevelChange>();
        buffLevelChanges.Push(new BuffLevelChange(config, config.Level, requested));
    }

    public static void EndBuffLevelChange(ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled
            || buffLevelChanges == null
            || buffLevelChanges.Count == 0)
        {
            return;
        }

        var change = buffLevelChanges.Pop();
        var actual = change.Config.Level;
        if (actual == change.Previous)
        {
            return;
        }

        RecordRefreshCause("buff-level(" + OwnerId(change.Config.status)
            + "," + Safe(change.Config.BuffId)
            + "," + change.Previous
            + "->" + actual
            + (actual == change.Requested ? "" : ",requested=" + change.Requested)
            + ")");
    }

    public static void RecordBuffMutation(string operation, ModHookContext context)
    {
        if (!TerriasPerformanceSettings.CountersEnabled)
        {
            return;
        }

        var owner = context.Target as IStatusManager;
        var args = context.Arguments;
        var buffId = "unknown";
        if (args != null && args.Length > 0)
        {
            if (args[0] is string text)
            {
                buffId = text;
            }
            else if (args[0] is IBuffItemConfig config)
            {
                buffId = config.BuffId;
            }
        }

        RecordRefreshCause("buff-" + Safe(operation) + "(" + OwnerId(owner) + "," + Safe(buffId) + ")");
    }

    public static void RecordRefreshCause(string cause)
    {
        if (!TerriasPerformanceSettings.CountersEnabled || string.IsNullOrWhiteSpace(cause))
        {
            return;
        }

        pendingCauses ??= new List<RefreshCause>();
        var frame = Time.frameCount;
        var normalized = cause.Trim();
        if (pendingCauses.Any(item => item.Frame == frame && string.Equals(item.Text, normalized, StringComparison.Ordinal)))
        {
            return;
        }

        if (pendingCauses.Count >= MaxPendingCauses)
        {
            pendingCauses.RemoveAt(0);
        }

        pendingCauses.Add(new RefreshCause(frame, normalized));
    }

    private static void RecordSegment(string name, double elapsedMilliseconds)
    {
        if (scopes == null || scopes.Count == 0)
        {
            return;
        }

        var scope = scopes.Pop();
        var key = string.IsNullOrWhiteSpace(name) ? "unknown" : name.Trim();
        scope.Segments ??= new Dictionary<string, double>(StringComparer.Ordinal);
        scope.Segments[key] = scope.Segments.TryGetValue(key, out var previous)
            ? previous + elapsedMilliseconds
            : elapsedMilliseconds;
        scopes.Push(scope);
    }

    private static string CardId(ModHookContext context)
    {
        if (context.Target is CardItem card && card.dataConfig != null)
        {
            return DictionaryUtil.Get(card.dataConfig.data, "Id", "unknown");
        }

        if (context.Target is ScriptExecutor executor && executor.dataConfig != null)
        {
            return DictionaryUtil.Get(executor.dataConfig.data, "Id", "unknown");
        }

        var args = context.Arguments;
        if (args != null)
        {
            foreach (var arg in args)
            {
                if (arg is IDataConfig config)
                {
                    return DictionaryUtil.Get(config.data, "Id", "unknown");
                }
            }
        }

        return "unknown";
    }

    private static bool IsDataUpdateTarget(string target)
    {
        return string.Equals(target, "CardItem.DataUpdate", StringComparison.Ordinal)
            || string.Equals(target, "AttackCardItem.DataUpdate", StringComparison.Ordinal);
    }

    private static string OwnerId(IStatusManager? owner)
    {
        return Safe(owner?.InstanceId ?? "unknown");
    }

    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value!.Trim().Replace(",", "_");
    }

    private struct Scope
    {
        public Scope(string target, string cardId)
        {
            Target = target ?? "";
            CardId = cardId ?? "unknown";
        }

        public string Target { get; }
        public string CardId { get; }
        public Dictionary<string, double>? Segments { get; set; }
    }

    private readonly struct BuffLevelChange
    {
        public BuffLevelChange(BuffItemConfig config, int previous, int requested)
        {
            Config = config;
            Previous = previous;
            Requested = requested;
        }

        public BuffItemConfig Config { get; }
        public int Previous { get; }
        public int Requested { get; }
    }

    private readonly struct RefreshCause
    {
        public RefreshCause(int frame, string text)
        {
            Frame = frame;
            Text = text;
        }

        public int Frame { get; }
        public string Text { get; }
    }

    private sealed class RefreshBatch
    {
        public RefreshBatch(int frame, int handCount, int skillCount, string roleId, List<string> causes)
        {
            Frame = frame;
            HandCount = handCount;
            SkillCount = skillCount;
            RoleId = roleId;
            Causes = causes;
        }

        public int Frame { get; }
        public int HandCount { get; }
        public int SkillCount { get; }
        public string RoleId { get; }
        public List<string> Causes { get; }
        public List<CardTiming> Cards { get; } = new();
        public int CardUpdates { get; set; }
    }

    private readonly struct CardTiming
    {
        public CardTiming(string cardId, double elapsedMilliseconds, CardBreakdown breakdown)
        {
            CardId = cardId;
            ElapsedMilliseconds = elapsedMilliseconds;
            Breakdown = breakdown;
        }

        public string CardId { get; }
        public double ElapsedMilliseconds { get; }
        public CardBreakdown Breakdown { get; }

        public string Format()
        {
            return CardId
                + "="
                + ElapsedMilliseconds.ToString("0.###")
                + "ms[setMsg="
                + Breakdown.SetCardMsgMilliseconds.ToString("0.###")
                + ",runScript="
                + Breakdown.RunScriptMilliseconds.ToString("0.###")
                + ",description="
                + Breakdown.DescriptionMilliseconds.ToString("0.###")
                + ",translate="
                + Breakdown.TranslateMilliseconds.ToString("0.###")
                + ",terriasInit="
                + Breakdown.TerriasInitMilliseconds.ToString("0.###")
                + ",remainder="
                + Breakdown.RemainderMilliseconds.ToString("0.###")
                + "]";
        }
    }

    private readonly struct CardBreakdown
    {
        private const string SetCardMsgKey = "Nested.ICard.SetCardMsg";
        private const string RunScriptKey = SetCardMsgKey + "/Nested.ScriptExecutor.RunScript";
        private const string DescriptionKey = SetCardMsgKey + "/Nested.LocalizeEx.Description";
        private const string TranslateKey = DescriptionKey + "/Nested.TextTranslator.Translate";

        public CardBreakdown(
            double setCardMsgMilliseconds,
            double runScriptMilliseconds,
            double descriptionMilliseconds,
            double translateMilliseconds,
            double terriasInitMilliseconds)
        {
            SetCardMsgMilliseconds = setCardMsgMilliseconds;
            RunScriptMilliseconds = runScriptMilliseconds;
            DescriptionMilliseconds = descriptionMilliseconds;
            TranslateMilliseconds = translateMilliseconds;
            TerriasInitMilliseconds = terriasInitMilliseconds;
        }

        public double SetCardMsgMilliseconds { get; }
        public double RunScriptMilliseconds { get; }
        public double DescriptionMilliseconds { get; }
        public double TranslateMilliseconds { get; }
        public double TerriasInitMilliseconds { get; }
        public double RemainderMilliseconds => Math.Max(
            0d,
            SetCardMsgMilliseconds - Math.Max(RunScriptMilliseconds, TerriasInitMilliseconds) - DescriptionMilliseconds);

        public static CardBreakdown From(Dictionary<string, double>? segments)
        {
            return new CardBreakdown(
                Segment(segments, SetCardMsgKey),
                SegmentAny(segments, RunScriptKey, "Nested.ScriptExecutor.RunScript"),
                SegmentAny(segments, DescriptionKey, "Nested.LocalizeEx.Description"),
                SegmentAny(
                    segments,
                    TranslateKey,
                    "Nested.LocalizeEx.Description/Nested.TextTranslator.Translate",
                    "Nested.TextTranslator.Translate"),
                SegmentAny(segments, "Manual.CardScripts.Init", "Nested.Manual.CardScripts.Init"));
        }

        public CardBreakdown WithSetCardMsgFallback(double dataUpdateMilliseconds)
        {
            return SetCardMsgMilliseconds > 0d
                ? this
                : new CardBreakdown(
                    Math.Max(0d, dataUpdateMilliseconds),
                    RunScriptMilliseconds,
                    DescriptionMilliseconds,
                    TranslateMilliseconds,
                    TerriasInitMilliseconds);
        }

        private static double Segment(Dictionary<string, double>? segments, string key)
        {
            return segments != null && segments.TryGetValue(key, out var value)
                ? Math.Max(0d, value)
                : 0d;
        }

        private static double SegmentAny(Dictionary<string, double>? segments, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = Segment(segments, key);
                if (value > 0d)
                {
                    return value;
                }
            }

            return 0d;
        }
    }
}
