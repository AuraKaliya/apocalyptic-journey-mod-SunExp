namespace Terrias.Dll.Mechanics;

public static class ProjectionActorTurnPolicy
{
    public static bool CanEndTurn(int legalNonEndActionCount)
    {
        return legalNonEndActionCount <= 0;
    }
}
