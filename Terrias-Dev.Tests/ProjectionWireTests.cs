using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terrias.Dll.Contracts;

internal static partial class Program
{
    private static void TestProjectionWireIdentity()
    {
        var source = new ProjectionCompanionSnapshot { BattleEpoch = 4, RoleId = "fixture", StatusId = "projection.1", CurrentHp = 17, Active = true };
        var wire = new Terrias.Dll.Network.ProjectionCompanionSnapshot(source);
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
        var json = JsonConvert.SerializeObject(wire, settings);
        True(JObject.Parse(json)["$type"]!.Value<string>()!.StartsWith("Terrias.Dll.Network.ProjectionCompanionSnapshot,", StringComparison.Ordinal),
            "projection wire keeps the CLR identity required by the native TypeNameHandling.All serializer");
        var decoded = JsonConvert.DeserializeObject<ProjectionCompanionSnapshot>(json, settings)!;
        True(decoded.RoleId == source.RoleId && decoded.CurrentHp == 17 && decoded.BattleEpoch == 4,
            "native metadata deserializes through the application contract without changing payload values");
        var result = new ProjectionSummonResultSnapshot { Token = "request", RefundCard = true, Accepted = false };
        var resultWire = new Terrias.Dll.Network.ProjectionSummonResultSnapshot(result);
        True(JToken.DeepEquals(JObject.FromObject(result), JObject.FromObject(resultWire)), "summon-result adapter preserves every contract property");
        var turn = new ProjectionSummonTurnSnapshot { Token = "turn", Revision = 8, State = ProjectionSummonTurnTransactionState.Ready };
        var turnWire = new Terrias.Dll.Network.ProjectionSummonTurnSnapshot(turn);
        True(JToken.DeepEquals(JObject.FromObject(turn), JObject.FromObject(turnWire)) && turnWire.ToTransaction().Revision == 8,
            "turn adapter preserves the transaction identity and revision");
    }
}
