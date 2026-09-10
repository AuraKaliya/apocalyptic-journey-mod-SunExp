using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class OlimyaRoleApplication
{
    private static readonly OlimyaGoldenizationLedger Commands = new();
    private static long nextSequence;
    private static bool battleReady;
    private static readonly Dictionary<string, Tuple<string, bool>> Results = new(StringComparer.Ordinal);
    private static OlimyaGoldenizationCommand? pending;
    private static DateTime retryAt;
    private static int attempts;
    public static Func<OlimyaGoldenizationCommand, bool>? DispatchCommand { private get; set; }

    public static bool UseGoldenTouch(ScriptExecutor self)
    {
        if (self?.Self == null || !battleReady || !AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation
            || !OlimyaGameApi.IsLocalPlayer(self.Self)
            || !OlimyaDamageService.IsDreamweaver(self.Self)) return false;
        if (PlayerApi.GetSkillTime(OlimyaIds.GoldenTouch) > 0)
        {
            PlayerApi.ShowCaption("点金尚未冷却。");
            return false;
        }
        if (pending != null) { PlayerApi.ShowCaption("点金正在等待主机确认。"); return false; }
        var target = TargetApi.PrimaryTarget(self);
        if (!OlimyaGameApi.IsHostileEnemy(target)
            || !TargetApi.OpposingSideTargets(self, self.Self).Any(enemy => enemy.InstanceId == target!.InstanceId)) return false;
        var command = Command(OlimyaGoldenizationCommandKind.Apply, self.Self.InstanceId, target!.InstanceId);
        TargetApi.SetStatusForTarget(self, target, "Target");
        pending = command; attempts = 1; retryAt = DateTime.UtcNow.AddSeconds(1);
        if (DispatchCommand?.Invoke(command) != true) { pending = null; return false; }
        return true;
    }

    public static void BeginLocalTurn()
    {
        if (!battleReady || !OlimyaGameApi.IsFreshPlayerTurn()) return;
        var status = FightPlayer.Instance!.Status;
        if (OlimyaDamageService.IsDreamweaver(status))
        {
            OlimyaGameApi.ClearLocalShield();
            PlayerApi.SetSkillTime(OlimyaIds.GoldenTouch, Math.Max(0, PlayerApi.GetSkillTime(OlimyaIds.GoldenTouch) - 1));
        }
    }

    public static bool HandleAuthoritative(OlimyaGoldenizationCommand command, bool senderOwnsStatus)
    {
        if (!senderOwnsStatus || command == null || command.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || !battleReady || !CompanionAuthorityService.IsAuthoritative() || !AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation) return false;
        var key = command.OwnerStatusId;
        var fingerprint = command.Token + ":" + command.Version + ":" + command.BattleEpoch + ":" + command.Sequence + ":" + command.Kind + ":" + command.TargetStatusId;
        if (Results.TryGetValue(key, out var cached) && cached.Item1 == fingerprint) return cached.Item2;
        foreach (var retired in Results.Keys.Where(id => StatusApi.FindById(id) == null).ToArray()) Results.Remove(retired);
        if (Commands.BattleEpoch != CompanionAuthorityService.BattleEpoch) Commands.Reset(CompanionAuthorityService.BattleEpoch);
        if (!Commands.TryAccept(command, senderOwnsStatus)) return false;
        var owner = StatusApi.FindById(command.OwnerStatusId);
        var enemy = StatusApi.FindById(command.TargetStatusId);
        var accepted = owner != null && StatusApi.IsAlive(owner) && OlimyaRules.IsOlimya(PolymorphStateStore.EffectiveCombatRoleIdFor(owner))
            && OlimyaGameApi.IsHostileEnemy(enemy) && OlimyaGameApi.ApplyGoldenization(enemy!);
        Results[key] = Tuple.Create(fingerprint, accepted);
        return accepted;
    }

    public static void ReceiveResult(OlimyaGoldenizationCommand command, bool accepted, string battleId)
    {
        if (pending == null || command == null || pending.Token != command.Token
            || pending.OwnerStatusId != command.OwnerStatusId || !AuraNetworkIdentityRuntime.MatchesBattle(battleId)) return;
        pending = null;
        if (accepted) PlayerApi.SetSkillTime(OlimyaIds.GoldenTouch, OlimyaIds.GoldenTouchCooldown);
        else PlayerApi.ShowCaption("点金未被主机接受，未进入冷却。");
    }

    public static void TickPending()
    {
        if (pending == null || !battleReady || DateTime.UtcNow < retryAt || attempts >= 8) return;
        attempts++; retryAt = DateTime.UtcNow.AddSeconds(2);
        DispatchCommand?.Invoke(pending);
        if (pending != null && attempts >= 8) PlayerApi.ShowCaption("点金结果尚未确认，本场等待主机恢复，避免重复执行。");
    }

    public static bool HandleLocalAuthoritative(OlimyaGoldenizationCommand command)
    {
        return command != null && HandleAuthoritative(command,
            OlimyaGameApi.IsLocalPlayer(StatusApi.FindById(command.OwnerStatusId)));
    }

    public static void EndBattle()
    {
        battleReady = false;
        pending = null;
        Results.Clear();
        Commands.Reset(0);
        nextSequence = 0;
        OlimyaDamageService.Clear();
        OlimyaEconomyService.ClearTransient();
    }

    public static void BeginBattle() => battleReady = true;

    private static OlimyaGoldenizationCommand Command(OlimyaGoldenizationCommandKind kind, string ownerId, string targetId)
    {
        return new OlimyaGoldenizationCommand
        {
            Kind = kind,
            BattleEpoch = CompanionAuthorityService.BattleEpoch,
            OwnerStatusId = ownerId,
            TargetStatusId = targetId,
            Token = Guid.NewGuid().ToString("N"),
            Sequence = ++nextSequence
        };
    }
}
