namespace SunExp.Dll.Hooks;

public static class SunExpHookTargets
{
    public const string GameEntryStartGame = "GameEntryUI.StartGame";

    public const string FightStartInit = "Fight_Start.Init";
    public const string FightInitInit = "FightInit.Init";
    public const string FightWinInit = "Fight_Win.Init";
    public const string FightWinResetStates = "Fight_Win.ResetStates";
    public const string FightEscapeInit = "Fight_Escape.Init";
    public const string FightEscapeResetStates = "Fight_Escape.ResetStates";
    public const string FightLossInit = "Fight_Loss.Init";
    public const string FightPlayerTurnInit = "Fight_PlayerTurn.Init";
    public const string FightUiCreateCardItem = "FightUI.CreateCardItem";
    public const string FightUiCreateCardItemInternal = "FightUI.CreateCardItemInternal";
    public const string FightUiCallActionAnimation = "FightUI.CallActionAnimation";

    public const string ICardSetCardStyle = "ICard.SetCardStyle";
    public const string CardItemInit = "CardItem.Init";
    public const string AttackCardItemInit = "AttackCardItem.Init";
    public const string CardItemDataUpdate = "CardItem.DataUpdate";
    public const string AttackCardItemDataUpdate = "AttackCardItem.DataUpdate";
    public const string CardItemDrawEffect = "CardItem.DrawEffect";
    public const string CommonCardItemDrawEffect = "CommonCardItem.DrawEffect";
    public const string AttackCardItemDrawEffect = "AttackCardItem.DrawEffect";
    public const string CommonCardItemTrueUse = "CommonCardItem.TrueUse";
    public const string AttackCardItemTrueUse = "AttackCardItem.TrueUse";
    public const string CardChoiceItemInitialize = "CardChoiceItem.Initialize";
    public const string CardChoiceUiSelect = "CardChoiceUI.Select";
    public const string ScriptExecutorGetCardFromDeck = "ScriptExecutor.GetCardFromDeck";
    public const string ScriptExecutorRandomAddCard = "ScriptExecutor.RandomAddCard";

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
    public const string OtherObjDoOneAction = "OtherObj.DoOneAction";
    public const string SkillItemTrueUse = "SkillItem.TrueUse";
}
