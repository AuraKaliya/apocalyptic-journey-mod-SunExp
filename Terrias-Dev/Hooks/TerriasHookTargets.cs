namespace Terrias.Dll.Hooks;

public static class TerriasHookTargets
{
    public const string GameEntryStartGame = "GameEntryUI.StartGame";

    public const string FightUiCreateCardItem = "FightUI.CreateCardItem";
    public const string FightUiCreateCardItemInternal = "FightUI.CreateCardItemInternal";
    public const string FightUiUpdateCardMsg = "FightUI.UpdateCardMsg";
    public const string FightUiFadeIn = "FightUI.FadeIn";

    public const string ICardSetCardStyle = "ICard.SetCardStyle";
    public const string ICardSetCardMsg = "ICard.SetCardMsg";
    public const string ScriptExecutorRunScript = "ScriptExecutor.RunScript";
    public const string LocalizeExDescription = "LocalizeEx.Description";
    public const string TextTranslatorTranslate = "TextTranslator.Translate";
    public const string CardItemInit = "CardItem.Init";
    public const string AttackCardItemInit = "AttackCardItem.Init";
    public const string CardItemDataUpdate = "CardItem.DataUpdate";
    public const string AttackCardItemDataUpdate = "AttackCardItem.DataUpdate";
    public const string CardItemDrawEffect = "CardItem.DrawEffect";
    public const string CardItemEffectOfBurnCard = "CardItem.EffectOfBurnCard";
    public const string CardItemEffectOfThrowCard = "CardItem.EffectOfThrowCard";
    public const string CommonCardItemDrawEffect = "CommonCardItem.DrawEffect";
    public const string AttackCardItemDrawEffect = "AttackCardItem.DrawEffect";
    public const string FightCardManagerCardTagCheck = "FightCardManager.CardTagCheck";
    public const string CardChoiceItemInitialize = "CardChoiceItem.Initialize";
    public const string CardChoiceUiSelect = "CardChoiceUI.Select";
    public const string ScriptExecutorGetCardFromDeck = "ScriptExecutor.GetCardFromDeck";
    public const string ScriptExecutorRandomAddCard = "ScriptExecutor.RandomAddCard";
    public const string ObjTargetActionInternalExecute = "ObjTargetAction.InternalExecute";

    public const string DictItemInit = "DictItem.Init";
    public const string DictionaryShowItemInit = "DictionaryShowItem.Init";
    public const string DisplayCardInit = "DisplayCard.Init";
    public const string ShowCardInit = "ShowCard.Init";
    public const string SafeBoxItemInit = "SafeBoxItem.Init";
    public const string EnchCardItemInit = "EnchCardItem.Init";
    public const string PackShowItemInit = "PackShowItem.Init";
    public const string ShopItemInit = "ShopItem.Init";
    public const string WarehouseItemInit = "WarehouseItem.Init";

    public const string PlayerInfoAddCard = "PlayerInfo.AddCard";
    public const string PlayerInfoAddCardById = "PlayerInfo.AddCardById";
    public const string PlayerInfoRandomAddCard = "PlayerInfo.RandomAddCard";

    public const string EnemyInit = "Enemy.Init";
    public const string EnemyManagerAddEnemy = "EnemyManager.AddEnemy";
    public const string OtherObjDoOneAction = "OtherObj.DoOneAction";
    public const string OtherObjSetAction = "OtherObj.SetAction";
    public const string SkillItemTryUse = "SkillItem.TryUse";
    public const string ScriptExecutorUpdateSkillTime = "ScriptExecutor.UpdateSkillTime";

    public const string StatusManagerAddBuff = "StatusManager.AddBuff";
    public const string StatusManagerRemoveBuff = "StatusManager.RemoveBuff";
    public const string BuffItemConfigSetLevel = "BuffItemConfig.set_Level";
    public const string BuffBarUiCheckAllBuff = "BuffBarUI.CheckAllBuff";
    public const string StatusManagerHit = "StatusManager.Hit";
    public const string StatusManagerEnemyDead = "StatusManager.EnemyDead";
    public const string StatusManagerSetCurHp = "StatusManager.set_CurHp";
    public const string StatusManagerSetMaxHp = "StatusManager.set_MaxHp";
    public const string StatusManagerSetState = "StatusManager.set_state";
    public const string StatusManagerInitAnimator = "StatusManager.InitAnimator";
    public const string StatusManagerSetSprite = "StatusManager.SetSprite";
}
