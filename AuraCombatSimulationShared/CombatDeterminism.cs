using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AuraCombatSimulation.Shared;

public sealed class CombatRandomDraw
{
    public string StreamId { get; set; } = "";

    public ulong Counter { get; set; }

    public ulong Value { get; set; }
}

public static class CombatDeterministicRandom
{
    public static ulong NextUInt64(
        ulong rootSeed,
        CombatRandomCounterState state,
        string streamId,
        out CombatRandomDraw evidence)
    {
        var normalized = string.IsNullOrWhiteSpace(streamId) ? "default" : streamId.Trim();
        var counter = state.Counters.TryGetValue(normalized, out var current) ? current : 0UL;
        state.Counters[normalized] = counter + 1UL;
        var streamHash = StableStringHash(normalized);
        var value = SplitMix64(rootSeed ^ RotateLeft(streamHash, 17) ^ counter * 0x9E3779B97F4A7C15UL);
        evidence = new CombatRandomDraw
        {
            StreamId = normalized,
            Counter = counter,
            Value = value
        };
        return value;
    }

    public static int NextInt(
        ulong rootSeed,
        CombatRandomCounterState state,
        string streamId,
        int exclusiveMaximum,
        out CombatRandomDraw evidence)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }
        var value = NextUInt64(rootSeed, state, streamId, out evidence);
        return (int)(value % (uint)exclusiveMaximum);
    }

    public static double NextUnit(
        ulong rootSeed,
        CombatRandomCounterState state,
        string streamId,
        out CombatRandomDraw evidence)
    {
        var value = NextUInt64(rootSeed, state, streamId, out evidence);
        return (value >> 11) * (1d / 9007199254740992d);
    }

    public static List<CombatRandomDraw> Shuffle<T>(
        ulong rootSeed,
        CombatRandomCounterState state,
        string streamId,
        IList<T> values)
    {
        var evidence = new List<CombatRandomDraw>();
        for (var i = values.Count - 1; i > 0; i--)
        {
            var selected = NextInt(rootSeed, state, streamId, i + 1, out var draw);
            evidence.Add(draw);
            if (selected == i)
            {
                continue;
            }
            var temporary = values[i];
            values[i] = values[selected];
            values[selected] = temporary;
        }
        return evidence;
    }

    private static ulong StableStringHash(string value)
    {
        var hash = 1469598103934665603UL;
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static ulong SplitMix64(ulong value)
    {
        value += 0x9E3779B97F4A7C15UL;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    private static ulong RotateLeft(ulong value, int count)
    {
        return (value << count) | (value >> (64 - count));
    }
}

public static class CombatBattleStateHasher
{
    public static string Hash(CombatBattleState state)
    {
        if (state == null)
        {
            return "0000000000000000";
        }

        var hash = 1469598103934665603UL;
        Mix(ref hash, state.Turn);
        Mix(ref hash, (int)state.Phase);
        Mix(ref hash, (int)state.Outcome);
        Mix(ref hash, state.PlayerActorId);
        Mix(ref hash, state.ActionSequence);
        Mix(ref hash, state.EventSequence);
        foreach (var actor in state.Actors.OrderBy(actor => actor.ActorId))
        {
            Mix(ref hash, actor.ActorId);
            Mix(ref hash, actor.DefinitionId);
            Mix(ref hash, (int)actor.Kind);
            Mix(ref hash, actor.Hp);
            Mix(ref hash, actor.MaxHp);
            Mix(ref hash, actor.Block);
            Mix(ref hash, actor.Energy);
            Mix(ref hash, actor.CurrentIntentId);
            Mix(ref hash, actor.PreviousIntentId);
            foreach (var intentId in actor.CurrentIntentIds)
            {
                Mix(ref hash, intentId);
            }
            foreach (var intentId in actor.PreviousIntentIds)
            {
                Mix(ref hash, intentId);
            }
            foreach (var variable in actor.Variables
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                Mix(ref hash, variable.Key);
                Mix(ref hash, BitConverter.DoubleToInt64Bits(variable.Value));
            }
            foreach (var cooldown in actor.IntentCooldowns
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                Mix(ref hash, cooldown.Key);
                Mix(ref hash, cooldown.Value);
            }
            foreach (var status in actor.Statuses
                         .OrderBy(status => status.StatusId, StringComparer.Ordinal)
                         .ThenBy(status => status.SourceActorId))
            {
                Mix(ref hash, status.StatusId);
                Mix(ref hash, status.Stacks);
                Mix(ref hash, status.Duration);
                Mix(ref hash, status.SourceActorId);
                foreach (var counter in status.TriggerCounts
                             .OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    Mix(ref hash, counter.Key);
                    Mix(ref hash, counter.Value);
                }
            }
        }
        foreach (var card in state.Cards.OrderBy(card => card.InstanceId))
        {
            Mix(ref hash, card.InstanceId);
            Mix(ref hash, card.CardId);
            Mix(ref hash, card.CostModifier);
            foreach (var tag in card.Tags.OrderBy(
                         item => item,
                         StringComparer.OrdinalIgnoreCase))
            {
                Mix(ref hash, tag);
            }
        }
        MixList(ref hash, state.DrawPile);
        MixList(ref hash, state.Hand);
        MixList(ref hash, state.DiscardPile);
        MixList(ref hash, state.ExhaustPile);
        foreach (var pair in state.Random.Counters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Mix(ref hash, pair.Key);
            Mix(ref hash, pair.Value);
        }
        foreach (var deferred in state.DeferredVictoryVariableChanges
                     .OrderBy(item => item.ActorId)
                     .ThenBy(item => item.DefinitionId, StringComparer.Ordinal))
        {
            Mix(ref hash, deferred.ActorId);
            Mix(ref hash, deferred.DefinitionId);
            Mix(ref hash, deferred.Amount);
            Mix(ref hash, deferred.PersistAcrossBattles ? 1 : 0);
            Mix(ref hash, deferred.MinimumVariableValue);
            Mix(ref hash, deferred.MaximumVariableValue);
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static void MixList(ref ulong hash, IEnumerable<int> values)
    {
        foreach (var value in values)
        {
            Mix(ref hash, value);
        }
        Mix(ref hash, -1);
    }

    private static void Mix(ref ulong hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= 1099511628211UL;
        }
        hash ^= 0xff;
        hash *= 1099511628211UL;
    }

    private static void Mix(ref ulong hash, long value)
    {
        unchecked
        {
            hash ^= (ulong)value;
            hash *= 1099511628211UL;
        }
    }

    private static void Mix(ref ulong hash, ulong value)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }
}
