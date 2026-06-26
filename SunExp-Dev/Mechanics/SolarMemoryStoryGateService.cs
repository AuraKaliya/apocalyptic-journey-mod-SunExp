using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class SolarMemoryStoryGateService
{
    public static bool TryStartPostPreparationDialogue(bool isSolarMemoryRun, bool alreadySeen, Action complete)
    {
        if (!ShouldShowPostPreparationDialogue(isSolarMemoryRun, alreadySeen))
        {
            return false;
        }

        var definition = new DialogueFlowDefinition(
            SunExpIds.SolarMemoryPostPreparationDialogueFlowId,
            SunExpIds.SolarMemoryPostPreparationDialogueId,
            _ => complete());

        if (!DialogueFlowService.Start(definition))
        {
            SunExpLog.Warn("[SolarMemoryStory] post-preparation dialogue could not be shown; continuing memory flow.");
            return false;
        }

        SunExpLog.Info("[SolarMemoryStory] opened post-preparation dialogue.");
        return true;
    }

    public static bool ShouldShowPostPreparationDialogue(bool isSolarMemoryRun, bool alreadySeen)
    {
        return isSolarMemoryRun && !alreadySeen;
    }
}
