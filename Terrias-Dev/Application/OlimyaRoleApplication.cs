using System;
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
        var target = TargetApi.PrimaryTarget(self);
        if (!OlimyaGameApi.IsHostileEnemy(target)
            || !TargetApi.OpposingSideTargets(self, self.Self).Any(enemy => enemy.InstanceId == target!.InstanceId)) return false;
        var command = Command(OlimyaGoldenizationCommandKind.Apply, self.Self.InstanceId, target!.InstanceId);
        TargetApi.SetStatusForTarget(self, target, "Target");
        if (DispatchCommand?.Invoke(command) != true) return false;
        PlayerApi.SetSkillTime(OlimyaIds.GoldenTouch, OlimyaIds.GoldenTouchCooldown);
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
        if (!battleReady || !CompanionAuthorityService.IsAuthoritative() || !AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation) return false;
        if (Commands.BattleEpoch != CompanionAuthorityService.BattleEpoch) Commands.Reset(CompanionAuthorityService.BattleEpoch);
        if (!Commands.TryAccept(command, senderOwnsStatus)) return false;
        var owner = StatusApi.FindById(command.OwnerStatusId);
        if (owner == null) return false;
        if (!StatusApi.IsAlive(owner) || !OlimyaRules.IsOlimya(PolymorphStateStore.EffectiveCombatRoleIdFor(owner))) return false;
        var enemy = StatusApi.FindById(command.TargetStatusId);
        return OlimyaGameApi.IsHostileEnemy(enemy) && OlimyaGameApi.ApplyGoldenization(enemy!);
    }

    public static bool HandleLocalAuthoritative(OlimyaGoldenizationCommand command)
    {
        return command != null && HandleAuthoritative(command,
            OlimyaGameApi.IsLocalPlayer(StatusApi.FindById(command.OwnerStatusId)));
    }

    public static void EndBattle()
    {
        battleReady = false;
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
