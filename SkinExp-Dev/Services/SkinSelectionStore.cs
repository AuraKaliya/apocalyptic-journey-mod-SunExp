using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SkinExp.Dll.GameApi;
using SkinExp.Dll.Infrastructure;

namespace SkinExp.Dll.Services;

public static class SkinSelectionStore
{
    private sealed class SelectionFile
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> Selections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static SelectionFile state = new();

    public static IEnumerable<string> CareerIds => state.Selections.Keys;

    public static void Load()
    {
        try
        {
            state = File.Exists(SkinPaths.SettingsPath)
                ? JsonConvert.DeserializeObject<SelectionFile>(File.ReadAllText(SkinPaths.SettingsPath)) ?? new SelectionFile()
                : new SelectionFile();
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

        if (string.IsNullOrWhiteSpace(skinId))
        {
            state.Selections.Remove(normalizedCareerId);
        }
        else
        {
            state.Selections[normalizedCareerId] = skinId;
        }

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

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(SkinPaths.SettingsDirectory);
            var tempPath = SkinPaths.SettingsPath + ".tmp";
            File.WriteAllText(tempPath, JsonConvert.SerializeObject(state, Formatting.Indented));
            if (File.Exists(SkinPaths.SettingsPath))
            {
                File.Delete(SkinPaths.SettingsPath);
            }

            File.Move(tempPath, SkinPaths.SettingsPath);
        }
        catch (Exception ex)
        {
            SkinLog.Error("Failed to save skin selections", ex);
        }
    }
}
