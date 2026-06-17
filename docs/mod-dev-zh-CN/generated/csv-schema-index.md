# 生成的 CSV 结构索引

从当前工作区 CSV 文件生成。英文主索引可通过 `tools\Export-ModDevDocs.ps1` 刷新。

## 官方 ModTemplate

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Achievement/achievementsample.csv`

列：

- `Id`
- `ListenScript`
- `Type`
- `Reward`
- `RewardType`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Affection/affectionsample.csv`

列：

- `Id`
- `Character`
- `Reward`
- `InitScript`
- `Target`
- `Belong`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Blessing/blessingsample.csv`

列：

- `Id`
- `Weight`
- `OwnScript`
- `FightScript`
- `Icon`
- `Type`
- `Source`
- `Rarity`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Buff/buffsample.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Card/cardsample.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Career/careersample.csv`

列：

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`
- `Skill2`
- `ChoiceIcon`
- `DollIcon`
- `Character`
- `Avatar`
- `CareerImage`
- `ActionImage1`
- `ActionImage2`
- `Dialogue`
- `EmojiPath`
- `AttackEffect`
- `SkillEffect`
- `HitEffect`
- `DefendEffect`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Coin/coinsample.csv`

列：

- `Id`
- `Type`
- `NodeId`
- `TokenType`
- `TokenWeight`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Destiny/destinysample.csv`

列：

- `Id`
- `Rarity`
- `OwnScript`
- `FightScript`
- `Icon`
- `Type`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Dialogue/dialoguesample.csv`

列：

- `Id`
- `BaseScript`
- `EndScript`
- `Roles`
- `EventName`
- `ChoiceCount`
- `ChoiceScript1`
- `ChoiceScript2`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Effect/effectsample.csv`

列：

- `Id`
- `InitScript`
- `Timepoint`
- `Script`
- `Cost`
- `DesValType`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/EnchTag/enchtagsample.csv`

列：

- `Id`
- `Tag`
- `LoadScript`
- `DrawScript`
- `DropScript`
- `PreUseScript`
- `UseScript`
- `UnloadScript`
- `Rarity`
- `Icon`
- `PackBelong`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Enemy/enemysample.csv`

列：

- `Id`
- `Name`
- `Hp`
- `Attack`
- `Defend`
- `ActionCount`
- `Rarity`
- `InitScript`
- `CardList`
- `AttributeText`
- `Animation`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/EnemyBless/enemyblesssample.csv`

列：

- `Id`
- `Rarity`
- `FightScript`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/EnemyCard/enemycardsample.csv`

列：

- `Id`
- `InitScript`
- `TargetScript`
- `UseScript`
- `BackIcon`
- `Icon`
- `Tag`
- `Effects`
- `Action`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/EventList/eventlistsample.csv`

列：

- `Id`
- `1Script`
- `2Script`
- `3Script`
- `4Script`
- `InitScript`
- `IsHighRisk`
- `EntryScript`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Food/foodsample.csv`

列：

- `Id`
- `Icon`
- `Hp`
- `HPPercent`
- `Rarity`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Hard/hardsample.csv`

列：

- `Id`
- `Belong`
- `Level`
- `UseScript`
- `FightScript`
- `MaxCount`
- `Type`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/HouseDialogue/housedialoguesample.csv`

列：

- `Id`
- `BaseScript`
- `EndScript`
- `Roles`
- `ChoiceCount`
- `ChoiceScript1`
- `ChoiceScript2`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Item/itemsample.csv`

列：

- `Id`
- `Rarity`
- `Type`
- `Icon`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Level/levelsample.csv`

列：

- `Id`
- `EnemyIds`
- `Note`
- `Level`
- `BGM`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Map/mapsample.csv`

列：

- `Id`
- `Type`
- `NodeId`
- `Level`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/OutSideShop/outsideshopsample.csv`

列：

- `Id`
- `PriceType`
- `Price`
- `TimePrice`
- `Icon`
- `Type`
- `Toid`
- `BuyScript`
- `BuyCount`
- `CanClose`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Partner/partnersample.csv`

列：

