using System;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class DialogueUiApi
{
    private static readonly FieldInfo? DataConfigField = typeof(DialogueUI).GetField(
        "dataConfig",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static bool TryGetDialogueId(object? dialogueUi, out string dialogueId)
    {
        dialogueId = "";
        if (dialogueUi == null || DataConfigField == null)
        {
            return false;
        }

        try
        {
            if (DataConfigField.GetValue(dialogueUi) is not IDataConfig config
                || config.data == null
                || !config.data.TryGetValue("Id", out var id)
                || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            dialogueId = id;
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[DialogueUiApi] failed to read current dialogue id: " + ex.Message);
            return false;
        }
    }
}
