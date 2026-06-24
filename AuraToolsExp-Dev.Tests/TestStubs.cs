namespace AuraToolsExp.Dll.Infrastructure;

public static class RoleCatalog
{
    public static string NormalizeRoleId(string? roleId)
    {
        return roleId?.Trim() ?? "";
    }
}
