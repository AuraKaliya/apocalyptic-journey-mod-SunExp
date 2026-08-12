using System;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Data.Save;
using Newtonsoft.Json;
using Witch;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Supplies the small map contract required by native fight initialization without
/// entering an adventure, generating a map, or writing to GameSaveManager.
/// </summary>
internal sealed class MatchReplayModeContext : IModeManager
{
    private MatchReplayModeContext(
        string mapMode,
        int level,
        bool hasRecordedDiceMetadata,
        string diceSource)
    {
        MapMode = mapMode;
        Level = level;
        // Native fight-view construction sorts the source deck through NowDice.
        // Replay state is authoritative recorded data, so this cursor must be an
        // isolated view-bootstrap cursor rather than the adventure's recorded cursor.
        NowDice = Dice.Default;
        HasRecordedDiceMetadata = hasRecordedDiceMetadata;
        DiceSource = diceSource;
    }

    internal string MapMode { get; }

    internal string DiceSource { get; }

    internal bool HasRecordedDiceMetadata { get; }

    public bool lazyLoad { get; set; }

    public Dice NowDice { get; set; }

    public int Level { get; set; }

    public MapTree MapTree => GameSaveManager.MapTree;

    internal static MatchReplayModeContext Create(MatchReplayInitialState initialState)
    {
        if (initialState == null)
        {
            throw new ArgumentNullException(nameof(initialState));
        }

        var mapMode = string.IsNullOrWhiteSpace(initialState.MapMode)
            ? "Normal"
            : initialState.MapMode.Trim();
        var hasRecordedDiceMetadata = HasValidRecordedDiceMetadata(initialState.DiceJson);
        return new MatchReplayModeContext(
            mapMode,
            Math.Max(0, initialState.MapLevel),
            hasRecordedDiceMetadata,
            hasRecordedDiceMetadata
                ? "view-bootstrap:recorded-metadata"
                : "view-bootstrap:compatibility");
    }

    public bool CanMultiplayer() => true;

    public void ReadyToChangeMap()
    {
    }

    public void GeneratrMap()
    {
    }

    public void ShowMapSelect()
    {
    }

    public void RpcLoadMap(string type, string id)
    {
    }

    public void MapItemInit(MapSelectUI mapSelectUI)
    {
    }

    public bool WinTheGame() => false;

    public RoleTable InitRoleTable(RoleTable roleTable) => roleTable;

    public void SetReward(BattleRewardsUI battleRewardsUI)
    {
    }

    public void MapUIStart(MapSelectUI mapSelectUI)
    {
    }

    public void CloseMapUI()
    {
    }

    public void SetRewardType(string rewardType)
    {
    }

    public float GetCurrentEnemyPositiveMultiplier() => 1f;

    public int GetCurrentSettlementScoreBonus() => 0;

    public bool EnableWheelBattleForMultiEnemy() => false;

    public void CardCountSet(RoleTable roleTable)
    {
    }

    private static bool HasValidRecordedDiceMetadata(string json)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var restored = JsonConvert.DeserializeObject<Dice>(json);
                if (restored != null)
                {
                    return true;
                }
            }
            catch
            {
                // Imported protocol v2/v3 records and malformed optional context use the
                // deterministic compatibility cursor instead of blocking analysis/playback.
            }
        }

        return false;
    }
}
