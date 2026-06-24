using System;
using System.Collections.Generic;
using System.Globalization;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.DamageMeter;

public static class AuraToolsDamageMeterRuntime
{
    private static readonly AuraToolsDamageMeterState State = new();
    private static readonly AuraToolsDamageAttribution Attribution = new();
    private static readonly Stack<DamageSnapshot?> HitSnapshots = new();
    private static readonly Stack<PureChangeSnapshot?> PureChangeSnapshots = new();
    private static readonly Stack<bool> PureChangeContextPushes = new();
    private static readonly Stack<bool> GenericScriptContextPushes = new();
    private static readonly Stack<bool> ScriptAddBuffContextPushes = new();
    private static readonly Stack<bool> BuffProcessContextPushes = new();
    private static readonly Stack<PendingBuffApplication?> ScriptBuffApplications = new();
    private static readonly Stack<PendingBuffApplication?> StatusBuffApplications = new();
    private static KeyCode hotkey = KeyCode.F8;
    private static float nextRefreshAt;
    private static int lastRoundResetFrame = -1000;
    private static long lastRoundResetFight = -1;
    private static bool uiDirty = true;

    public static bool Visible { get; private set; }

    public static bool Enabled => AuraToolsConfigService.Root.MatchExperience.Enabled
                                  && AuraToolsConfigService.MatchExperience.DamageMeter.Enabled;

