using System;

namespace SunExp.Dll.Mechanics;

public sealed class DialogueFlowDefinition
{
    public DialogueFlowDefinition(string flowId, string dialogueId, Action<int> complete)
    {
        FlowId = flowId ?? "";
        DialogueId = dialogueId ?? "";
        Complete = complete ?? (_ => { });
    }

    public string FlowId { get; }

    public string DialogueId { get; }

    public Action<int> Complete { get; }
}