- `Id`
- `InitScript`
- `ChoiceIcon`
- `Model`
- `Animation`
- `Bless`
- `CareerImage`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/PartnerCard/partnercardsample.csv`

列：

- `Id`
- `InitScript`
- `TargetScript`
- `UseScript`
- `Icon`
- `Tag`
- `Effects`
- `Action`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Relic/relicsample.csv`

列：

- `Id`
- `Rarity`
- `OwnScript`
- `FightScript`
- `Icon`
- `PackBelong`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/RoleData/roledatasample.csv`

列：

- `Id`
- `Avatar`
- `CharacterImage`
- `HouseAvatar`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/SlotCal/slotcalsample.csv`

列：

- `Id`
- `Type`
- `NodeId`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/SlotReward/slotrewardsample.csv`

列：

- `Id`
- `Type`
- `NodeId`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Task/tasksample.csv`

列：

- `Id`
- `Reward`
- `InitScript`
- `Target`
- `Belong`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Data/Tutorial/tutorialsample.csv`

列：

- `Id`
- `EventName`
- `Initial`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Achievement/achievementsample.csv`

列：

- `Id`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Affection/affectionsample.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `NeedDes`
- `NeedDes_zh-Hant`
- `NeedDes_en`
- `NeedDes_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Announcement/announcementsample.csv`

列：

- `Id`
- `Note`
- `Image`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Ver`
- `Date`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Blessing/blessingsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Buff/buffsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Card/cardsample.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/CardPack/cardpacksample.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Icon`
- `Type`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Career/careersample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Title`
- `Title_zh-Hant`
- `Title_en`
- `Title_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Action1`
- `Action1_zh-Hant`
- `Action1_en`
- `Action1_ja`
- `Action2`
- `Action2_zh-Hant`
- `Action2_en`
- `Action2_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`
- `Passive2`
- `Passive2_zh-Hant`
- `Passive2_en`
- `Passive2_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Coin/coinsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Destiny/destinysample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Dialogue/dialoguesample.csv`

列：

- `Id`
- `Text`
- `Text_zh-Hant`
- `Text_en`
- `Text_ja`
- `ChoiceText1`
- `ChoiceText1_zh-Hant`
- `ChoiceText1_en`
- `ChoiceText1_ja`
- `ChoiceText2`
- `ChoiceText2_zh-Hant`
- `ChoiceText2_en`
- `ChoiceText2_ja`
- `Notification`
- `Notification_zh-Hant`
- `Notification_en`
- `Notification_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/EnchTag/enchtagsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Enemy/enemysample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description1`
- `Description1_zh-Hant`
- `Description1_en`
- `Description1_ja`
- `Description2`
- `Description2_zh-Hant`
- `Description2_en`
- `Description2_ja`
- `Level`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/EnemyBless/enemyblesssample.csv`

列：

- `Id`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/EnemyCard/enemycardsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/EventList/eventlistsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `TotalDescribe`
- `TotalDescribe_zh-Hant`
- `TotalDescribe_en`
- `TotalDescribe_ja`
- `1Describe`
- `1Describe_zh-Hant`
- `1Describe_en`
- `1Describe_ja`
- `2Describe`
- `2Describe_zh-Hant`
- `2Describe_en`
- `2Describe_ja`
- `3Describe`
- `3Describe_zh-Hant`
- `3Describe_en`
- `3Describe_ja`
- `4Describe`
- `4Describe_zh-Hant`
- `4Describe_en`
- `4Describe_ja`
- `CompareUse`
- `CompareUse_zh-Hant`
- `CompareUse_en`
- `CompareUse_ja`
- `Column 24`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Hard/hardsample.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `第 1 列`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/HouseDialogue/housedialoguesample.csv`

列：

- `Id`
- `Text`
- `Text_zh-Hant`
- `Text_en`
- `Text_ja`
- `ChoiceText1`
- `ChoiceText1_zh-Hant`
- `ChoiceText1_en`
- `ChoiceText1_ja`
- `ChoiceText2`
- `ChoiceText2_zh-Hant`
- `ChoiceText2_en`
- `ChoiceText2_ja`
- `Type`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/IllustratedBook/illustratedbooksample.csv`

列：

