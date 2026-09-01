using AuraShared.Core;

namespace Terrias.Dll.GameApi;

/// <summary>
/// Single game-facing gate for work that may continue a synthetic companion
/// action or rebuild its native presentation. The shared lifecycle closes
/// producers at OutcomeEntering; FightType is retained as the host-state
/// cross-check for direct native transitions.
/// </summary>
public static class BattleLifecycleApi
{
    public static bool AcceptsCompanionContinuation =>
        AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation
        && FightManager.Instance != null
        && IsActiveFightType(FightManager.Instance.fightType);

    public static bool IsActiveFightType(FightType value) =>
        value is not (FightType.None or FightType.Win or FightType.Loss or FightType.Escape);
}
