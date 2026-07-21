using System;
using Terrias.Dll.Infrastructure;
using Witch;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public static class DialogueApi
{
    public static bool ShowDialogue(string dialogueId)
    {
        if (string.IsNullOrWhiteSpace(dialogueId))
        {
            return false;
        }

        try
        {
            Singleton<DialogueManager>.Instance.ShowDialogue(dialogueId);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[DialogueApi] ShowDialogue failed for " + dialogueId + ": " + ex.Message);
            return false;
        }
    }

    public static void EndDialogue()
    {
        try
        {
            Singleton<DialogueManager>.Instance.EndDialogue();
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[DialogueApi] EndDialogue failed: " + ex.Message);
        }
    }
}
