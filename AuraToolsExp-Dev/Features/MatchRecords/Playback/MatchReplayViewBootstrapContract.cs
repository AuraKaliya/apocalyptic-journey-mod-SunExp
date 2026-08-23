namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Observable policy for the passive native replay view. Keeping these exclusions explicit
/// prevents later compatibility repairs from silently turning playback back into simulation.
/// </summary>
internal static class MatchReplayViewBootstrapContract
{
    internal const string AdapterName = "native-view-projection-v2";

    internal const bool UsesNativeFightInitializer = false;

    internal const bool RunsCareerOrRelicScripts = false;

    internal const bool RunsEnemyInitScripts = false;

    internal const bool StartsTurnRuntime = false;

    internal const bool StartsNetworkOrRpc = false;

    internal static string Describe()
    {
        return AdapterName
               + "; fightInitializer=off"
               + "; careerRelicScripts=off"
               + "; enemyInitScripts=off"
               + "; turnRuntime=off"
               + "; networkRpc=off";
    }
}
