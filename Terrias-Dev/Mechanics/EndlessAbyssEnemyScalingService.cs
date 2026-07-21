using System;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssEnemyScalingConfig
{
    public double HpLinearPerFloor { get; set; } = 0.14;

    public double HpQuadraticPerFloor { get; set; } = 0.007;

    public double AttackLinearPerFloor { get; set; } = 0.025;

    public double AttackQuadraticPerFloor { get; set; } = 0.0007;

    public int EndlessStartFloor { get; set; } = 7;

    public double EndlessHpMultiplier { get; set; } = 1.30;

    public double EndlessAttackMultiplier { get; set; } = 1.12;

    public int CycleFloorCount { get; set; } = 6;

    public double CycleHpGrowth { get; set; } = 0.05;

    public double CycleAttackGrowth { get; set; } = 0.02;

    public double HpSoftCap { get; set; } = 80.0;

    public double HpSoftCapOverflowRatio { get; set; } = 0.25;

    public double AttackSoftCap { get; set; } = 8.0;

    public double AttackSoftCapOverflowRatio { get; set; } = 0.25;

    public double GazeHpGrowth { get; set; } = 0.025;

    public double GazeHpGrowthCap { get; set; } = 0.50;

    public double GazeAttackGrowth { get; set; } = 0.0075;

    public double GazeAttackGrowthCap { get; set; } = 0.15;

    public double EliteHpMultiplier { get; set; } = 1.12;

    public double EliteAttackMultiplier { get; set; } = 1.05;

    public double BossHpMultiplier { get; set; } = 1.20;

    public double BossAttackMultiplier { get; set; } = 1.08;

    public double EndlessBossHpMultiplier { get; set; } = 1.30;

    public double EndlessBossAttackMultiplier { get; set; } = 1.12;
}

public readonly struct EndlessAbyssEnemyScalingResult
{
    public EndlessAbyssEnemyScalingResult(double hpMultiplier, double attackMultiplier)
    {
        HpMultiplier = hpMultiplier;
        AttackMultiplier = attackMultiplier;
    }

    public double HpMultiplier { get; }

    public double AttackMultiplier { get; }
}

public static class EndlessAbyssEnemyScalingService
{
    public static EndlessAbyssEnemyScalingResult Calculate(
        int floor,
        int gaze,
        EndlessSeaNodeKind nodeKind,
        EndlessAbyssEnemyScalingConfig? config)
    {
        var settings = config ?? new EndlessAbyssEnemyScalingConfig();
        var normalizedFloor = Math.Max(1, floor);
        var floorOffset = normalizedFloor - 1;
        var hpMultiplier = 1.0
            + (Math.Max(0.0, settings.HpLinearPerFloor) * floorOffset)
            + (Math.Max(0.0, settings.HpQuadraticPerFloor) * floorOffset * floorOffset);
        var attackMultiplier = 1.0
            + (Math.Max(0.0, settings.AttackLinearPerFloor) * floorOffset)
            + (Math.Max(0.0, settings.AttackQuadraticPerFloor) * floorOffset * floorOffset);

        var endlessStartFloor = Math.Max(1, settings.EndlessStartFloor);
        if (normalizedFloor >= endlessStartFloor)
        {
            hpMultiplier *= Math.Max(1.0, settings.EndlessHpMultiplier);
            attackMultiplier *= Math.Max(1.0, settings.EndlessAttackMultiplier);

            var cycleFloorCount = Math.Max(1, settings.CycleFloorCount);
            var cycle = Math.Max(0, (normalizedFloor - endlessStartFloor) / cycleFloorCount);
            hpMultiplier *= 1.0 + (Math.Max(0.0, settings.CycleHpGrowth) * cycle);
            attackMultiplier *= 1.0 + (Math.Max(0.0, settings.CycleAttackGrowth) * cycle);
        }

        hpMultiplier = SoftCap(
            hpMultiplier,
            Math.Max(1.0, settings.HpSoftCap),
            Clamp(settings.HpSoftCapOverflowRatio, 0.0, 1.0));
        attackMultiplier = SoftCap(
            attackMultiplier,
            Math.Max(1.0, settings.AttackSoftCap),
            Clamp(settings.AttackSoftCapOverflowRatio, 0.0, 1.0));

        var gazeOffset = Math.Max(0, gaze - 1);
        hpMultiplier *= 1.0 + Math.Min(
            Math.Max(0.0, settings.GazeHpGrowthCap),
            Math.Max(0.0, settings.GazeHpGrowth) * gazeOffset);
        attackMultiplier *= 1.0 + Math.Min(
            Math.Max(0.0, settings.GazeAttackGrowthCap),
            Math.Max(0.0, settings.GazeAttackGrowth) * gazeOffset);

        var nodeMultipliers = NodeMultipliers(nodeKind, settings);
        return new EndlessAbyssEnemyScalingResult(
            hpMultiplier * nodeMultipliers.HpMultiplier,
            attackMultiplier * nodeMultipliers.AttackMultiplier);
    }

    private static EndlessAbyssEnemyScalingResult NodeMultipliers(
        EndlessSeaNodeKind nodeKind,
        EndlessAbyssEnemyScalingConfig settings)
    {
        return nodeKind switch
        {
            EndlessSeaNodeKind.Elite => new EndlessAbyssEnemyScalingResult(
                Math.Max(1.0, settings.EliteHpMultiplier),
                Math.Max(1.0, settings.EliteAttackMultiplier)),
            EndlessSeaNodeKind.Boss => new EndlessAbyssEnemyScalingResult(
                Math.Max(1.0, settings.BossHpMultiplier),
                Math.Max(1.0, settings.BossAttackMultiplier)),
            EndlessSeaNodeKind.EndlessBoss => new EndlessAbyssEnemyScalingResult(
                Math.Max(1.0, settings.EndlessBossHpMultiplier),
                Math.Max(1.0, settings.EndlessBossAttackMultiplier)),
            _ => new EndlessAbyssEnemyScalingResult(1.0, 1.0)
        };
    }

    private static double SoftCap(double value, double knee, double overflowRatio)
    {
        return value <= knee ? value : knee + ((value - knee) * overflowRatio);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
