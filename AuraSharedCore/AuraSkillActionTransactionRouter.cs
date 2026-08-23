using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraShared.Core;

[Flags]
public enum AuraSkillActionPhase
{
    None = 0,
    Attempting = 1 << 0,
    NativeStarted = 1 << 1,
    Committed = 1 << 2,
    Completed = 1 << 3,
    Aborted = 1 << 4,
    All = Attempting | NativeStarted | Committed | Completed | Aborted
}

public sealed class AuraSkillActionContext
{
    public long BattleSessionId { get; internal set; }
    public long Sequence { get; internal set; }
    public string TransactionId { get; internal set; } = "";
    public AuraSkillActionPhase Phase { get; internal set; }
    public string SkillDataId { get; internal set; } = "";
    public string SkillInstanceId { get; internal set; } = "";
    public IDataConfig? Config { get; internal set; }
    public SkillItem? Skill { get; internal set; }
    public IStatusManager? OwnerStatus { get; internal set; }
    public string OwnerStatusId { get; internal set; } = "";
    public string OwnerRoleId { get; internal set; } = "";
    public ModHookContext NativeContext { get; internal set; } = new();
    public string AbortReason { get; internal set; } = "";
}

public sealed class AuraSkillActionSubscription
{
    public AuraSkillActionPhase Phases { get; set; } = AuraSkillActionPhase.All;
    public int Priority { get; set; }
    public Action<AuraSkillActionContext>? Handler { get; set; }
}