    public static void Initialize(ModConfig modConfig)
    {
        Configure();
        AuraToolsDamageMeterUi.EnsureDriver();
        RegisterBefore(modConfig, "StatusManager.Hit", BeforeHit);
        RegisterAfter(modConfig, "StatusManager.Hit", AfterHit);
        RegisterBefore(modConfig, "ScriptExecutor.PureChangeHp", BeforePureChangeHp);
        RegisterAfter(modConfig, "ScriptExecutor.PureChangeHp", AfterPureChangeHp);

        RegisterBefore(modConfig, "ScriptExecutor.Damage", BeforeScriptContext);
        RegisterAfter(modConfig, "ScriptExecutor.Damage", AfterScriptContext);
        RegisterBefore(modConfig, "ScriptExecutor.OnlineDamage", BeforeScriptContext);
        RegisterAfter(modConfig, "ScriptExecutor.OnlineDamage", AfterScriptContext);
        RegisterBefore(modConfig, "ScriptExecutor.ChangeHp", BeforeScriptContext);
        RegisterAfter(modConfig, "ScriptExecutor.ChangeHp", AfterScriptContext);
        RegisterBefore(modConfig, "ScriptExecutor.RunScript", BeforeScriptContext);
        RegisterAfter(modConfig, "ScriptExecutor.RunScript", AfterScriptContext);

        RegisterBefore(modConfig, "ScriptExecutor.AddBuff", BeforeScriptAddBuff);
        RegisterAfter(modConfig, "ScriptExecutor.AddBuff", AfterScriptAddBuff);
        RegisterBefore(modConfig, "StatusManager.AddBuff", BeforeStatusAddBuff);
        RegisterAfter(modConfig, "StatusManager.AddBuff", AfterStatusAddBuff);
        RegisterBefore(modConfig, "BuffItem.BuffProcess", BeforeBuffProcess);
        RegisterAfter(modConfig, "BuffItem.BuffProcess", AfterBuffProcess);

        RegisterBefore(modConfig, "FightInit.Init", OnFightInitStarting);
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStartFallback);
        RegisterAfter(modConfig, "Fight_PlayerTurn.Init", OnPlayerRoundStart);
        RegisterBefore(modConfig, "Fight_Win.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Escape.ResetStates", OnFightEnding);
        RegisterBefore(modConfig, "Fight_Loss.Init", OnFightEnding);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);

        AuraToolsConfigService.Changed += Configure;
        AuraToolsLog.Info("[DamageMeter] hooks registered.");
    }

    public static void Tick()
    {
        try
        {
            Configure();
            if (Input.GetKeyDown(hotkey))
            {
                SetVisible(!Visible);
            }

            if (!Enabled)
            {
                AuraToolsDamageMeterUi.SetVisible(false);
                return;
            }

            if (!uiDirty && Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + 0.18f;
            uiDirty = false;
            AuraToolsDamageMeterUi.Refresh(State, AuraToolsConfigService.MatchExperience.DamageMeter);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] tick failed: " + ex.Message);
        }
    }

    public static void SetVisible(bool visible)
    {
        Visible = visible;
        uiDirty = true;
        AuraToolsDamageMeterUi.SetVisible(visible && Enabled && State.InFight);
        if (!visible)
        {
            AuraToolsDamageMeterUi.CloseDetails();
        }
    }

    private static void Configure()
    {
        var settings = AuraToolsConfigService.MatchExperience.DamageMeter;
        if (Enum.TryParse(settings.Hotkey, true, out KeyCode parsed))
        {
            hotkey = parsed;
        }
        else
        {
            hotkey = KeyCode.F8;
        }
    }

    private static void OnFightInitStarting(ModHookContext context)
    {
        StartNewFight("fight init");
    }

    private static void OnFightStartFallback(ModHookContext context)
    {
        if (!State.InFight)
        {
            StartNewFight("fight start fallback");
        }
    }

    private static void StartNewFight(string source)
    {
        RunHook(source, () =>
        {
            State.ResetFight();
            Attribution.Clear();
            HitSnapshots.Clear();
            PureChangeSnapshots.Clear();
            PureChangeContextPushes.Clear();
            GenericScriptContextPushes.Clear();
            ScriptAddBuffContextPushes.Clear();
            BuffProcessContextPushes.Clear();
            ScriptBuffApplications.Clear();
            StatusBuffApplications.Clear();
            Visible = AuraToolsConfigService.MatchExperience.DamageMeter.ShowPanelByDefault;
            uiDirty = true;
            AuraToolsDamageMeterUi.CloseDetails();
        });
    }

    private static void OnFightEnding(ModHookContext context)
    {
        RunHook("fight ending", () =>
        {
            AuraToolsDamageMeterUi.CloseDetails();
            AuraToolsDamageMeterUi.SetVisible(false);
            uiDirty = true;
        });
    }

    private static void OnFightEnded(ModHookContext context)
    {
        RunHook("fight ended", () =>
        {
            State.EndFight();
            Attribution.Clear();
            HitSnapshots.Clear();
            PureChangeSnapshots.Clear();
            PureChangeContextPushes.Clear();
            GenericScriptContextPushes.Clear();
            ScriptAddBuffContextPushes.Clear();
            BuffProcessContextPushes.Clear();
            ScriptBuffApplications.Clear();
            StatusBuffApplications.Clear();
            uiDirty = true;
        });
    }

    private static void OnPlayerRoundStart(ModHookContext context)
    {
        RunHook("round start", () =>
        {
            if (!Enabled)
            {
                return;
            }

            if (lastRoundResetFight == State.FightIndex && Time.frameCount - lastRoundResetFrame <= 2)
            {
                return;
            }

            lastRoundResetFight = State.FightIndex;
            lastRoundResetFrame = Time.frameCount;
            State.ResetRound();
            uiDirty = true;
        });
    }

    private static void BeforeHit(ModHookContext context)
    {
        DamageSnapshot? snapshot = null;
        RunHook("before hit", () =>
        {
            if (!Enabled || context.Target is not IStatusManager target)
            {
                return;
            }

            var args = context.Arguments ?? Array.Empty<object>();
            snapshot = new DamageSnapshot
            {
                Target = target,
                BeforeHp = SafeHp(target),
                BeforeDefend = SafeDefend(target),
                DamageType = ArgumentString(args, 1, "Unknown"),
                FromDataId = ArgumentString(args, 2, ""),
                FromInstanceId = ArgumentString(args, 3, "")
            };
        });
        HitSnapshots.Push(snapshot);
    }

    private static void AfterHit(ModHookContext context)
    {
        RunHook("after hit", () =>
        {
            if (!Enabled || HitSnapshots.Count == 0)
            {
                return;
            }

            var snapshot = HitSnapshots.Pop();
            if (snapshot == null)
            {
                return;
            }

            RecordSnapshotDamage(snapshot);
        });
    }

    private static void BeforePureChangeHp(ModHookContext context)
    {
        var pushed = false;
        PureChangeSnapshot? snapshot = null;
        RunHook("before pure hp", () =>
        {
            if (!Enabled || context.Target is not IScriptExecutor executor)
            {
                return;
            }

            if (ParseInt(ArgumentString(context.Arguments, 0, "0")) >= 0)
            {
                return;
            }

            pushed = Attribution.PushScript(executor, "PureChangeHp");
            snapshot = new PureChangeSnapshot
            {
                Source = Attribution.Resolve(executor.status ?? executor.Target, "", AuraToolsDamageAttribution.SafeDataId(executor.dataConfig), "PureChangeHp"),
                Targets = CaptureTargets(executor, includeDefend: false)
            };
        });
        PureChangeSnapshots.Push(snapshot);
        PureChangeContextPushes.Push(pushed);
    }

    private static void AfterPureChangeHp(ModHookContext context)
    {
        RunHook("after pure hp", () =>
        {
            var snapshot = PureChangeSnapshots.Count > 0 ? PureChangeSnapshots.Pop() : null;
            if (snapshot != null)
            {
                foreach (var target in snapshot.Targets)
                {
                    var hpLoss = Math.Max(0, target.BeforeHp - SafeHp(target.Target));
                    if (hpLoss <= 0)
                    {
                        continue;
                    }

                    RecordDamage(target.Target, snapshot.Source, hpLoss, 0, "PureChangeHp");
                }
            }

            PopIfPushed(PureChangeContextPushes);
        });
    }

    private static void BeforeScriptContext(ModHookContext context)
    {
        var pushed = false;
        RunHook("before script context", () =>
        {
            if (!Enabled)
            {
                return;
            }

            pushed = Attribution.PushScript(context.Target as IScriptExecutor, TargetActionName(context));
        });
        GenericScriptContextPushes.Push(pushed);
    }

    private static void AfterScriptContext(ModHookContext context)
    {
        RunHook("after script context", () => PopIfPushed(GenericScriptContextPushes));
    }

    private static void BeforeScriptAddBuff(ModHookContext context)
    {
        var pushed = false;
        PendingBuffApplication? pending = null;
        RunHook("before script add buff", () =>
        {
            if (!Enabled)
            {
                return;
            }

            var executor = context.Target as IScriptExecutor;
            pushed = Attribution.PushScript(executor, "AddBuff");
            pending = Attribution.CaptureBuffApplication(executor, ArgumentString(context.Arguments, 0, ""));
        });
        ScriptAddBuffContextPushes.Push(pushed);
        ScriptBuffApplications.Push(pending);
    }

    private static void AfterScriptAddBuff(ModHookContext context)
    {
        RunHook("after script add buff", () =>
        {
            var pending = ScriptBuffApplications.Count > 0 ? ScriptBuffApplications.Pop() : null;
            if (pending != null && context.Target is IScriptExecutor executor)
            {
                foreach (var target in ResolveTargets(executor))
                {
                    Attribution.RememberBuffOwner(target, pending.BuffId, pending);
                }
            }

            PopIfPushed(ScriptAddBuffContextPushes);
        });
    }

    private static void BeforeStatusAddBuff(ModHookContext context)
    {
        PendingBuffApplication? pending = null;
        RunHook("before status add buff", () =>
        {
            if (!Enabled)
            {
                return;
            }

            var buffId = ResolveBuffIdFromArguments(context.Arguments);
            pending = ScriptBuffApplications.Count > 0 && ScriptBuffApplications.Peek()?.Owner != null
                ? new PendingBuffApplication
                {
                    BuffId = buffId,
                    Owner = ScriptBuffApplications.Peek()!.Owner
                }
                : Attribution.CaptureCurrentBuffApplication(buffId);
        });
        StatusBuffApplications.Push(pending);
    }

    private static void AfterStatusAddBuff(ModHookContext context)
    {
        RunHook("after status add buff", () =>
        {
            if (StatusBuffApplications.Count == 0)
            {
                return;
            }

            var pending = StatusBuffApplications.Pop();
            if (pending == null)
            {
                return;
            }

            Attribution.RememberBuffOwner(context.Target as IStatusManager, pending.BuffId, pending);
        });
    }

    private static void BeforeBuffProcess(ModHookContext context)
    {
        var pushed = false;
        RunHook("before buff process", () =>
        {
            if (!Enabled)
            {
                return;
            }

            var isActing = context.Arguments == null
                           || context.Arguments.Length == 0
                           || context.Arguments[0] is not bool value
                           || value;
            if (!isActing)
            {
                return;
            }

            pushed = Attribution.PushBuff(context.Target as BuffItem, "BuffProcess");
        });
        BuffProcessContextPushes.Push(pushed);
    }

    private static void AfterBuffProcess(ModHookContext context)
    {
        RunHook("after buff process", () => PopIfPushed(BuffProcessContextPushes));
    }

    private static void RecordSnapshotDamage(DamageSnapshot snapshot)
    {
        var target = snapshot.Target;
        if (target == null)
        {
            return;
        }

        var hpLoss = Math.Max(0, snapshot.BeforeHp - SafeHp(target));
        var shieldLoss = Math.Max(0, snapshot.BeforeDefend - SafeDefend(target));
        if (hpLoss <= 0 && shieldLoss <= 0)
        {
            return;
        }

        var source = Attribution.Resolve(target, snapshot.FromInstanceId, snapshot.FromDataId, snapshot.DamageType);
        RecordDamage(target, source, hpLoss, shieldLoss, snapshot.DamageType);
    }

    private static void RecordDamage(IStatusManager target, DamageSource source, int hpLoss, int shieldLoss, string damageType)
    {
        State.RememberStatus(target);
        if (source.Source != null)
        {
            State.RememberStatus(source.Source);
        }

        var changed = State.AddDamage(
            source.Source,
            target,
            source.SourceInstanceId,
            source.SourceDataId,
            damageType,
            source.DetailLabel,
            hpLoss,
            shieldLoss,
            AuraToolsConfigService.MatchExperience.DamageMeter.CountShieldLoss);
        if (changed)
        {
            uiDirty = true;
        }
    }

    private static List<TargetSnapshot> CaptureTargets(IScriptExecutor executor, bool includeDefend)
    {
        var result = new List<TargetSnapshot>();
        foreach (var status in ResolveTargets(executor))
        {
            result.Add(new TargetSnapshot
            {
                Target = status,
                BeforeHp = SafeHp(status),
                BeforeDefend = includeDefend ? SafeDefend(status) : 0
            });
        }

        return result;
    }

    private static IEnumerable<IStatusManager> ResolveTargets(IScriptExecutor executor)
    {
        if (executor.Object != null && executor.Object.Count > 0)
        {
            foreach (var item in executor.Object)
            {
                if (item != null)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (executor.status != null)
        {
            yield return executor.status;
            yield break;
        }

        if (executor.Target != null)
        {
            yield return executor.Target;
            yield break;
        }

        if (executor.Self != null)
        {
            yield return executor.Self;
        }
    }

    private static string ResolveBuffIdFromArguments(object[]? arguments)
    {
        if (arguments == null || arguments.Length == 0)
        {
            return "";
        }

        if (arguments[0] is string id)
        {
            return id;
        }

        if (arguments[0] is IBuffItemConfig config)
        {
            return AuraToolsDamageAttribution.SafeBuffId(config);
        }

        return "";
    }

    private static string TargetActionName(ModHookContext context)
    {
        var script = ArgumentString(context.Arguments, 0, "");
        return string.IsNullOrWhiteSpace(script) ? "Script" : script;
    }

    private static string ArgumentString(object[]? arguments, int index, string fallback)
    {
        if (arguments == null || index < 0 || index >= arguments.Length)
        {
            return fallback;
        }

        return arguments[index]?.ToString() ?? fallback;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int SafeHp(IStatusManager status)
    {
        try
        {
            return status.CurHp;
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeDefend(IStatusManager status)
    {
        try
        {
            return status.Defend;
        }
        catch
        {
            return 0;
        }
    }

    private static void RegisterBefore(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(modConfig, target, action, warn: AuraToolsLog.Warn);
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(modConfig, target, action, warn: AuraToolsLog.Warn);
    }

    private static void RunHook(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] " + name + " failed: " + ex.Message);
        }
    }

    private static void PopIfPushed(Stack<bool> pushStack)
    {
        if (pushStack.Count == 0)
        {
            return;
        }

        if (pushStack.Pop())
        {
            Attribution.Pop();
        }
    }

    private sealed class DamageSnapshot
    {
        public IStatusManager? Target { get; set; }

        public int BeforeHp { get; set; }

        public int BeforeDefend { get; set; }

        public string DamageType { get; set; } = "";

        public string FromDataId { get; set; } = "";

        public string FromInstanceId { get; set; } = "";
    }

    private sealed class TargetSnapshot
    {
        public IStatusManager Target { get; set; } = null!;

        public int BeforeHp { get; set; }

        public int BeforeDefend { get; set; }
    }

    private sealed class PureChangeSnapshot
    {
        public DamageSource Source { get; set; } = new(null, "", "", "PureChangeHp");

        public List<TargetSnapshot> Targets { get; set; } = new();
    }
}
