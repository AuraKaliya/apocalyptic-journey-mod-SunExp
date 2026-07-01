namespace SunExp.Dll.Infrastructure;

public static class SunExpIds
{
    public const string ModId = "SunExp";

    public const string ModLogTag = "SunExp.DLL";

    public const string SunCardVisualSkinId = "sunexp.card_visual.sun";
    public const string MorningStarCardVisualSkinId = "sunexp.card_visual.morning_star";
    public const string CardFaceEffectShaderId = "sunexp.card_visual_effect.card_face.shader";
    public const string CardFaceFoilHoloVisualEffectId = "sunexp.card_visual_effect.foil_holo";
    public const string CardFaceStardustVisualEffectId = "sunexp.card_visual_effect.stardust_overture";
    public const string CardFrameHoloFlowShaderId = CardFaceEffectShaderId;
    public const string CardFrameHoloFlowVisualEffectId = CardFaceFoilHoloVisualEffectId;
    public const string BlazingCrownCollapseHoloEffectBindingId = "sunexp.card_visual_effect.blazing_crown_collapse.holo_flow";
    public const string StellarOvertureStardustEffectBindingId = "sunexp.card_visual_effect.stellar_overture.stardust";
    public const string SunCardFramePath = "Mods/SunExp/ModResource/Images/UI/\u65e5\u8000-\u5361\u68461.png";
    public const string SunCardBackgroundPath = "Mods/SunExp/ModResource/Images/UI/\u5361\u9762\u80cc\u666f.png";
    public const string MorningStarCardFramePath = "Mods/SunExp/ModResource/Images/UI/\u6668\u661f-\u5361\u68461.png";
    public const string SunCardIconPathPrefix = "Mods/SunExp/ModResource/Images/Card/SunExp/";
    public const string StellarOvertureCardIconPathPrefix = "Mods/SunExp/ModResource/Images/Card/Loneer/stellar_overture_";
    public const string RadiantSparkCardPackId = "SunExp_sunexp_cardpack_radiant_spark";
    public const string EmberCrownCardPackId = "SunExp_sunexp_cardpack_ember_crown";
    public const string SolarCanopyCardPackId = "SunExp_sunexp_cardpack_solar_canopy";

    public static readonly string[] SunThemeCardPackIds =
    {
        RadiantSparkCardPackId,
        EmberCrownCardPackId,
        SolarCanopyCardPackId
    };

    public const string WhiteRadianceTag = "白曜";
    public const string MorningStarSealTag = "启明星";

    public const string SolarRadiance = "SunExp_sunexp_solar_radiance";
    public const string SolarCrown = "SunExp_sunexp_solar_crown";
    public const string SolarCrownTier = "SunExp_sunexp_solar_crown_tier";
    public const string GatheredFlame = "SunExp_sunexp_gathered_flame";
    public const string Burn = "buff_burn";
    public const string Ember = "SunExp_sunexp_ember";
    public const string EmberCloak = "SunExp_sunexp_ember_cloak";
    public const string ScorchingCanopy = "SunExp_sunexp_scorching_canopy";
    public const string BodyBurn = "SunExp_sunexp_body_burn";
    public const string OriginCoreRadiance = "SunExp_sunexp_origin_core_radiance";
    public const string CycleGatheredFlame = "SunExp_sunexp_cycle_gathered_flame";
    public const string AfterglowOmen = "SunExp_sunexp_afterglow_omen";
    public const string DuskAfterheatRecoveryTrait = "SunExp_sunexp_dusk_afterheat_recovery_trait";
    public const string BossTraitMirrorArray = "SunExp_sunexp_boss_trait_mirror_array";
    public const string BossTraitMercilessDaylight = "SunExp_sunexp_boss_trait_merciless_daylight";
    public const string BossTraitWhiteRadianceSaint = "SunExp_sunexp_boss_trait_white_radiance_saint";
    public const string BossWhiteRadianceCrown = "SunExp_sunexp_boss_white_radiance_crown";
    public const string StarStonePouch = "SunExp_sunexp_star_stone_pouch";
    public const string MiracleClock = "SunExp_sunexp_miracle_clock";
    public const string Starlight = "SunExp_sunexp_starlight";
    public const string StarBlessing = "SunExp_sunexp_star_blessing";
    public const string StarScore = "SunExp_sunexp_star_score";
    public const string Resonance = "SunExp_sunexp_resonance";
    public const string StarClayBody = "SunExp_sunexp_star_clay_body";
    public const string StarClayDollTrait = "SunExp_sunexp_star_clay_doll_trait";
    public const string Cripple = "buff_cripple";
    public const string Extraordinary = "buff_extraordinary";
    public const string EnemyCardSaintWhiteEdict = "SunExp_sunexp_enemycard_saint_white_edict";

