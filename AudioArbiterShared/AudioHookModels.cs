using System;

namespace AudioArbiter.Shared;

internal sealed class AudioCareerObservation
{
    public string CareerId { get; set; } = "";

    public string SourceName { get; set; } = "";
}

internal sealed class AudioCombatActionObservation
{
    public string CardId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string StatusInstanceId { get; set; } = "";

    public string EffectName { get; set; } = "";

    public string ActionName { get; set; } = "";

    public string SourceName { get; set; } = "";
}

internal sealed class AudioBuffObservation
{
    public string BuffId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string StatusInstanceId { get; set; } = "";

    public string SourceName { get; set; } = "";
}

internal sealed class AudioVocalObservation
{
    public string VocalState { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string StatusInstanceId { get; set; } = "";

    public string SourceName { get; set; } = "";
}

internal sealed class AudioStatusSnapshot
{
    public string StatusInstanceId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string CareerId { get; set; } = "";

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public float HpRatio { get; set; }

    public bool IsLocalOwner { get; set; }

    public string SourceName { get; set; } = "";
}

internal sealed class AudioBattleObservation
{
    public string Result { get; set; } = "";

    public string CareerId { get; set; } = "";

    public string SourceName { get; set; } = "";
}

internal sealed class AudioNarrationObservation
{
    public int[] NarrationIds { get; set; } = Array.Empty<int>();
}
