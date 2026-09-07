namespace Terrias.Dll.Infrastructure;

public static class OlimyaIds
{
    public const string Career = "Terrias_olimya_olimya";
    public const string GoldenTouch = "Terrias_olimya_olimya_golden_touch";
    public const string Goldenized = "Terrias_terrias_olimya_goldenized";
    public const string IncomeRemainder = "TerriasOlimyaIncomeRemainder";
    public const string SpendingRemainder = "TerriasOlimyaSpendingRemainder";
    public const int StartingMaxHp = 80;
    public const int GoldenTouchCooldown = 3;
}

public enum OlimyaGoldenizationCommandKind
{
    Apply = 1,
    OwnerTurnStarted = 2
}

[System.Serializable]
public sealed class OlimyaGoldenizationCommand
{
    public int Version { get; set; } = 1;
    public int BattleEpoch { get; set; }
    public OlimyaGoldenizationCommandKind Kind { get; set; }
    public string OwnerStatusId { get; set; } = "";
    public string TargetStatusId { get; set; } = "";
    public string Token { get; set; } = "";
    public long Sequence { get; set; }
}
