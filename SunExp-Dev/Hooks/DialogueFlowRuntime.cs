using System;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class DialogueFlowRuntime
{
    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "DialogueUI.ChooseOption", OnChooseOptionAfter);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "DialogueFlow");
    }

    private static void OnChooseOptionAfter(ModHookContext context)
    {
        try
        {
            if (!DialogueUiApi.TryGetDialogueId(context.Target, out var dialogueId)
                || !DialogueFlowService.IsManagedDialogue(dialogueId))
            {
                return;
            }

            var choiceIndex = ChoiceIndexFromArgs(context.Arguments);
            DialogueFlowService.CompleteChoice(dialogueId, choiceIndex);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Dialogue flow choice hook failed", ex);
        }
    }

    private static int ChoiceIndexFromArgs(object[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return 1;
        }

        try
        {
            return Convert.ToInt32(args[0]);
        }
        catch
        {
            return 1;
        }
    }
}
