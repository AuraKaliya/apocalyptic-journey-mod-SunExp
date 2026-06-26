using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public static class DialogueFlowRegistry
{
    private static readonly Dictionary<string, DialogueFlowDefinition> ByFlowId = new();
    private static readonly Dictionary<string, string> FlowIdByDialogueId = new();

    public static void Register(DialogueFlowDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.FlowId) || string.IsNullOrWhiteSpace(definition.DialogueId))
        {
            return;
        }

        ByFlowId[definition.FlowId] = definition;
        FlowIdByDialogueId[definition.DialogueId] = definition.FlowId;
    }

    public static bool TryGetByFlowId(string flowId, out DialogueFlowDefinition definition)
    {
        return ByFlowId.TryGetValue(flowId, out definition!);
    }

    public static bool TryGetByDialogueId(string dialogueId, out DialogueFlowDefinition definition)
    {
        definition = null!;
        if (!FlowIdByDialogueId.TryGetValue(dialogueId, out var flowId))
        {
            return false;
        }

        return TryGetByFlowId(flowId, out definition);
    }
}
