using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class OlimyaDamageService
{
    private static readonly Stack<DamageFrame> Frames = new();

    public static void Begin(IStatusManager target, IStatusManager? source, bool ignoresShield)
    {
        Frames.Push(new DamageFrame(target, source, ResolveRecipient(source), ignoresShield));
    }

    public static void ObserveResolvedDamage(IStatusManager target, IStatusManager? source, int amount)
    {
        if (Frames.Count == 0) return; // A replicated DamageText is presentation only.
        var frame = Frames.Peek();
        if (frame.Observed || !ReferenceEquals(frame.Target, target) || !ReferenceEquals(frame.Source, source)) return;
        frame.Observed = true;
        var reward = OlimyaRules.Damage(frame.Hp, frame.Shield, amount, frame.IgnoresShield);
        if (frame.Dreamweaver && IsDreamweaver(target) && StatusApi.IsAlive(target))
            StatusApi.TryAddShield(target, reward.HpLoss);
        if (frame.Goldenized) OlimyaGameApi.AwardAttackGold(frame.Recipient, reward.Gold);
    }

    public static void End(IStatusManager target)
    {
        if (Frames.Count > 0 && ReferenceEquals(Frames.Peek().Target, target)) Frames.Pop();
    }

    public static void Clear() => Frames.Clear();

    public static bool IsDreamweaver(IStatusManager? status)
    {
        return OlimyaGameApi.IsLocalPlayer(status)
            && OlimyaRules.IsOlimya(PolymorphStateStore.EffectiveCombatRoleIdFor(status));
    }

    private static IStatusManager? ResolveRecipient(IStatusManager? source)
    {
        if (source == null) return null;
        var identity = CompanionOwnershipService.Find(source.InstanceId);
        if (identity != null) return StatusApi.FindById(identity.SemanticOwnerStatusId);
        var spirit = SpiritStateStore.Find(source.InstanceId);
        if (spirit != null) return StatusApi.FindById(spirit.OwnerStatusId);
        var projection = ProjectionStateStore.Find(source.InstanceId);
        return projection != null ? StatusApi.FindById(projection.OwnerStatusId) : source;
    }

    private sealed class DamageFrame
    {
        public DamageFrame(IStatusManager target, IStatusManager? source, IStatusManager? recipient, bool ignoresShield)
        {
            Target = target;
            Source = source;
            Recipient = recipient;
            // The native first-death rescue can convert a hit on a downed
            // player into healing, even after that player has accumulated HP.
            Hp = StatusApi.IsAlive(target) ? Math.Max(0, target.CurHp) : 0;
            Shield = Math.Max(0, target.Defend);
            IgnoresShield = ignoresShield;
            Dreamweaver = IsDreamweaver(target);
            Goldenized = BuffApi.Level(target, OlimyaIds.Goldenized) > 0;
        }
        public IStatusManager Target { get; }
        public IStatusManager? Source { get; }
        public IStatusManager? Recipient { get; }
        public int Hp { get; }
        public int Shield { get; }
        public bool IgnoresShield { get; }
        public bool Dreamweaver { get; }
        public bool Goldenized { get; }
        public bool Observed { get; set; }
    }
}
