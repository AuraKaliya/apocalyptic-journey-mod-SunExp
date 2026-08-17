using AuraTooling.Shared;

var assertions = 0;
void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
    assertions++;
}

AuraToolExtensionRegistry.ClearForTests();
var observedRevision = 0L;
var goodNotifications = 0;
var stateNotifications = 0;
void ThrowingHandler(long _) => throw new InvalidOperationException("expected test failure");
void GoodHandler(long revision)
{
    observedRevision = revision;
    goodNotifications++;
}
AuraToolExtensionRegistry.Changed += ThrowingHandler;
AuraToolExtensionRegistry.Changed += GoodHandler;
void StateHandler(string qualifiedModuleId, long revision)
{
    if (qualifiedModuleId == "ExampleTools:sample-tool" && revision == 9)
    {
        stateNotifications++;
    }
}
AuraToolExtensionRegistry.StateChanged += StateHandler;

var provider = new TestProvider();
var result = AuraToolExtensionRegistry.Register("ExampleTools", provider);
Assert(result.Success && result.Handle != null && !result.AlreadyRegistered,
    "compatible owner-qualified provider registers");
Assert(observedRevision > 0 && goodNotifications == 1,
    "faulty registry observer does not block later observers");

var snapshot = AuraToolExtensionRegistry.Snapshot();
Assert(snapshot.Count == 1
       && snapshot[0].Descriptor.QualifiedModuleId == "ExampleTools:sample-tool"
       && snapshot[0].Descriptor.CategoryId == AuraToolExtensionProtocol.DefaultCategoryId,
    "registry snapshot exposes normalized stable identity and category");
Assert(snapshot[0].Descriptor.SearchTerms.Count == AuraToolExtensionProtocol.MaximumSearchTerms,
    "registry bounds and deduplicates search metadata");
Assert(AuraToolExtensionRegistry.NotifyStateChanged(
           "ExampleTools",
           "sample-tool",
           provider,
           9)
       && stateNotifications == 1,
    "registered provider can publish a targeted state revision");
Assert(!AuraToolExtensionRegistry.NotifyStateChanged(
        "ExampleTools",
        "sample-tool",
        provider,
        9),
    "duplicate or stale provider state revisions are rejected");
Assert(!AuraToolExtensionRegistry.NotifyStateChanged(
        "ExampleTools",
        "sample-tool",
        new TestProvider(),
        10),
    "unregistered provider cannot publish state for another owner lease");

var duplicate = AuraToolExtensionRegistry.Register("ExampleTools", provider);
Assert(duplicate.Success && duplicate.AlreadyRegistered
       && AuraToolExtensionRegistry.Snapshot().Count == 1,
    "same-provider registration is idempotent");
Assert(!AuraToolExtensionRegistry.Register("OtherOwner", provider).Success,
    "registration owner must match descriptor owner");
Assert(!AuraToolExtensionRegistry.Register("ExampleTools", new TestProvider()).Success,
    "different provider cannot claim an existing identity");
Assert(!AuraToolExtensionRegistry.Register("Bad/Owner", new UnsafeProvider()).Success,
    "unsafe owner and module path characters are rejected");
Assert(!AuraToolExtensionRegistry.Register("LegacyTools", new LegacyProvider()).Success,
    "incompatible protocol versions are rejected");
Assert(!AuraToolExtensionRegistry.Register(
        "UndeclaredFutureTools",
        new UndeclaredFutureProvider()).Success,
    "a future extension must explicitly preserve the current compatibility baseline");
var futureCompatible = AuraToolExtensionRegistry.Register(
    "FutureTools",
    new FutureCompatibleProvider());
Assert(futureCompatible.Success && futureCompatible.Handle != null,
    "a future extension protocol is accepted when it preserves the current compatibility baseline");
futureCompatible.Handle!.Dispose();

duplicate.Handle!.Dispose();
Assert(AuraToolExtensionRegistry.Snapshot().Count == 1,
    "disposing one idempotent registration lease keeps the original registration alive");
result.Handle!.Dispose();
Assert(AuraToolExtensionRegistry.Snapshot().Count == 0
       && goodNotifications == 4,
    "registration handle removes only its live registration and advances revision");

