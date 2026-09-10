using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class OlimyaGoldenizationLedger
{
    private readonly Dictionary<string, long> sequences = new(StringComparer.Ordinal);
    public int BattleEpoch { get; private set; }

    public void Reset(int battleEpoch)
    {
        BattleEpoch = battleEpoch;
        sequences.Clear();
    }

    public bool TryAccept(OlimyaGoldenizationCommand command, bool senderOwnsStatus)
    {
        if (!senderOwnsStatus || command == null || command.Version != 2
            || BattleEpoch <= 0 || command.BattleEpoch != BattleEpoch
            || command.Kind != OlimyaGoldenizationCommandKind.Apply
            || string.IsNullOrWhiteSpace(command.OwnerStatusId) || command.OwnerStatusId.Length > 128
            || string.IsNullOrWhiteSpace(command.TargetStatusId) || command.TargetStatusId.Length > 128
            || command.Sequence <= 0 || !Guid.TryParseExact(command.Token, "N", out _)) return false;
        if (sequences.TryGetValue(command.OwnerStatusId, out var previous) && command.Sequence <= previous) return false;
        sequences[command.OwnerStatusId] = command.Sequence;
        return true;
    }
}
