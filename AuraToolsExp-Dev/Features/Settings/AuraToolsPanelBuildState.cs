namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class AuraToolsPanelBuildState
{
    private int generation;

    internal bool IsBuilt { get; private set; }

    internal bool IsBuilding { get; private set; }

    internal int Begin()
    {
        if (IsBuilding)
        {
            return 0;
        }

        IsBuilding = true;
        IsBuilt = false;
        return ++generation;
    }

    internal bool IsCurrent(int ticket)
    {
        return IsBuilding && ticket == generation;
    }

    internal void Complete(int ticket, bool succeeded)
    {
        if (ticket != generation)
        {
            return;
        }

        IsBuilding = false;
        IsBuilt = succeeded;
    }

    internal void CancelBuild()
    {
        if (!IsBuilding)
        {
            return;
        }

        generation++;
        IsBuilding = false;
    }

    internal void Adopt(bool built)
    {
        generation++;
        IsBuilding = false;
        IsBuilt = built;
    }

    internal void Reset()
    {
        generation++;
        IsBuilding = false;
        IsBuilt = false;
    }
}
