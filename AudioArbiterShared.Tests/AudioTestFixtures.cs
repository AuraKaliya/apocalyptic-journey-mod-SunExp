using AudioArbiter.Shared;
using AuraAudio.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private static SoundPlaybackRequest CreateRequest()
    {
        return new SoundPlaybackRequest
        {
            EventId = "event-1",
            FightToken = "fight-1",
            IssuerPlayerId = "player-1",
            ProviderId = "provider-1",
            OwnerModId = "owner-1",
            Kind = "CardUse",
            Stage = "PresentationCommitted",
            CareerId = "career-1",
            RoleId = "role-1",
            StatusInstanceId = "status-1",
            CardId = "card-1",
            SkillId = "skill-1",
            SkillSlot = 2,
            BuffId = "buff-1",
            EffectName = "effect-1",
            ActionName = "action-1",
            VocalState = "vocal-1",
            BattleResult = "Victory",
            Hp = 25,
            MaxHp = 100,
            PreviousHpRatio = 0.5f,
            HpRatio = 0.25f,
            SourceName = "source-1",
            CreatedAtUtcTicks = 638000000000000000L,
            MaxAgeMilliseconds = 4321,
            IsLocalOwner = true
        };
    }
    
    private void Equal<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
        }
    }
    
    private void Null(object? actual, string name)
    {
        assertions++;
        if (actual != null)
        {
            throw new InvalidOperationException($"{name}: expected null, actual={actual}");
        }
    }
    
    private void Same(object expected, object actual, string name)
    {
        assertions++;
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException(name + ": expected same reference");
        }
    }
    
    private void NotSame(object expected, object actual, string name)
    {
        assertions++;
        if (ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException(name + ": expected independent reference");
        }
    }
    
    private void True(bool actual, string name)
    {
        assertions++;
        if (!actual)
        {
            throw new InvalidOperationException(name + ": expected true");
        }
    }
    
    private sealed class FakeResource
    {
        public FakeResource(string id)
        {
            Id = id;
        }
    
        public string Id { get; }
    }
    
    private sealed class FakeProvider : IAudioProviderCandidate<FakeResource>
    {
        public FakeProvider(
            string providerId,
            string ownerModId,
            int priority,
            bool hardClaim,
            string loadState,
            FakeResource? resource)
        {
            ProviderId = providerId;
            OwnerModId = ownerModId;
            QualifiedProviderId = AudioProviderResolver.QualifyProviderId(ownerModId, providerId);
            Priority = priority;
            HardClaim = hardClaim;
            LoadState = loadState;
            Resource = resource;
        }
    
        public string ProviderId { get; }
    
        public string OwnerModId { get; }
    
        public string QualifiedProviderId { get; }
    
        public int Priority { get; }
    
        public bool HardClaim { get; }
    
        public bool Matches { get; set; } = true;
    
        private string LoadState { get; }
    
        private FakeResource? Resource { get; }
    
        public bool Evaluate(object request)
        {
            return Matches;
        }
    
        public string GetLoadState()
        {
            return LoadState;
        }
    
        public FakeResource? GetResource(object request)
        {
            return Resource;
        }
    }
    
    private sealed class PropertySource
    {
        public string Text => "alpha";
        public int Number => 17;
        public string IntegerText => "29";
        public long LongNumber => 1234567890123L;
        public bool Flag => true;
        public string BooleanText => "false";
        public float Ratio => 0.25f;
        public string FloatText => "1.5";
        public string Invalid => "not-a-value";
        public string Throwing => throw new InvalidOperationException("getter failure");
    }
    
    private sealed class RequestLike
    {
        public string EventId => "event-1";
        public string FightToken => "fight-1";
        public string IssuerPlayerId => "player-1";
        public string ProviderId => "provider-1";
        public string OwnerModId => "owner-1";
        public string Kind => "CardUse";
        public string Stage => "PresentationCommitted";
        public string CareerId => "career-1";
        public string RoleId => "role-1";
        public string StatusInstanceId => "status-1";
        public string CardId => "card-1";
        public string SkillId => "skill-1";
        public int SkillSlot => 2;
        public string BuffId => "buff-1";
        public string EffectName => "effect-1";
        public string ActionName => "action-1";
        public string VocalState => "vocal-1";
        public string BattleResult => "Victory";
        public string Hp => "25";
        public int MaxHp => 100;
        public string PreviousHpRatio => "0.5";
        public float HpRatio => 0.25f;
        public string SourceName => "source-1";
        public long CreatedAtUtcTicks => 638000000000000000L;
        public string MaxAgeMilliseconds => "4321";
        public bool IsLocalOwner => true;
    }
}
