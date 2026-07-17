using System;
using System.Collections.Generic;

namespace AudioArbiter.Shared;

internal enum AudioHookRegistrationKind
{
    Before,
    After,
    CombatActionBefore
}

internal enum AudioHookCallbackKind
{
    CareerSessionReset,
    FightStartBefore,
    FightStartAfter,
    CareerDetailShown,
    CombatActionBefore,
    NativeEffectBefore,
    BuffApplied,
    VocalState,
    NarrationPlay,
    PotentialHpChanged,
    StatusHpChanged,
    FightWin,
    FightEscape
}

internal readonly struct AudioHookDefinition
{
    public AudioHookDefinition(
        string handlerId,
        string target,
        AudioHookRegistrationKind registrationKind,
        AudioHookCallbackKind callbackKind)
    {
        HandlerId = handlerId ?? "";
        Target = target ?? "";
        RegistrationKind = registrationKind;
        CallbackKind = callbackKind;
    }

    public string HandlerId { get; }

    public string Target { get; }

    public AudioHookRegistrationKind RegistrationKind { get; }

    public AudioHookCallbackKind CallbackKind { get; }
}

internal static class AudioHookCatalog
{
    private static readonly IReadOnlyList<AudioHookDefinition> Definitions = Array.AsReadOnly(new[]
    {
        new AudioHookDefinition("career-session-reset", "GameEntryUI.Init", AudioHookRegistrationKind.After, AudioHookCallbackKind.CareerSessionReset),
        new AudioHookDefinition("fight-start-before", "Fight_Start.Init", AudioHookRegistrationKind.Before, AudioHookCallbackKind.FightStartBefore),
        new AudioHookDefinition("fight-start-after", "Fight_Start.Init", AudioHookRegistrationKind.After, AudioHookCallbackKind.FightStartAfter),
        new AudioHookDefinition("career-detail-shown", "GameEntryUI.ShowDetail", AudioHookRegistrationKind.After, AudioHookCallbackKind.CareerDetailShown),
        new AudioHookDefinition("combat-action", "FightUI.CallActionAnimation", AudioHookRegistrationKind.CombatActionBefore, AudioHookCallbackKind.CombatActionBefore),
        new AudioHookDefinition("native-effect", "EffectSound.Start", AudioHookRegistrationKind.Before, AudioHookCallbackKind.NativeEffectBefore),
        new AudioHookDefinition("buff-applied", "BuffItem.Init", AudioHookRegistrationKind.After, AudioHookCallbackKind.BuffApplied),
        new AudioHookDefinition("vocal-state", "StatusManager.PlayVocal", AudioHookRegistrationKind.After, AudioHookCallbackKind.VocalState),
        new AudioHookDefinition("narration-play", "NarrationManager.Play", AudioHookRegistrationKind.After, AudioHookCallbackKind.NarrationPlay),
        new AudioHookDefinition("script-change-hp", "ScriptExecutor.ChangeHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("script-pure-change-hp", "ScriptExecutor.PureChangeHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("script-set-hp", "ScriptExecutor.SetHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("script-change-max-hp", "ScriptExecutor.ChangeMaxHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("script-damage", "ScriptExecutor.Damage", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("script-online-damage", "ScriptExecutor.OnlineDamage", AudioHookRegistrationKind.After, AudioHookCallbackKind.PotentialHpChanged),
        new AudioHookDefinition("status-cur-hp", "StatusManager.set_CurHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.StatusHpChanged),
        new AudioHookDefinition("status-max-hp", "StatusManager.set_MaxHp", AudioHookRegistrationKind.After, AudioHookCallbackKind.StatusHpChanged),
        new AudioHookDefinition("fight-win", "Fight_Win.ResetStates", AudioHookRegistrationKind.After, AudioHookCallbackKind.FightWin),
        new AudioHookDefinition("fight-escape", "Fight_Escape.ResetStates", AudioHookRegistrationKind.After, AudioHookCallbackKind.FightEscape)
    });

    public static IReadOnlyList<AudioHookDefinition> All => Definitions;
}
