namespace AuraToolsExp.Dll.Modules;

public static class AuraToolModuleIds
{
    public const string StarterDeck = "gameplay.starter-deck";
    public const string CardRefresh = "gameplay.card-refresh";
    public const string Feast = "gameplay.feast";
    internal const string FeastCg = "presentation.feast-cg";
    public const string SafeBox = "gameplay.safe-box";
    public const string Skin = "presentation.skin";
    public const string BattleBgm = "presentation.battle-bgm";
    public const string CardUseAudio = "presentation.card-use-audio";
    public const string Voice = "presentation.character-voice";
    public const string PixelEmoji = "presentation.pixel-emoji";
    public const string SkillCg = "presentation.skill-cg";
    public const string CardUseCg = "presentation.card-use-cg";
    public const string EventCg = "presentation.event-cg";
    public const string CardVisual = "presentation.card-visual";
    public const string DamageStatistics = "records.damage-statistics";
    public const string BattleReplay = "records.battle-replay";
    public const string AdventureArchive = "records.adventure-archive";
    public const string ModSync = "multiplayer.mod-sync";
    public const string LobbyStatus = "multiplayer.lobby-status";
    public const string AutoBattle = "intelligence.auto-battle";
    public const string StrategyLab = "intelligence.strategy-model-lab";
    public const string FileLogging = "system.file-logging";
    public const string PresetLibrary = "system.preset-library";
    public const string ModHealth = "system.mod-health";
    internal const string Diagnostics = "system.card-ui-diagnostics";

    public static readonly string[] Persisted =
    {
        StarterDeck,
        CardRefresh,
        Feast,
        FeastCg,
        SafeBox,
        Skin,
        BattleBgm,
        CardUseAudio,
        Voice,
        PixelEmoji,
        SkillCg,
        CardUseCg,
        EventCg,
        CardVisual,
        DamageStatistics,
        BattleReplay,
        AdventureArchive,
        ModSync,
        LobbyStatus,
        AutoBattle,
        FileLogging,
        PresetLibrary,
        ModHealth
    };
}
