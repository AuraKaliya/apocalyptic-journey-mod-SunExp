namespace Terrias.Dll.Hooks.Ui;

internal enum SpiritArtifactWishNavigationAction
{
    None,
    SkipToResults,
    AcknowledgeAndClose
}

internal sealed class SpiritArtifactWishNavigationState
{
    private bool resultsVisible;
    private bool closeRequested;

    public void Reset()
    {
        resultsVisible = false;
        closeRequested = false;
    }

    public void MarkResultsVisible()
    {
        resultsVisible = true;
        closeRequested = false;
    }

    public SpiritArtifactWishNavigationAction RequestEscape()
    {
        return resultsVisible
            ? RequestClose()
            : SpiritArtifactWishNavigationAction.SkipToResults;
    }

    public SpiritArtifactWishNavigationAction RequestClose()
    {
        if (!resultsVisible || closeRequested) return SpiritArtifactWishNavigationAction.None;
        closeRequested = true;
        return SpiritArtifactWishNavigationAction.AcknowledgeAndClose;
    }
}
