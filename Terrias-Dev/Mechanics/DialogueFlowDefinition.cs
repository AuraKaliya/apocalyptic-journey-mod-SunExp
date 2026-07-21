using System;

namespace Terrias.Dll.Mechanics;

public sealed class DialogueFlowDefinition
{
    public DialogueFlowDefinition(string flowId, string dialogueId, Action<int> complete)
        : this(flowId, dialogueId, dialogueId, complete)
    {
    }

    public DialogueFlowDefinition(string flowId, string dialogueId, string completeDialogueId, Action<int> complete)
    {
        FlowId = flowId ?? "";
        DialogueId = dialogueId ?? "";
        CompleteDialogueId = completeDialogueId ?? "";
        Complete = complete ?? (_ => { });
    }

    public string FlowId { get; }

    public string DialogueId { get; }

    public string CompleteDialogueId { get; }

    public Action<int> Complete { get; }
}