    public const string TempWhiteRadiance = "SunExpTempWhiteRadiance";
    public const string TempWhiteRadianceLockId = "SunExpTempWhiteRadianceLockId";
    public const string TempWhiteRadianceResolved = "SunExpTempWhiteRadianceResolved";

    public const string SolarTriggerCost = "SunExpSolarTriggerCost";

    public const string WunaActive = "SunExpWunaActive";
    public const string WunaPersistentEmber = "SunExpWunaPersistentEmber";
    public const string WunaWhiteSunPrayerCardId = "SunExp_wuna_wuna_white_sun_prayer";
    public const string WunaCoronationTokenCardId = "SunExp_wuna_wuna_coronation_token";
    public const string LoneerCareerId = "loneer";
    public const string LoneerActive = "SunExpLoneerActive";
    public const string LoneerMorningPrayerSkillCardId = "SunExp_loneer_loneer_morning_star_prayer";
    public const string StellarOvertureStartCardId = "SunExp_sunexp_stellar_overture_start";
    public const string StellarOvertureSustainCardId = "SunExp_sunexp_stellar_overture_sustain";
    public const string StellarOvertureTurnCardId = "SunExp_sunexp_stellar_overture_turn";
    public const string StellarOvertureCloseCardId = "SunExp_sunexp_stellar_overture_close";
    public const string WitchStarScoreCardId = "SunExp_sunexp_witch_star_score";

    public static readonly string[] StellarOvertureCardIds =
    {
        StellarOvertureStartCardId,
        StellarOvertureSustainCardId,
        StellarOvertureTurnCardId,
        StellarOvertureCloseCardId
    };

    public static readonly string[] SunThemeExplicitCardIds =
    {
        WunaCoronationTokenCardId,
        "SunExp_wuna_card_*wuna_coronation_token",
        "*wuna_coronation_token",
        "wuna_coronation_token"
    };

    public static readonly string[] SunThemeCardIconPathPrefixes =
    {
        SunCardIconPathPrefix
    };

    public const string StarClayDollPartnerId = "SunExp_sunexp_star_clay_doll";
    public const string StarClayDollBlessingId = "SunExp_sunexp_star_clay_doll_placeholder";
    public const string RuntimeMarkersKey = "SunExpRuntimeMarkers";
    public const string LoneerDerivedMarker = "SunExpLoneerDerived";
    public const string LoneerGuidanceMarker = "SunExpLoneerGuidance";
    public const string LoneerDerivedTag = "衍生牌";
    public const string LoneerGuidanceTag = "指引牌";

