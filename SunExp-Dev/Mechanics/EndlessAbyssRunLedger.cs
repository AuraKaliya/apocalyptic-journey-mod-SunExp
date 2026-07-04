using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Data.Save;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public sealed class EndlessAbyssLedgerDocument
{
    public List<string> Entries { get; set; } = new();
}

public static class EndlessAbyssRunLedger
{
    private const int MaxEntries = 512;

    public static void Initialize(SaveInfo saveInfo)
    {
        if (saveInfo?.GameVars == null)
        {
            return;
        }

        saveInfo.GameVars[SunExpIds.EndlessAbyssLedgerKey] = JsonConvert.SerializeObject(new EndlessAbyssLedgerDocument());
        saveInfo.GameVars[SunExpIds.EndlessAbyssPendingShockKey] = "";
    }

    public static bool Contains(string key)
    {
        return Load().Entries.Contains(NormalizeKey(key), StringComparer.Ordinal);
    }

    public static bool TryClaim(string key, string source)
    {
        key = NormalizeKey(key);
        if (key.Length == 0)
        {
            return false;
        }

        var document = Load();
        if (document.Entries.Contains(key, StringComparer.Ordinal))
        {
            return false;
        }

        document.Entries.Add(key);
        if (document.Entries.Count > MaxEntries)
        {
            document.Entries = document.Entries.Skip(document.Entries.Count - MaxEntries).ToList();
        }

        Save(document);
        SunExpLog.Debug("[EndlessAbyssLedger] claimed " + key + " from " + source + ".");
        return true;
    }

    private static EndlessAbyssLedgerDocument Load()
    {
        try
        {
            var json = CurrentValue(SunExpIds.EndlessAbyssLedgerKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new EndlessAbyssLedgerDocument();
            }

            return JsonConvert.DeserializeObject<EndlessAbyssLedgerDocument>(json)
                   ?? new EndlessAbyssLedgerDocument();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssLedger] load failed: " + ex.Message);
            return new EndlessAbyssLedgerDocument();
        }
    }

    private static void Save(EndlessAbyssLedgerDocument document)
    {
        SetValue(SunExpIds.EndlessAbyssLedgerKey, JsonConvert.SerializeObject(document ?? new EndlessAbyssLedgerDocument()));
    }

    private static string NormalizeKey(string key)
    {
        return (key ?? "").Trim();
    }

    private static string CurrentValue(string key)
    {
        try
        {
            var save = GameSaveManager.GetNowSave();
            return save?.GameVars != null && save.GameVars.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static void SetValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            try
            {
                GameSaveManager.GetNowSave()?.SetValue(key, value);
            }
            catch
            {
                var save = GameSaveManager.GetNowSave();
                if (save?.GameVars != null)
                {
                    save.GameVars[key] = value;
                }
            }
        }
    }
}

public static class EndlessAbyssGazeService
{
    public static int InitialLevel => Math.Max(1, EndlessAbyssConfigStore.Current.Gaze.InitialLevel);

    public static void Initialize(SaveInfo saveInfo)
    {
        if (saveInfo?.GameVars == null)
        {
            return;
        }

        saveInfo.GameVars[SunExpIds.EndlessAbyssGazeLevelKey] = InitialLevel.ToString(CultureInfo.InvariantCulture);
    }

    public static int CurrentLevel()
    {
        return Math.Max(InitialLevel, GameSaveManager.GetValue<int>(SunExpIds.EndlessAbyssGazeLevelKey));
    }

    public static int RequiredShockChoices()
    {
        return RequiredShockChoices(CurrentLevel());
    }

    public static int RequiredShockChoices(int gazeLevel)
    {
        var config = EndlessAbyssConfigStore.Current.Gaze;
        var step = Math.Max(1, config.ChoiceStep);
        var required = 1 + (Math.Max(1, gazeLevel) - 1) / step;
        return Math.Max(1, Math.Min(Math.Max(1, config.MaxRequiredChoices), required));
    }

    public static bool EnsureInitialized(string source)
    {
        var current = GameSaveManager.GetValue<string>(SunExpIds.EndlessAbyssGazeLevelKey);
        if (!string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        SetLevel(InitialLevel, source);
        return true;
    }

    public static bool EnsureAtLeast(int level, string source)
    {
        level = Math.Max(InitialLevel, level);
        if (CurrentLevel() >= level)
        {
            return false;
        }

        SetLevel(level, source);
        return true;
    }

    public static int Increase(int amount, string source)
    {
        var delta = Math.Max(0, amount);
        var next = CurrentLevel() + delta;
        SetLevel(next, source);
        return next;
    }

    public static void SetLevel(int level, string source)
    {
        var next = Math.Max(InitialLevel, level);
        SetValue(SunExpIds.EndlessAbyssGazeLevelKey, next.ToString(CultureInfo.InvariantCulture));
        SunExpLog.Info("[EndlessAbyssGaze] level=" + next + " from " + source + ".");
    }

    private static void SetValue(string key, string value)
    {
        try
        {
            GameSaveManager.SetValue(key, value);
        }
        catch
        {
            try
            {
                GameSaveManager.GetNowSave()?.SetValue(key, value);
            }
            catch
            {
                var save = GameSaveManager.GetNowSave();
                if (save?.GameVars != null)
                {
                    save.GameVars[key] = value;
                }
            }
        }
    }
}
