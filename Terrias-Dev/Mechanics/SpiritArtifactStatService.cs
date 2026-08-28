using System;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactStatService
{
    public static SpiritOriginVector AddOrigins(SpiritOriginVector? origins, SpiritArtifactBattleSnapshot? artifacts)
    {
        origins ??= new SpiritOriginVector();
        artifacts ??= new SpiritArtifactBattleSnapshot();
        return new SpiritOriginVector
        {
            Magic = Math.Max(0, origins.Magic) + Math.Max(0, artifacts.OriginMagic),
            Spirit = Math.Max(0, origins.Spirit) + Math.Max(0, artifacts.OriginSpirit),
            Luck = Math.Max(0, origins.Luck) + Math.Max(0, artifacts.OriginLuck),
            Perception = Math.Max(0, origins.Perception) + Math.Max(0, artifacts.OriginPerception)
        };
    }

    public static CompanionStats ApplyFlatBattleStats(CompanionStats? stats, SpiritArtifactBattleSnapshot? artifacts)
    {
        stats ??= new CompanionStats(1, 1, 1, 1, 100);
        artifacts ??= new SpiritArtifactBattleSnapshot();
        var result = new CompanionStats(
            Math.Max(1, stats.MaxHp + Math.Max(0, artifacts.FlatLife)),
            Math.Max(1, stats.MaxMagic + Math.Max(0, artifacts.MaxMagic)),
            Math.Max(1, stats.Attack),
            Math.Max(0, stats.Armor + Math.Max(0, artifacts.FlatArmor)),
            Math.Max(1, stats.Speed + Math.Max(0, artifacts.Speed)));
        result.SetCurrentMagic(Math.Min(result.MaxMagic, stats.CurrentMagic + Math.Max(0, artifacts.MaxMagic)));
        return result;
    }
}
