using System;
using System.Collections.Generic;
using Fight.ActionCommand;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraShared.Core;

/// <summary>
/// Observes the game's already-authoritative remote combat presentation stream.
/// It never sends commands and never infers private hand state.
/// </summary>
public static class AuraRemoteCombatActionRouter
{
    private const string ExecuteTarget = "ActionCommandBase.Execute";
    private const string CardPresentationTarget = "FightUI.DoCardUseAnimation";
    private const string ActionPresentationTarget = "FightUI.DOActionAnimation";
    private const string StatusPopulateTarget = "StatusDataTransfer.Populate";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static IDisposable? executeBeforeRegistration;
    private static IDisposable? executeAfterRegistration;
    private static IDisposable? cardRegistration;
    private static IDisposable? actionRegistration;
    private static IDisposable? statusRegistration;
    private static readonly Stack<RemoteCommandScope> CommandScopes = new();
    private static long commandSequence;

    public static IDisposable Register(
        ModConfig modConfig,
        string handlerId,
        AuraRemoteCombatActionSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        var id = string.IsNullOrWhiteSpace(handlerId)
            ? "handler-" + Guid.NewGuid().ToString("N")
            : handlerId.Trim();

        lock (Gate)
        {
            Handlers[id] = new Handler(id, subscription ?? new AuraRemoteCombatActionSubscription(), warn);
            executeBeforeRegistration ??= AuraSharedHooks.RegisterBeforeRouted(
                modConfig, ExecuteTarget, BeginRemoteCommand, info, warn, safeInvoke: true);
            executeAfterRegistration ??= AuraSharedHooks.RegisterAfterRouted(
                modConfig, ExecuteTarget, EndRemoteCommand, info, warn, safeInvoke: true);
            cardRegistration ??= AuraSharedHooks.RegisterBeforeRouted(
                modConfig, CardPresentationTarget, DispatchCardPresentation, info, warn, safeInvoke: true);
            actionRegistration ??= AuraSharedHooks.RegisterBeforeRouted(
                modConfig, ActionPresentationTarget, DispatchActionPresentation, info, warn, safeInvoke: true);
            statusRegistration ??= AuraSharedHooks.RegisterAfterRouted(
                modConfig, StatusPopulateTarget, DispatchStatus, info, warn, safeInvoke: true);
        }

        return new Subscription(id);
    }

    internal static AuraRemoteCombatActionContext? TryBuildCommandContext(object? value, string localPlayerId = "")
    {
        if (value is not ActionCommandBase command)
        {
            return null;
        }

        var actorId = command.From?.Trim() ?? "";
        var localId = string.IsNullOrWhiteSpace(localPlayerId)
            ? PlayerManager.Instance?.PlayerId?.Trim() ?? ""
            : localPlayerId.Trim();
        if (string.IsNullOrWhiteSpace(actorId)
            || (!string.IsNullOrWhiteSpace(localId)
                && string.Equals(actorId, localId, StringComparison.Ordinal)))
        {
            return null;
        }

        return new AuraRemoteCombatActionContext
        {
            ActorId = actorId,
            ActionCommand = command,
            CommandType = command.GetType().FullName ?? command.Type ?? "",
            CommandSequence = ++commandSequence,
            CreatedAt = Time.unscaledTime
        };
    }

    internal static AuraRemoteCombatActionContext? TryBuildCardPresentationContext(
        string actorId,
        long sequence,
        object[]? arguments)
    {
        if (string.IsNullOrWhiteSpace(actorId)
            || arguments == null
            || arguments.Length == 0
            || arguments[0] is not UseCard.CardUseData data)
        {
            return null;
        }

        return new AuraRemoteCombatActionContext
        {
            Kind = AuraRemoteCombatActionKinds.CardUse,
            ActorId = actorId,
            CommandSequence = sequence,
            CardData = data.cardData,
            IsBurning = data.isBurning,
            CreatedAt = Time.unscaledTime
        };
    }

    internal static AuraRemoteCombatActionContext? TryBuildActionPresentationContext(
        string actorId,
        long sequence,
        object? target)
    {
        if (string.IsNullOrWhiteSpace(actorId)
            || target is not FightUI fightUi
            || fightUi.animationQueue == null
            || fightUi.animationQueue.Count == 0)
        {
            return null;
        }

        var data = fightUi.animationQueue.Peek();
        var result = new AuraRemoteCombatActionContext
        {
            Kind = AuraRemoteCombatActionKinds.ActionAnimation,
            ActorId = actorId,
            CommandSequence = sequence,
            EffectName = data.effectName ?? "",
            CreatedAt = Time.unscaledTime
        };
        var statuses = data.status ?? Array.Empty<StatusManager>();
        var states = data.animationState ?? Array.Empty<IStatusManager.AnimatedState>();
        for (var index = 0; index < statuses.Length; index++)
        {
            result.AnimationTargets.Add(new AuraRemoteAnimationTarget
            {
                StatusInstanceId = statuses[index]?.InstanceId ?? "",
                AnimationState = index < states.Length ? states[index].ToString() : ""
            });
        }

        return result;
    }

