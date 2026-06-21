using System;
using System.Collections.Generic;
using AuraShared.Core;
using AuraSkin.Shared.Infrastructure;

namespace AuraSkin.Shared.GameApi;

public static class ResourceRedirectApi
{
    private sealed class RedirectSnapshot
    {
        public bool Existed { get; set; }
        public string PreviousValue { get; set; } = "";
    }

    private static readonly Dictionary<string, RedirectSnapshot> Snapshots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Owners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> KeysByCareer = new(StringComparer.OrdinalIgnoreCase);
    private static IDictionary<string, string>? redirectDictionary;
    private static bool reflectionAttempted;

    public static bool TryRedirect(string careerId, string originalPath, string replacementPath)
    {
        var key = Normalize(originalPath);
        var replacement = Normalize(replacementPath);
        if (string.IsNullOrWhiteSpace(careerId) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(replacement))
        {
            return false;
        }

        if (Owners.TryGetValue(key, out var owner)
            && !string.Equals(owner, careerId, StringComparison.OrdinalIgnoreCase))
        {
            SkinLog.Warn("Animation redirect collision: " + key + " is already owned by career " + owner);
            return false;
        }

        var dictionary = GetRedirectDictionary();
        if (!Snapshots.ContainsKey(key))
        {
            var snapshot = new RedirectSnapshot();
            if (dictionary != null && dictionary.TryGetValue(key, out var previous))
            {
                snapshot.Existed = true;
                snapshot.PreviousValue = previous;
            }

            Snapshots.Add(key, snapshot);
        }

        ResourceLoader.RedirectPath(key, replacement);
        Owners[key] = careerId;
        if (!KeysByCareer.TryGetValue(careerId, out var keys))
        {
            keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            KeysByCareer.Add(careerId, keys);
        }

        keys.Add(key);
        return true;
    }

    public static void RestoreCareer(string careerId)
    {
        if (string.IsNullOrWhiteSpace(careerId) || !KeysByCareer.TryGetValue(careerId, out var keys))
        {
            return;
        }

        foreach (var key in new List<string>(keys))
        {
            RestoreKey(key, careerId);
        }

        KeysByCareer.Remove(careerId);
    }

    public static void RestoreAll()
    {
        foreach (var careerId in new List<string>(KeysByCareer.Keys))
        {
            RestoreCareer(careerId);
        }
    }

    private static void RestoreKey(string key, string owner)
    {
        if (!Owners.TryGetValue(key, out var currentOwner)
            || !string.Equals(currentOwner, owner, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dictionary = GetRedirectDictionary();
        if (Snapshots.TryGetValue(key, out var snapshot))
        {
            if (snapshot.Existed)
            {
                ResourceLoader.RedirectPath(key, snapshot.PreviousValue);
            }
            else if (dictionary != null)
            {
                dictionary.Remove(key);
            }
            else
            {
                ResourceLoader.RedirectPath(key, key);
            }
        }

        Owners.Remove(key);
        Snapshots.Remove(key);
    }

    private static IDictionary<string, string>? GetRedirectDictionary()
    {
        if (reflectionAttempted)
        {
            return redirectDictionary;
        }

        reflectionAttempted = true;
        try
        {
            redirectDictionary = AuraSharedReflection.GetStaticMemberValue(typeof(ResourceLoader), "redirectedPaths")
                as IDictionary<string, string>;
            if (redirectDictionary == null)
            {
                SkinLog.Warn("ResourceLoader redirect dictionary is unavailable; default restore uses identity redirects");
            }
        }
        catch (Exception ex)
        {
            SkinLog.Warn("Failed to inspect ResourceLoader redirects: " + ex.Message);
        }

        return redirectDictionary;
    }

    private static string Normalize(string value)
    {
        return (value ?? "").Trim().Replace('\\', '/').TrimEnd('/');
    }
}
