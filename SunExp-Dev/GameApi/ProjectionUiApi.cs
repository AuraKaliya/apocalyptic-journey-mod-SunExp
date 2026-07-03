using SunExp.Dll.Hooks.Ui;

namespace SunExp.Dll.GameApi;

public static class ProjectionUiApi
{
    public static bool OpenRoleSelection(ScriptExecutor self)
    {
        return PolymorphRoleSelectionWindow.Open(self, PolymorphRoleSelectionRequest.Projection(self));
    }

    public static void CloseRoleSelection(string source)
    {
        PolymorphRoleSelectionWindow.Close(source);
    }
}
