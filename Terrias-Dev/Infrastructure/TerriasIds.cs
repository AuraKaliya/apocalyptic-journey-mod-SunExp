namespace Terrias.Dll.Infrastructure;

public static class TerriasIds
{
    public const string ColumbinaCareerId = "Terrias_columbina_columbina";
    public const string ColumbinaEternalTideCardId = "Terrias_columbina_columbina_eternal_tide";
    public const string ColumbinaHomesicknessCardId = "Terrias_columbina_columbina_homesickness";
    public const string FateStarCardShortId = "fate_star";
    public const string FateStarCardId = "Terrias_terrias_fate_star";
    public const string GravityRipple = "Terrias_terrias_gravity_ripple";
    public const string GravityValue = "Terrias_terrias_gravity_value";
    public const string MoonDomain = "Terrias_terrias_moon_domain";
    public const string Constellation = "Terrias_terrias_constellation";
    public const string ConstellationStorage = "TerriasConstellation";
    public const string OriginStrength50Blessing = "Terrias_terrias_origin_strength_50";
    public const string OriginSpirit50Blessing = "Terrias_terrias_origin_spirit_50";
    public const string OriginFortune50Blessing = "Terrias_terrias_origin_fortune_50";
    public const string OriginPerceive50Blessing = "Terrias_terrias_origin_perceive_50";
    public const string ModId = "Terrias";

    public const string ModLogTag = "Terrias.DLL";
    public const string FamiliarBlessingRegistryFile = "familiar.blessing.registry.json";
    public const string LocalizationRegistryFile = "localization.registry.json";
    public const string WitchArchiveRegistryFile = "witch.archive.registry.json";
    public const string WitchArchiveResourceCategory = "ui.witch-archive";
    public const string FamiliarProfileDirectory = "FamiliarGrowthProfiles";
    public const string FamiliarRunActivePartnerKey = "Terrias_FamiliarRunActivePartner";
    public const string SpiritProfileDirectory = "SpiritCollectionProfiles";
    public const string SpiritAdventureSessionDirectory = "SpiritAdventureSessions";

    public const string SunCardVisualSkinId = "terrias.card_visual.sun";
    public const string MorningStarCardVisualSkinId = "terrias.card_visual.morning_star";
    public const string CardFaceEffectShaderId = "terrias.card_visual_effect.card_face.shader";
    public const string CardFaceFoilHoloVisualEffectId = "terrias.card_visual_effect.foil_holo";
    public const string CardFaceStardustVisualEffectId = "terrias.card_visual_effect.stardust_overture";
    public const string CardFrameHoloFlowShaderId = CardFaceEffectShaderId;
    public const string CardFrameHoloFlowVisualEffectId = CardFaceFoilHoloVisualEffectId;
    public const string BlazingCrownCollapseHoloEffectBindingId = "terrias.card_visual_effect.blazing_crown_collapse.holo_flow";
    public const string StellarOvertureStardustEffectBindingId = "terrias.card_visual_effect.stellar_overture.stardust";
    public const string StellarOvertureCardUseFxId = "stellar-overture-star-trail";
    public const string StellarOvertureCardUseVisualEffectId = CardFaceStardustVisualEffectId;
    public const string SunCardFramePath = "Mods/Terrias/ModResource/Images/UI/\u65e5\u8000-\u5361\u68461.png";
    public const string SunCardBackgroundPath = "Mods/Terrias/ModResource/Images/UI/\u5361\u9762\u80cc\u666f.png";
    public const string MorningStarCardFramePath = "Mods/Terrias/ModResource/Images/UI/\u6668\u661f-\u5361\u68461.png";
    public const string SunCardIconPathPrefix = "Mods/Terrias/ModResource/Images/Card/Terrias/";
    public const string StellarOvertureCardIconPathPrefix = "Mods/Terrias/ModResource/Images/Card/Loneer/stellar_overture_";
    public const string SolarEmberCrownCanopyCardPackId = "Terrias_terrias_cardpack_solar_ember_crown_canopy";
    public const string RadiantSparkCardPackId = "Terrias_terrias_cardpack_radiant_spark";
    public const string EmberCrownCardPackId = "Terrias_terrias_cardpack_ember_crown";
    public const string SolarCanopyCardPackId = "Terrias_terrias_cardpack_solar_canopy";
    public const string MorningStarOvertureCardPackId = "Terrias_terrias_cardpack_morning_star_overture";
    public const string FalseGoldDreamCardPackId = "Terrias_terrias_cardpack_false_gold_dream";
    public const string EmberCloakLiningRelicId = "*ember_cloak_lining";
    public const string LegacyEmberCloakLiningRelicId = "ember_cloak_lining";

    public static readonly string[] LegacySunCardPackIds =
    {
        RadiantSparkCardPackId,
        EmberCrownCardPackId,
        SolarCanopyCardPackId
    };

    public static readonly string[] SunThemeCardPackIds =
    {
        SolarEmberCrownCanopyCardPackId,
        RadiantSparkCardPackId,
        EmberCrownCardPackId,
        SolarCanopyCardPackId
    };

    public static readonly string[] MorningStarThemeCardPackIds =
    {
        MorningStarOvertureCardPackId
    };

