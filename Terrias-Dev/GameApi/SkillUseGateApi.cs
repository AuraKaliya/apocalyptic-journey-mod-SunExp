using System;

namespace SunExp.Dll.GameApi;

public readonly struct SkillUseGateSnapshot
{
    public SkillUseGateSnapshot(
        string skillId,
        bool nativeAllowed,
        bool cardUseAllowed,
        bool statusAvailable,
        string statusState,
        string fightType,
        bool skillTimePresent,
        int skillTime)
    {
        SkillId = skillId ?? "";
        NativeAllowed = nativeAllowed;
        CardUseAllowed = cardUseAllowed;
        StatusAvailable = statusAvailable;
        StatusState = statusState ?? "unknown";
        FightType = fightType ?? "unknown";
        SkillTimePresent = skillTimePresent;
        SkillTime = Math.Max(0, skillTime);
    }

    public string SkillId { get; }
    public bool NativeAllowed { get; }
    public bool CardUseAllowed { get; }
    public bool StatusAvailable { get; }
    public string StatusState { get; }
    public string FightType { get; }
    public bool SkillTimePresent { get; }
    public int SkillTime { get; }

    public string RejectionReason()
    {
        if (!StatusAvailable)
        {
            return "status-unavailable";
        }

        if (string.Equals(StatusState, IStatusManager.State.Dead.ToString(), StringComparison.Ordinal)
            || string.Equals(StatusState, IStatusManager.State.NoAction.ToString(), StringComparison.Ordinal))
        {
            return "status-" + StatusState;
        }

        if (!string.Equals(FightType, global::FightType.Player.ToString(), StringComparison.Ordinal))
        {
            return "fight-" + FightType;
        }

        if (!CardUseAllowed)
        {
            return "card-ui-busy";
        }

        if (!SkillTimePresent)
        {
            return "skill-time-missing";
        }

        if (SkillTime > 0)
        {
            return "cooldown-" + SkillTime;
        }

        return NativeAllowed ? "allowed" : "native-rejected";
    }
}

public static class SkillUseGateApi
{
    public static SkillUseGateSnapshot Capture(SkillItem? skillItem)
    {
        if (skillItem == null)
        {
            return default;
        }

        var skillId = RoleSkillApi.NormalizeSkillId(CardConfigApi.Id(skillItem.dataConfig));
        var status = skillItem.status;
        var present = PlayerApi.TryReadSkillTime(skillId, out var cooldown);
        bool allowed;
        try
        {
            allowed = skillItem.TryUse();
        }
        catch
        {
            allowed = false;
        }

        return new SkillUseGateSnapshot(
            skillId,
            allowed,
            CardItem.canUse,
            status != null,
            status?.state.ToString() ?? "unavailable",
            FightManager.Instance?.fightType.ToString() ?? "unavailable",
            present,
            cooldown);
    }
}
