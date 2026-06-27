using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.DamageMeter.Model;

public static class OutOfRunDamageHistoryStatus
{
    public const string Completed = "完成";
    public const string Failed = "失败";
}

[Serializable]
public sealed class OutOfRunDamageHistoryFile
{
    public int SchemaVersion { get; set; } = 1;

    public List<OutOfRunDamageHistoryRecord> Records { get; set; } = new();
}

[Serializable]
public sealed class OutOfRunDamageHistoryRecord
{
    public int Sequence { get; set; }

    public string AdventureId { get; set; } = "";

    public string ModeId { get; set; } = "";

    public string ModeDisplayName { get; set; } = "";

    public string Status { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public List<OutOfRunTeamMemberSnapshot> TeamMembers { get; set; } = new();

    public DamageBestHitRecord? BestHit { get; set; }

    public long TeamTotalDamage { get; set; }

    public int TotalRounds { get; set; }

    public double TeamDps { get; set; }

    public DamageMeterMvpResult Mvp { get; set; } = new();
}

[Serializable]
public sealed class OutOfRunTeamMemberSnapshot
{
    public string InstanceId { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public string PlayerDisplayName { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string RoleDisplayName { get; set; } = "";

    // Legacy display field kept for existing encrypted history files.
    public string DisplayName { get; set; } = "";

    public string AvatarPngBase64 { get; set; } = "";

    public string AvatarSha256 { get; set; } = "";

    public long TotalDamage { get; set; }

    public double Dps { get; set; }
}

public sealed class OutOfRunDamageHistoryBuildRequest
{
    public string AdventureId { get; set; } = "";

    public string ModeId { get; set; } = "";

    public string ModeDisplayName { get; set; } = "";

    public string Status { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public IReadOnlyList<OutOfRunTeamMemberSnapshot> TeamMembers { get; set; } =
        Array.Empty<OutOfRunTeamMemberSnapshot>();
}