    public static bool IsHiddenRelicId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var localId = TerriasContentIdCompatibility.LocalId(id);
        return localId.StartsWith("*", System.StringComparison.Ordinal)
            || string.Equals(localId, LegacyEmberCloakLiningRelicId, System.StringComparison.Ordinal);
    }

    public static bool IsTechnicalBlessingId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var localId = TerriasContentIdCompatibility.LocalId(id).TrimStart('*');
        return string.Equals(localId, "dusk_afterheat_recovery", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "star_clay_doll_placeholder", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "sandrone_cat_placeholder", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "origin_strength_50", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "origin_spirit_50", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "origin_fortune_50", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(localId, "origin_perceive_50", System.StringComparison.OrdinalIgnoreCase);
    }

    public const string WhiteRadianceTag = "白曜";
    public const string MorningStarSealTag = "启明星";
    public const string SolarFlameSealTag = "阳炣";
    public const string GoldDreamTag = "黄金梦";
    public const string GoldDreamTemporaryMarker = "TerriasTempGoldDream";
    public const string GoldDreamSkipOnce = "TerriasGoldDreamSkipOnce";
    public const string FortuneThrowAscension = "TerriasFortuneThrowAscension";
    public const string SolarWitchBlessing = "solar_witch";
    public const string WhiteRadianceSaintBlessing = "white_radiance_saint";
    public const string SunPriestBlessing = "sun_priest";
    public const string ForgottenOneBlessing = "forgotten_one";
    public const string DreamTalkerBlessing = "dream_talker";
    public const string DeliriousTalkerBlessing = "delirious_talker";
    public const string WisherBlessing = "wisher";
    public const string UnspeakableOneBlessing = "unspeakable_one";
    public const string WitheredOneBlessing = "withered_one";
    public const string BlindOneBlessing = "blind_one";

    public const string ReverseFormulaCardShortId = "reverse_formula";
    public const string MorningStarAfterglowCardShortId = "morning_star_afterglow";
    public const string OmenTransferCardShortId = "omen_transfer";
    public const string AllBeingsAspectCardShortId = "all_beings_aspect";
    public const string AllBeingsWishCardShortId = "all_beings_wish";
    public const string AllBeingsFerryCardShortId = "all_beings_ferry";
    public const string MorningStarElegyCardShortId = "morning_star_elegy";
    public const string ReverseFormulaCardId = "Terrias_terrias_reverse_formula";
    public const string MorningStarAfterglowCardId = "Terrias_terrias_morning_star_afterglow";
    public const string OmenTransferCardId = "Terrias_terrias_omen_transfer";
    public const string AllBeingsAspectCardId = "Terrias_terrias_all_beings_aspect";
    public const string AllBeingsWishCardId = "Terrias_terrias_all_beings_wish";
    public const string AllBeingsFerryCardId = "Terrias_terrias_all_beings_ferry";
    public const string MorningStarElegyCardId = "Terrias_terrias_morning_star_elegy";

    public const string TimelessClockRelic = "timeless_clock";
    public const string LoneerStarStonePouchRelic = "loneer_star_stone_pouch";
    public const string FoxWomanHarpRelic = "fox_woman_harp";
    public const string DimStarStoneRelic = "dim_star_stone";
    public const string BlackSunCrossRelic = "black_sun_cross";
    public const string TimelessClockZeroCostMarker = "TerriasTimelessClockZeroCost";

    public const string SolarRadiance = "Terrias_terrias_solar_radiance";
    public const string SolarCrown = "Terrias_terrias_solar_crown";
    public const string SolarCrownTier = "Terrias_terrias_solar_crown_tier";
    public const string GatheredFlame = "Terrias_terrias_gathered_flame";
    public const string Burn = "buff_burn";
    public const string Vulnerability = "buff_vulnerability";
    public const string Weak = "buff_weak";
    public const string PyroAttachment = "Terrias_terrias_element_pyro";
    public const string ElectroAttachment = "Terrias_terrias_element_electro";
    public const string CryoAttachment = "Terrias_terrias_element_cryo";
    public const string HydroAttachment = "Terrias_terrias_element_hydro";
    public const string DendroAttachment = "Terrias_terrias_element_dendro";
    public const string DendroCore = "Terrias_terrias_dendro_core";
    public const string Frozen = "Terrias_terrias_frozen";
    public const string ElementalEnemyMagicKey = "TerriasElementalMagic";
    public const string ElementalEnemyMagicRarityKey = "TerriasElementalMagicRarity";
    public const string ElementalCrystalUiRoot = "Terrias_ElementalCrystalOverlay";
    public const string Ember = "Terrias_terrias_ember";
    public const string EmberCloak = "Terrias_terrias_ember_cloak";
    public const string ScorchingCanopy = "Terrias_terrias_scorching_canopy";
    public const string SamsaraGarden = "Terrias_terrias_samsara_garden";
    public const string BodyBurn = "Terrias_terrias_body_burn";
    public const string OriginCoreRadiance = "Terrias_terrias_origin_core_radiance";
    public const string CycleGatheredFlame = "Terrias_terrias_cycle_gathered_flame";
    public const string AfterglowOmen = "Terrias_terrias_afterglow_omen";
    public const string Rebirth = "buff_rebirth";
    public const string DuskAfterheatRecoveryTrait = "Terrias_terrias_dusk_afterheat_recovery_trait";
    public const string BossTraitMirrorArray = "Terrias_terrias_boss_trait_mirror_array";
    public const string BossTraitMercilessDaylight = "Terrias_terrias_boss_trait_merciless_daylight";
    public const string BossTraitWhiteRadianceSaint = "Terrias_terrias_boss_trait_white_radiance_saint";
    public const string BossWhiteRadianceCrown = "Terrias_terrias_boss_white_radiance_crown";
    public const string StarStonePouch = "Terrias_terrias_star_stone_pouch";
    public const string RelicStarStonePouch = "Terrias_terrias_relic_star_stone_pouch";
    public const string MiracleClock = "Terrias_terrias_miracle_clock";
    public const string Starlight = "Terrias_terrias_starlight";
    public const string Moonlight = "Terrias_terrias_moonlight";
    public const string StarBlessing = "Terrias_terrias_star_blessing";
    public const string StarScore = "Terrias_terrias_star_score";
    public const string Resonance = "Terrias_terrias_resonance";
    public const string StarStage = "Terrias_terrias_star_stage";
    public const string StarClayBody = "Terrias_terrias_star_clay_body";
    public const string StarClayDollTrait = "Terrias_terrias_star_clay_doll_trait";
    public const string SandroneCatTrait = "Terrias_terrias_sandrone_cat_trait";
    public const string Cripple = "buff_cripple";
    public const string Extraordinary = "buff_extraordinary";
    public const string KeenEdge = "buff_keenedge";
    public const string Resilient = "buff_resilient";
    public const string Evergreen = "buff_evergreen";
    public const string Impregnable = "buff_impregnable";
    public const string Poised = "buff_poised";
    public const string FalseGold = "Terrias_terrias_false_gold";
    public const string DebtDueOne = "Terrias_terrias_debt_due_1";
    public const string DebtDueTwo = "Terrias_terrias_debt_due_2";
    public const string DebtDueThree = "Terrias_terrias_debt_due_3";
    public const string GoldenPotentialZero = "Terrias_terrias_golden_potential_zero";
    public const string GoldenPotentialK = "Terrias_terrias_golden_potential_k";
    public const string GoldenPotentialM = "Terrias_terrias_golden_potential_m";
    public const string GoldenPotentialB = "Terrias_terrias_golden_potential_b";
    public const string VowPower = "buff_VowPower";
    public const string ForgottenCardId = "cursecard_6";
    public const string DreamCardId = "cursecard_11";
    public const string ThoughtDisorderCardId = "cursecard_2";
    public const string PhantomPainCardId = "cursecard_10";
    public const string HiddenIllnessCardId = "cursecard_7";
    public const string DecayCardId = "cursecard_5";
    public const string FearCardId = "cursecard_8";
    public const string EnemyCardSaintWhiteEdict = "Terrias_terrias_enemycard_saint_white_edict";
    public const string AbyssLifeTheftCardId = "Terrias_cursecard_abyss_life_theft";
    public const string AbyssDeficitCardId = "Terrias_cursecard_abyss_deficit";
    public const string AbyssGazeBuffI = "Terrias_terrias_abyss_gaze_i";
    public const string AbyssGazeBuffII = "Terrias_terrias_abyss_gaze_ii";
    public const string AbyssGazeBuffIII = "Terrias_terrias_abyss_gaze_iii";
    public const string AbyssBlessingBuff = "Terrias_terrias_abyss_blessing";
    public const string AbyssLifeTheftEnemyCardId = "Terrias_terrias_enemycard_abyss_life_theft";
    public const string AbyssDeficitEnemyCardId = "Terrias_terrias_enemycard_abyss_deficit";
    public const string EndlessAbyssEvolutionLevelKey = "Terrias_EndlessAbyssEvolutionLevel";
    public const string EndlessAbyssEvolutionTraitRegistryFile = "endless_abyss.evolution_traits.registry.json";
    public const string EndlessAbyssEvolutionTraitPoolId = "endless_abyss.evolution.advanced_traits";

    public const string TempWhiteRadiance = "TerriasTempWhiteRadiance";
    public const string TempWhiteRadianceLockId = "TerriasTempWhiteRadianceLockId";
    public const string TempWhiteRadianceResolved = "TerriasTempWhiteRadianceResolved";

    public const string SolarTriggerCost = "TerriasSolarTriggerCost";

    public const string WunaActive = "TerriasWunaActive";
    public const string PersistentEmber = "TerriasPersistentEmber";
    public const string WunaPersistentEmber = "TerriasWunaPersistentEmber";
    public const string WunaWhiteSunPrayerCardId = "Terrias_wuna_wuna_white_sun_prayer";
    public const string WunaCoronationTokenCardId = "Terrias_wuna_wuna_coronation_token";
    public const string LoneerCareerId = "loneer";
    public const string LoneerActive = "TerriasLoneerActive";
    public const string LoneerMorningPrayerSkillCardId = "Terrias_loneer_loneer_morning_star_prayer";
    public const string StellarOvertureStartShortCardId = "stellar_overture_start";
    public const string StellarOvertureSustainShortCardId = "stellar_overture_sustain";
    public const string StellarOvertureTurnShortCardId = "stellar_overture_turn";
    public const string StellarOvertureCloseShortCardId = "stellar_overture_close";
    public const string StellarOvertureStartCardId = "Terrias_terrias_stellar_overture_start";
    public const string StellarOvertureSustainCardId = "Terrias_terrias_stellar_overture_sustain";
    public const string StellarOvertureTurnCardId = "Terrias_terrias_stellar_overture_turn";
    public const string StellarOvertureCloseCardId = "Terrias_terrias_stellar_overture_close";
    public const string WitchStarScoreCardId = "Terrias_terrias_witch_star_score";
    public const string StarMapCardId = "Terrias_terrias_star_map";
    public const string BlankStarScoreCardId = "Terrias_terrias_blank_star_score";
    public const string MeterRewriteCardId = "Terrias_terrias_meter_rewrite";
    public const string PrewrittenMeasureCardId = "Terrias_terrias_prewritten_measure";
    public const string StarOrbitTransposeCardId = "Terrias_terrias_star_orbit_transpose";
    public const string RestMarkCardId = "Terrias_terrias_rest_mark";
    public const string MorningStarStageCardId = "Terrias_terrias_morning_star_stage";
    public const string StarScoreEchoCardId = "Terrias_terrias_star_score_echo";
    public const string GildedButterflyCardShortId = "gilded_butterfly";
    public const string WagerCardShortId = "wager";
    public const string FortuneThrowCardShortId = "fortune_throw";
    public const string DisplayWealthCardShortId = "display_wealth";
    public const string BlankCheckCardShortId = "blank_check";
    public const string GoldenDreamlandCardShortId = "golden_dreamland";
    public const string GildedButterflyCardId = "Terrias_terrias_gilded_butterfly";
    public const string WagerCardId = "Terrias_terrias_wager";
    public const string FortuneThrowCardId = "Terrias_terrias_fortune_throw";
    public const string DisplayWealthCardId = "Terrias_terrias_display_wealth";
    public const string BlankCheckCardId = "Terrias_terrias_blank_check";
    public const string GoldenDreamlandCardId = "Terrias_terrias_golden_dreamland";
    public const string PolymorphCardShortId = "polymorph";
    public const string PolymorphRoleTemplateShortId = "polymorph_role_template";
    public const string PolymorphRoleTemplateCardId = "Terrias_terrias_polymorph_role_template";
    public const string PolymorphTraitBuffShortId = "polymorph_trait";
    public const string PolymorphTraitBuffId = "Terrias_terrias_polymorph_trait";
    public const string PolymorphRoleCardMarker = "TerriasPolymorphRoleCard";
    public const string PolymorphRoleIdKey = "TerriasPolymorphRoleId";
    public const string PolymorphRoleNameKey = "TerriasPolymorphRoleName";
    public const string PolymorphRoleCardFacePathKey = "TerriasPolymorphRoleCardFacePath";
    public const string PolymorphRoleCropXKey = "TerriasPolymorphRoleCropX";
    public const string PolymorphRoleCropYKey = "TerriasPolymorphRoleCropY";
    public const string PolymorphCropConfigFile = "polymorph.role-crops.json";
    public const string PolymorphSourceResourceCategory = "polymorph.role-source";
    public const string PolymorphGeneratedFaceCategory = "polymorph.generated-card-face";
    public const string PolymorphBaseCardIconPath = "Mods/Terrias/ModResource/Images/Card/MoreDimension/bai_bian";
    public const string PolymorphPlaceholderCardIconPath = PolymorphBaseCardIconPath;
    public const string ProjectionCardShortId = "witch_projection";
    public const string ProjectionRoleTemplateShortId = "projection_role_template";
    public const string ProjectionRoleTemplateCardId = "Terrias_terrias_projection_role_template";
    public const string ProjectionRoleCardMarker = "TerriasProjectionRoleCard";
    public const string ProjectionRoleIdKey = "TerriasProjectionRoleId";
    public const string ProjectionRoleNameKey = "TerriasProjectionRoleName";
    public const string ProjectionRoleCardFacePathKey = "TerriasProjectionRoleCardFacePath";
    public const string ProjectionOwnerStatusIdKey = "TerriasProjectionOwnerStatusId";
    public const string ProjectionStatusIdPrefix = "sp";
    public const string ProjectionBaseCardIconPath = "Mods/Terrias/ModResource/Images/Card/MoreDimension/help_me";
    public const string SpiritBallCardShortId = "spirit_ball";
    public const string SpiritBallCardId = "Terrias_terrias_spirit_ball";
    public const string SpiritCardTemplateShortId = "spirit_card_template";
    public const string SpiritCardTemplateId = "Terrias_terrias_spirit_card_template";
    public const string SpiritWithdrawCardShortId = "spirit_withdraw";
    public const string SpiritWithdrawCardId = "Terrias_terrias_spirit_withdraw";
    public const string SpiritCardMarker = "TerriasSpiritCard";
    public const string SpiritUidKey = "TerriasSpiritUid";
    public const string SpiritEnemyIdKey = "TerriasSpiritEnemyId";
    public const string SpiritVariantIdKey = "TerriasSpiritVariantId";
    public const string SpiritSourceModIdKey = "TerriasSpiritSourceModId";
    public const string SpiritSpeciesIdKey = "TerriasSpiritSpeciesId";
    public const string SpiritGrowthProfileIdKey = "TerriasSpiritGrowthProfileId";
    public const string SpiritDisplayNameKey = "TerriasSpiritDisplayName";
    public const string SpiritDescriptionKey = "TerriasSpiritDescription";
    public const string SpiritAnimationPathKey = "TerriasSpiritAnimationPath";
    public const string SpiritDictPathKey = "TerriasSpiritDictPath";
    public const string SpiritIdlePathKey = "TerriasSpiritIdlePath";
    public const string SpiritProfileVersionKey = "TerriasSpiritProfileVersion";
    public const string SpiritCaptureOriginKey = "TerriasSpiritCaptureOrigin";
    public const string SpiritCapturedAtKey = "TerriasSpiritCapturedAt";
    public const string SpiritExchangeCountKey = "TerriasSpiritExchangeCount";
    public const string SpiritIntentTurnIndexKey = "TerriasSpiritIntentTurnIndex";
    public const string SpiritIntentReadyOnTurnKey = "TerriasSpiritIntentReadyOnTurn";
    public const string SpiritBattleStateKey = "TerriasSpiritBattleState";
    public const string SpiritStatusIdPrefix = "ss";
    public const string SpiritBallIconPath = "Mods/Terrias/ModResource/Images/Card/MoreDimension/spirit_ball";
    public const string SpiritIntentRegistryFile = "spirit.intent.registry.json";
    public const string SpiritCaptureRegistryFile = "spirit.capture.registry.json";
    public const string SpiritGrowthRegistryFile = "spirit.growth.registry.json";
    public const string SpiritTrainingRegistryFile = "spirit.training.registry.json";
    public const string MoreDimensionsCardPackId = "Terrias_terrias_cardpack_more_dimensions";
    public const string DimensionShopConfigSystem = "DimensionShop";
    public const string DimensionShopConfigFile = "settings.json";
    public const string DimensionShopBundledConfigRelativePath = "Config/DimensionShop/default.json";
    public const string DimensionShopMapShortId = "dimension_shop";
    public const string DimensionShopMapId = "Terrias_terrias_dimension_shop";
    public const string DimensionShopNodeId = "TerriasDimensionShop";
    public const string DimensionShopRunInitializedKey = "Terrias_DimensionShop_RunInitialized";
    public const string DimensionShopRunVersionKey = "Terrias_DimensionShop_RunVersion";
    public const string DimensionShopRunSeedKey = "Terrias_DimensionShop_RunSeed";
    public const string DimensionShopCardPoolKey = "Terrias_DimensionShop_CardPool";
    public const string DimensionShopRelicPoolKey = "Terrias_DimensionShop_RelicPool";
    public const string DimensionShopPlayerInitializedKey = "Terrias_DimensionShop_PlayerInitialized";
    public const string DimensionShopPlayerVersionKey = "Terrias_DimensionShop_PlayerVersion";
    public const string DimensionShopCurrentCardKey = "Terrias_DimensionShop_CurrentCard";
    public const string DimensionShopCurrentRelicKey = "Terrias_DimensionShop_CurrentRelic";
    public const string DimensionShopCardBoughtKey = "Terrias_DimensionShop_CardBought";
    public const string DimensionShopCurrentCardsKey = "Terrias_DimensionShop_CurrentCards";
    public const string DimensionShopCurrentRelicsKey = "Terrias_DimensionShop_CurrentRelics";
    public const string DimensionShopCardBoughtSlotsKey = "Terrias_DimensionShop_CardBoughtSlots";
    public const string DimensionShopRelicPurchaseUsedKey = "Terrias_DimensionShop_RelicPurchaseUsed";
    public const string DimensionShopPurchasedRelicIdKey = "Terrias_DimensionShop_PurchasedRelicId";
    public const string DimensionShopRefreshCountKey = "Terrias_DimensionShop_RefreshCount";
    public const string DimensionShopBoughtRelicsKey = "Terrias_DimensionShop_BoughtRelics";
    public const string HeartChangeCardShortId = "heart_change";
    public const string HeartChangeBuffShortId = "heart_change_control";
    public const string HeartChangeBuffId = "Terrias_terrias_heart_change_control";
    public const string HeartChangeCardIconPath = "Mods/Terrias/ModResource/Images/Card/MoreDimension/xin_bian";
    public const string ProjectionActionStaffTap = "staff_tap";
    public const string ProjectionActionShieldBlessing = "shield_blessing";
    public const string ProjectionActionStaffCombo = "staff_combo";
    public const string ProjectionActionMagicInterference = "magic_interference";
    public const string ProjectionActionYouAreEnhanced = "you_are_enhanced";
    public const string ProjectionActionCharge = "charge";
    public const string ProjectionActionHolyHeal = "holy_heal";
    public const string ProjectionActionWait = "system.wait";
    public const string ProjectionActionStaffTapCardId = "Terrias_terrias_enemycard_projection_staff_tap";
    public const string ProjectionActionShieldBlessingCardId = "Terrias_terrias_enemycard_projection_shield_blessing";
    public const string ProjectionActionStaffComboCardId = "Terrias_terrias_enemycard_projection_staff_combo";
    public const string ProjectionActionMagicInterferenceCardId = "Terrias_terrias_enemycard_projection_magic_interference";
    public const string ProjectionActionYouAreEnhancedCardId = "Terrias_terrias_enemycard_projection_you_are_enhanced";
    public const string ProjectionActionChargeCardId = "Terrias_terrias_enemycard_projection_charge";
    public const string ProjectionActionHolyHealCardId = "Terrias_terrias_enemycard_projection_holy_heal";
    public const string ProjectionActionWaitCardId = "Terrias_terrias_enemycard_projection_wait";
    public const string SpiritIntentAdapterCardId = "Terrias_terrias_enemycard_spirit_intent_adapter";
    public const string SpiritIntentSourceCardVar = "TerriasSpiritIntentSourceCardId";

    public static readonly string[] StellarOvertureCardIds =
    {
        StellarOvertureStartCardId,
        StellarOvertureSustainCardId,
        StellarOvertureTurnCardId,
        StellarOvertureCloseCardId
    };

    public static readonly string[] StellarOvertureCardEffectIds =
    {
        StellarOvertureStartCardId,
        StellarOvertureSustainCardId,
        StellarOvertureTurnCardId,
        StellarOvertureCloseCardId,
        StellarOvertureStartShortCardId,
        StellarOvertureSustainShortCardId,
        StellarOvertureTurnShortCardId,
        StellarOvertureCloseShortCardId,
        "*" + StellarOvertureStartShortCardId,
        "*" + StellarOvertureSustainShortCardId,
        "*" + StellarOvertureTurnShortCardId,
        "*" + StellarOvertureCloseShortCardId
    };

    public static readonly string[] SunThemeExplicitCardIds =
    {
        WunaCoronationTokenCardId,
        "Terrias_wuna_card_*wuna_coronation_token",
        "*wuna_coronation_token",
        "wuna_coronation_token"
    };

    public static readonly string[] SunThemeCardIconPathPrefixes =
    {
        SunCardIconPathPrefix
    };

    public const string StarClayDollPartnerId = "Terrias_terrias_star_clay_doll";
    public const string StarClayDollBlessingId = "Terrias_terrias_star_clay_doll_placeholder";
    public const string SandroneCatPartnerId = "Terrias_terrias_sandrone_cat";
    public const string SandroneCatBlessingId = "Terrias_terrias_sandrone_cat_placeholder";
    public const string RuntimeMarkersKey = "TerriasRuntimeMarkers";
    public const string LoneerDerivedMarker = "TerriasLoneerDerived";
    public const string LoneerGuidanceMarker = "TerriasLoneerGuidance";
    public const string LoneerDerivedTag = "衍生牌";
    public const string LoneerGuidanceTag = "指引牌";

    public const string SolarMemoryModeKey = "Terrias_SolarMemoryMode";
    public const string SolarMemorySemanticModeId = "Terrias:solar-memory";
    public const string SolarMemorySelectedPacksKey = "Terrias_SolarMemorySelectedPacks";
    public const string SolarMemoryOriginPointsKey = "Terrias_SolarMemoryOriginPoints";
    public const string SolarMemoryBlessPickCountKey = "Terrias_SolarMemoryBlessPickCount";
    public const string SolarMemoryBlessSelectedIdsKey = "Terrias_SolarMemoryBlessSelectedIds";
    public const string SolarMemoryDeckConfiguredKey = "Terrias_SolarMemoryDeckConfigured";
    public const string SolarMemoryStarterDeckAppliedKey = "Terrias_SolarMemoryStarterDeckApplied";
    public const string SolarMemoryStarterDeckModeKey = "Terrias_SolarMemoryStarterDeckMode";
    public const string StarterDeckOwnerKey = "StarterDeck.Owner";
    public const string StarterDeckScopeKey = "StarterDeck.Scope";
    public const string StarterDeckStateKey = "StarterDeck.State";
    public const string StarterDeckOwnerSolarMemory = "Terrias.SolarMemory";
    public const string StarterDeckOwnerEndlessSea = "Terrias.EndlessSea";
    public const string StarterDeckStatePending = "pending";
    public const string StarterDeckStateApplied = "applied";
    public const string StarterDeckStateOfficial = "official";
    public const string SolarMemoryOriginConfiguredKey = "Terrias_SolarMemoryOriginConfigured";
    public const string SolarMemoryBlessConfiguredKey = "Terrias_SolarMemoryBlessConfigured";
    public const string SolarMemorySetupFinishedKey = "Terrias_SolarMemorySetupFinished";
    public const string SolarMemorySetupCommitTokenKey = "Terrias_SolarMemorySetupCommitToken";
    public const string SolarMemoryPrepStepKey = "Terrias_SolarMemoryPrepStep";
    public const string SolarMemoryPreparedKey = "Terrias_SolarMemoryPrepared";
    public const string SolarMemoryPostPreparationDialogueSeenKey = "Terrias_SolarMemoryPostPreparationDialogueSeen";
    public const string SolarMemoryPostPreparationDialoguePendingKey = "Terrias_SolarMemoryPostPreparationDialoguePending";
    public const string SolarMemorySaintWunaBossPendingKey = "Terrias_SolarMemorySaintWunaBossPending";
    public const string HardSunsetFightCountKey = "Terrias_Hard_SunsetFightCount";
    public const string SolarMemoryPostPreparationDialogueFlowId = "Terrias.SolarMemory.PostPreparationDialogue";
    public const string SolarMemorySecondSunEndingDialogueFlowId = "Terrias.SolarMemory.SecondSunEndingDialogue";
    public const string SolarMemorySaintWunaPreludeDialogueFlowId = "Terrias.SolarMemory.SaintWunaPreludeDialogue";
    public const string SolarMemorySaintWunaEndingDialogueFlowId = "Terrias.SolarMemory.SaintWunaEndingDialogue";
    public const string SolarMemoryPostPreparationDialogueId = "Terrias_terrias_solar_memory_opening_1";
    public const string SolarMemoryPostPreparationCompleteDialogueId = "Terrias_terrias_solar_memory_opening_4";
    public const string SolarMemorySecondSunEndingDialogueId = "Terrias_terrias_solar_memory_second_sun_end_1";
    public const string SolarMemorySecondSunEndingCompleteDialogueId = "Terrias_terrias_solar_memory_second_sun_end_2";
    public const string SolarMemorySaintWunaPreludeDialogueId = "Terrias_terrias_solar_memory_saint_wuna_prelude_1";
    public const string SolarMemorySaintWunaPreludeCompleteDialogueId = "Terrias_terrias_solar_memory_saint_wuna_prelude_6";
    public const string SolarMemorySaintWunaEndingDialogueId = "Terrias_terrias_solar_memory_saint_wuna_end_1";
    public const string SolarMemorySaintWunaEndingCompleteDialogueId = "Terrias_terrias_solar_memory_saint_wuna_end_3";
    public const string SolarMemoryEventId = "Sub_solar_memory_black_sun_after";
    public const string SolarMemoryFullEventId = "Terrias_terrias_Sub_solar_memory_black_sun_after";
    public const string SolarMemoryMapId = "Terrias_terrias_solar_memory_black_sun_after";
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
        "Terrias_terrias_Sub_solar_memory_black_sun_after",
        "Terrias_terrias_Sub_solar_memory_second_sun",
        "Terrias_terrias_Sub_solar_memory_saint_daily",
        "Terrias_terrias_Sub_solar_memory_polluted_light",
        "Terrias_terrias_Sub_solar_memory_grief_struggle",
        "Terrias_terrias_Sub_solar_memory_above_sacred_wheel"
    };

    public static readonly string[] SolarMemoryMapIds =
    {
        "Terrias_terrias_solar_memory_black_sun_after",
        "Terrias_terrias_solar_memory_second_sun",
        "Terrias_terrias_solar_memory_saint_daily",
        "Terrias_terrias_solar_memory_polluted_light",
        "Terrias_terrias_solar_memory_grief_struggle",
        "Terrias_terrias_solar_memory_above_sacred_wheel"
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

    public const string EndlessSeaModeKey = "Terrias_EndlessSeaMode";
    public const string EndlessAbyssSemanticModeId = "Terrias:endless-abyss";
    public const string NativeNormalModeType = "Normal";
    public const string EndlessSeaFloorKey = "Terrias_EndlessSeaFloor";
    public const string EndlessSeaGeneratedFloorKey = "Terrias_EndlessSeaGeneratedFloor";
    public const string EndlessSeaSeedKey = "Terrias_EndlessSeaSeed";
    public const string EndlessSeaFloorPlanKey = "Terrias_EndlessSeaFloorPlan";
    public const string EndlessSeaIntroSeenKey = "Terrias_EndlessSeaIntroSeen";
    public const string EndlessSeaStarterDeckAppliedKey = "Terrias_EndlessSeaStarterDeckApplied";
    public const string EndlessSeaStarterDeckModeKey = "Terrias_EndlessSeaStarterDeckMode";
    public const string EndlessSeaModeType = "TerriasEndlessSea";
    public const string EndlessSeaRunIdKey = "Terrias_EndlessSeaRunId";
    public const string EndlessSeaRunVersionKey = "Terrias_EndlessSeaRunVersion";
    public const string EndlessSeaRunPhaseKey = "Terrias_EndlessSeaRunPhase";
    public const string EndlessSeaRunEndedKey = "Terrias_EndlessSeaRunEnded";
    public const string EndlessSeaRunUpdatedAtKey = "Terrias_EndlessSeaRunUpdatedAt";
    public const string EndlessAbyssEvacuationTokenKey = "Terrias_EndlessAbyssEvacuationToken";
    public const string EndlessAbyssEvacuationReasonKey = "Terrias_EndlessAbyssEvacuationReason";
    public const string EndlessAbyssEvacuationFloorKey = "Terrias_EndlessAbyssEvacuationFloor";
    public const string EndlessAbyssEvacuationDepthKey = "Terrias_EndlessAbyssEvacuationDepth";
    public const string EndlessAbyssEvacuationAtKey = "Terrias_EndlessAbyssEvacuationAt";
    public const string EndlessSeaStarterDeckBaselineMarker = "TerriasEndlessSeaStarterDeckBaseline";
    public const string EndlessSeaAutoBurnoutMarker = "TerriasEndlessSeaAutoBurnout";
    public const string EndlessAbyssConfigFile = "endless_abyss.config.json";
    public const string EndlessAbyssGazeLevelKey = "Terrias_EndlessAbyssGazeLevel";
    public const string EndlessAbyssLedgerKey = "Terrias_EndlessAbyssLedger";
    public const string EndlessAbyssPendingShockKey = "Terrias_EndlessAbyssPendingShock";
    public const string EndlessAbyssTitle = "\u65e0\u5c3d\u4e4b\u6e0a";
    public const string EndlessAbyssStealthModeName = "\u6f5c\u884c\u6a21\u5f0f";
    public const string EndlessAbyssEndlessModeName = "\u65e0\u5c3d\u6a21\u5f0f";
    public const string EndlessAbyssGazeName = "\u6ce8\u89c6\u7b49\u7ea7";
    public const string EndlessAbyssShockName = "\u6df1\u6e0a\u9707\u8361";
    public const string EndlessAbyssOtherDimensionCardPoolId = "milestone.other_dimension.cards";
    public const string EndlessSeaTitle = EndlessAbyssTitle;
    public const string EndlessSeaDescription = "\u65e0\u5c3d\u4e4b\u6218";
    public const string EndlessSeaSubtitle = "";
    public const int EndlessSeaLayerNodeCount = 6;
    public const int EndlessSeaNativeDefaultNodeCount = 2;
    public const int EndlessSeaSelectableNodeCount = 8;
    public const int EndlessSeaStartSlotIndex = 0;
    public const int EndlessSeaBossSlotIndex = 5;
    public const string EndlessSeaNodeFloorKey = "EndlessSeaFloor";
    public const string EndlessSeaNodeSlotKey = "EndlessSeaSlot";
    public const string EndlessSeaNodeKindKey = "EndlessSeaKind";
    public const string EndlessSeaNodeLockedKey = "EndlessSeaLocked";
    public const string EndlessSeaNodePoolSourceKey = "EndlessSeaPoolSource";

    public const string SolarFinaleSavedNamesKey = "Terrias_SolarFinaleSavedNames";
    public const string SolarFinaleBurnedNamesKey = "Terrias_SolarFinaleBurnedNames";
    public const string SolarFinaleNamelessNamesKey = "Terrias_SolarFinaleNamelessNames";
    public const string SolarBossOrbitMirrorMapId = "Terrias_terrias_solar_memory_boss_orbit_mirror_array";
    public const string SolarBossSecondSunMapId = "Terrias_terrias_solar_memory_boss_second_sun_last_day";
    public const string SolarBossSaintWunaMapId = "Terrias_terrias_solar_memory_boss_saint_wuna";
    public const string SolarBossOrbitMirrorShortMapId = "solar_memory_boss_orbit_mirror_array";
    public const string SolarBossSecondSunShortMapId = "solar_memory_boss_second_sun_last_day";
    public const string SolarBossSaintWunaShortMapId = "solar_memory_boss_saint_wuna";
    public const string SolarBossOrbitMirrorLevelId = "Terrias_terrias_level_orbit_mirror_array";
    public const string SolarBossSecondSunLevelId = "Terrias_terrias_level_second_sun_last_day";
    public const string SolarBossSaintWunaLevelId = "Terrias_terrias_level_saint_wuna";
    public const string SolarBossOrbitMirrorEnemyId = "Terrias_terrias_boss_orbit_mirror_array";
    public const string SolarBossSecondSunEnemyId = "Terrias_terrias_boss_second_sun_last_day";
    public const string SolarBossSaintWunaEnemyId = "Terrias_terrias_boss_saint_wuna";
    public const string SolarBossSecondSunMapTexturePath = "Mods/Terrias/ModResource/AnimationLib/SecondSunWeel_e/Map/Map_00.png";
    public const string SolarBossSaintWunaMapTexturePath = "Mods/Terrias/ModResource/AnimationLib/WuNa_e/Map/Map_00.png";
    public const string BlazingCrownCollapseShortCardId = "blazing_crown_collapse";
    public const string BlazingCrownCollapseCardId = "Terrias_terrias_blazing_crown_collapse";
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

        var normalized = TerriasContentIdCompatibility.Canonicalize(id);
        if (string.Equals(normalized, SolarBossOrbitMirrorMapId, System.StringComparison.Ordinal)
            || string.Equals(normalized, SolarBossOrbitMirrorShortMapId, System.StringComparison.Ordinal)
            || string.Equals(normalized, SolarBossSecondSunMapId, System.StringComparison.Ordinal)
            || string.Equals(normalized, SolarBossSecondSunShortMapId, System.StringComparison.Ordinal)
            || string.Equals(normalized, SolarBossSaintWunaMapId, System.StringComparison.Ordinal)
            || string.Equals(normalized, SolarBossSaintWunaShortMapId, System.StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var value in SolarMemoryMapIds)
        {
            if (string.Equals(normalized, value, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var value in SolarMemoryShortMapIds)
        {
            if (string.Equals(normalized, value, System.StringComparison.Ordinal))
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

        var value = TerriasContentIdCompatibility.Canonicalize(id);
        return value.StartsWith("Breaks_solar_memory_", System.StringComparison.Ordinal)
            || value.StartsWith("Sub_solar_memory_", System.StringComparison.Ordinal)
            || value.StartsWith("Terrias_terrias_Sub_solar_memory_", System.StringComparison.Ordinal);
    }
}
