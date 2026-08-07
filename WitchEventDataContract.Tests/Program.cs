using System;
using System.Collections.Generic;
using MemoryPack;

internal static class Program
{
    private static int assertions;

    private static void Main()
    {
        TestActionData();
        TestAddBuffData();
        TestBurnData();
        TestCreateData();
        TestDamageData();
        Console.WriteLine("Witch event data rebuild contracts passed: " + assertions + " assertions.");
    }

    private static void TestActionData()
    {
        var value = new ActionData("card-action", DataType.Card, "raw-action", "actor-a");
        var roundTrip = MemoryPackSerializer.Deserialize<ActionData>(MemoryPackSerializer.Serialize(value));
        Equal("card-action", roundTrip.dataId, "ActionData keeps its config id");
        Equal("actor-a", roundTrip.Id, "ActionData keeps its actor id");
        Rebuild(roundTrip, data => (ActionData)data, "ActionData", "card-action", DataType.Card, "raw-action");
    }

    private static void TestAddBuffData()
    {
        var value = new AddBuffData("buff-a", DataType.Buff, "raw-buff", "from-a", "source-a", "target-a");
        var roundTrip = MemoryPackSerializer.Deserialize<AddBuffData>(MemoryPackSerializer.Serialize(value));
        Equal("from-a", roundTrip.fromId, "AddBuffData keeps its from id");
        Equal("source-a", roundTrip.dataFromid, "AddBuffData keeps its data source id");
        Equal("target-a", roundTrip.toId, "AddBuffData keeps its target id");
        Rebuild(roundTrip, data => (AddBuffData)data, "AddBuffData", "buff-a", DataType.Buff, "raw-buff");
    }

    private static void TestBurnData()
    {
        var value = new BurnData("card-burn", DataType.Card, "raw-burn", "actor-b");
        var roundTrip = MemoryPackSerializer.Deserialize<BurnData>(MemoryPackSerializer.Serialize(value));
        Equal("card-burn", roundTrip.dataId, "BurnData keeps its config id");
        Equal("actor-b", roundTrip.Id, "BurnData keeps its actor id");
        Rebuild(roundTrip, data => (BurnData)data, "BurnData", "card-burn", DataType.Card, "raw-burn");
    }

    private static void TestCreateData()
    {
        var value = new CreateData("card-create", DataType.Card, "raw-create", "actor-c");
        var roundTrip = MemoryPackSerializer.Deserialize<CreateData>(MemoryPackSerializer.Serialize(value));
        Equal("card-create", roundTrip.dataId, "CreateData keeps its config id");
        Equal("actor-c", roundTrip.Id, "CreateData keeps its actor id");
        Rebuild(roundTrip, data => (CreateData)data, "CreateData", "card-create", DataType.Card, "raw-create");
    }

    private static void TestDamageData()
    {
        var value = new DamageData("card-damage", DataType.Card, "raw-damage");
        var roundTrip = MemoryPackSerializer.Deserialize<DamageData>(MemoryPackSerializer.Serialize(value));
        Equal("card-damage", roundTrip.dataId, "DamageData keeps its config id");
        Rebuild(roundTrip, data => (DamageData)data, "DamageData", "card-damage", DataType.Card, "raw-damage");
    }

    private static void Rebuild<T>(
        T roundTrip,
        Func<ISourceData, T> cast,
        string name,
        string expectedId,
        DataType expectedType,
        string expectedRawData)
        where T : struct, IRebuildableEventData
    {
        var dataField = typeof(T).GetField("data")
            ?? throw new InvalidOperationException(name + " no longer exposes its rebuilt data field.");
        True(dataField.GetValue(roundTrip) == null, name + " excludes IDataConfig from the MemoryPack payload");

        var builder = new CaptureBuilder();
        var rebuilt = cast(roundTrip.RebuildEventDataConfig(builder));
        Equal(expectedId, builder.Id, name + " forwards its config id to the rebuild builder");
        Equal(expectedType, builder.Type, name + " forwards its config type to the rebuild builder");
        Equal(expectedRawData, builder.RawData, name + " forwards raw config data to the rebuild builder");
        True(ReferenceEquals(dataField.GetValue(rebuilt), builder.Created), name + " stores the authoritative rebuilt IDataConfig");
    }

    private static void True(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        True(EqualityComparer<T>.Default.Equals(expected, actual),
            message + ". Expected <" + expected + ">, got <" + actual + ">.");
    }

    private sealed class CaptureBuilder : IEventDataConfigBuilder
    {
        public DataType Type { get; private set; }

        public string Id { get; private set; } = "";

        public string RawData { get; private set; } = "";

        public IDataConfig? Created { get; private set; }

        public IDataConfig CreateDataConfig(string id, DataType type, string rawData)
        {
            Id = id ?? "";
            Type = type;
            RawData = rawData ?? "";
            Created = new FakeDataConfig(Id, type, RawData);
            return Created;
        }
    }

    private sealed class FakeDataConfig : IDataConfig
    {
        public FakeDataConfig(string id, DataType type, string rawData)
        {
            Type = type;
            data = new Dictionary<string, string> { ["Id"] = id ?? "" };
            Vars = new Dictionary<string, string>
            {
                ["Id"] = id ?? "",
                ["RawData"] = rawData ?? ""
            };
        }

        public IDictionary<string, string> data { get; set; }

        public IDictionary<string, string> Vars { get; }

        public string InstanceID => Vars["Id"];

        public DataType Type { get; }

        public IScriptExecutor scriptExecutor => null!;

        public bool isCompiling => false;
    }
}
