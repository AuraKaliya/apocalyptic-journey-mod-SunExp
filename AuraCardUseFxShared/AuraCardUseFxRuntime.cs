using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCardUseFx.Shared;

public enum AuraCardUseFxTriggerChannel
{
    LocalCommitted,
    RemoteObserved
}

public sealed class AuraCardUseFxSourceSnapshot
{
    private static readonly Vector3[] WorldCorners = new Vector3[4];

    public AuraCardUseFxSourceSnapshot(Vector2 screenPoint, Vector2 screenSize, float rotationZ, bool isValid)
    {
        ScreenPoint = screenPoint;
        ScreenSize = screenSize;
        RotationZ = rotationZ;
        IsValid = isValid;
    }

    public Vector2 ScreenPoint { get; }

    public Vector2 ScreenSize { get; }

    public float RotationZ { get; }

    public bool IsValid { get; }

    public static AuraCardUseFxSourceSnapshot Capture(Transform? source)
    {
        if (source == null)
        {
            return new AuraCardUseFxSourceSnapshot(Vector2.zero, Vector2.zero, 0f, false);
        }

        try
        {
            var visual = source.Find("Front/icon") ?? source.Find("Front/background") ?? source;
            var canvas = source.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (visual is RectTransform rect)
            {
                rect.GetWorldCorners(WorldCorners);
                var bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[0]);
                var topLeft = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[1]);
                var topRight = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[2]);
                var bottomRight = RectTransformUtility.WorldToScreenPoint(camera, WorldCorners[3]);
                var horizontal = bottomRight - bottomLeft;

                return new AuraCardUseFxSourceSnapshot(
                    (bottomLeft + topRight) * 0.5f,
                    new Vector2(
                        Mathf.Max(1f, Vector2.Distance(bottomLeft, bottomRight)),
                        Mathf.Max(1f, Vector2.Distance(bottomLeft, topLeft))),
                    Mathf.Atan2(horizontal.y, horizontal.x) * Mathf.Rad2Deg,
                    true);
            }

            return new AuraCardUseFxSourceSnapshot(
                RectTransformUtility.WorldToScreenPoint(camera, visual.position),
                Vector2.one,
                visual.eulerAngles.z,
                true);
        }
        catch
        {
            return new AuraCardUseFxSourceSnapshot(Vector2.zero, Vector2.zero, 0f, false);
        }
    }
}

public sealed class AuraCardUseFxTrigger
{
    public AuraCardUseFxTrigger(
        AuraCardUseFxRegistryEntry entry,
        Transform sourceTransform,
        IDataConfig cardConfig,
        long useSequence,
        float createdAt)
        : this(
            entry,
            AuraCardUseFxTriggerChannel.RemoteObserved,
            AuraCardUseFxSourceSnapshot.Capture(sourceTransform),
            sourceTransform,
            cardConfig,
            useSequence,
            createdAt)
    {
    }

    public AuraCardUseFxTrigger(
        AuraCardUseFxRegistryEntry entry,
        AuraCardUseFxTriggerChannel channel,
        AuraCardUseFxSourceSnapshot sourceSnapshot,
        Transform? sourceTransform,
        IDataConfig cardConfig,
        long useSequence,
        float createdAt)
    {
        Entry = entry;
        Channel = channel;
        SourceSnapshot = sourceSnapshot;
        SourceTransform = sourceTransform;
        CardConfig = cardConfig;
        UseSequence = useSequence;
        CreatedAt = createdAt;
    }

    public AuraCardUseFxRegistryEntry Entry { get; }

    public AuraCardUseFxTriggerChannel Channel { get; }

    public AuraCardUseFxSourceSnapshot SourceSnapshot { get; }

    public Transform? SourceTransform { get; }

    public IDataConfig CardConfig { get; }

    public long UseSequence { get; }

    public float CreatedAt { get; }
}

public static class AuraCardUseFxRuntime
{
    public const string RuntimeOwnerId = "AuraCardUseFxShared";
    private const float DedupeSeconds = 2f;
    private const int MaxDedupeEntries = 256;

