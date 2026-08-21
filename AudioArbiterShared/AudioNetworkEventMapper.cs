namespace AudioArbiter.Shared;

internal static class AudioNetworkEventMapper
{
    public static SoundPlaybackRequest CreateRemoteCopy(SoundPlaybackRequest request)
    {
        return new SoundPlaybackRequest
        {
            EventId = request.EventId,
            FightToken = request.FightToken,
            IssuerPlayerId = request.IssuerPlayerId,
            ProviderId = request.ProviderId,
            OwnerModId = request.OwnerModId,
            Kind = request.Kind,
            Stage = request.Stage,
            CareerId = request.CareerId,
            RoleId = request.RoleId,
            StatusInstanceId = request.StatusInstanceId,
            CardId = request.CardId,
            BuffId = request.BuffId,
            EffectName = request.EffectName,
            ActionName = request.ActionName,
            VocalState = request.VocalState,
            BattleResult = request.BattleResult,
            Hp = request.Hp,
            MaxHp = request.MaxHp,
            PreviousHpRatio = request.PreviousHpRatio,
            HpRatio = request.HpRatio,
            SourceName = request.SourceName,
            CreatedAtUtcTicks = request.CreatedAtUtcTicks,
            MaxAgeMilliseconds = request.MaxAgeMilliseconds,
            IsLocalOwner = request.IsLocalOwner,
            DisableSync = true
        };
    }
}
