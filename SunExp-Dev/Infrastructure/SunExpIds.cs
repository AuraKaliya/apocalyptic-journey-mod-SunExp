namespace SunExp.Dll.Infrastructure;

public static class SunExpIds
{
    public const string ModLogTag = "SunExp.DLL";

    public const string WhiteRadianceTag = "白曜";

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

    public const string TempWhiteRadiance = "SunExpTempWhiteRadiance";
    public const string TempWhiteRadianceLockId = "SunExpTempWhiteRadianceLockId";
    public const string TempWhiteRadianceResolved = "SunExpTempWhiteRadianceResolved";

    public const string SolarTriggerCost = "SunExpSolarTriggerCost";

    public const string WunaActive = "SunExpWunaActive";
    public const string WunaPersistentEmber = "SunExpWunaPersistentEmber";

    public const string WunaEventProgressKey = "SunExp_WunaEventProgressV2";
    public const string SolarEventMapId = "SunExp_sunexp_solar_event";
    public const string SolarEventShortMapId = "solar_event";
    public const string WunaEventPrefix = "Sub_wuna_event_";
    public const string WunaEventFullPrefix = "SunExp_sunexp_Sub_wuna_event_";
    public const string WunaEventRepeat = "Sub_wuna_event_repeat";
    public const string WunaEventFullRepeat = "SunExp_sunexp_Sub_wuna_event_repeat";
    public const int WunaEventMaxProgress = 6;

    public const string SolarMemoryModeKey = "SunExp_SolarMemoryMode";
    public const string SolarMemorySelectedPacksKey = "SunExp_SolarMemorySelectedPacks";
    public const string SolarMemoryOriginPointsKey = "SunExp_SolarMemoryOriginPoints";
    public const string SolarMemoryBlessPickCountKey = "SunExp_SolarMemoryBlessPickCount";
    public const string SolarMemoryBlessSelectedIdsKey = "SunExp_SolarMemoryBlessSelectedIds";
    public const string SolarMemoryDeckConfiguredKey = "SunExp_SolarMemoryDeckConfigured";
    public const string SolarMemoryStarterDeckAppliedKey = "SunExp_SolarMemoryStarterDeckApplied";
    public const string SolarMemoryStarterDeckModeKey = "SunExp_SolarMemoryStarterDeckMode";
    public const string SolarMemoryOriginConfiguredKey = "SunExp_SolarMemoryOriginConfigured";
    public const string SolarMemoryBlessConfiguredKey = "SunExp_SolarMemoryBlessConfigured";
    public const string SolarMemorySetupFinishedKey = "SunExp_SolarMemorySetupFinished";
    public const string SolarMemoryPrepStepKey = "SunExp_SolarMemoryPrepStep";
    public const string SolarMemoryPreparedKey = "SunExp_SolarMemoryPrepared";
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
    public const string SolarFinaleSecondSunDefeatedKey = "SunExp_SolarFinaleSecondSunDefeated";
    public const string SolarFinaleEndingKey = "SunExp_SolarFinaleEnding";
    public const string SolarFinaleSaintGateEventId = "Sub_solar_finale_saint_gate";
    public const string SolarFinaleFullSaintGateEventId = "SunExp_sunexp_Sub_solar_finale_saint_gate";
    public const string SolarFinaleSaintGateResolvedKey = "SunExp_SolarFinaleSaintGateResolved";
    public const string SolarFinalePendingSaintBattleKey = "SunExp_SolarFinalePendingSaintBattle";
    public const string SolarFinaleSaintDefeatedKey = "SunExp_SolarFinaleSaintDefeated";
    public const string SolarBossOrbitMirrorMapId = "SunExp_sunexp_solar_memory_boss_orbit_mirror_array";
    public const string SolarBossSecondSunMapId = "SunExp_sunexp_solar_memory_boss_second_sun_last_day";
    public const string SolarBossSaintWunaMapId = "SunExp_sunexp_solar_memory_boss_saint_wuna";
    public const string SolarBossOrbitMirrorLevelId = "SunExp_sunexp_level_orbit_mirror_array";
    public const string SolarBossSecondSunLevelId = "SunExp_sunexp_level_second_sun_last_day";
    public const string SolarBossSaintWunaLevelId = "SunExp_sunexp_level_saint_wuna";
    public const string SolarBossOrbitMirrorEnemyId = "SunExp_sunexp_boss_orbit_mirror_array";
    public const string SolarBossSecondSunEnemyId = "SunExp_sunexp_boss_second_sun_last_day";
    public const string SolarBossSaintWunaEnemyId = "SunExp_sunexp_boss_saint_wuna";
    public const int SolarFinaleNameCount = 8;
    public const int SolarFinaleHiddenBossNameThreshold = 5;
}
