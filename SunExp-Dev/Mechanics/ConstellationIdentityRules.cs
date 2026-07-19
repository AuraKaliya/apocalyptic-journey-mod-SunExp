namespace SunExp.Dll.Mechanics;

public static class ConstellationIdentityRules
{
    public static string ResolveAdventureRole(
        string? boundAdventureRole,
        string? polymorphOriginalRole,
        string? currentCombatRole)
    {
        var bound = Clean(boundAdventureRole);
        if (bound.Length > 0)
        {
            return bound;
        }

        var original = Clean(polymorphOriginalRole);
        if (original.Length > 0)
        {
            return original;
        }

        return Clean(currentCombatRole);
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }
}
