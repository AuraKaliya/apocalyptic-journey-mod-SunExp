using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraSkin.Shared.GameApi;
using AuraSkin.Shared.Infrastructure;

namespace AuraSkin.Shared.Services;

public static class SkinSelectionStore
{
    private const string AuthorityId = "AuraSkin";
    private const string ConfigFileName = "selections.json";

    private sealed class SelectionFile
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> Selections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static SelectionFile state = new();
    private static long revision;

    public static IEnumerable<string> CareerIds => state.Selections.Keys;

    public static void Load()
    {
        try
        {
            var snapshot = AuraSharedConfigStore.ReadShared(
                AuthorityId,
                AuraSharedSystems.Skin,
                ConfigFileName,
                new SelectionFile());
            state = snapshot.Value;
            revision = snapshot.Revision;
            NormalizeSelectionKeys();
        }
        catch (Exception ex)
        {
            state = new SelectionFile();
            SkinLog.Warn("Failed to load skin selections: " + ex.Message);
        }
    }

    public static string Get(string careerId)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        return !string.IsNullOrWhiteSpace(normalizedCareerId) && state.Selections.TryGetValue(normalizedCareerId, out var skinId)
            ? skinId
            : "";
    }

    public static void Set(string careerId, string skinId)
    {
        var normalizedCareerId = CareerConfigApi.NormalizeId(careerId);
        if (string.IsNullOrWhiteSpace(normalizedCareerId))
        {
            return;
        }

        ApplySelection(normalizedCareerId, skinId);
        if (Save())
        {
            return;
        }

        Load();
        ApplySelection(normalizedCareerId, skinId);
        Save();
    }

    private static void NormalizeSelectionKeys()
    {
        if (state.Selections == null)
        {
            state.Selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var item in state.Selections)
        {
            var key = CareerConfigApi.NormalizeId(item.Key);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(item.Value))
            {
                changed = true;
                continue;
            }

            normalized[key] = item.Value;
            changed |= !string.Equals(key, item.Key, StringComparison.OrdinalIgnoreCase);
        }

        state.Selections = normalized;
        if (changed)
        {
            Save();
        }
    }

    private static void ApplySelection(string careerId, string skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
        {
            state.Selections.Remove(careerId);
        }
        else
        {
            state.Selections[careerId] = skinId;
        }
    }

    private static bool Save()
    {
        try
        {
            var result = AuraSharedConfigStore.WriteShared(
                AuthorityId,
                AuraSharedSystems.Skin,
                ConfigFileName,
                state,
                revision,
                schemaVersion: 1);
            if (!result.Success)
            {
                SkinLog.Warn("Failed to save skin selections: " + result.Message);
                return false;
            }

            revision = result.Revision;
            return true;
        }
        catch (Exception ex)
        {
            SkinLog.Error("Failed to save skin selections", ex);
            return false;
        }
    }
}
