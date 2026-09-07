using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Infrastructure;

internal static partial class AuraToolsTestSuite
{
    public static void TestModuleConfigCommitRollback()
    {
        AuraSharedConfigStore.ResetForTests();
        var store = new AuraToolModuleConfigStore();
        var settings = store.Load(AuraToolModuleIds.CardRefresh, new CardRefreshSettings { Enabled = false }, out _);
        Assert(store.Save(AuraToolModuleIds.CardRefresh, settings, out _), "config fixture commits its initial state");
        settings.Enabled = true;
        AuraSharedConfigStore.FailNextWriteForTests = true;
        Assert(!store.Save(AuraToolModuleIds.CardRefresh, settings, out _) && !settings.Enabled,
            "a failed write restores the existing in-memory object before returning failure");
        var read = new AuraToolModuleConfigStore().Load(AuraToolModuleIds.CardRefresh, new CardRefreshSettings(), out _);
        Assert(!read.Enabled, "failed candidate does not leak into committed storage through object aliasing");
        settings.Enabled = true;
        AuraSharedConfigStore.SetForTests(AuraToolsIds.ModId, AuraToolModuleConfigStore.ConfigSystem,
            AuraToolModuleConfigStore.FileName(AuraToolModuleIds.CardRefresh),
            new AuraToolModuleConfigDocument<CardRefreshSettings> { ModuleId = AuraToolModuleIds.CardRefresh,
                Settings = new CardRefreshSettings { Enabled = false } }, 50, 1);
        Assert(!store.Save(AuraToolModuleIds.CardRefresh, settings, out var revision) && revision == 50 && !settings.Enabled,
            "revision conflict preserves the external writer and rolls back the local candidate");

        var aggregate = store.Load(AuraToolModuleIds.SkillCg, new AuraToolsSkillCgSettings { Enabled = true }, out _);
        aggregate.EventCg = store.Load(AuraToolModuleIds.EventCg, aggregate.EventCg, out _);
        var child = aggregate.EventCg;
        aggregate.Enabled = false;
        child.Enabled = false;
        Assert(store.Save(AuraToolModuleIds.EventCg, child, out _), "child module commits independently");
        AuraSharedConfigStore.FailNextWriteForTests = true;
        Assert(!store.Save(AuraToolModuleIds.SkillCg, aggregate, out _) && aggregate.Enabled,
            "child commit does not bless uncommitted parent edits");
        Assert(ReferenceEquals(child, aggregate.EventCg) && !aggregate.EventCg.Enabled,
            "parent rollback preserves the independently committed child and its identity");
        child.Enabled = true;
        AuraSharedConfigStore.FailNextWriteForTests = true;
        Assert(!store.Save(AuraToolModuleIds.EventCg, child, out _) && !aggregate.EventCg.Enabled,
            "child rollback remains connected to the live aggregate after parent rollback");
    }
}