    public const string SolarMemoryModeKey = "SunExp_SolarMemoryMode";
    public const string SolarMemorySelectedPacksKey = "SunExp_SolarMemorySelectedPacks";
    public const string SolarMemoryOriginPointsKey = "SunExp_SolarMemoryOriginPoints";
    public const string SolarMemoryBlessPickCountKey = "SunExp_SolarMemoryBlessPickCount";
    public const string SolarMemoryBlessSelectedIdsKey = "SunExp_SolarMemoryBlessSelectedIds";
    public const string SolarMemoryDeckConfiguredKey = "SunExp_SolarMemoryDeckConfigured";
    public const string SolarMemoryStarterDeckAppliedKey = "SunExp_SolarMemoryStarterDeckApplied";
    public const string SolarMemoryStarterDeckModeKey = "SunExp_SolarMemoryStarterDeckMode";
    public const string StarterDeckOwnerKey = "StarterDeck.Owner";
    public const string StarterDeckScopeKey = "StarterDeck.Scope";
    public const string StarterDeckStateKey = "StarterDeck.State";
    public const string StarterDeckOwnerSolarMemory = "SunExp.SolarMemory";
    public const string StarterDeckStatePending = "pending";
    public const string StarterDeckStateApplied = "applied";
    public const string StarterDeckStateOfficial = "official";
    public const string SolarMemoryOriginConfiguredKey = "SunExp_SolarMemoryOriginConfigured";
    public const string SolarMemoryBlessConfiguredKey = "SunExp_SolarMemoryBlessConfigured";
    public const string SolarMemorySetupFinishedKey = "SunExp_SolarMemorySetupFinished";
    public const string SolarMemorySetupCommitTokenKey = "SunExp_SolarMemorySetupCommitToken";
    public const string SolarMemoryPrepStepKey = "SunExp_SolarMemoryPrepStep";
    public const string SolarMemoryPreparedKey = "SunExp_SolarMemoryPrepared";
    public const string SolarMemoryPostPreparationDialogueSeenKey = "SunExp_SolarMemoryPostPreparationDialogueSeen";
    public const string SolarMemoryPostPreparationDialoguePendingKey = "SunExp_SolarMemoryPostPreparationDialoguePending";
    public const string SolarMemorySaintWunaBossPendingKey = "SunExp_SolarMemorySaintWunaBossPending";
    public const string HardSunsetFightCountKey = "SunExp_Hard_SunsetFightCount";
    public const string SolarMemoryPostPreparationDialogueFlowId = "SunExp.SolarMemory.PostPreparationDialogue";
    public const string SolarMemorySecondSunEndingDialogueFlowId = "SunExp.SolarMemory.SecondSunEndingDialogue";
    public const string SolarMemorySaintWunaPreludeDialogueFlowId = "SunExp.SolarMemory.SaintWunaPreludeDialogue";
    public const string SolarMemorySaintWunaEndingDialogueFlowId = "SunExp.SolarMemory.SaintWunaEndingDialogue";
    public const string SolarMemoryPostPreparationDialogueId = "SunExp_sunexp_solar_memory_opening_1";
    public const string SolarMemoryPostPreparationCompleteDialogueId = "SunExp_sunexp_solar_memory_opening_4";
    public const string SolarMemorySecondSunEndingDialogueId = "SunExp_sunexp_solar_memory_second_sun_end_1";
    public const string SolarMemorySecondSunEndingCompleteDialogueId = "SunExp_sunexp_solar_memory_second_sun_end_2";
    public const string SolarMemorySaintWunaPreludeDialogueId = "SunExp_sunexp_solar_memory_saint_wuna_prelude_1";
    public const string SolarMemorySaintWunaPreludeCompleteDialogueId = "SunExp_sunexp_solar_memory_saint_wuna_prelude_6";
    public const string SolarMemorySaintWunaEndingDialogueId = "SunExp_sunexp_solar_memory_saint_wuna_end_1";
    public const string SolarMemorySaintWunaEndingCompleteDialogueId = "SunExp_sunexp_solar_memory_saint_wuna_end_3";
    public const string SolarMemoryEventId = "Sub_solar_memory_black_sun_after";
    public const string SolarMemoryFullEventId = "SunExp_sunexp_Sub_solar_memory_black_sun_after";
    public const string SolarMemoryMapId = "SunExp_sunexp_solar_memory_black_sun_after";
    public const string SolarMemoryShortMapId = "solar_memory_black_sun_after";
    public static readonly string[] SolarMemoryEventIds =
    {
        "Sub_solar_memory_black_sun_after",
        "Sub_solar_memory_second_sun",
        "Sub_solar_memory_saint_daily",
        "Sub_solar_memory_polluted_light",
        "Sub_solar_memory_grief_struggle",
        "Sub_solar_memory_above_sacred_wheel"
    };

    public static readonly string[] SolarMemoryFullEventIds =
    {
        "SunExp_sunexp_Sub_solar_memory_black_sun_after",
        "SunExp_sunexp_Sub_solar_memory_second_sun",
        "SunExp_sunexp_Sub_solar_memory_saint_daily",
        "SunExp_sunexp_Sub_solar_memory_polluted_light",
        "SunExp_sunexp_Sub_solar_memory_grief_struggle",
        "SunExp_sunexp_Sub_solar_memory_above_sacred_wheel"
    };

    public static readonly string[] SolarMemoryMapIds =
    {
        "SunExp_sunexp_solar_memory_black_sun_after",
        "SunExp_sunexp_solar_memory_second_sun",
        "SunExp_sunexp_solar_memory_saint_daily",
        "SunExp_sunexp_solar_memory_polluted_light",
        "SunExp_sunexp_solar_memory_grief_struggle",
        "SunExp_sunexp_solar_memory_above_sacred_wheel"
    };

