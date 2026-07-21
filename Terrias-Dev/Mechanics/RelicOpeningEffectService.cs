using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

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
                TerriasIds.ModId,
                "RelicOpeningEffect",
                "BlazingCrownHeart",
                statusId,
                "non-field",
                BlazingCrownHeartRelicId))
        {
            return false;
        }

        executor.Self.AddBuff(TerriasIds.SolarRadiance, 8);
        executor.Self.AddBuff(TerriasIds.SolarCrown, 1);
        TerriasLog.Debug("[RelicOpeningEffect] applied Blazing Crown Heart; source=" + (source ?? ""));
        return true;
    }
}
