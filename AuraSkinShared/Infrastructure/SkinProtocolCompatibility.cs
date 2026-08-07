namespace AuraSkin.Shared.Infrastructure;

internal static class SkinProtocolCompatibility
{
    public static bool IsCompatible(
        int localCurrent,
        int localMinimumSupported,
        int remoteCurrent,
        int remoteMinimumSupported)
    {
        if (localCurrent < 1
            || localMinimumSupported < 1
            || remoteCurrent < 1
            || remoteMinimumSupported < 1
            || localMinimumSupported > localCurrent
            || remoteMinimumSupported > remoteCurrent)
        {
            return false;
        }

        return remoteCurrent >= localMinimumSupported
               && remoteMinimumSupported <= localCurrent;
    }
}
