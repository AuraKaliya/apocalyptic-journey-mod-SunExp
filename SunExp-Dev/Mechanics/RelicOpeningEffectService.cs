using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class RelicOpeningEffectService
{
    private const string BlazingCrownHeartRelicId = "blazing_crown_heart";

    public static bool Apply(ScriptExecutor? executor, string source)
    {
        if (!FieldApi.IsAuthoritativeFieldWriter()
            || executor?.Self == null
            || !RelicApi.HasRelic(BlazingCrownHeartRelicId))
        {
            return false;
        }

        var statusId = string.IsNullOrWhiteSpace(executor.Self.InstanceId)
            ? "local"
            : executor.Self.InstanceId;
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                SunExpIds.ModId,
                "RelicOpeningEffect",
                "BlazingCrownHeart",
                statusId,
                "non-field",
                BlazingCrownHeartRelicId))
        {
            return false;
        }

        executor.Self.AddBuff(SunExpIds.SolarRadiance, 8);
        executor.Self.AddBuff(SunExpIds.SolarCrown, 1);
        SunExpLog.Debug("[RelicOpeningEffect] applied Blazing Crown Heart; source=" + (source ?? ""));
        return true;
    }
}
