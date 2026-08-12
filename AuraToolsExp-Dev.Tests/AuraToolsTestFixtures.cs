using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.DamageMeter.Capture;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Features.ModSync;
using AuraToolsExp.Dll.Features.SafeBox;
using AuraToolsExp.Dll.Features.StarterDeck;
using AuraToolsExp.Dll.Infrastructure;
using AuraSkin.Shared.Models;
using Newtonsoft.Json;
internal static partial class AuraToolsTestSuite
{
    public static int assertions;
    
    public static DamageLedger NewLedger()
    {
        var ledger = new DamageLedger();
        ledger.StartFight("session", true);
        return ledger;
    }
    
    public static void Apply(
        DamageLedger ledger,
        long sequence,
        string source,
        int hp,
        int shield,
        DamageTeam team,
        string detail)
    {
        Assert(ledger.Apply(Event(ledger, sequence, source, hp, shield, team, detail)),
            "event " + sequence + " accepted");
    }
    
    public static DamageEvent Event(
        DamageLedger ledger,
        long sequence,
        string source,
        int hp,
        int shield,
        DamageTeam team,
        string detail)
    {
        return new DamageEvent
        {
            SessionId = ledger.SessionId,
            ReporterPlayerId = "reporter",
            ReporterSequence = sequence,
            ServerSequence = sequence,
            RoundIndex = Math.Max(1, ledger.CurrentRoundIndex),
            SourceInstanceId = source,
            SourceDisplayName = source,
            SourceTeam = team,
            TargetInstanceId = "target",
            SourceDataId = detail,
            DetailLabel = detail,
            DamageType = "Normal",
            HpDamage = hp,
            ShieldDamage = shield,
            FinalDamage = hp + shield,
            AttributionConfidence = DamageAttributionConfidence.Exact
        };
    }
    
    public static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + name);
        }
    
        assertions++;
    }
}