- `Id`
- `Note`
- `Chapter`
- `Chapter_zh-Hant`
- `Chapter_en`
- `Chapter_ja`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tip`
- `Tip_zh-Hant`
- `Tip_en`
- `Tip_ja`
- `Text`
- `Text_zh-Hant`
- `Text_en`
- `Text_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Item/itemsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/KeyWordsDic/keywordsdicsample.csv`

列：

- `Id`
- `Note`
- `Description`
- `Keywords`
- `Keywords_zh-Hant`
- `Keywords_en`
- `Description_zh-Hant`
- `Description_en`
- `Keywords_ja`
- `Description_ja`
- `ShouldShow`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Map/mapsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `AttributeText`
- `AttributeText_zh-Hant`
- `AttributeText_en`
- `AttributeText_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Narration/narrationsample.csv`

列：

- `Id`
- `Time`
- `Text`
- `Text_zh-Hant`
- `Text_en`
- `Text_ja`
- `Note`
- `Path`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/OutSideShop/outsideshopsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tag1`
- `Tag1_zh-Hant`
- `Tag1_en`
- `Tag1_ja`
- `Tag2`
- `Tag2_zh-Hant`
- `Tag2_en`
- `Tag2_ja`
- `Tag3`
- `Tag3_zh-Hant`
- `Tag3_en`
- `Tag3_ja`
- `Tag4`
- `Tag4_zh-Hant`
- `Tag4_en`
- `Tag4_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Partner/partnersample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/PartnerCard/partnercardsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Relic/relicsample.csv`

列：

- `Id`
- `Note`
- `Series`
- `Tag`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/RoleData/roledatasample.csv`

列：

- `Id`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Title`
- `Title_en`
- `Title_zh-Hant`
- `Title_ja`
- `Dia`
- `Dia_en`
- `Dia_zh-Hant`
- `Dia_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/SlotCal/slotcalsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/SlotReward/slotrewardsample.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Task/tasksample.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Des`
- `Des_zh-Hant`
- `Des_en`
- `Des_ja`
- `NeedDes`
- `NeedDes_zh-Hant`
- `NeedDes_en`
- `NeedDes_ja`

### `apocalyptic-journey-mod-tutorial/ModTemplate/Text/Tutorial/tutorialsample.csv`

列：

- `Id`
- `Note`
- `Image`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`

## GoldExp

### `GoldExp/Data/Blessing/goldexp.csv`

列：

- `Id`
- `Weight`
- `OwnScript`
- `FightScript`
- `Icon`
- `Type`
- `Source`
- `Rarity`

### `GoldExp/Data/Buff/goldexp.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `GoldExp/Data/Card/goldexp.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `GoldExp/Data/Card/goldwitch.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `GoldExp/Data/CardPack/goldexp.csv`

列：

- `Id`
- `Type`
- `Icon`

### `GoldExp/Data/Career/goldwitch.csv`

