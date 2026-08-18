using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsMatchExperienceSettings
{
    private MatchRecordSettings matchRecords = new();
    private bool hasMatchRecords;
    private DamageMeterSettings? legacyDamageMeter;
    private bool hasLegacyDamageMeter;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 31;

    [JsonProperty("starterDeck")]
    public StarterDeckSettings StarterDeck { get; set; } = new();

    [JsonProperty("safeBox")]
    public SafeBoxSettings SafeBox { get; set; } = new();

    [JsonProperty("modSync")]
    public ModSyncSettings ModSync { get; set; } = new();

    [JsonProperty("feast")]
    public FeastSettings Feast { get; set; } = new();

    [JsonProperty("matchRecords", ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public MatchRecordSettings MatchRecords
    {
        get => matchRecords;
        set
        {
            matchRecords = value ?? new MatchRecordSettings();
            hasMatchRecords = true;
        }
    }

    [JsonIgnore]
    public DamageMeterSettings DamageMeter => MatchRecords.Statistics;

    [JsonProperty("damageMeter")]
    private DamageMeterSettings? LegacyDamageMeter
    {
        set
        {
            legacyDamageMeter = value;
            hasLegacyDamageMeter = true;
        }
    }

    [JsonProperty("cardRefresh")]
    public CardRefreshSettings CardRefresh { get; set; } = new();

    [JsonProperty("autoBattle")]
    public AutoBattleSettings AutoBattle { get; set; } = new();

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        if (!hasMatchRecords && hasLegacyDamageMeter)
        {
            matchRecords = MatchRecordSettings.FromLegacy(legacyDamageMeter);
        }

        SchemaVersion = Math.Max(31, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        ModSync ??= new ModSyncSettings();
        Feast ??= new FeastSettings();
        if (loadedSchemaVersion < 6)
        {
            Feast.Enabled = true;
        }

        matchRecords ??= new MatchRecordSettings();
        CardRefresh ??= new CardRefreshSettings();
        AutoBattle ??= new AutoBattleSettings();
        StarterDeck.Normalize();
        Feast.Normalize();
        MatchRecords.Normalize();
        AutoBattle.Normalize();
    }
}
