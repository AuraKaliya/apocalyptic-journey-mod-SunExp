using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Data.Save;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssRunLedger
{
    private const int MaxEntries = 512;

    public static void Initialize(SaveInfo saveInfo)
    {
        if (saveInfo?.GameVars == null)
        {
            return;
        }

        saveInfo.GameVars[TerriasIds.EndlessAbyssLedgerKey] = JsonConvert.SerializeObject(new EndlessAbyssLedgerDocument());
        saveInfo.GameVars[TerriasIds.EndlessAbyssPendingShockKey] = "";
    }

    public static bool Contains(string key)
    {
        return Load().Entries.Contains(NormalizeKey(key), StringComparer.Ordinal);
    }

    public static bool ContainsPrefix(string prefix)
    {
        prefix = NormalizeKey(prefix);
        if (prefix.Length == 0)
        {
            return false;
        }

        return Load().Entries.Any(entry => NormalizeKey(entry).StartsWith(prefix, StringComparison.Ordinal));
    }

    public static bool TryClaim(string key, string source)
    {
        key = NormalizeKey(key);
        if (key.Length == 0)
        {
            return false;
        }

        if (!EndlessAbyssLedgerCodec.TryCommitClaim(key,
            () => CurrentValue(TerriasIds.EndlessAbyssLedgerKey),
            json => SetValue(TerriasIds.EndlessAbyssLedgerKey, json), MaxEntries)) return false;
        TerriasLog.Debug("[EndlessAbyssLedger] claimed " + key + " from " + source + ".");
        return true;
    }

    public static string MergeRemotePreservingLocalMilestones(string remoteJson)
    {
        var remote = Parse(remoteJson);
        var local = Load();
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in remote.Entries.Where(entry => !IsPlayerMilestone(entry)))
        {
            if (seen.Add(NormalizeKey(entry)))
            {
                entries.Add(NormalizeKey(entry));
            }
        }

        foreach (var entry in local.Entries.Where(IsPlayerMilestone))
        {
            if (seen.Add(NormalizeKey(entry)))
            {
                entries.Add(NormalizeKey(entry));
            }
        }

        if (entries.Count > MaxEntries)
        {
            entries = entries.Skip(entries.Count - MaxEntries).ToList();
        }

        return JsonConvert.SerializeObject(new EndlessAbyssLedgerDocument { Entries = entries });
    }

    private static EndlessAbyssLedgerDocument Load()
    {
        return Parse(CurrentValue(TerriasIds.EndlessAbyssLedgerKey));
    }

    private static EndlessAbyssLedgerDocument Parse(string json)
    {
        return EndlessAbyssLedgerCodec.Read(json);
    }

    private static string NormalizeKey(string key)
    {
        return (key ?? "").Trim();
    }

    private static bool IsPlayerMilestone(string key)
    {
        return NormalizeKey(key).StartsWith("milestone:player:", StringComparison.Ordinal);
    }

    private static string CurrentValue(string key)
    {
        var save = GameSaveManager.GetNowSave();
        if (save?.GameVars == null) throw new InvalidOperationException("Adventure save is unavailable; abyss claims cannot be read.");
        return save.GameVars.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static void SetValue(string key, string value)
    {
        if (GameSaveManager.GetNowSave()?.GameVars == null)
            throw new InvalidOperationException("Adventure save is unavailable; abyss claim was not committed.");
        GameSaveManager.SetValue(key, value);
        if (!string.Equals(CurrentValue(key), value, StringComparison.Ordinal))
            throw new InvalidOperationException("Abyss claim was not committed to the active adventure.");
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

        saveInfo.GameVars[TerriasIds.EndlessAbyssGazeLevelKey] = InitialLevel.ToString(CultureInfo.InvariantCulture);
    }

    public static int CurrentLevel()
    {
        return Math.Max(InitialLevel, GameSaveManager.GetValue<int>(TerriasIds.EndlessAbyssGazeLevelKey));
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
        var current = GameSaveManager.GetValue<string>(TerriasIds.EndlessAbyssGazeLevelKey);
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
        SetValue(TerriasIds.EndlessAbyssGazeLevelKey, next.ToString(CultureInfo.InvariantCulture));
        TerriasLog.Info("[EndlessAbyssGaze] level=" + next + " from " + source + ".");
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
