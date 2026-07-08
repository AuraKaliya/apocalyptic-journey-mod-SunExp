using System;
using System.Collections.Generic;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraShared.Core;

public static class AuraCombatActionRouter
{
    private const string ActionAnimationTarget = "FightUI.CallActionAnimation";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static IDisposable? hookRegistration;
    private static long actionSequence;

    public static IDisposable RegisterBefore(
        ModConfig modConfig,
        string handlerId,
        Action<AuraCombatActionContext> action,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        var id = string.IsNullOrWhiteSpace(handlerId)
            ? "handler-" + Guid.NewGuid().ToString("N")
            : handlerId.Trim();

        lock (Gate)
        {
            Handlers[id] = new Handler(id, action, warn);
            EnsureHookRegistered(modConfig, info, warn);
        }

        return new Subscription(id);
    }

    private static void EnsureHookRegistered(ModConfig modConfig, Action<string>? info, Action<string>? warn)
    {
        if (hookRegistration != null)
        {
            return;
        }

        hookRegistration = AuraSharedHooks.RegisterBeforeRouted(
            modConfig,
            ActionAnimationTarget,
            Dispatch,
            info,
            warn,
            safeInvoke: true);
    }

    private static void Dispatch(ModHookContext hookContext)
    {
        Handler[] snapshot;
        lock (Gate)
        {
            if (Handlers.Count == 0)
            {
                return;
            }

            snapshot = new Handler[Handlers.Count];
            Handlers.Values.CopyTo(snapshot, 0);
        }

        var context = BuildContext(hookContext);
        if (!context.IsCardAction)
        {
            return;
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i].Invoke(context);
        }
    }

    private static AuraCombatActionContext BuildContext(ModHookContext hookContext)
    {
        var executor = hookContext.Arguments != null && hookContext.Arguments.Length > 0
            ? hookContext.Arguments[0] as IScriptExecutor
            : null;
        var dataConfig = executor?.dataConfig;
        if (dataConfig == null || dataConfig.Type != DataType.Card || dataConfig.data == null)
        {
            return AuraCombatActionContext.Empty;
        }

        var cardId = ReadData(dataConfig, "Id");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            cardId = dataConfig.InstanceID ?? "";
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            return AuraCombatActionContext.Empty;
        }

        var owner = executor?.Self as StatusManager;
        var ownerInstanceId = owner?.InstanceId ?? "";
        var currentRoleId = ReadCurrentCareerId();
        var ownerRoleId = ReadStatusRoleId(owner, currentRoleId);
        var sequence = ++actionSequence;

        return new AuraCombatActionContext
        {
            IsCardAction = true,
            HookContext = hookContext,
            ScriptExecutor = executor,
            DataConfig = dataConfig,
            OwnerStatus = owner,
            ActionSequence = sequence,
            EventToken = BuildEventToken(ownerInstanceId, cardId, sequence),
            Action = ReadData(dataConfig, "Action"),
            Effects = ReadData(dataConfig, "Effects"),
            CardId = cardId,
            OwnerInstanceId = ownerInstanceId,
            OwnerRoleId = ownerRoleId,
            CurrentRoleId = currentRoleId,
            CreatedAt = Time.unscaledTime
        };
    }

    private static string BuildEventToken(string ownerInstanceId, string cardId, long sequence)
    {
        return (string.IsNullOrWhiteSpace(ownerInstanceId) ? "local" : ownerInstanceId.Trim())
               + ":"
               + (string.IsNullOrWhiteSpace(cardId) ? "*" : cardId.Trim())
               + ":"
               + sequence.ToString();
    }

    private static string ReadData(IDataConfig? dataConfig, string key)
    {
        try
        {
            return dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadCurrentCareerId()
    {
        return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
    }

    private static string ReadDataId(IDataConfig? data)
    {
        try
        {
            if (data?.data != null && data.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static string ReadStatusRoleId(StatusManager? status, string currentRoleId)
    {
        var fatherId = "";
        try
        {
            fatherId = AuraSharedReflection.ReadString(status?.fatherObject, "Id", "id");
        }
        catch
        {
        }

        return AuraSharedIdentity.SelectRoleId(fatherId, currentRoleId);
    }

    private sealed class Handler
    {
        private readonly Action<AuraCombatActionContext> action;
        private readonly Action<string>? warn;

        public Handler(string id, Action<AuraCombatActionContext> action, Action<string>? warn)
        {
            Id = id;
            this.action = action;
            this.warn = warn;
        }

        public string Id { get; }

        public void Invoke(AuraCombatActionContext context)
        {
            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                warn?.Invoke("[AuraCombatActionRouter] handler failed: " + Id + ", error=" + ex.Message);
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
}

public sealed class AuraCombatActionContext
{
    public static readonly AuraCombatActionContext Empty = new();

    public bool IsCardAction { get; set; }

    public ModHookContext? HookContext { get; set; }

    public IScriptExecutor? ScriptExecutor { get; set; }

    public IDataConfig? DataConfig { get; set; }

    public StatusManager? OwnerStatus { get; set; }

    public long ActionSequence { get; set; }

    public string EventToken { get; set; } = "";

    public string Action { get; set; } = "";

    public string Effects { get; set; } = "";

    public string CardId { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public string OwnerRoleId { get; set; } = "";

    public string CurrentRoleId { get; set; } = "";

    public float CreatedAt { get; set; }
}
