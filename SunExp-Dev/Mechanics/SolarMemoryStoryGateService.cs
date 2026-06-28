using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SolarMemoryStoryGateService
{
    public static bool TryStartPostPreparationDialogue(bool isSolarMemoryRun, bool alreadySeen, Action<int> complete)
    {
        if (!ShouldShowPostPreparationDialogue(isSolarMemoryRun, alreadySeen))
        {
            return false;
        }

        return TryStartDialogue(
            SunExpIds.SolarMemoryPostPreparationDialogueFlowId,
            SunExpIds.SolarMemoryPostPreparationDialogueId,
            SunExpIds.SolarMemoryPostPreparationCompleteDialogueId,
            "post-preparation",
            complete);
    }

    public static bool TryStartDialogue(string flowId, string dialogueId, string completeDialogueId, string label, Action<int> complete)
    {
        var definition = new DialogueFlowDefinition(flowId, dialogueId, completeDialogueId, complete);
        if (!DialogueFlowService.Start(definition))
        {
            SunExpLog.Warn("[SolarMemoryStory] " + label + " dialogue could not be shown; continuing memory flow.");
            return false;
        }

        SunExpLog.Info("[SolarMemoryStory] opened " + label + " dialogue.");
        return true;
    }

    public static bool ShouldShowPostPreparationDialogue(bool isSolarMemoryRun, bool alreadySeen)
    {
        return isSolarMemoryRun && !alreadySeen;
    }
}
