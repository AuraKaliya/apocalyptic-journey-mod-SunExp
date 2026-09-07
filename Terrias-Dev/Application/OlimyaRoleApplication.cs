using System;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class OlimyaRoleApplication
{
    private static readonly OlimyaGoldenizationLedger Marks = new();
    private static long nextSequence;
    private static bool awaitingNextTurn;
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
        awaitingNextTurn = true;
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
        // Marks expire on the caster's turn even if that player has since
        // changed form and neither of Olimya's career passives is active.
        if (awaitingNextTurn && DispatchCommand?.Invoke(Command(
                OlimyaGoldenizationCommandKind.OwnerTurnStarted, status.InstanceId, "")) == true)
            awaitingNextTurn = false;
    }

    public static bool HandleAuthoritative(OlimyaGoldenizationCommand command, bool senderOwnsStatus)
    {
        if (!battleReady || !CompanionAuthorityService.IsAuthoritative() || !AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation) return false;
        if (Marks.BattleEpoch != CompanionAuthorityService.BattleEpoch) Marks.Reset(CompanionAuthorityService.BattleEpoch);
        if (!Marks.TryAccept(command, senderOwnsStatus)) return false;
        var owner = StatusApi.FindById(command.OwnerStatusId);
        if (owner == null) return false;
        if (command.Kind == OlimyaGoldenizationCommandKind.OwnerTurnStarted)
        {
            foreach (var targetId in Marks.TakeExpired(command.OwnerStatusId))
            {
                var target = StatusApi.FindById(targetId);
                if (target != null) OlimyaGameApi.SetGoldenization(target, false);
            }
            return true;
        }
        if (!StatusApi.IsAlive(owner) || !OlimyaRules.IsOlimya(PolymorphStateStore.EffectiveCombatRoleIdFor(owner))) return false;
        var enemy = StatusApi.FindById(command.TargetStatusId);
        if (!OlimyaGameApi.IsHostileEnemy(enemy) || !OlimyaGameApi.SetGoldenization(enemy!, true)) return false;
        Marks.Mark(command.TargetStatusId, command.OwnerStatusId);
        return true;
    }

    public static bool HandleLocalAuthoritative(OlimyaGoldenizationCommand command)
    {
        return command != null && HandleAuthoritative(command,
            OlimyaGameApi.IsLocalPlayer(StatusApi.FindById(command.OwnerStatusId)));
    }

    public static void EndBattle()
    {
        battleReady = false;
        Marks.Reset(0);
        awaitingNextTurn = false;
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