AuraToolExtensionRegistry.Changed -= ThrowingHandler;
AuraToolExtensionRegistry.Changed -= GoodHandler;
AuraToolExtensionRegistry.StateChanged -= StateHandler;
AuraToolExtensionRegistry.ClearForTests();
Console.WriteLine($"AuraTooling.Shared tests passed: {assertions} assertions.");

internal sealed class TestProvider : IAuraToolExtensionProvider
{
    private bool enabled = true;

    public AuraToolExtensionDescriptor Descriptor { get; } = new()
    {
        OwnerModId = "ExampleTools",
        ModuleId = "sample-tool",
        CategoryId = "",
        DisplayName = "示例工具",
        Description = "共享工具扩展协议测试。",
        HasSettingsPage = true,
        SearchTerms = Enumerable.Range(0, 20)
            .Select(index => "term-" + index)
            .Concat(new[] { "term-0" })
            .ToArray()
    };

    public AuraToolExtensionState SnapshotState() => new()
    {
        Revision = 1,
        ConfiguredEnabled = enabled,
        EffectiveEnabled = enabled,
        Availability = enabled
            ? AuraToolExtensionAvailability.Ready
            : AuraToolExtensionAvailability.Disabled,
        Summary = enabled ? "就绪" : "关闭"
    };

    public AuraToolExtensionOperationResult SetEnabled(bool value)
    {
        enabled = value;
        return AuraToolExtensionOperationResult.Ok();
    }

    public void ShowSettings(UnityEngine.Transform parent)
    {
    }
}

internal sealed class UnsafeProvider : IAuraToolExtensionProvider
{
    public AuraToolExtensionDescriptor Descriptor { get; } = new()
    {
        OwnerModId = "Bad/Owner",
        ModuleId = "bad/tool",
        DisplayName = "Bad"
    };

    public AuraToolExtensionState SnapshotState() => new();
    public AuraToolExtensionOperationResult SetEnabled(bool enabled) =>
        AuraToolExtensionOperationResult.Ok();
    public void ShowSettings(UnityEngine.Transform parent)
    {
    }
}

internal sealed class LegacyProvider : IAuraToolExtensionProvider
{
    public AuraToolExtensionDescriptor Descriptor { get; } = new()
    {
        ProtocolVersion = 0,
        OwnerModId = "LegacyTools",
        ModuleId = "legacy",
        DisplayName = "Legacy"
    };

    public AuraToolExtensionState SnapshotState() => new();
    public AuraToolExtensionOperationResult SetEnabled(bool enabled) =>
        AuraToolExtensionOperationResult.Ok();
    public void ShowSettings(UnityEngine.Transform parent)
    {
    }
}

internal sealed class FutureCompatibleProvider : IAuraToolExtensionProvider
{
    public AuraToolExtensionDescriptor Descriptor { get; } = new()
    {
        ProtocolVersion = AuraToolExtensionProtocol.CurrentVersion + 1,
        MinimumSupportedProtocolVersion =
            AuraToolExtensionProtocol.CurrentVersion,
        OwnerModId = "FutureTools",
        ModuleId = "future-compatible",
        DisplayName = "Future Compatible"
    };

    public AuraToolExtensionState SnapshotState() => new();
    public AuraToolExtensionOperationResult SetEnabled(bool enabled) =>
        AuraToolExtensionOperationResult.Ok();
    public void ShowSettings(UnityEngine.Transform parent)
    {
    }
}

internal sealed class UndeclaredFutureProvider : IAuraToolExtensionProvider
{
    public AuraToolExtensionDescriptor Descriptor { get; } = new()
    {
        ProtocolVersion = AuraToolExtensionProtocol.CurrentVersion + 1,
        OwnerModId = "UndeclaredFutureTools",
        ModuleId = "future-undeclared",
        DisplayName = "Future Undeclared"
    };

    public AuraToolExtensionState SnapshotState() => new();
    public AuraToolExtensionOperationResult SetEnabled(bool enabled) =>
        AuraToolExtensionOperationResult.Ok();
    public void ShowSettings(UnityEngine.Transform parent)
    {
    }
}
