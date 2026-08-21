using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal sealed class DamageMeterHookRegistrationSet
{
    private readonly Dictionary<string, IDisposable> registrations = new(StringComparer.Ordinal);

    internal int Count => registrations.Count;

    internal bool Register(string key, Func<IDisposable> factory)
    {
        if (registrations.ContainsKey(key))
        {
            return false;
        }

        registrations.Add(key, factory());
        return true;
    }

    internal int DisposeAll(Action<string, Exception> onFailure)
    {
        var keys = new List<string>(registrations.Keys);
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            try
            {
                registrations[key].Dispose();
                registrations.Remove(key);
            }
            catch (Exception ex)
            {
                onFailure(key, ex);
            }
        }

        return registrations.Count;
    }

    internal int DisposeWhere(
        Func<string, bool> predicate,
        Action<string, Exception> onFailure)
    {
        var keys = new List<string>(registrations.Keys);
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            if (!predicate(key)) continue;
            try
            {
                registrations[key].Dispose();
                registrations.Remove(key);
            }
            catch (Exception ex)
            {
                onFailure(key, ex);
            }
        }

        return registrations.Count;
    }
}
