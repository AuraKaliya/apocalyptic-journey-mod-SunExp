using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class ProjectionStrategyService
{
    public const int StaffTapDamage = 8;
    public const int ShieldBlessingBlock = 10;
    public const int StaffTapBasePriority = 20;
    public const int ShieldBlessingBasePriority = 16;

    public static int ProjectionMaxHp(PolymorphRoleSpec role)
    {
        return CompanionStatsService.ProjectionStats(role).MaxHp;
    }

    public static void InitAction(ScriptExecutor self, string actionId)
    {
        CompanionIntentExecutor.InitAction(self, NormalizeActionId(actionId));
    }

    public static void Target(ScriptExecutor self, string actionId)
    {
        CompanionIntentExecutor.Target(self, NormalizeActionId(actionId));
    }

    public static void UseAction(ScriptExecutor self, string actionId)
    {
        CompanionIntentExecutor.UseAction(self, NormalizeActionId(actionId));
    }

    private static string NormalizeActionId(string actionId)
    {
        return string.IsNullOrWhiteSpace(actionId)
            ? SunExpIds.ProjectionActionStaffTap
            : actionId.Trim();
    }
}
