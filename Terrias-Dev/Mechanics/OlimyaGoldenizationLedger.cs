using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class OlimyaGoldenizationLedger
{
    private readonly Dictionary<string, string> owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> sequences = new(StringComparer.Ordinal);
    public int BattleEpoch { get; private set; }

    public void Reset(int battleEpoch)
    {
        BattleEpoch = battleEpoch;
        owners.Clear();
        sequences.Clear();
    }

    public bool TryAccept(OlimyaGoldenizationCommand command, bool senderOwnsStatus)
    {
        if (!senderOwnsStatus || command == null || command.Version != 1
            || BattleEpoch <= 0 || command.BattleEpoch != BattleEpoch
            || command.Kind is not (OlimyaGoldenizationCommandKind.Apply or OlimyaGoldenizationCommandKind.OwnerTurnStarted)
            || string.IsNullOrWhiteSpace(command.OwnerStatusId) || command.OwnerStatusId.Length > 128
            || command.TargetStatusId == null || command.TargetStatusId.Length > 128
            || command.Sequence <= 0 || !Guid.TryParseExact(command.Token, "N", out _)
            || command.Kind == OlimyaGoldenizationCommandKind.Apply && string.IsNullOrWhiteSpace(command.TargetStatusId)) return false;
        if (sequences.TryGetValue(command.OwnerStatusId, out var previous) && command.Sequence <= previous) return false;
        sequences[command.OwnerStatusId] = command.Sequence;
        return true;
    }

    public void Mark(string targetId, string ownerId) => owners[targetId] = ownerId;

    public IReadOnlyList<string> TakeExpired(string ownerId)
    {
        var targets = owners.Where(pair => pair.Value == ownerId).Select(pair => pair.Key).ToArray();
        foreach (var target in targets) owners.Remove(target);
        return targets;
    }
}
