using AudioArbiter.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private void VerifySkillSlotResolver()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = "wuna",
            ["Skill1"] = "Terrias_wuna_wuna_white_sun_prayer",
            ["Skill2"] = "*wuna_grave_song",
            ["SkillScript"] = "ignored"
        };

        Equal(1, AudioSkillSlotResolver.Resolve(row, "Terrias_wuna_wuna_white_sun_prayer"),
            "full configured skill resolves slot one");
        Equal(1, AudioSkillSlotResolver.Resolve(row, "*wuna_white_sun_prayer"),
            "short runtime skill alias resolves slot one");
        Equal(2, AudioSkillSlotResolver.Resolve(row, "Terrias_wuna_wuna_grave_song"),
            "full runtime skill resolves wildcard-configured slot two");
        Equal(0, AudioSkillSlotResolver.Resolve(row, "unrelated_skill"),
            "unconfigured active skill fails closed");
        Equal(0, AudioSkillSlotResolver.Resolve(null, "skill"),
            "missing role row fails closed");
        Equal(true, AudioSkillSlotResolver.IsConfiguredSlot(row, 1),
            "configured slot one is recognized");
        Equal(true, AudioSkillSlotResolver.IsConfiguredSlot(row, 2),
            "configured slot two is recognized");
        Equal(false, AudioSkillSlotResolver.IsConfiguredSlot(row, 3),
            "role skill count bounds voice slots");

        var multiSkillSlot = new Dictionary<string, string>
        {
            ["Skill1"] = "alpha; beta|gamma"
        };
        Equal(1, AudioSkillSlotResolver.Resolve(multiSkillSlot, "beta"),
            "multi-id configured skill slot is parsed");
    }
}