    private static readonly Stack<LocalCardUseScope> LocalScopes = new();
    private static readonly Stack<ObservedCardUseScope> ObservedScopes = new();
    private static readonly Dictionary<string, float> RecentObserverTriggers = new(StringComparer.OrdinalIgnoreCase);
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
        AuraCardLifecycleRouter.Register(
            modConfig,
            RuntimeOwnerId,
            "LocalCardUse",
            new AuraCardLifecycleSubscription
            {
                BeforeCommonCardUse = BeforeLocalCardUse,
                BeforeAttackCardUse = BeforeLocalCardUse,
                AfterCommonCardUse = AfterLocalCardUse,
                AfterAttackCardUse = AfterLocalCardUse
            },
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraCombatActionRouter.RegisterBefore(
            modConfig,
            RuntimeOwnerId + ".LocalCommitted",
            OnLocalCardUseCommitted,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));

        AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            "FightUI.DoCardUseAnimation",
            BeforeObservedCardUseAnimation,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            "ICard.SetCardStyle",
            AfterObservedSetCardStyle,
            message => AuraSharedLog.DebugLog(RuntimeOwnerId, message, false),
            message => AuraSharedLog.Warn(RuntimeOwnerId, message));
        AuraSharedHooks.RegisterAfterRouted(
            modConfig,
            "FightUI.DoCardUseAnimation",
            AfterObservedCardUseAnimation,
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

        AuraSharedLog.InfoOnce(RuntimeOwnerId, "initialized", "Card-use FX local commit and observer bridges initialized.");
    }

    public static void ClearTransient()
    {
        LocalScopes.Clear();
        ObservedScopes.Clear();
        RecentObserverTriggers.Clear();
    }

    private static void BeforeLocalCardUse(ModHookContext context)
    {
        try
        {
            var card = context.Target as CardItem;
            var config = card?.dataConfig;
            var entries = ResolveEntries(config, AuraCardUseFxTriggerChannel.LocalCommitted);
            LocalScopes.Push(new LocalCardUseScope(
                ++nextUseSequence,
                config,
                entries,
                card?.transform,
                AuraCardUseFxSourceSnapshot.Capture(card?.transform)));
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Local card-use scope begin failed.", ex);
            LocalScopes.Push(LocalCardUseScope.Empty(++nextUseSequence));
        }
    }

    private static void OnLocalCardUseCommitted(AuraCombatActionContext context)
    {
        if (LocalScopes.Count == 0 || context.DataConfig == null)
        {
            return;
        }

        try
        {
            foreach (var scope in LocalScopes)
            {
                if (scope.Committed || scope.Config == null || !SameCard(scope.Config, context.DataConfig))
                {
                    continue;
                }

                scope.Committed = true;
                PublishScope(scope, AuraCardUseFxTriggerChannel.LocalCommitted, dedupeObservers: false);
                return;
            }
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Local card-use commit failed.", ex);
        }
    }

    private static void AfterLocalCardUse(ModHookContext context)
    {
        if (LocalScopes.Count > 0)
        {
            LocalScopes.Pop();
        }
    }

    private static void BeforeObservedCardUseAnimation(ModHookContext context)
    {
        try
        {
            var config = ReadCardConfigFromUseData(context.Arguments);
            var entries = ResolveEntries(config, AuraCardUseFxTriggerChannel.RemoteObserved);
            ObservedScopes.Push(new ObservedCardUseScope(++nextUseSequence, config, entries));
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Observed card-use animation scope begin failed.", ex);
            ObservedScopes.Push(new ObservedCardUseScope(++nextUseSequence, null, Array.Empty<AuraCardUseFxRegistryEntry>()));
        }
    }

    private static void AfterObservedSetCardStyle(ModHookContext context)
    {
        if (ObservedScopes.Count == 0)
        {
            return;
        }

        try
        {
            var scope = ObservedScopes.Peek();
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
                scope.SourceSnapshot = AuraCardUseFxSourceSnapshot.Capture(source);
            }
        }
        catch (Exception ex)
        {
            AuraSharedLog.Error(RuntimeOwnerId, "Observed central card clone capture failed.", ex);
        }
    }

    private static void AfterObservedCardUseAnimation(ModHookContext context)
    {
        if (ObservedScopes.Count == 0)
        {
            return;
        }

        var scope = ObservedScopes.Pop();
        PublishScope(scope, AuraCardUseFxTriggerChannel.RemoteObserved, dedupeObservers: true);
    }

    private static IReadOnlyList<AuraCardUseFxRegistryEntry> ResolveEntries(
        IDataConfig? config,
        AuraCardUseFxTriggerChannel channel)
    {
        return config == null
            ? Array.Empty<AuraCardUseFxRegistryEntry>()
            : AuraCardUseFxRegistryRuntime.Resolve(ReadCardId(config))
                .Where(entry => SupportsChannel(entry, channel))
                .ToArray();
    }

    private static bool SupportsChannel(AuraCardUseFxRegistryEntry entry, AuraCardUseFxTriggerChannel channel)
    {
        return entry.PresentationScope == AuraCardUseFxPresentationScopes.All
               || (channel == AuraCardUseFxTriggerChannel.LocalCommitted
                   && entry.PresentationScope == AuraCardUseFxPresentationScopes.OwnerLocal)
               || (channel == AuraCardUseFxTriggerChannel.RemoteObserved
                   && entry.PresentationScope == AuraCardUseFxPresentationScopes.Observers);
    }

    private static void PublishScope(
        CardUseScope scope,
        AuraCardUseFxTriggerChannel channel,
        bool dedupeObservers)
    {
        if (scope.Config == null || !scope.SourceSnapshot.IsValid || scope.Entries.Count == 0)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (dedupeObservers)
        {
            PruneRecentObservers(now);
        }

        foreach (var entry in scope.Entries)
        {
            if (dedupeObservers)
            {
                var sourceId = scope.SourceTransform == null ? 0 : scope.SourceTransform.GetInstanceID();
                var key = sourceId + ":" + entry.QualifiedEffectId;
                if (RecentObserverTriggers.TryGetValue(key, out var last) && now - last <= DedupeSeconds)
                {
                    continue;
                }

                RecentObserverTriggers[key] = now;
            }

            Publish(new AuraCardUseFxTrigger(
                entry,
                channel,
                scope.SourceSnapshot,
                scope.SourceTransform,
                scope.Config,
                scope.UseSequence,
                now));
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

    private static void PruneRecentObservers(float now)
    {
        if (RecentObserverTriggers.Count == 0)
        {
            return;
        }

        foreach (var key in RecentObserverTriggers
                     .Where(pair => now - pair.Value > DedupeSeconds)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RecentObserverTriggers.Remove(key);
        }

        if (RecentObserverTriggers.Count <= MaxDedupeEntries)
        {
            return;
        }

        foreach (var key in RecentObserverTriggers
                     .OrderBy(pair => pair.Value)
                     .Take(RecentObserverTriggers.Count - MaxDedupeEntries)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            RecentObserverTriggers.Remove(key);
        }
    }

    private abstract class CardUseScope
    {
        protected CardUseScope(
            long useSequence,
            IDataConfig? config,
            IReadOnlyList<AuraCardUseFxRegistryEntry> entries,
            Transform? sourceTransform,
            AuraCardUseFxSourceSnapshot sourceSnapshot)
        {
            UseSequence = useSequence;
            Config = config;
            Entries = entries;
            SourceTransform = sourceTransform;
            SourceSnapshot = sourceSnapshot;
        }

        public long UseSequence { get; }

        public IDataConfig? Config { get; }

        public IReadOnlyList<AuraCardUseFxRegistryEntry> Entries { get; }

        public Transform? SourceTransform { get; set; }

        public AuraCardUseFxSourceSnapshot SourceSnapshot { get; set; }
    }

    private sealed class LocalCardUseScope : CardUseScope
    {
        public LocalCardUseScope(
            long useSequence,
            IDataConfig? config,
            IReadOnlyList<AuraCardUseFxRegistryEntry> entries,
            Transform? sourceTransform,
            AuraCardUseFxSourceSnapshot sourceSnapshot)
            : base(useSequence, config, entries, sourceTransform, sourceSnapshot)
        {
        }

        public bool Committed { get; set; }

        public static LocalCardUseScope Empty(long useSequence)
        {
            return new LocalCardUseScope(
                useSequence,
                null,
                Array.Empty<AuraCardUseFxRegistryEntry>(),
                null,
                AuraCardUseFxSourceSnapshot.Capture(null));
        }
    }

    private sealed class ObservedCardUseScope : CardUseScope
    {
        public ObservedCardUseScope(
            long useSequence,
            IDataConfig? config,
            IReadOnlyList<AuraCardUseFxRegistryEntry> entries)
            : base(
                useSequence,
                config,
                entries,
                null,
                AuraCardUseFxSourceSnapshot.Capture(null))
        {
        }
    }
}
