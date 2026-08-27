using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Features.Cg;

public static class AuraToolsCgOutcomeReasonPolicy
{
    public static string ResolveWin(bool ritualTriggered, bool curseConditionMet)
    {
        if (ritualTriggered) return AuraToolsEventCgOutcomeReasons.RitualVictory;
        if (curseConditionMet) return AuraToolsEventCgOutcomeReasons.CurseVictory;
        return AuraToolsEventCgOutcomeReasons.StandardVictory;
    }

    public static string ResolveEscape(bool midasTriggered)
    {
        return midasTriggered
            ? AuraToolsEventCgOutcomeReasons.MidasEscape
            : AuraToolsEventCgOutcomeReasons.Escape;
    }
}
