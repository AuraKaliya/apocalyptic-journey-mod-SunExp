namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal enum ReplayAudioReadPath
{
    AudioClipGetData,
    UnitySampleProvider
}

internal static class ReplayAudioCapturePolicy
{
    internal static ReplayAudioReadPath SelectReadPath(bool isStreamingClip)
    {
        return isStreamingClip
            ? ReplayAudioReadPath.UnitySampleProvider
            : ReplayAudioReadPath.AudioClipGetData;
    }
}
