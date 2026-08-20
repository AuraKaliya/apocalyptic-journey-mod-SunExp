using System;
using System.Collections.Generic;
using AuraShared.Core;
using Witch.Core;
using Witch.Mod;

namespace AudioArbiter.Shared;

internal sealed class AudioHookCallbacks
{
    public Action<ModHookContext>? CareerSessionReset { get; set; }

    public Action<ModHookContext>? FightStartBefore { get; set; }

    public Action<ModHookContext>? FightStartAfter { get; set; }

    public Action<ModHookContext>? CareerDetailShown { get; set; }

    public Action<AuraCardActionContext>? CombatActionBefore { get; set; }

    public Action<ModHookContext>? NativeEffectBefore { get; set; }

    public Action<ModHookContext>? BuffApplied { get; set; }

    public Action<ModHookContext>? VocalState { get; set; }

    public Action<ModHookContext>? NarrationPlay { get; set; }

    public Action<ModHookContext>? PotentialHpChanged { get; set; }

    public Action<ModHookContext>? StatusHpChanged { get; set; }

    public Action<ModHookContext>? FightWin { get; set; }

    public Action<ModHookContext>? FightEscape { get; set; }

    public Action<ModHookContext>? Resolve(AudioHookCallbackKind callbackKind)
    {
        return callbackKind switch
        {
            AudioHookCallbackKind.CareerSessionReset => CareerSessionReset,
            AudioHookCallbackKind.FightStartBefore => FightStartBefore,
            AudioHookCallbackKind.FightStartAfter => FightStartAfter,
            AudioHookCallbackKind.CareerDetailShown => CareerDetailShown,
            AudioHookCallbackKind.NativeEffectBefore => NativeEffectBefore,
            AudioHookCallbackKind.BuffApplied => BuffApplied,
            AudioHookCallbackKind.VocalState => VocalState,
            AudioHookCallbackKind.NarrationPlay => NarrationPlay,
            AudioHookCallbackKind.PotentialHpChanged => PotentialHpChanged,
            AudioHookCallbackKind.StatusHpChanged => StatusHpChanged,
            AudioHookCallbackKind.FightWin => FightWin,
            AudioHookCallbackKind.FightEscape => FightEscape,
            _ => null
        };
    }
}

internal sealed class AudioHookAdapter : IDisposable
{
    private readonly ModConfig modConfig;
    private readonly string ownerModId;
    private readonly AudioHookCallbacks callbacks;
    private readonly Action<string>? info;
    private readonly Action<string>? warn;
    private readonly List<IDisposable> routedRegistrations = new();
    private AuraHookRegistry? hookRegistry;
    private bool registrationAttempted;
    private bool disposed;

    public AudioHookAdapter(
        ModConfig modConfig,
        string ownerModId,
        AudioHookCallbacks callbacks,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        this.modConfig = modConfig ?? throw new ArgumentNullException(nameof(modConfig));
        this.ownerModId = string.IsNullOrWhiteSpace(ownerModId) ? "AudioArbiter" : ownerModId.Trim();
        this.callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        this.info = info;
        this.warn = warn;
    }

    public bool IsRegistered => registrationAttempted && !disposed;

    public void Register()
    {
        if (registrationAttempted || disposed)
        {
            return;
        }

        registrationAttempted = true;
        hookRegistry = new AuraHookRegistry(modConfig, ownerModId + ".Audio", info, warn);
        var registered = 0;
        var skipped = 0;
        foreach (var definition in AudioHookCatalog.All)
        {
            try
            {
                if (RegisterDefinition(definition))
                {
                    registered++;
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                skipped++;
                warn?.Invoke("Hook registration failed: handler=" + definition.HandlerId
                             + ", target=" + definition.Target
                             + " -> " + ex.Message);
            }
        }

        info?.Invoke("Hooks registered by owner=" + ownerModId
                     + ", definitions=" + AudioHookCatalog.All.Count
                     + ", registered=" + registered
                     + ", skipped=" + skipped);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        for (var i = routedRegistrations.Count - 1; i >= 0; i--)
        {
            try
            {
                routedRegistrations[i].Dispose();
            }
            catch (Exception ex)
            {
                warn?.Invoke("Hook registration dispose failed: " + ex.Message);
            }
        }

        routedRegistrations.Clear();
        hookRegistry?.Dispose();
        hookRegistry = null;
    }

    private bool RegisterDefinition(AudioHookDefinition definition)
    {
        if (definition.RegistrationKind == AudioHookRegistrationKind.CombatActionBefore)
        {
            if (callbacks.CombatActionBefore == null)
            {
                warn?.Invoke("Hook registration skipped: combat callback missing, handler=" + definition.HandlerId);
                return false;
            }

            routedRegistrations.Add(AuraCardActionTransactionRouter.Register(
                modConfig,
                ownerModId,
                ownerModId + ".Audio",
                new AuraCardActionSubscription
                {
                    Phases = AuraCardActionPhase.PresentationCommitted,
                    Handler = callbacks.CombatActionBefore
                },
                info,
                warn));
            return true;
        }

        var callback = callbacks.Resolve(definition.CallbackKind);
        if (callback == null || hookRegistry == null)
        {
            warn?.Invoke("Hook registration skipped: callback missing, handler=" + definition.HandlerId
                         + ", callback=" + definition.CallbackKind);
            return false;
        }

        switch (definition.RegistrationKind)
        {
            case AudioHookRegistrationKind.Before:
                hookRegistry.BeforeRouted(definition.Target, callback, definition.HandlerId);
                return true;
            case AudioHookRegistrationKind.After:
                hookRegistry.AfterRouted(definition.Target, callback, definition.HandlerId);
                return true;
            default:
                warn?.Invoke("Hook registration skipped: unsupported registration kind="
                             + definition.RegistrationKind
                             + ", handler=" + definition.HandlerId);
                return false;
        }
    }
}
