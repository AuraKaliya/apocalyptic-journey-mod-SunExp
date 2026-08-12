namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal sealed class MatchReplayFailureNotificationState
{
    private int generation;

    internal bool IsVisible { get; private set; }

    internal int Schedule()
    {
        IsVisible = false;
        return ++generation;
    }

    internal bool TryPresent(int ticket)
    {
        if (ticket != generation)
        {
            return false;
        }

        IsVisible = true;
        return true;
    }

    internal void Dismiss()
    {
        generation++;
        IsVisible = false;
    }
}