    public static readonly string[] SolarMemoryShortMapIds =
    {
        "solar_memory_black_sun_after",
        "solar_memory_second_sun",
        "solar_memory_saint_daily",
        "solar_memory_polluted_light",
        "solar_memory_grief_struggle",
        "solar_memory_above_sacred_wheel"
    };

    public static readonly string[] SolarMemoryLayerNames =
    {
        "\u7279\u745e\u5384\u65af",
        "\u767d\u66dc\u5723\u5ead",
        "\u5723\u8f6e"
    };
    public const string SolarMemoryTitle = "日耀回忆";
    public const string SolarMemoryDescription = "乌娜的专属回忆";
    public const string SolarMemorySubtitle = "Boss连战";
    public const int SolarMemoryMaxLayer = 3;

    public const string SolarFinaleSavedNamesKey = "SunExp_SolarFinaleSavedNames";
    public const string SolarFinaleBurnedNamesKey = "SunExp_SolarFinaleBurnedNames";
    public const string SolarFinaleNamelessNamesKey = "SunExp_SolarFinaleNamelessNames";
    public const string SolarBossOrbitMirrorMapId = "SunExp_sunexp_solar_memory_boss_orbit_mirror_array";
    public const string SolarBossSecondSunMapId = "SunExp_sunexp_solar_memory_boss_second_sun_last_day";
    public const string SolarBossSaintWunaMapId = "SunExp_sunexp_solar_memory_boss_saint_wuna";
    public const string SolarBossOrbitMirrorShortMapId = "solar_memory_boss_orbit_mirror_array";
    public const string SolarBossSecondSunShortMapId = "solar_memory_boss_second_sun_last_day";
    public const string SolarBossSaintWunaShortMapId = "solar_memory_boss_saint_wuna";
    public const string SolarBossOrbitMirrorLevelId = "SunExp_sunexp_level_orbit_mirror_array";
    public const string SolarBossSecondSunLevelId = "SunExp_sunexp_level_second_sun_last_day";
    public const string SolarBossSaintWunaLevelId = "SunExp_sunexp_level_saint_wuna";
    public const string SolarBossOrbitMirrorEnemyId = "SunExp_sunexp_boss_orbit_mirror_array";
    public const string SolarBossSecondSunEnemyId = "SunExp_sunexp_boss_second_sun_last_day";
    public const string SolarBossSaintWunaEnemyId = "SunExp_sunexp_boss_saint_wuna";
    public const string SolarBossSecondSunMapTexturePath = "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Map/Map_00.png";
    public const string SolarBossSaintWunaMapTexturePath = "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Map/Map_00.png";
    public const string BlazingCrownCollapseShortCardId = "blazing_crown_collapse";
    public const string BlazingCrownCollapseCardId = "SunExp_sunexp_blazing_crown_collapse";
    public static readonly string[] BlazingCrownCollapseCardEffectIds =
    {
        BlazingCrownCollapseCardId,
        BlazingCrownCollapseShortCardId
    };
    public const int SolarFinaleNameCount = 8;

    public static bool IsSolarMemoryExclusiveMapId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (string.Equals(id, SolarBossOrbitMirrorMapId, System.StringComparison.Ordinal)
            || string.Equals(id, SolarBossOrbitMirrorShortMapId, System.StringComparison.Ordinal)
            || string.Equals(id, SolarBossSecondSunMapId, System.StringComparison.Ordinal)
            || string.Equals(id, SolarBossSecondSunShortMapId, System.StringComparison.Ordinal)
            || string.Equals(id, SolarBossSaintWunaMapId, System.StringComparison.Ordinal)
            || string.Equals(id, SolarBossSaintWunaShortMapId, System.StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var value in SolarMemoryMapIds)
        {
            if (string.Equals(id, value, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var value in SolarMemoryShortMapIds)
        {
            if (string.Equals(id, value, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSolarMemoryExclusiveEventId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var value = id ?? "";
        return value.StartsWith("Breaks_solar_memory_", System.StringComparison.Ordinal)
            || value.StartsWith("Sub_solar_memory_", System.StringComparison.Ordinal)
            || value.StartsWith("SunExp_sunexp_Sub_solar_memory_", System.StringComparison.Ordinal);
    }
}