列：

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`
- `Skill2`
- `ChoiceIcon`
- `DollIcon`
- `Character`
- `Avatar`
- `CareerImage`
- `ActionImage1`
- `ActionImage2`
- `Dialogue`
- `EmojiPath`
- `AttackEffect`
- `SkillEffect`
- `HitEffect`
- `DefendEffect`

### `GoldExp/Data/EnchTag/goldexp.csv`

列：

- `Id`
- `Tag`
- `LoadScript`
- `DrawScript`
- `DropScript`
- `PreUseScript`
- `UseScript`
- `UnloadScript`
- `Rarity`
- `Icon`
- `PackBelong`

### `GoldExp/Data/Partner/goldexp.csv`

列：

- `Id`
- `Hp`
- `Attack`
- `Defend`
- `ActionCount`
- `Rarity`
- `InitScript`
- `CardList`
- `ChoiceIcon`
- `Model`
- `Animation`
- `Bless`
- `CareerImage`

### `GoldExp/Data/PartnerCard/goldexp.csv`

列：

- `Id`
- `CardId`

### `GoldExp/Data/Relic/goldexp.csv`

列：

- `Id`
- `Rarity`
- `OwnScript`
- `FightScript`
- `Icon`
- `PackBelong`

### `GoldExp/Data/RoleData/goldwitch.csv`

列：

- `Id`
- `Avatar`
- `CharacterImage`
- `HouseAvatar`

### `GoldExp/Text/Blessing/goldexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`

### `GoldExp/Text/Buff/goldexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `GoldExp/Text/Card/goldexp.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `GoldExp/Text/Card/goldwitch.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `GoldExp/Text/CardPack/goldexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `GoldExp/Text/Career/goldwitch.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Title`
- `Title_zh-Hant`
- `Title_en`
- `Title_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Action1`
- `Action1_zh-Hant`
- `Action1_en`
- `Action1_ja`
- `Action2`
- `Action2_zh-Hant`
- `Action2_en`
- `Action2_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`
- `Passive2`
- `Passive2_zh-Hant`
- `Passive2_en`
- `Passive2_ja`

### `GoldExp/Text/EnchTag/goldexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`

### `GoldExp/Text/KeyWordsDic/goldexp.csv`

列：

- `Id`
- `Note`
- `Description`
- `Keywords`
- `Keywords_zh-Hant`
- `Keywords_en`
- `Description_zh-Hant`
- `Description_en`
- `Keywords_ja`
- `Description_ja`
- `ShouldShow`

### `GoldExp/Text/Partner/goldexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `GoldExp/Text/PartnerCard/goldexp.csv`

列：

- `Id`
- `Name`

### `GoldExp/Text/Relic/goldexp.csv`

列：

- `Id`
- `Note`
- `Series`
- `Tag`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `GoldExp/Text/RoleData/goldwitch.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`

## StarExp

### `StarExp/Data/Buff/star_miracle.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `StarExp/Data/Card/star_miracle.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `StarExp/Data/CardPack/star_miracle.csv`

列：

- `Id`
- `Type`
- `Icon`

### `StarExp/Data/Career/star_miracle.csv`

列：

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`
- `Skill2`
- `ChoiceIcon`
- `DollIcon`
- `Character`
- `Avatar`
- `CareerImage`
- `ActionImage1`
- `ActionImage2`
- `Dialogue`
- `EmojiPath`
- `AttackEffect`
- `SkillEffect`
- `HitEffect`
- `DefendEffect`

### `StarExp/Data/RoleData/star_miracle.csv`

列：

- `Id`
- `Avatar`
- `CharacterImage`
- `HouseAvatar`

### `StarExp/Text/Buff/star_miracle.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `StarExp/Text/Card/star_miracle.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `StarExp/Text/CardPack/star_miracle.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `StarExp/Text/Career/star_miracle.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Title`
- `Title_zh-Hant`
- `Title_en`
- `Title_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Action1`
- `Action1_zh-Hant`
- `Action1_en`
- `Action1_ja`
- `Action2`
- `Action2_zh-Hant`
- `Action2_en`
- `Action2_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`
- `Passive2`
- `Passive2_zh-Hant`
- `Passive2_en`
- `Passive2_ja`

### `StarExp/Text/KeyWordsDic/star_miracle.csv`

列：

- `Id`
- `Note`
- `Description`
- `Keywords`
- `Keywords_zh-Hant`
- `Keywords_en`
- `Description_zh-Hant`
- `Description_en`
- `Keywords_ja`
- `Description_ja`
- `ShouldShow`

### `StarExp/Text/RoleData/star_miracle.csv`

列：

- `Id`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Title`
- `Title_en`
- `Title_zh-Hant`
- `Title_ja`
- `Dia`
- `Dia_en`
- `Dia_zh-Hant`
- `Dia_ja`

## SunExp

### `SunExp/Data/Blessing/sunexp.csv`

列：

- `Id`
- `Weight`
- `OwnScript`
- `FightScript`
- `Icon`
- `Type`
- `Source`
- `Rarity`

### `SunExp/Data/Buff/sunexp.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `SunExp/Data/Buff/wuna.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `SunExp/Data/Card/sunexp.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `SunExp/Data/Card/wuna.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `SunExp/Data/CardPack/sunexp.csv`

列：

- `Id`
- `Type`
- `Icon`

### `SunExp/Data/Career/wuna.csv`

列：

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`
- `Skill2`
- `ChoiceIcon`
- `DollIcon`
- `Character`
- `Avatar`
- `CareerImage`
- `ActionImage1`
- `ActionImage2`
- `Dialogue`
- `EmojiPath`
- `AttackEffect`
- `SkillEffect`
- `HitEffect`
- `DefendEffect`

