using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraShared.Core;

[Flags]
public enum AuraCardActionPhase
{
    None = 0,
    Attempting = 1 << 0,
    NativeStarted = 1 << 1,
    Committed = 1 << 2,
    PresentationCommitted = 1 << 3,
    Completed = 1 << 4,
    Aborted = 1 << 5,
    All = Attempting | NativeStarted | Committed | PresentationCommitted | Completed | Aborted
}

public sealed class AuraCardActionSubscription
{
    public AuraCardActionPhase Phases { get; set; } = AuraCardActionPhase.All;
    public int Priority { get; set; }
    public Action<AuraCardActionContext>? Handler { get; set; }
}

public sealed class AuraCardActionContext
{
    public long BattleSessionId { get; internal set; }
    public long Sequence { get; internal set; }
    public string TransactionId { get; internal set; } = "";
    public AuraCardActionPhase Phase { get; internal set; }
    public string OwnerStatusId { get; internal set; } = "";
    public StatusManager? OwnerStatus { get; internal set; }
    public string OwnerRoleId { get; internal set; } = "";
    public string CardDataId { get; internal set; } = "";
    public string CardInstanceId { get; internal set; } = "";
    public IDataConfig? Config { get; internal set; }
    public CardItem? Card { get; internal set; }
    public ActionData? NativePayload { get; internal set; }
    public int StartCost { get; internal set; }
    public int CreatedFrame { get; internal set; }
    public float CreatedAt { get; internal set; }
    public string Action { get; internal set; } = "";
    public string Effects { get; internal set; } = "";
    public string AbortReason { get; internal set; } = "";
}

public static class AuraCardActionTransactionRouter
{
    private const string RuntimeOwnerId = "AuraCardActionTransaction";
    private static readonly object Gate = new();
    private static readonly object EventOwner = new();
    private static readonly Dictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Transaction>> TransactionsByOwner = new(StringComparer.Ordinal);
    private static PhaseSnapshot phaseSnapshot = PhaseSnapshot.Empty;
    private static IDisposable? cardLifecycleRegistration;
    private static IDisposable? battleLifecycleRegistration;
    private static IDisposable? presentationHookRegistration;
    private static string registeredStatusId = "";
    private static bool initialized;
    private static bool watchdogScheduled;
    private static long watchdogSessionId;
    private static long nextSequence;

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        AuraCardActionSubscription subscription,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (modConfig == null || subscription?.Handler == null)
        {
            return EmptyDisposable.Instance;
        }

        EnsureInitialized(modConfig, info, warn);
        var id = Normalize(ownerModId, "AuraShared") + ":" + Normalize(handlerId, Guid.NewGuid().ToString("N"));
        Handler handler;
        lock (Gate)
        {
            handler = new Handler(id, subscription.Phases, subscription.Priority, subscription.Handler, warn);
            Handlers[id] = handler;
            RebuildPhaseSnapshotNoLock();
        }

