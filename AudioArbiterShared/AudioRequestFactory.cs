using System;

namespace AudioArbiter.Shared;

internal static class AudioRequestFactory
{
    public static SoundPlaybackRequest CreateCareerSelected(AudioCareerObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.CareerSelected,
            Stage = AudioSignalStages.Committed,
            CareerId = observation.CareerId,
            RoleId = observation.CareerId,
            SourceName = observation.SourceName
        };
    }

    public static SoundPlaybackRequest CreateCardUse(
        AudioCombatActionObservation observation,
        string cardUseEventId)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            EventId = cardUseEventId ?? "",
            Kind = SoundEventKinds.CardUse,
            Stage = AudioSignalStages.PresentationCommitted,
            CardId = observation.CardId,
            CareerId = observation.CareerId,
            RoleId = observation.RoleId,
            StatusInstanceId = observation.StatusInstanceId,
            EffectName = observation.EffectName,
            ActionName = observation.ActionName,
            SourceName = observation.SourceName
        };
    }

    public static SoundPlaybackRequest CreateSkillVoice(
        AudioSkillActionObservation observation,
        string transactionId)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            EventId = transactionId ?? "",
            Kind = SoundEventKinds.SkillVoice,
            Stage = AudioSignalStages.Committed,
            SkillId = observation.SkillId,
            SkillSlot = observation.SkillSlot,
            CareerId = observation.CareerId,
            RoleId = observation.RoleId,
            StatusInstanceId = observation.StatusInstanceId,
            SourceName = observation.SourceName,
            IsLocalOwner = true
        };
    }

    public static SoundPlaybackRequest CreateBuffApplied(AudioBuffObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.BuffApplied,
            Stage = AudioSignalStages.Applied,
            BuffId = observation.BuffId,
            CareerId = observation.CareerId,
            RoleId = observation.CareerId,
            StatusInstanceId = observation.StatusInstanceId,
            SourceName = observation.SourceName
        };
    }

    public static SoundPlaybackRequest CreateVocalState(AudioVocalObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.VocalState,
            Stage = AudioSignalStages.Observed,
            VocalState = observation.VocalState,
            CareerId = observation.CareerId,
            RoleId = observation.RoleId,
            StatusInstanceId = observation.StatusInstanceId,
            IsLocalOwner = observation.IsLocalOwner,
            SourceName = observation.SourceName
        };
    }

    public static SoundPlaybackRequest CreateLowHealth(AudioStatusSnapshot snapshot, float previousHpRatio)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.LowHealth,
            Stage = AudioSignalStages.ThresholdCrossedDown,
            CareerId = snapshot.CareerId,
            RoleId = string.IsNullOrWhiteSpace(snapshot.RoleId) ? snapshot.CareerId : snapshot.RoleId,
            StatusInstanceId = snapshot.StatusInstanceId,
            Hp = snapshot.Hp,
            MaxHp = snapshot.MaxHp,
            PreviousHpRatio = previousHpRatio,
            HpRatio = snapshot.HpRatio,
            SourceName = snapshot.SourceName,
            IsLocalOwner = snapshot.IsLocalOwner
        };
    }

    public static SoundPlaybackRequest CreateBattleCompleted(AudioBattleObservation observation)
    {
        if (observation == null) throw new ArgumentNullException(nameof(observation));
        return new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.BattleCompleted,
            Stage = AudioSignalStages.Completed,
            BattleResult = observation.Result,
            CareerId = observation.CareerId,
            RoleId = observation.CareerId,
            SourceName = observation.SourceName
        };
    }
}
