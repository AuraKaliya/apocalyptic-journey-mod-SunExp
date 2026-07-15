using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCardUseFx.Shared;

public sealed class AuraCardUseFxTrigger
{
    public AuraCardUseFxTrigger(
        AuraCardUseFxRegistryEntry entry,
        Transform sourceTransform,
        IDataConfig cardConfig,
        long useSequence,
        float createdAt)
    {
        Entry = entry;
        SourceTransform = sourceTransform;
        CardConfig = cardConfig;
        UseSequence = useSequence;
        CreatedAt = createdAt;
    }

    public AuraCardUseFxRegistryEntry Entry { get; }

    public Transform SourceTransform { get; }

    public IDataConfig CardConfig { get; }

    public long UseSequence { get; }

    public float CreatedAt { get; }
}

public static class AuraCardUseFxRuntime
{
    public const string RuntimeOwnerId = "AuraCardUseFxShared";
    private const float DedupeSeconds = 2f;
    private const int MaxDedupeEntries = 256;

    private static readonly Stack<CardUseScope> Scopes = new();
    private static readonly Dictionary<string, float> RecentTriggers = new(StringComparer.OrdinalIgnoreCase);
    private static bool initialized;
    private static long nextUseSequence;

    public static event Action<AuraCardUseFxTrigger>? Triggered;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            "FightUI.DoCardUseAnimation",
            BeforeCardUseAnimation,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            "ICard.SetCardStyle",
            AfterSetCardStyle,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            "FightUI.DoCardUseAnimation",
            AfterCardUseAnimation,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));

        foreach (var target in new[] { "Fight_Win.Init", "Fight_Loss.Init", "Fight_Escape.Init" })
        {
            AuraSharedHooks.RegisterBeforeRouted(
                modConfig,
                target,
                _ => ClearTransient(),
                message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
                message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        }

        AuraSharedLog.InfoOnce(RuntimeOwnerId, "initialized", "Card-use FX trigger bridge initialized.");
    }

    public static void ClearTransient()
    {
        Scopes.Clear();
        RecentTriggers.Clear();
    }

    private static void BeforeCardUseAnimation(ModHookContext context)
    {
        try
        {
            var config = ReadCardConfigFromUseData(context.Arguments);
            var entries = config == null
                ? Array.Empty<AuraCardUseFxRegistryEntry>()
                : AuraCardUseFxRegistryRuntime.Resolve(ReadCardId(config)).ToArray();
            Scopes.Push(new CardUseScope(++nextUseSequence, config, entries));
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Card-use animation scope begin failed.", ex);
            Scopes.Push(new CardUseScope(++nextUseSequence, null, Array.Empty<AuraCardUseFxRegistryEntry>()));
        }
    }

    private static void AfterSetCardStyle(ModHookContext context)
    {
        if (Scopes.Count == 0)
        {
            return;
        }

        try
        {
            var scope = Scopes.Peek();
            if (scope.Config == null || scope.Entries.Count == 0 || scope.SourceTransform != null)
            {
                return;
            }

            var args = context.Arguments;
            if (args == null || args.Length < 2 || args[0] is not Transform source || args[1] is not IDataConfig config)
            {
                return;
            }

            if (SameCard(scope.Config, config))
            {
                scope.SourceTransform = source;
            }
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Central card clone capture failed.", ex);
        }
    }

    private static void AfterCardUseAnimation(ModHookContext context)
    {
        if (Scopes.Count == 0)
        {
            return;
        }

        var scope = Scopes.Pop();
        if (scope.Config == null || scope.SourceTransform == null || scope.Entries.Count == 0)
        {
            return;
        }

        var now = Time.unscaledTime;
        PruneRecent(now);
        foreach (var entry in scope.Entries)
        {
            var key = scope.SourceTransform.GetInstanceID() + ":" + entry.QualifiedEffectId;
            if (RecentTriggers.TryGetValue(key, out var last) && now - last <= DedupeSeconds)
            {
                continue;
            }

            RecentTriggers[key] = now;
            Publish(new AuraCardUseFxTrigger(entry, scope.SourceTransform, scope.Config, scope.UseSequence, now));
        }
    }

    private static IDataConfig? ReadCardConfigFromUseData(object[]? args)
    {
        if (args == null || args.Length == 0 || args[0] == null)
        {
            return null;
        }

        var payload = args[0];
        var field = payload.GetType().GetField("cardData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(payload) as IDataConfig;
    }

    private static string ReadCardId(IDataConfig config)
    {
        if (config.Vars != null && config.Vars.TryGetValue("Id", out var runtimeId) && !string.IsNullOrWhiteSpace(runtimeId))
        {
            return runtimeId;
        }

        return config.data != null && config.data.TryGetValue("Id", out var baseId)
            ? baseId ?? ""
            : "";
    }

    private static bool SameCard(IDataConfig expected, IDataConfig actual)
    {
        if (ReferenceEquals(expected, actual))
        {
            return true;
        }

        var expectedInstance = expected.InstanceID ?? "";
        var actualInstance = actual.InstanceID ?? "";
        return expectedInstance.Length > 0
            && string.Equals(expectedInstance, actualInstance, StringComparison.Ordinal);
    }

    private static void Publish(AuraCardUseFxTrigger trigger)
    {
        var handlers = Triggered;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<AuraCardUseFxTrigger> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(trigger);
            }
            catch (Exception ex)
            {
                AuraSharedLog.Error(RuntimeOwnerId, "Card-use FX subscriber failed.", ex);
            }
        }
    }

    private static void PruneRecent(float now)
    {
        if (RecentTriggers.Count == 0)
        {
            return;
        }

        foreach (var key in RecentTriggers
                     .Where(pair => now - pair.Value > DedupeSeconds)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RecentTriggers.Remove(key);
        }

        if (RecentTriggers.Count <= MaxDedupeEntries)
        {
            return;
        }

        foreach (var key in RecentTriggers
                     .OrderBy(pair => pair.Value)
                     .Take(RecentTriggers.Count - MaxDedupeEntries)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RecentTriggers.Remove(key);
        }
    }

    private sealed class CardUseScope
    {
        public CardUseScope(long useSequence, IDataConfig? config, IReadOnlyList<AuraCardUseFxRegistryEntry> entries)
        {
            UseSequence = useSequence;
            Config = config;
            Entries = entries;
        }

        public long UseSequence { get; }

        public IDataConfig? Config { get; }

        public IReadOnlyList<AuraCardUseFxRegistryEntry> Entries { get; }

        public Transform? SourceTransform { get; set; }
    }
}