### `SunExp/Data/EnchTag/sunexp.csv`

列：

- `Id`
- `Tag`
- `LoadScript`
- `DrawScript`
- `DropScript`
- `PreUseScript`
- `UseScript`
- `UnloadScript`
- `Rarity`
- `Icon`
- `PackBelong`

### `SunExp/Data/Enemy/sunexp.csv`

列：

- `Id`
- `Name`
- `Hp`
- `Attack`
- `Defend`
- `ActionCount`
- `Rarity`
- `InitScript`
- `CardList`
- `AttributeText`
- `Animation`

### `SunExp/Data/EnemyCard/sunexp.csv`

列：

- `Id`
- `InitScript`
- `TargetScript`
- `UseScript`
- `BackIcon`
- `Icon`
- `Tag`
- `Effects`
- `Action`

### `SunExp/Data/EventList/sunexp.csv`

列：

- `Id`
- `1Script`
- `2Script`
- `3Script`
- `4Script`
- `InitScript`
- `IsHighRisk`
- `EntryScript`

### `SunExp/Data/Level/sunexp.csv`

列：

- `Id`
- `EnemyIds`
- `Note`
- `Level`
- `BGM`

### `SunExp/Data/Map/sunexp.csv`

列：

- `Id`
- `Type`
- `NodeId`
- `Level`

### `SunExp/Data/Partner/sunexp.csv`

列：

- `Id`
- `Hp`
- `Attack`
- `Defend`
- `ActionCount`
- `Rarity`
- `InitScript`
- `CardList`
- `ChoiceIcon`
- `Model`
- `Animation`
- `Bless`
- `CareerImage`

### `SunExp/Data/PartnerCard/sunexp.csv`

列：

- `Id`
- `InitScript`
- `TargetScript`
- `UseScript`
- `Icon`
- `Tag`
- `Effects`
- `Action`

### `SunExp/Data/Relic/sunexp.csv`

列：

- `Id`
- `Rarity`
- `OwnScript`
- `FightScript`
- `Icon`
- `PackBelong`

### `SunExp/Data/RoleData/wuna.csv`

列：

- `Id`
- `Avatar`
- `CharacterImage`
- `HouseAvatar`

### `SunExp/Text/Blessing/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`

### `SunExp/Text/Buff/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `SunExp/Text/Buff/wuna.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `SunExp/Text/Card/sunexp.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SunExp/Text/Card/wuna.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SunExp/Text/CardPack/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `SunExp/Text/Career/wuna.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Title`
- `Title_zh-Hant`
- `Title_en`
- `Title_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Action1`
- `Action1_zh-Hant`
- `Action1_en`
- `Action1_ja`
- `Action2`
- `Action2_zh-Hant`
- `Action2_en`
- `Action2_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`
- `Passive2`
- `Passive2_zh-Hant`
- `Passive2_en`
- `Passive2_ja`

### `SunExp/Text/EnchTag/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`

### `SunExp/Text/Enemy/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description1`
- `Description1_zh-Hant`
- `Description1_en`
- `Description1_ja`
- `Description2`
- `Description2_zh-Hant`
- `Description2_en`
- `Description2_ja`
- `Level`

### `SunExp/Text/EnemyCard/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SunExp/Text/EventList/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `TotalDescribe`
- `TotalDescribe_zh-Hant`
- `TotalDescribe_en`
- `TotalDescribe_ja`
- `1Describe`
- `1Describe_zh-Hant`
- `1Describe_en`
- `1Describe_ja`
- `2Describe`
- `2Describe_zh-Hant`
- `2Describe_en`
- `2Describe_ja`
- `3Describe`
- `3Describe_zh-Hant`
- `3Describe_en`
- `3Describe_ja`
- `4Describe`
- `4Describe_zh-Hant`
- `4Describe_en`
- `4Describe_ja`
- `CompareUse`
- `CompareUse_zh-Hant`
- `CompareUse_en`
- `CompareUse_ja`
- `Column 24`

