using System;

namespace AuraCombatSimulation.Shared;

public static class CombatTurnTransitionRules
{
    public static int NextTurnPower(int currentPower, int maxPower)
    {
        return Math.Max(0, Math.Max(currentPower, maxPower));
    }

    public static double NextTurnPower(double currentPower, double maxPower)
    {
        return Math.Max(0d, Math.Max(currentPower, maxPower));
    }

    public static double EnergyCarryOpportunityCost(
        double currentPower,
        double maxPower,
        double actionCost,
        double actionEnergyGain)
    {
        var before = NextTurnPower(currentPower, maxPower);
        var afterAction = Math.Max(
            0d,
            currentPower
            - Math.Max(0d, actionCost)
            + Math.Max(0d, actionEnergyGain));
        return Math.Max(
            0d,
            before - NextTurnPower(afterAction, maxPower));
    }
}
