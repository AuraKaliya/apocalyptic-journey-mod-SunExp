namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritVirtualGridRefreshState
{
    private const float ViewportEpsilon = 0.25f;
    private bool pending;
    private bool force;
    private bool hasViewport;
    private float viewportWidth;
    private float viewportHeight;

    public bool Request(bool forceRefresh)
    {
        force |= forceRefresh;
        if (pending) return false;
        pending = true;
        return true;
    }

    public bool Drain()
    {
        var result = force;
        pending = false;
        force = false;
        return result;
    }

    public bool ObserveViewport(float width, float height)
    {
        if (hasViewport
            && System.Math.Abs(width - viewportWidth) <= ViewportEpsilon
            && System.Math.Abs(height - viewportHeight) <= ViewportEpsilon)
            return false;
        hasViewport = true;
        viewportWidth = width;
        viewportHeight = height;
        return true;
    }

    public void Cancel()
    {
        pending = false;
        force = false;
    }

    public void Reset()
    {
        Cancel();
        hasViewport = false;
        viewportWidth = 0f;
        viewportHeight = 0f;
    }
}