        EnsureEventLane("register:" + id);
        return new Subscription(id, handler);
    }

    public static void Clear(string source)
    {
        List<Transaction> pending;
        lock (Gate)
        {
            pending = TransactionsByOwner.Values.SelectMany(value => value).ToList();
            TransactionsByOwner.Clear();
            registeredStatusId = "";
        }

        try
        {
            EventCenter.Instance.Clear(EventOwner);
        }
        catch
        {
        }

        foreach (var transaction in pending)
        {
            if (!transaction.Completed)
            {
                Publish(transaction, AuraCardActionPhase.Aborted, "battle-boundary:" + source, null);
            }
        }
    }

    private static void EnsureInitialized(ModConfig modConfig, Action<string>? info, Action<string>? warn)
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            cardLifecycleRegistration = AuraCardLifecycleRouter.Register(
                modConfig,
                RuntimeOwnerId,
                "TransactionScope",
                new AuraCardLifecycleSubscription
                {
                    Priority = int.MaxValue,
                    BeforeCommonCardUse = BeginAttempt,
                    BeforeAttackCardUse = BeginAttempt,
                    AfterCommonCardUse = EndAttempt,
                    AfterAttackCardUse = EndAttempt
                },
                info,
                warn);
            battleLifecycleRegistration = AuraBattleLifecycleRouter.Register(
                modConfig,
                RuntimeOwnerId,
                "BattleScope",
                new AuraBattleLifecycleSubscription
                {
                    BattleMaterialized = _ => ResetAndRegister("BattleMaterialized"),
                    BattleOpening = _ => EnsureEventLane("BattleOpening"),
                    BattleRestarting = _ => Clear("BattleRestarting"),
                    BattleSettling = _ => Clear("BattleSettling"),
                    BattleEnded = _ => Clear("BattleEnded")
                },
                info,
                warn);
            presentationHookRegistration = AuraSharedHooks.RegisterBeforeRouted(
                modConfig,
                "FightUI.CallActionAnimation",
                OnPresentationCommitted,
                info,
                warn,
                safeInvoke: true);
        }
    }

    private static void ResetAndRegister(string source)
    {
        Clear(source);
        EnsureEventLane(source);
    }

    private static void EnsureEventLane(string source)
    {
        var statusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        if (statusId.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            if (string.Equals(registeredStatusId, statusId, StringComparison.Ordinal))
            {
                return;
            }
        }

        try
        {
            EventCenter.Instance.Clear(EventOwner);
            EventCenter.Instance.AddEventListener(
                "Action" + statusId,
                new Action<ActionData>(OnNativeStarted),
                EventOwner,
                EventDispose.OnFightEnd);
            EventCenter.Instance.AddEventListener(
                "ActionAfter" + statusId,
                new Action<ActionData>(OnCommitted),
                EventOwner,
                EventDispose.OnFightEnd);
            lock (Gate)
            {
                registeredStatusId = statusId;
            }
        }
        catch (Exception ex)
        {
            AuraSharedLog.Warn(RuntimeOwnerId, "Action event lane registration failed from " + source + ": " + ex.Message);
        }
    }

    private static void BeginAttempt(ModHookContext context)
    {
        EnsureEventLane("BeginAttempt");
        if (context.Target is not CardItem card || card.dataConfig == null)
        {
            return;
        }

        var config = (IDataConfig)card.dataConfig;
        var owner = config.scriptExecutor?.Self ?? FightPlayer.Instance?.Status;
        var ownerStatusId = owner?.InstanceId ?? "";
        var sessionId = AuraLifecycleSessionRuntime.EnsureBattleSession();
        var sequence = Interlocked.Increment(ref nextSequence);
        var transaction = new Transaction
        {
            BattleSessionId = sessionId,
            Sequence = sequence,
            TransactionId = sessionId + ":" + (ownerStatusId.Length == 0 ? "local" : ownerStatusId) + ":" + sequence,
            OwnerStatusId = ownerStatusId,
            OwnerStatus = owner as StatusManager,
            OwnerRoleId = ReadOwnerRoleId(owner),
            CardDataId = ReadCardId(config),
            CardInstanceId = config.InstanceID ?? "",
            Config = config,
            Card = card,
            StartCost = ReadCost(config),
            CreatedFrame = SafeFrameCount(),
            CreatedAt = Time.unscaledTime,
            Action = ReadData(config, "Action"),
            Effects = ReadData(config, "Effects")
        };

        lock (Gate)
        {
            var key = OwnerKey(ownerStatusId);
            if (!TransactionsByOwner.TryGetValue(key, out var stack))
            {
                stack = new List<Transaction>();
                TransactionsByOwner[key] = stack;
            }

            stack.Add(transaction);
        }

        Publish(transaction, AuraCardActionPhase.Attempting, "", null);
        ScheduleWatchdog();
    }

    private static void ScheduleWatchdog()
    {
        var sessionId = AuraLifecycleSessionRuntime.CurrentBattleSessionId;
        lock (Gate)
        {
            if (watchdogScheduled && watchdogSessionId == sessionId)
            {
                return;
            }
            watchdogScheduled = true;
            watchdogSessionId = sessionId;
        }

        AuraSharedFrameScheduler.RunOnceAfterFrames(new AuraSharedFrameActionRequest
        {
            OwnerId = RuntimeOwnerId,
            Key = "AbortWatchdog." + sessionId,
            Source = RuntimeOwnerId + ".AbortWatchdog",
            DelayFrames = 1,
            Phase = AuraSharedFramePhase.Reconcile,
            Priority = 1000,
            EstimatedCost = 1,
            Action = () => AbortStaleTransactions(sessionId)
        });
    }

    private static void EndAttempt(ModHookContext context)
    {
        if (context.Target is not CardItem card || card.dataConfig == null)
        {
            return;
        }

        var transaction = TakeLatest(card.dataConfig, remove: true);
        if (transaction == null)
        {
            return;
        }

        if (transaction.Committed)
        {
            transaction.Completed = true;
            Publish(transaction, AuraCardActionPhase.Completed, "", null);
        }
        else
        {
            transaction.Completed = true;
            Publish(transaction, AuraCardActionPhase.Aborted, "native-action-not-observed", null);
        }
    }

    private static void OnNativeStarted(ActionData payload)
    {
        var transaction = FindOrCreateForPayload(payload);
        if (transaction == null || transaction.NativeStarted)
        {
            return;
        }

        transaction.NativeStarted = true;
        transaction.NativePayload = payload;
        if (payload.data != null)
        {
            transaction.StartCost = ReadCost(payload.data);
        }
        Publish(transaction, AuraCardActionPhase.NativeStarted, "", payload);
    }

    private static void OnCommitted(ActionData payload)
    {
        var transaction = FindLatest(payload.data, payload.dataId);
        if (transaction == null)
        {
            AuraSharedLog.Warn(RuntimeOwnerId, "ActionAfter payload had no active transaction: card=" + payload.dataId);
            return;
        }

        if (transaction.Committed)
        {
            return;
        }

        transaction.NativePayload = payload;
        transaction.Committed = true;
        Publish(transaction, AuraCardActionPhase.Committed, "", payload);
    }

    private static void OnPresentationCommitted(ModHookContext context)
    {
        var executor = context.Arguments != null && context.Arguments.Length > 0
            ? context.Arguments[0] as IScriptExecutor
            : null;
        var config = executor?.dataConfig;
        var transaction = FindLatest(config, config?.InstanceID ?? "");
        if (transaction == null || !transaction.Committed || transaction.PresentationCommitted)
        {
            return;
        }

        transaction.PresentationCommitted = true;
        Publish(transaction, AuraCardActionPhase.PresentationCommitted, "", transaction.NativePayload);
    }

    private static Transaction? FindOrCreateForPayload(ActionData payload)
    {
        var existing = FindLatest(payload.data, payload.dataId);
        if (existing != null)
        {
            return existing;
        }

        var config = payload.data;
        if (config == null)
        {
            return null;
        }

        var ownerStatusId = FightPlayer.Instance?.Status?.InstanceId ?? "";
        var sessionId = AuraLifecycleSessionRuntime.EnsureBattleSession();
        var sequence = Interlocked.Increment(ref nextSequence);
        var transaction = new Transaction
        {
            BattleSessionId = sessionId,
            Sequence = sequence,
            TransactionId = sessionId + ":" + OwnerKey(ownerStatusId) + ":" + sequence,
            OwnerStatusId = ownerStatusId,
            OwnerStatus = FightPlayer.Instance?.Status as StatusManager,
            OwnerRoleId = ReadOwnerRoleId(FightPlayer.Instance?.Status),
            CardDataId = ReadCardId(config),
            CardInstanceId = config.InstanceID ?? "",
            Config = config,
            StartCost = ReadCost(config),
            CreatedFrame = SafeFrameCount(),
            CreatedAt = Time.unscaledTime,
            Action = ReadData(config, "Action"),
            Effects = ReadData(config, "Effects")
        };
        lock (Gate)
        {
            var key = OwnerKey(ownerStatusId);
            if (!TransactionsByOwner.TryGetValue(key, out var stack))
            {
                stack = new List<Transaction>();
                TransactionsByOwner[key] = stack;
            }

            stack.Add(transaction);
        }

        Publish(transaction, AuraCardActionPhase.Attempting, "native-start-without-true-use", null);
        return transaction;
    }

    private static Transaction? FindLatest(IDataConfig? config, string dataId)
    {
        lock (Gate)
        {
            foreach (var stack in TransactionsByOwner.Values)
            {
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    if (!stack[i].Completed && Matches(stack[i], config, dataId))
                    {
                        return stack[i];
                    }
                }
            }
        }

        return null;
    }

    private static Transaction? TakeLatest(IDataConfig config, bool remove)
    {
        lock (Gate)
        {
            string? emptyKey = null;
            Transaction? found = null;
            foreach (var pair in TransactionsByOwner)
            {
                var stack = pair.Value;
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    if (!Matches(stack[i], config, config.InstanceID ?? ""))
                    {
                        continue;
                    }

                    var transaction = stack[i];
                    if (remove)
                    {
                        stack.RemoveAt(i);
                        if (stack.Count == 0)
                        {
                            emptyKey = pair.Key;
                        }
                    }

                    found = transaction;
                    break;
                }

                if (found != null) break;
            }

            if (emptyKey != null) TransactionsByOwner.Remove(emptyKey);
            return found;
        }
    }

    private static void AbortStaleTransactions(long sessionId)
    {
        var stale = new List<Transaction>();
        lock (Gate)
        {
            if (watchdogSessionId == sessionId)
            {
                watchdogScheduled = false;
                watchdogSessionId = 0;
            }

            foreach (var pair in TransactionsByOwner.ToArray())
            {
                for (var i = pair.Value.Count - 1; i >= 0; i--)
                {
                    var transaction = pair.Value[i];
                    if (transaction.Completed || transaction.BattleSessionId != sessionId)
                    {
                        continue;
                    }

                    stale.Add(transaction);
                    pair.Value.RemoveAt(i);
                }

                if (pair.Value.Count == 0)
                {
                    TransactionsByOwner.Remove(pair.Key);
                }
            }
        }

        for (var i = 0; i < stale.Count; i++)
        {
            var transaction = stale[i];
            if (!transaction.Completed)
            {
                transaction.Completed = true;
                Publish(transaction, AuraCardActionPhase.Aborted, "after-hook-missing", transaction.NativePayload);
            }
        }
    }

    private static bool Matches(Transaction transaction, IDataConfig? config, string dataId)
    {
        if (config != null && ReferenceEquals(transaction.Config, config))
        {
            return true;
        }

        var instanceId = config?.InstanceID ?? "";
        if (instanceId.Length > 0 && string.Equals(transaction.CardInstanceId, instanceId, StringComparison.Ordinal))
        {
            return true;
        }

        var candidateId = config == null ? dataId : ReadCardId(config);
        return candidateId.Length > 0 && string.Equals(transaction.CardDataId, candidateId, StringComparison.Ordinal);
    }

    private static void Publish(
        Transaction transaction,
        AuraCardActionPhase phase,
        string abortReason,
        ActionData? payload)
    {
        var snapshot = phaseSnapshot;
        if (!snapshot.Handlers.TryGetValue(phase, out var handlers) || handlers.Length == 0)
        {
            return;
        }

        var context = new AuraCardActionContext
        {
            BattleSessionId = transaction.BattleSessionId,
            Sequence = transaction.Sequence,
            TransactionId = transaction.TransactionId,
            Phase = phase,
            OwnerStatusId = transaction.OwnerStatusId,
            OwnerStatus = transaction.OwnerStatus,
            OwnerRoleId = transaction.OwnerRoleId,
            CardDataId = transaction.CardDataId,
            CardInstanceId = transaction.CardInstanceId,
            Config = transaction.Config,
            Card = transaction.Card,
            NativePayload = payload,
            StartCost = transaction.StartCost,
            CreatedFrame = transaction.CreatedFrame,
            CreatedAt = transaction.CreatedAt,
            Action = transaction.Action,
            Effects = transaction.Effects,
            AbortReason = abortReason ?? ""
        };

        for (var i = 0; i < handlers.Length; i++)
        {
            handlers[i].Invoke(context);
        }
    }

    private static void RebuildPhaseSnapshotNoLock()
    {
        var result = new Dictionary<AuraCardActionPhase, Handler[]>();
        foreach (var phase in new[]
                 {
                     AuraCardActionPhase.Attempting,
                     AuraCardActionPhase.NativeStarted,
                     AuraCardActionPhase.Committed,
                     AuraCardActionPhase.PresentationCommitted,
                     AuraCardActionPhase.Completed,
                     AuraCardActionPhase.Aborted
                 })
        {
            result[phase] = Handlers.Values
                .Where(handler => (handler.Phases & phase) != 0)
                .OrderByDescending(handler => handler.Priority)
                .ThenBy(handler => handler.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        phaseSnapshot = new PhaseSnapshot(result);
    }

    private static string ReadOwnerRoleId(IStatusManager? owner)
    {
        var fatherId = AuraSharedReflection.ReadString(owner?.fatherObject, "Id", "id");
        var careerId = ReadData(RoleTable.Instance?.Career ?? GameEntryUI.career, "Id");
        return AuraSharedIdentity.SelectRoleId(fatherId, careerId);
    }

    private static string ReadCardId(IDataConfig config)
    {
        var runtimeId = ReadVars(config, "Id");
        return runtimeId.Length > 0 ? runtimeId : ReadData(config, "Id");
    }

    private static string ReadData(IDataConfig? config, string key)
    {
        try
        {
            return config?.data != null && config.data.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadVars(IDataConfig config, string key)
    {
        try
        {
            return config.Vars != null && config.Vars.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static int ReadCost(IDataConfig config)
    {
        return Math.Max(0,
            Parse(ReadData(config, "Expend"))
            + Parse(ReadVars(config, "TotalExCost"))
            + Parse(ReadVars(config, "ExCost"))
            + Parse(ReadVars(config, "OnceExCost")));
    }

    private static int Parse(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static int SafeFrameCount()
    {
        try
        {
            return Time.frameCount;
        }
        catch
        {
            return -1;
        }
    }

    private static string OwnerKey(string ownerStatusId)
    {
        return string.IsNullOrWhiteSpace(ownerStatusId) ? "local" : ownerStatusId.Trim();
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed class Transaction
    {
        public long BattleSessionId;
        public long Sequence;
        public string TransactionId = "";
        public string OwnerStatusId = "";
        public StatusManager? OwnerStatus;
        public string OwnerRoleId = "";
        public string CardDataId = "";
        public string CardInstanceId = "";
        public IDataConfig? Config;
        public CardItem? Card;
        public ActionData? NativePayload;
        public int StartCost;
        public int CreatedFrame;
        public float CreatedAt;
        public string Action = "";
        public string Effects = "";
        public bool NativeStarted;
        public bool Committed;
        public bool PresentationCommitted;
        public bool Completed;
    }

    private sealed class Handler
    {
        private readonly Action<AuraCardActionContext> action;
        private readonly Action<string>? warn;

        public Handler(
            string id,
            AuraCardActionPhase phases,
            int priority,
            Action<AuraCardActionContext> action,
            Action<string>? warn)
        {
            Id = id;
            Phases = phases;
            Priority = priority;
            this.action = action;
            this.warn = warn;
        }

        public string Id { get; }
        public AuraCardActionPhase Phases { get; }
        public int Priority { get; }

        public void Invoke(AuraCardActionContext context)
        {
            try
            {
                action(context);
            }
            catch (Exception ex)
            {
                var message = "[AuraCardAction] handler failed: " + Id + ", phase=" + context.Phase + " -> " + ex.Message;
                if (warn != null)
                {
                    warn(message);
                }
                else
                {
                    AuraSharedLog.Warn(RuntimeOwnerId, message);
                }
            }
        }
    }

    private sealed class PhaseSnapshot
    {
        public static readonly PhaseSnapshot Empty = new(new Dictionary<AuraCardActionPhase, Handler[]>());

        public PhaseSnapshot(Dictionary<AuraCardActionPhase, Handler[]> handlers)
        {
            Handlers = handlers;
        }

        public Dictionary<AuraCardActionPhase, Handler[]> Handlers { get; }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly string id;
        private readonly Handler handler;
        private bool disposed;

        public Subscription(string id, Handler handler)
        {
            this.id = id;
            this.handler = handler;
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
                if (Handlers.TryGetValue(id, out var current) && ReferenceEquals(current, handler))
                {
                    Handlers.Remove(id);
                    RebuildPhaseSnapshotNoLock();
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
