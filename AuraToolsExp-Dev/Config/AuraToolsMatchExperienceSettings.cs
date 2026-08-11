using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsMatchExperienceSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 28;

    [JsonProperty("starterDeck")]
    public StarterDeckSettings StarterDeck { get; set; } = new();

    [JsonProperty("safeBox")]
    public SafeBoxSettings SafeBox { get; set; } = new();

    [JsonProperty("modSync")]
    public ModSyncSettings ModSync { get; set; } = new();

    [JsonProperty("feast")]
    public FeastSettings Feast { get; set; } = new();

    [JsonProperty("damageMeter")]
    public DamageMeterSettings DamageMeter { get; set; } = new();

    [JsonProperty("cardRefresh")]
    public CardRefreshSettings CardRefresh { get; set; } = new();

    [JsonProperty("autoBattle")]
    public AutoBattleSettings AutoBattle { get; set; } = new();

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        SchemaVersion = Math.Max(28, SchemaVersion);
        StarterDeck ??= new StarterDeckSettings();
        SafeBox ??= new SafeBoxSettings();
        ModSync ??= new ModSyncSettings();
        Feast ??= new FeastSettings();
        if (loadedSchemaVersion < 6)
        {
            Feast.Enabled = true;
        }

        DamageMeter ??= new DamageMeterSettings();
        CardRefresh ??= new CardRefreshSettings();
        AutoBattle ??= new AutoBattleSettings();
        StarterDeck.Normalize();
        Feast.Normalize();
        DamageMeter.Normalize();
        AutoBattle.Normalize();
    }
}
