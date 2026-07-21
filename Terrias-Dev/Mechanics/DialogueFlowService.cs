using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class DialogueFlowService
{
    private static string activeFlowId = "";
    private static string activeDialogueId = "";
    private static bool completingChoice;

    public static bool Start(DialogueFlowDefinition definition)
    {
        DialogueFlowRegistry.Register(definition);
        if (string.IsNullOrWhiteSpace(definition.FlowId) || string.IsNullOrWhiteSpace(definition.DialogueId))
        {
            return false;
        }

        activeFlowId = definition.FlowId;
        activeDialogueId = definition.DialogueId;
        if (DialogueApi.ShowDialogue(definition.DialogueId))
        {
            SunExpLog.Info("[DialogueFlow] started " + definition.FlowId + " -> " + definition.DialogueId);
            return true;
        }

        ClearActive(definition.FlowId);
        return false;
    }

    public static bool CompleteChoice(string dialogueId, int choiceIndex)
    {
        if (completingChoice
            || string.IsNullOrWhiteSpace(dialogueId)
            || !DialogueFlowRegistry.TryGetByDialogueId(dialogueId, out var definition))
        {
            return false;
        }

        completingChoice = true;
        try
        {
            if (!string.Equals(activeDialogueId, dialogueId, StringComparison.Ordinal))
            {
                SunExpLog.Info("[DialogueFlow] recovering unmanaged active session for " + dialogueId);
            }

            DialogueApi.EndDialogue();
            ClearActive(definition.FlowId);
            definition.Complete(choiceIndex);
            SunExpLog.Info("[DialogueFlow] completed " + definition.FlowId + " choice " + choiceIndex);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[DialogueFlow] completion failed for " + dialogueId, ex);
            return false;
        }
        finally
        {
            completingChoice = false;
        }
    }

    public static bool IsManagedDialogue(string dialogueId)
    {
        return !string.IsNullOrWhiteSpace(dialogueId)
            && DialogueFlowRegistry.TryGetByDialogueId(dialogueId, out _);
    }

    private static void ClearActive(string flowId)
    {
        if (string.Equals(activeFlowId, flowId, StringComparison.Ordinal))
        {
            activeFlowId = "";
            activeDialogueId = "";
        }
    }
}
