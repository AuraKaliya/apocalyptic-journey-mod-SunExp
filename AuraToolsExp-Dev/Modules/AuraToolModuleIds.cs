namespace AuraToolsExp.Dll.Modules;

public static class AuraToolModuleIds
{
    public const string StarterDeck = "gameplay.starter-deck";
    public const string CardRefresh = "gameplay.card-refresh";
    public const string Feast = "gameplay.feast";
    public const string SafeBox = "gameplay.safe-box";
    public const string Skin = "presentation.skin";
    public const string BattleBgm = "presentation.battle-bgm";
    public const string CardUseAudio = "presentation.card-use-audio";
    public const string PixelEmoji = "presentation.pixel-emoji";
    public const string SkillCg = "presentation.skill-cg";
    public const string CardUseCg = "presentation.card-use-cg";
    public const string DamageStatistics = "records.damage-statistics";
    public const string BattleReplay = "records.battle-replay";
    public const string ModSync = "multiplayer.mod-sync";
    public const string AutoBattle = "intelligence.auto-battle";
    public const string FileLogging = "system.file-logging";
    internal const string Diagnostics = "system.card-ui-diagnostics";

    public static readonly string[] Persisted =
    {
        StarterDeck,
        CardRefresh,
        Feast,
        SafeBox,
        Skin,
        BattleBgm,
        CardUseAudio,
        PixelEmoji,
        SkillCg,
        CardUseCg,
        DamageStatistics,
        BattleReplay,
        ModSync,
        AutoBattle,
        FileLogging
    };
}