    internal static AuraAuthoritativeStatusContext? TryBuildStatusContext(object? target, object[]? arguments)
    {
        if (target is not StatusDataTransfer transfer
            || arguments == null
            || arguments.Length == 0
            || arguments[0] is not StatusManager status)
        {
            return null;
        }

        return new AuraAuthoritativeStatusContext
        {
            StatusInstanceId = string.IsNullOrWhiteSpace(transfer.InstanceId)
                ? status.InstanceId ?? ""
                : transfer.InstanceId,
            Version = transfer.Version,
            Status = status,
            Transfer = transfer,
            AppliedAt = Time.unscaledTime
        };
    }

    private static void BeginRemoteCommand(ModHookContext hookContext)
    {
        var context = TryBuildCommandContext(hookContext.Target);
        CommandScopes.Push(context == null
            ? RemoteCommandScope.Local
            : new RemoteCommandScope(context.ActorId, context.CommandSequence));
    }

    private static void EndRemoteCommand(ModHookContext hookContext)
    {
        if (CommandScopes.Count > 0)
        {
            CommandScopes.Pop();
        }
    }

    private static void DispatchCardPresentation(ModHookContext hookContext)
    {
        var scope = CurrentScope();
        var context = TryBuildCardPresentationContext(
            scope.ActorId,
            scope.CommandSequence,
            hookContext.Arguments);
        DispatchCommand(context);
    }

    private static void DispatchActionPresentation(ModHookContext hookContext)
    {
        var scope = CurrentScope();
        var context = TryBuildActionPresentationContext(
            scope.ActorId,
            scope.CommandSequence,
            hookContext.Target);
        DispatchCommand(context);
    }

    private static RemoteCommandScope CurrentScope()
    {
        return CommandScopes.Count == 0 ? RemoteCommandScope.Local : CommandScopes.Peek();
    }

    private static void DispatchCommand(AuraRemoteCombatActionContext? context)
    {
        if (context == null)
        {
            return;
        }

        foreach (var handler in Snapshot())
        {
            handler.InvokeCommand(context);
        }
    }

    private static void DispatchStatus(ModHookContext hookContext)
    {
        var context = TryBuildStatusContext(hookContext.Target, hookContext.Arguments);
        if (context == null)
        {
            return;
        }

        foreach (var handler in Snapshot())
        {
            handler.InvokeStatus(context);
        }
    }

    private static Handler[] Snapshot()
    {
        lock (Gate)
        {
            var snapshot = new Handler[Handlers.Count];
            Handlers.Values.CopyTo(snapshot, 0);
            return snapshot;
        }
    }

    private sealed class Handler
    {
        private readonly AuraRemoteCombatActionSubscription subscription;
        private readonly Action<string>? warn;

        public Handler(string id, AuraRemoteCombatActionSubscription subscription, Action<string>? warn)
        {
            Id = id;
            this.subscription = subscription;
            this.warn = warn;
        }

        public string Id { get; }

        public void InvokeCommand(AuraRemoteCombatActionContext context)
        {
            Invoke(() => subscription.CommandObserved?.Invoke(context), "command");
        }

        public void InvokeStatus(AuraAuthoritativeStatusContext context)
        {
            Invoke(() => subscription.AuthoritativeStatusApplied?.Invoke(context), "status");
        }

        private void Invoke(Action action, string stage)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                warn?.Invoke("[AuraRemoteCombatActionRouter] " + stage + " handler failed: "
                             + Id + ", error=" + ex.Message);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private bool disposed;

        public Subscription(string id)
        {
            this.id = id;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (Gate)
            {
                Handlers.Remove(id);
            }
        }
    }

    private readonly struct RemoteCommandScope
    {
        public static readonly RemoteCommandScope Local = new("", 0);

        public RemoteCommandScope(string actorId, long commandSequence)
        {
            ActorId = actorId ?? "";
            CommandSequence = commandSequence;
        }

        public string ActorId { get; }

        public long CommandSequence { get; }
    }
}

public static class AuraRemoteCombatActionKinds
{
    public const string CardUse = "CardUse";
    public const string ActionAnimation = "ActionAnimation";
    public const string Other = "Other";
}

public sealed class AuraRemoteCombatActionSubscription
{
    public Action<AuraRemoteCombatActionContext>? CommandObserved { get; set; }

    public Action<AuraAuthoritativeStatusContext>? AuthoritativeStatusApplied { get; set; }
}

public sealed class AuraRemoteCombatActionContext
{
    public string Kind { get; set; } = AuraRemoteCombatActionKinds.Other;

    public string ActorId { get; set; } = "";

    public string CommandType { get; set; } = "";

    public long CommandSequence { get; set; }

    public float CreatedAt { get; set; }

    public object? ActionCommand { get; set; }

    public IDataConfig? CardData { get; set; }

    public bool IsBurning { get; set; }

    public string EffectName { get; set; } = "";

    public List<AuraRemoteAnimationTarget> AnimationTargets { get; } = new();
}

public sealed class AuraRemoteAnimationTarget
{
    public string StatusInstanceId { get; set; } = "";

    public string AnimationState { get; set; } = "";
}

public sealed class AuraAuthoritativeStatusContext
{
    public string StatusInstanceId { get; set; } = "";

    public int Version { get; set; }

    public StatusManager? Status { get; set; }

    public object? Transfer { get; set; }

    public float AppliedAt { get; set; }
}
