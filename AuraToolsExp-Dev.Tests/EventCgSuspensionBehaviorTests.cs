using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class AuraToolsTestSuite
{
    public static void TestEventCgSuspension()
    {
        var settings = new AuraToolsEventCgSettings();
        Assert(!settings.Enabled && !settings.RequiresDisabledStateCommit,
            "event CG starts off without discarding its scene configuration");
        settings.Enabled = true;
        Assert(!settings.Enabled, "direct assignment cannot enable a suspended event CG module");

        var incoming = JObject.Parse(@"{
            'schemaVersion': 3, 'enabled': true, 'syncRemote': true,
            'scenes': { 'victory.standard': { 'enabled': true,
                'backgroundResource': 'Custom/events/victory.png', 'hold': 4.5, 'motionEnabled': false } }
        }");
        // Same typed decode/normalize/write boundary used by module storage and
        // the preset codec, including callers bypassing the visible switch.
        var imported = incoming.ToObject<AuraToolsEventCgSettings>()!;
        imported.Normalize();
        var scene = imported.GetScene(AuraToolsEventCgSceneIds.VictoryStandard);
        Assert(!imported.Enabled && imported.RequiresDisabledStateCommit
            && imported.SyncRemote && scene.Enabled
            && scene.BackgroundResource == "Custom/events/victory.png" && scene.EffectiveHold == 4.5f && !scene.MotionEnabled,
            "old enabled configs and imported presets become off while preserving scene, resource and presentation preferences");
        var saved = JObject.Parse(JsonConvert.SerializeObject(imported));
        Assert(saved.Value<bool>("enabled") == false && saved["RequiresDisabledStateCommit"] == null,
            "the current writer persists off without leaking the internal migration marker");
        var reopened = saved.ToObject<AuraToolsEventCgSettings>()!;
        Assert(!reopened.Enabled && !reopened.RequiresDisabledStateCommit,
            "a completed enable-flag reset does not require another startup write");

        var aggregate = JsonConvert.DeserializeObject<AuraToolsSkillCgSettings>(
            "{\"enabled\":true,\"cardUseCg\":{\"enabled\":true},\"eventCg\":{\"enabled\":true}}")!;
        aggregate.Normalize();
        Assert(aggregate.Enabled && aggregate.CardUseCg.Enabled && !aggregate.EventCg.Enabled,
            "suspending event CG preserves independently enabled role/skill and card CG");

        AuraSharedConfigStore.ResetForTests();
        var rawDocument = new JObject
        {
            ["schemaVersion"] = 1, ["moduleId"] = AuraToolModuleIds.EventCg, ["settings"] = incoming
        };
        AuraSharedConfigStore.SetForTests(AuraToolsIds.ModId, AuraToolModuleConfigStore.ConfigSystem,
            AuraToolModuleConfigStore.FileName(AuraToolModuleIds.EventCg), rawDocument, 7, 1);
        var store = new AuraToolModuleConfigStore();
        var loaded = store.Load(AuraToolModuleIds.EventCg, new AuraToolsEventCgSettings(), out var migrated);
        Assert(!migrated && loaded.RequiresDisabledStateCommit && !loaded.Enabled,
            "existing per-module documents expose a one-time enable reset independently of legacy aggregate migration");
        Assert(store.Save(AuraToolModuleIds.EventCg, loaded, out var revision) && revision == 8,
            "the existing transactional store commits the disabled state");
        loaded.MarkDisabledStateCommitted();
        Assert(!loaded.RequiresDisabledStateCommit, "successful persistence drains the one-time disable obligation");
        var read = store.Load(AuraToolModuleIds.EventCg, new AuraToolsEventCgSettings(), out _);
        Assert(!read.Enabled && !read.RequiresDisabledStateCommit
            && read.GetScene(AuraToolsEventCgSceneIds.VictoryStandard).BackgroundResource == "Custom/events/victory.png",
            "persisted shutdown retains custom scene values across reopening");
        rawDocument["schemaVersion"] = 99;
        AuraSharedConfigStore.SetForTests(AuraToolsIds.ModId, AuraToolModuleConfigStore.ConfigSystem,
            AuraToolModuleConfigStore.FileName(AuraToolModuleIds.EventCg), rawDocument, 9, 99);
        var newer = store.Load(AuraToolModuleIds.EventCg, new AuraToolsEventCgSettings(), out _);
        Assert(store.IsReadOnly(AuraToolModuleIds.EventCg) && !newer.Enabled
            && !store.Save(AuraToolModuleIds.EventCg, newer, out _),
            "newer-schema data stays read-only while the unavailable feature remains off");
        AuraSharedConfigStore.ResetForTests();
    }
}
