using System;
using SkillCGExp.Dll.Config;
using SkillCGExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SkillCGExp.Dll.Hooks;

public static class SkillCgRuntime
{
    private const string ModId = "SkillCGExp";
    private static SkillCgConfig? config;
    private static long actionSequence;

    public static void Initialize(ModConfig modConfig)
    {
        if (modConfig == null)
        {
            SkillCgExpLog.WarnOnce("null-mod-config", "Initialization skipped: modConfig is null.");
            return;
        }

        config = SkillCgConfig.Load(modConfig.DirectoryName);
        config.Normalize(modConfig.DirectoryName);
        SkillCgArbiterRuntime.Initialize(modConfig, ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = config.maxQueueLength,
            MaxRequestAgeSeconds = config.maxRequestAgeSeconds,
            DuplicateWindowSeconds = config.duplicateWindowSeconds
        });

        if (config.enabled)
        {
            SkillCgArbiterRuntime.RegisterProvider(modConfig, ModId, new ConfigSkillCgProvider(config.rules, config.syncRemote));
        }

        RegisterBefore(modConfig, "FightUI.CallActionAnimation", BeforeCallActionAnimation);
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart);
        RegisterAfter(modConfig, "FightInit.Init", OnFightStart);
        RegisterBefore(modConfig, "Fight_Win.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Loss.Init", OnFightEnding);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);

        SkillCgExpLog.InfoOnce(
            "initialized",
            "Initialized. enabled=" + config.enabled
            + ", syncRemote=" + config.syncRemote
            + ", rules=" + config.rules.Length
            + ", maxQueue=" + config.maxQueueLength
            + ", maxAge=" + config.maxRequestAgeSeconds.ToString("0.##") + "s");
    }

    private static void BeforeCallActionAnimation(ModHookContext context)
    {
        try
        {
            if (config == null || !config.enabled)
            {
                return;
            }

            var scriptExecutor = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null;
            var trigger = BuildTriggerContext(scriptExecutor);
            if (trigger == null)
            {
                return;
            }

            SkillCgArbiterRuntime.Trigger(config, ModId, trigger);
        }
        catch (Exception ex)
        {
            SkillCgExpLog.WarnOnce("trigger-failed", "CG trigger failed once; later errors are suppressed. error=" + ex.Message);
            SkillCgExpLog.DebugLog("CG trigger exception: " + ex);
        }
    }

    private static SkillCgTriggerContext? BuildTriggerContext(IScriptExecutor? scriptExecutor)
    {
        var dataConfig = scriptExecutor?.dataConfig;
        if (dataConfig == null || dataConfig.Type != DataType.Card || dataConfig.data == null)
        {
            return null;
        }

        var cardId = ReadData(dataConfig, "Id");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            cardId = dataConfig.InstanceID ?? "";
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        var action = ReadData(dataConfig, "Action");

        var owner = scriptExecutor?.Self as StatusManager;
        var ownerInstanceId = owner?.InstanceId ?? "";
        return new SkillCgTriggerContext
        {
            ActionSequence = ++actionSequence,
            Action = action,
            CardId = cardId,
            OwnerInstanceId = ownerInstanceId,
            CreatedAt = UnityEngine.Time.unscaledTime
        };
    }

    private static string ReadData(IDataConfig dataConfig, string key)
    {
        try
        {
            return dataConfig.data.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static void OnFightStart(ModHookContext context)
    {
        actionSequence = 0;
        SkillCgArbiterRuntime.Clear(ModId, "fight start");
    }

    private static void OnFightEnded(ModHookContext context)
    {
        SkillCgArbiterRuntime.Clear(ModId, "fight ended");
    }

    private static void OnFightEnding(ModHookContext context)
    {
        SkillCgArbiterRuntime.Clear(ModId, "fight ending");
    }

    private static void RegisterBefore(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        try
        {
            modConfig.AddMethodHookBefore(target, action);
            SkillCgExpLog.InfoOnce("hook-before:" + target, "Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SkillCgExpLog.WarnOnce("hook-before-failed:" + target, "Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        try
        {
            modConfig.AddMethodHookAfter(target, action);
            SkillCgExpLog.InfoOnce("hook-after:" + target, "Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SkillCgExpLog.WarnOnce("hook-after-failed:" + target, "Hook after failed: " + target + " -> " + ex.Message);
        }
    }
}
