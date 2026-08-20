using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AuraShared.Core;

public static class AuraFeatureSwitchRuntime
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, RegisteredFeature> Registered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> LocalOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> EffectiveOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static long revision;

    public static event Action<AuraFeatureSwitchChange>? EffectiveStateChanged;

    public static void RegisterFeature(string ownerModId, string featureId, bool defaultEnabled, string source = "")
    {
        var key = FeatureKey(ownerModId, featureId);
        if (key.Length == 0)
        {
            return;
        }

        AuraFeatureSwitchChange? change = null;
        lock (Gate)
        {
            var before = IsEnabledNoLock(ownerModId, featureId, "");
            Registered[key] = new RegisteredFeature(Normalize(ownerModId), Normalize(featureId), defaultEnabled, Normalize(source));
            var after = IsEnabledNoLock(ownerModId, featureId, "");
            if (before != after)
            {
                change = CreateChange(ownerModId, featureId, before, after, source);
            }
        }

        Publish(change);
    }

    public static void SetLocalOverride(string toolOwnerId, string ownerModId, string featureId, bool? enabled)
    {
        var key = OverrideKey(toolOwnerId, ownerModId, featureId);
        if (key.Length == 0)
        {
            return;
        }

        AuraFeatureSwitchChange? change = null;
        lock (Gate)
        {
            var before = IsEnabledNoLock(ownerModId, featureId, "");
            if (enabled.HasValue)
            {
                LocalOverrides[key] = enabled.Value;
                EffectiveOverrides[FeatureKey(ownerModId, featureId)] = enabled.Value;
            }
            else
            {
                LocalOverrides.Remove(key);
                EffectiveOverrides.Remove(FeatureKey(ownerModId, featureId));
            }

            var after = IsEnabledNoLock(ownerModId, featureId, "");
            if (before != after)
            {
                change = CreateChange(ownerModId, featureId, before, after, toolOwnerId);
            }
        }

        Publish(change);
    }

    public static bool IsEnabled(string ownerModId, string featureId, string toolOwnerId = "")
    {
        var featureKey = FeatureKey(ownerModId, featureId);
        if (featureKey.Length == 0)
        {
            return false;
        }

        lock (Gate)
        {
            return IsEnabledNoLock(ownerModId, featureId, toolOwnerId);
        }
    }

    private static bool IsEnabledNoLock(string ownerModId, string featureId, string toolOwnerId)
    {
        var featureKey = FeatureKey(ownerModId, featureId);
        if (featureKey.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(toolOwnerId)
            && LocalOverrides.TryGetValue(OverrideKey(toolOwnerId, ownerModId, featureId), out var overrideValue))
        {
            return overrideValue;
        }

        if (EffectiveOverrides.TryGetValue(featureKey, out var effectiveOverride))
        {
            return effectiveOverride;
        }

        return !Registered.TryGetValue(featureKey, out var feature) || feature.DefaultEnabled;
    }

    private static AuraFeatureSwitchChange CreateChange(
        string ownerModId,
        string featureId,
        bool previous,
        bool current,
        string source)
    {
        return new AuraFeatureSwitchChange(
            Normalize(ownerModId),
            Normalize(featureId),
            previous,
            current,
            Normalize(source),
            ++revision);
    }

    private static void Publish(AuraFeatureSwitchChange? change)
    {
        if (change == null)
        {
            return;
        }

        var handlers = EffectiveStateChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<AuraFeatureSwitchChange> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(change);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[AuraFeatureSwitch] subscriber failed: " + ex.Message);
            }
        }
    }

    public static IReadOnlyList<AuraFeatureSwitchSnapshot> Snapshot()
    {
        lock (Gate)
        {
            var values = new List<AuraFeatureSwitchSnapshot>(Registered.Count);
            foreach (var feature in Registered.Values)
            {
                values.Add(new AuraFeatureSwitchSnapshot(
                    feature.OwnerModId,
                    feature.FeatureId,
                    feature.DefaultEnabled,
                    feature.Source));
            }

            return values;
        }
    }

    private static string FeatureKey(string ownerModId, string featureId)
    {
        var owner = Normalize(ownerModId);
        var id = Normalize(featureId);
        return owner.Length == 0 || id.Length == 0 ? "" : owner + ":" + id;
    }

    private static string OverrideKey(string toolOwnerId, string ownerModId, string featureId)
    {
        var tool = Normalize(toolOwnerId);
        var feature = FeatureKey(ownerModId, featureId);
        return tool.Length == 0 || feature.Length == 0 ? "" : tool + "->" + feature;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private readonly struct RegisteredFeature
    {
        public RegisteredFeature(string ownerModId, string featureId, bool defaultEnabled, string source)
        {
            OwnerModId = ownerModId;
            FeatureId = featureId;
            DefaultEnabled = defaultEnabled;
            Source = source;
        }

        public string OwnerModId { get; }

        public string FeatureId { get; }

        public bool DefaultEnabled { get; }

        public string Source { get; }
    }
}

public sealed class AuraFeatureSwitchChange
{
    public AuraFeatureSwitchChange(
        string ownerModId,
        string featureId,
        bool previousEnabled,
        bool enabled,
        string source,
        long revision)
    {
        OwnerModId = ownerModId;
        FeatureId = featureId;
        PreviousEnabled = previousEnabled;
        Enabled = enabled;
        Source = source;
        Revision = revision;
    }

    public string OwnerModId { get; }
    public string FeatureId { get; }
    public bool PreviousEnabled { get; }
    public bool Enabled { get; }
    public string Source { get; }
    public long Revision { get; }
}

public sealed class AuraFeatureSwitchSnapshot
{
    public AuraFeatureSwitchSnapshot(string ownerModId, string featureId, bool defaultEnabled, string source)
    {
        OwnerModId = ownerModId;
        FeatureId = featureId;
        DefaultEnabled = defaultEnabled;
        Source = source;
    }

    public string OwnerModId { get; }

    public string FeatureId { get; }

    public bool DefaultEnabled { get; }

    public string Source { get; }
}
