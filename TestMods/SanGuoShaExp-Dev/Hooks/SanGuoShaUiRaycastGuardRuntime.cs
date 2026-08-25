using System;
using SanGuoShaExp.Dll.Infrastructure;
using UiTransitionGuardShared;
using Witch.Mod;

namespace SanGuoShaExp.Dll.Hooks;

public static class SanGuoShaUiRaycastGuardRuntime
{
    private const string ModId = "SanGuoShaExp";
    private static ModConfig? currentModConfig;

    public static bool IsTransitionGuardActive => UiTransitionGuardRuntime.IsGuardActive(currentModConfig, ModId);

    public static void Initialize(ModConfig modConfig)
    {
        currentModConfig = modConfig;
        UiTransitionGuardRuntime.Initialize(modConfig, ModId);
        SanGuoShaExpLog.Info("UI transition guard shared runtime initialized.");
    }

    public static void BeginTransitionGuard(string source)
    {
        UiTransitionGuardRuntime.BeginTransition(currentModConfig, ModId, source);
    }

    public static void RunAfterGuard(string source, Action action, int extraFrames = 4)
    {
        UiTransitionGuardRuntime.RunAfterGuard(currentModConfig, ModId, source, action, extraFrames);
    }
}