### `SunExp/Text/KeyWordsDic/sunexp.csv`

列：

- `Id`
- `Note`
- `Description`
- `Keywords`
- `Keywords_zh-Hant`
- `Keywords_en`
- `Description_zh-Hant`
- `Description_en`
- `Keywords_ja`
- `Description_ja`
- `ShouldShow`

### `SunExp/Text/Map/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `AttributeText`
- `AttributeText_zh-Hant`
- `AttributeText_en`
- `AttributeText_ja`

### `SunExp/Text/Partner/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Description`
- `Name_zh-Hant`
- `Name_en`
- `Description_zh-Hant`
- `Description_en`
- `Name_ja`
- `Description_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`

### `SunExp/Text/PartnerCard/sunexp.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SunExp/Text/Relic/sunexp.csv`

列：

- `Id`
- `Note`
- `Series`
- `Tag`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Tips`
- `Tips_zh-Hant`
- `Tips_en`
- `Tips_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SunExp/Text/RoleData/wuna.csv`

列：

- `Id`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Title`
- `Title_en`
- `Title_zh-Hant`
- `Title_ja`
- `Dia`
- `Dia_en`
- `Dia_zh-Hant`
- `Dia_ja`

## SanGuoShaExp

### `SanGuoShaExp/Data/Buff/shen_zhugeliang.csv`

列：

- `Id`
- `InitScript`
- `ApplyScript`
- `ClearScript`
- `ReducePerTurn`
- `ReducePerAttacked`
- `ReducePerUse`
- `UpperBound`
- `Icon`
- `Type`
- `Rarity`
- `Effects`
- `SoundEffects`
- `Action`
- `CanZero`

### `SanGuoShaExp/Data/Card/shen_zhugeliang.csv`

列：

- `Id`
- `Rarity`
- `Expend`
- `Tag`
- `InitScript`
- `DrawScript`
- `UseScript`
- `DropScript`
- `Icon`
- `Effects`
- `Action`
- `PackBelong`

### `SanGuoShaExp/Data/Career/shen_zhugeliang.csv`

列：

- `Id`
- `SanMax`
- `SkillScript`
- `Animation`
- `Vocal`
- `Skill1`
- `Skill2`
- `ChoiceIcon`
- `DollIcon`
- `Character`
- `Avatar`
- `CareerImage`
- `ActionImage1`
- `ActionImage2`
- `Dialogue`
- `EmojiPath`
- `AttackEffect`
- `SkillEffect`
- `HitEffect`
- `DefendEffect`

### `SanGuoShaExp/Data/RoleData/shen_zhugeliang.csv`

列：

- `Id`
- `Avatar`
- `CharacterImage`
- `HouseAvatar`

### `SanGuoShaExp/Text/Buff/shen_zhugeliang.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_ja`
- `Description_en`

### `SanGuoShaExp/Text/Card/shen_zhugeliang.csv`

列：

- `Id`
- `是否完成`
- `Type`
- `Note`
- `Name`
- `Name_en`
- `Name_zh-Hant`
- `Name_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`

### `SanGuoShaExp/Text/Career/shen_zhugeliang.csv`

列：

- `Id`
- `Note`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`
- `Title`
- `Title_zh-Hant`
- `Title_en`
- `Title_ja`
- `Description`
- `Description_zh-Hant`
- `Description_en`
- `Description_ja`
- `Action1`
- `Action1_zh-Hant`
- `Action1_en`
- `Action1_ja`
- `Action2`
- `Action2_zh-Hant`
- `Action2_en`
- `Action2_ja`
- `Passive1`
- `Passive1_zh-Hant`
- `Passive1_en`
- `Passive1_ja`
- `Passive2`
- `Passive2_zh-Hant`
- `Passive2_en`
- `Passive2_ja`

### `SanGuoShaExp/Text/RoleData/shen_zhugeliang.csv`

列：

- `Id`
- `Name`
- `Name_zh-Hant`
- `Name_en`
- `Name_ja`


