using System;

namespace AuraDirector.Shared;

public interface IAuraDirectorNativeStartHold
{
    string BackendId { get; }

    object NativeTarget { get; }

    bool IsReleased { get; }

    string ReleaseReason { get; }

    bool TryRelease(string reason);
}

public interface IAuraDirectorNativeStartHoldSink
{
    bool TryAccept(IAuraDirectorNativeStartHold hold);
}
