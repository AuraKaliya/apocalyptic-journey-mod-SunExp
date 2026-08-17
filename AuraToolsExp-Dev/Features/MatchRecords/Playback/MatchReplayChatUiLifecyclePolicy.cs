namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayChatUiFinalizationModes
{
    internal const string WaitForNetwork = "WaitForNetwork";
    internal const string NativeClose = "NativeClose";
    internal const string PreserveQuarantined = "PreserveQuarantined";
}

/// <summary>
/// Keeps the native chat RPC target alive until Mirror is fully stopped and
/// chooses the safe terminal action from the callback-detachment result.
/// </summary>
internal static class MatchReplayChatUiLifecyclePolicy
{
    internal static string ResolveFinalization(bool networkStopped, bool callbacksDetached)
    {
        if (!networkStopped)
        {
            return MatchReplayChatUiFinalizationModes.WaitForNetwork;
        }

        return callbacksDetached
            ? MatchReplayChatUiFinalizationModes.NativeClose
            : MatchReplayChatUiFinalizationModes.PreserveQuarantined;
    }
}