public static class AuraSkillActionTransactionRouter
{
    private const string RuntimeOwnerId = "AuraSkillActionTransaction";
    private const string SkillTrueUse = "SkillItem.TrueUse";
    private const string SkillRunScript = "SkillItem.RunScript";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static PhaseSnapshot phaseSnapshot = PhaseSnapshot.Empty;
    [ThreadStatic] private static Stack<Transaction>? transactions;
    private static bool initialized;
    private static long nextSequence;

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraSkillActionSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (modConfig == null || subscription?.Handler == null) return EmptyDisposable.Instance;
        EnsureInitialized(modConfig, info, warn);
        var id = Normalize(ownerModId, "AuraShared") + ":" + Normalize(handlerId, Guid.NewGuid().ToString("N"));
        Handler handler;
        lock (Gate)
        {
            handler = new Handler(id, subscription, warn);
            Handlers[id] = handler;
            RebuildSnapshotNoLock();
        }
        return new Subscription(id, handler);
    }

    private static void EnsureInitialized(ModConfig modConfig, Action<string>? info, Action<string>? warn)
    {
        lock (Gate)
        {
            if (initialized) return;
            initialized = true;
            var registry = new AuraHookRegistry(modConfig, RuntimeOwnerId, info, warn);
            registry.BeforeRouted(SkillTrueUse, BeginAttempt, "Attempting");
            registry.BeforeRouted(SkillRunScript, ObserveNativeUseScript, "NativeStarted");
            registry.AfterRouted(SkillTrueUse, EndAttempt, "Completed");
            AuraBattleLifecycleRouter.Register(
                modConfig,
                RuntimeOwnerId,
                "BattleScope",
                new AuraBattleLifecycleSubscription
                {
                    BattleInitializing = _ => AbortAll("BattleInitializing"),
                    BattleRestarting = _ => AbortAll("BattleRestarting"),
                    BattleSettling = _ => AbortAll("BattleSettling")
                },
                info,
                warn);
        }
    }

    private static void BeginAttempt(ModHookContext context)
    {
        if (context.Target is not SkillItem skill || skill.dataConfig == null) return;
        AbortAll("superseded-attempt");
        var sessionId = AuraLifecycleSessionRuntime.EnsureBattleSession();
        var sequence = Interlocked.Increment(ref nextSequence);
        var config = skill.dataConfig;
        var transaction = new Transaction
        {
            BattleSessionId = sessionId,
            Sequence = sequence,
            TransactionId = sessionId + ":skill:" + sequence,
            Skill = skill,
            Config = config,
            SkillDataId = ReadId(config),
            SkillInstanceId = config.InstanceID ?? "",
            OwnerStatus = config.scriptExecutor?.Self,
            AttemptContext = context
        };
        transactions ??= new Stack<Transaction>();
        transactions.Push(transaction);
        Publish(transaction, AuraSkillActionPhase.Attempting, context, "");
    }

    private static void ObserveNativeUseScript(ModHookContext context)
    {
        if (context.Target is not SkillItem skill
            || context.Arguments == null
            || context.Arguments.Length == 0
            || !string.Equals(Convert.ToString(context.Arguments[0]), "UseScript", StringComparison.Ordinal)
            || transactions == null
            || transactions.Count == 0)
        {
            return;
        }

        var transaction = transactions.Peek();
        if (!ReferenceEquals(transaction.Skill, skill) || transaction.NativeStarted) return;
        transaction.NativeStarted = true;
        Publish(transaction, AuraSkillActionPhase.NativeStarted, context, "");
    }

    private static void EndAttempt(ModHookContext context)
    {
        if (context.Target is not SkillItem skill || transactions == null || transactions.Count == 0) return;
        var transaction = transactions.Peek();
        if (!ReferenceEquals(transaction.Skill, skill)) return;
        transactions.Pop();
        if (transaction.NativeStarted)
        {
            Publish(transaction, AuraSkillActionPhase.Committed, context, "");
            Publish(transaction, AuraSkillActionPhase.Completed, context, "");
        }
        else
        {
            Publish(transaction, AuraSkillActionPhase.Aborted, context, "native-use-script-not-observed");
        }
    }

    private static void AbortAll(string reason)
    {
        if (transactions == null) return;
        while (transactions.Count > 0)
        {
            var transaction = transactions.Pop();
            Publish(transaction, AuraSkillActionPhase.Aborted, transaction.AttemptContext, reason);
        }
    }

    private static void Publish(Transaction transaction, AuraSkillActionPhase phase, ModHookContext nativeContext, string abortReason)
    {
        var handlers = phaseSnapshot.Get(phase);
        if (handlers.Length == 0) return;
        var context = new AuraSkillActionContext
        {
            BattleSessionId = transaction.BattleSessionId,
            Sequence = transaction.Sequence,
            TransactionId = transaction.TransactionId,
            Phase = phase,
            SkillDataId = transaction.SkillDataId,
            SkillInstanceId = transaction.SkillInstanceId,
            Config = transaction.Config,
            Skill = transaction.Skill,
            OwnerStatus = transaction.OwnerStatus,
            OwnerStatusId = transaction.OwnerStatus?.InstanceId ?? "",
            OwnerRoleId = ReadOwnerRoleId(transaction.OwnerStatus),
            NativeContext = nativeContext,
            AbortReason = abortReason ?? ""
        };
        for (var i = 0; i < handlers.Length; i++) handlers[i].Invoke(context);
    }

    private static void RebuildSnapshotNoLock()
    {
        var phases = new Dictionary<AuraSkillActionPhase, Handler[]>();
        foreach (var phase in new[]
                 {
                     AuraSkillActionPhase.Attempting,
                     AuraSkillActionPhase.NativeStarted,
                     AuraSkillActionPhase.Committed,
                     AuraSkillActionPhase.Completed,
                     AuraSkillActionPhase.Aborted
                 })
        {
            phases[phase] = Handlers.Values
                .Where(handler => (handler.Subscription.Phases & phase) != 0)
                .OrderByDescending(handler => handler.Subscription.Priority)
                .ThenBy(handler => handler.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        phaseSnapshot = new PhaseSnapshot(phases);
    }

    private static string ReadId(IDataConfig config)
    {
        return config.Vars != null && config.Vars.TryGetValue("Id", out var runtimeId) && !string.IsNullOrWhiteSpace(runtimeId)
            ? runtimeId
            : config.data != null && config.data.TryGetValue("Id", out var id) ? id ?? "" : "";
    }

    private static string ReadOwnerRoleId(IStatusManager? owner)
    {
        var fatherId = AuraSharedReflection.ReadString(owner?.fatherObject, "Id", "id");
        var careerId = ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
        return AuraSharedIdentity.SelectRoleId(fatherId, careerId);
    }

    private static string ReadDataId(IDataConfig? config)
    {
        try
        {
            return config?.data != null && config.data.TryGetValue("Id", out var id) ? id ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string Normalize(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class Transaction
    {
        public long BattleSessionId;
        public long Sequence;
        public string TransactionId = "";
        public SkillItem? Skill;
        public IDataConfig? Config;
        public string SkillDataId = "";
        public string SkillInstanceId = "";
        public IStatusManager? OwnerStatus;
        public ModHookContext AttemptContext = new();
        public bool NativeStarted;
    }

    private sealed class Handler
    {
        private readonly Action<AuraSkillActionContext> action;
        private readonly Action<string>? warn;
        public Handler(string id, AuraSkillActionSubscription subscription, Action<string>? warn)
        {
            Id = id;
            Subscription = subscription;
            action = subscription.Handler!;
            this.warn = warn;
        }
        public string Id { get; }
        public AuraSkillActionSubscription Subscription { get; }
        public void Invoke(AuraSkillActionContext context)
        {
            try { action(context); }
            catch (Exception ex) { warn?.Invoke("[AuraSkillAction] handler failed: " + Id + " @ " + context.Phase + " -> " + ex.Message); }
        }
    }

    private sealed class PhaseSnapshot
    {
        public static readonly PhaseSnapshot Empty = new(new Dictionary<AuraSkillActionPhase, Handler[]>());
        private readonly Dictionary<AuraSkillActionPhase, Handler[]> phases;
        public PhaseSnapshot(Dictionary<AuraSkillActionPhase, Handler[]> phases) => this.phases = phases;
        public Handler[] Get(AuraSkillActionPhase phase) => phases.TryGetValue(phase, out var handlers) ? handlers : Array.Empty<Handler>();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private readonly Handler handler;
        private bool disposed;
        public Subscription(string id, Handler handler) { this.id = id; this.handler = handler; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (Gate)
            {
                if (Handlers.TryGetValue(id, out var current) && ReferenceEquals(current, handler))
                {
                    Handlers.Remove(id);
                    RebuildSnapshotNoLock();
                }
            }
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
