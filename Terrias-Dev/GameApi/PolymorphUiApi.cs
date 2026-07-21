using SunExp.Dll.Hooks.Ui;

namespace SunExp.Dll.GameApi;

public static class PolymorphUiApi
{
    public static bool OpenRoleSelection(ScriptExecutor self)
    {
        return PolymorphRoleSelectionWindow.Open(self);
    }

    public static bool OpenRoleSelection(ScriptExecutor self, PolymorphRoleSelectionRequest request)
    {
        return PolymorphRoleSelectionWindow.Open(self, request);
    }

    public static void CloseRoleSelection(string source)
    {
        PolymorphRoleSelectionWindow.Close(source);
    }
}
