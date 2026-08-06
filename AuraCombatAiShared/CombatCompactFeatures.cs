using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace AuraCombatAi.Shared;

internal sealed class CombatCompactFeatureVector
{
    public static readonly CombatCompactFeatureVector Empty = new(
        Array.Empty<int>(),
        Array.Empty<float>());

    public CombatCompactFeatureVector(int[] tokenIds, float[] values)
    {
        TokenIds = tokenIds ?? Array.Empty<int>();
        Values = values ?? Array.Empty<float>();
        if (TokenIds.Length != Values.Length)
        {
            throw new ArgumentException(
                "Compact feature token/value lengths differ.");
        }
    }

    public int[] TokenIds { get; }

    public float[] Values { get; }

    public int Count => TokenIds.Length;

    public bool TryGetValue(string key, out double value)
    {
        value = 0d;
        if (!CombatFeatureTokenRegistry.TryGetToken(key, out var token))
        {
            return false;
        }
        for (var index = 0; index < TokenIds.Length; index++)
        {
            if (TokenIds[index] == token)
            {
                value = Values[index];
                return true;
            }
        }
        return false;
    }

    public Dictionary<string, double> Materialize()
    {
        var result = new Dictionary<string, double>(
            Math.Max(0, Count),
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < TokenIds.Length; index++)
        {
            if (CombatFeatureTokenRegistry.TryResolve(
                    TokenIds[index],
                    out var key))
            {
                result[key] = Values[index];
            }
        }
        return result;
    }
}

internal static class CombatFeatureTokenRegistry
{
    private static readonly ConcurrentDictionary<string, int> Tokens = new(
        StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, string> Names = new();
    private static int nextToken;

    public static int GetToken(string? key)
    {
        var normalized = key ?? "";
        return Tokens.GetOrAdd(normalized, value =>
        {
            var token = Interlocked.Increment(ref nextToken);
            Names[token] = value;
            return token;
        });
    }

    public static bool TryGetToken(string? key, out int token)
    {
        return Tokens.TryGetValue(key ?? "", out token);
    }

    public static bool TryResolve(int token, out string key)
    {
        return Names.TryGetValue(token, out key!);
    }

    public static Dictionary<int, string> CaptureCatalog()
    {
        return new Dictionary<int, string>(Names);
    }

    public static void RegisterCatalog(
        IReadOnlyDictionary<int, string>? catalog)
    {
        if (catalog == null)
        {
            return;
        }
        foreach (var pair in catalog)
        {
            if (pair.Key <= 0 || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }
            if (Names.TryGetValue(pair.Key, out var existingName)
                && !string.Equals(
                    existingName,
                    pair.Value,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Feature token catalog conflict for token " + pair.Key);
            }
            if (Tokens.TryGetValue(pair.Value, out var existingToken)
                && existingToken != pair.Key)
            {
                throw new InvalidOperationException(
                    "Feature token catalog conflict for key " + pair.Value);
            }
            Names[pair.Key] = pair.Value;
            Tokens[pair.Value] = pair.Key;
            var observed = Volatile.Read(ref nextToken);
            while (observed < pair.Key)
            {
                var prior = Interlocked.CompareExchange(
                    ref nextToken,
                    pair.Key,
                    observed);
                if (prior == observed)
                {
                    break;
                }
                observed = prior;
            }
        }
    }
}

internal sealed class CombatCompactFeatureBuilder
{
    private readonly List<int> tokenIds = new();
    private readonly List<float> values = new();
    private readonly Dictionary<int, int> positions = new();

    public void Clear()
    {
        tokenIds.Clear();
        values.Clear();
        positions.Clear();
    }

    public void Set(string key, double value)
    {
        var token = CombatFeatureTokenRegistry.GetToken(key);
        if (positions.TryGetValue(token, out var position))
        {
            values[position] = (float)value;
            return;
        }
        positions[token] = tokenIds.Count;
        tokenIds.Add(token);
        values.Add((float)value);
    }

    public void Add(string key, double value)
    {
        if (value == 0d)
        {
            return;
        }
        var token = CombatFeatureTokenRegistry.GetToken(key);
        if (positions.TryGetValue(token, out var position))
        {
            values[position] += (float)value;
            return;
        }
        positions[token] = tokenIds.Count;
        tokenIds.Add(token);
        values.Add((float)value);
    }

    public CombatCompactFeatureVector Build()
    {
        return tokenIds.Count == 0
            ? CombatCompactFeatureVector.Empty
            : new CombatCompactFeatureVector(
                tokenIds.ToArray(),
                values.ToArray());
    }
}
