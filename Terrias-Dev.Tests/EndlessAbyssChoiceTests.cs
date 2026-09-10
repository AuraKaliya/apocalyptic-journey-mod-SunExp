using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

internal static partial class Program
{
    private static void TestAbyssChoicesAndBurnout()
    {
        var gains = new ColumbinaCardGainLedger();
        var firstHandView = new object();
        True(gains.TryRecord(firstHandView), "First successful card materialization counts");
        False(gains.TryRecord(firstHandView), "Duplicate callbacks for one hand view do not count twice");
        True(gains.TryRecord(new object()), "Redrawing the same deck card into a new hand view counts again");
        False(gains.TryRecord(null), "Failed or queued draws without a hand view do not count");
        gains.Clear();
        True(gains.TryRecord(firstHandView), "A new battle clears the materialization ledger");

        var pool = new[] { "relic", "dimension", "purify", "extinction" };
        var state = EndlessAbyssChoiceState.Create("floor:2", pool, _ => 0);
        state.Validate(pool);
        Equal(3, state.Offers.Distinct().Count(), "Three choices are distinct");
        True(state.Toggle("relic", 1), "Select a visible option");
        True(state.Toggle("dimension", 1), "Single choice switches directly to another card");
        Equal("dimension", state.Selected.Single(), "Switching single choice replaces the selected card");
        False(state.Toggle("extinction", 1), "Hidden options cannot be selected");
        False(state.Refresh(1, pool, _ => false, _ => 0), "No eligible replacement leaves state untouched");
        False(state.Refreshed[1], "Failed refresh consumes no allowance");
        True(state.Refresh(1, pool, _ => true, _ => 0), "A slot can refresh for free");
        Equal("extinction", state.Offers[1], "Refresh selects an option outside the current three");
        Equal(0, state.Selected.Count, "Refreshing the selected slot clears its selection");
        False(state.Refresh(1, pool, _ => true, _ => 0), "Each slot refreshes at most once");
        var restored = JsonConvert.DeserializeObject<EndlessAbyssChoiceState>(JsonConvert.SerializeObject(state))!;
        restored.Validate(pool);
        False(restored.Refresh(1, pool, _ => true, _ => 0), "Reopening or loading preserves spent allowance");
        True(restored.Refresh(0, pool, _ => true, _ => 0), "Another slot retains its independent refresh");
        foreach (var id in restored.Offers) restored.Toggle(id, 3);
        True(restored.IsReady(3, _ => true), "High gaze still requires all three selected options");
        False(restored.IsReady(3, id => id != restored.Offers[0]), "A now-ineligible selection prevents confirmation");
        var damaged = JsonConvert.DeserializeObject<EndlessAbyssChoiceState>(JsonConvert.SerializeObject(restored))!;
        damaged.Offers[1] = damaged.Offers[0];
        var rejected = false;
        try { damaged.Validate(pool); } catch (InvalidDataException) { rejected = true; }
        True(rejected, "Damaged saved options are rejected without resetting refreshes");

        var reward = new DataConfig(new Dictionary<string, string> { ["Id"] = "reward", ["Tag"] = "Retain" });
        True(EndlessSeaBurnoutPolicy.AttachReward(reward), "A newly owned reward gains Burnout immediately");
        True(EndlessSeaBurnoutPolicy.HasBurnout(reward), "The inventory instance exposes Burnout before battle");
        Equal("Retain", reward.data["Tag"], "Reward attachments never rewrite base definitions");
        True(EndlessSeaBurnoutPolicy.Purify(reward), "A reward card can be purified");
        Equal("Retain", reward.Vars["Tag"], "Purification preserves other tags");
        False(EndlessSeaBurnoutPolicy.AttachReward(reward), "Inventory transfer or later normalization cannot reattach Burnout");
        False(EndlessSeaBurnoutPolicy.HasBurnout(reward), "Purified reward stays out of candidates");
        False(EndlessSeaBurnoutPolicy.Purify(reward), "The same instance cannot be purified twice");

        var native = new DataConfig(new Dictionary<string, string> { ["Id"] = "native", ["Tag"] = "Burnout" });
        True(EndlessSeaBurnoutPolicy.Purify(native), "Native Burnout can also be removed");
        Equal("", native.Vars["Tag"], "Removing the last tag stores an explicit empty override");
        False(EndlessSeaBurnoutPolicy.HasBurnout(native), "An empty override does not resurrect base Burnout");
        CardMutationService.AddNativeTags(native, "Retain");
        Equal("Retain", native.Vars["Tag"], "Adding an unrelated tag after purification does not restore Burnout");

        var starter = new DataConfig(new Dictionary<string, string> { ["Id"] = "starter", ["Tag"] = "" });
        EndlessSeaBurnoutPolicy.MarkStarter(starter);
        False(EndlessSeaBurnoutPolicy.AttachReward(starter), "Equipping an initial deck card does not apply reward Burnout");
        var copiedStarter = new DataConfig(starter.data, new Dictionary<string, string>(starter.Vars));
        True(EndlessSeaBurnoutPolicy.AttachReward(copiedStarter), "A newly awarded copy does not inherit another instance's starter exemption");
        var copiedPurified = new DataConfig(reward.data, new Dictionary<string, string>(reward.Vars));
        True(EndlessSeaBurnoutPolicy.AttachReward(copiedPurified), "A new copy does not inherit another instance's purification");
        Equal(2, EndlessSeaBurnoutPolicy.DistinctInstances(new IDataConfig[] { reward, reward, copiedPurified }).Count,
            "Duplicate references disappear while distinct copies of one card remain");
        True(EndlessSeaBurnoutPolicy.RestorePurification(copiedPurified), "An old exact-instance receipt restores purification");
        False(EndlessSeaBurnoutPolicy.AttachReward(copiedPurified), "Migrated purification remains exempt");
        var failingReward = new DataConfig(new Dictionary<string, string> { ["Id"] = "failure", ["Tag"] = "Burnout" });
        var commitFailed = false;
        try
        {
            EndlessAbyssCardRewardTransaction.Apply(failingReward, () => EndlessSeaBurnoutPolicy.Purify(failingReward),
                () => throw new IOException("injected receipt write failure"));
        }
        catch (IOException) { commitFailed = true; }
        True(commitFailed, "A receipt write failure is surfaced");
        True(EndlessSeaBurnoutPolicy.HasBurnout(failingReward), "A failed commit restores the selected card");
        False(EndlessSeaBurnoutPolicy.IsPurified(failingReward), "A failed commit does not leave an unclaimed purification");
        True(EndlessAbyssCardRewardTransaction.Apply(failingReward, () => EndlessSeaBurnoutPolicy.Purify(failingReward), () => { }),
            "The same card can be retried after commit recovery");
        // Drain the real invalidation service before the next independently
        // measured test resets the scheduler's diagnostics.
        for (var i = 0; i < 100; i++)
        {
            var pending = AuraSharedFrameScheduler.TakePendingRequest();
            if (pending == null) break;
            pending.ExecuteSlice?.Invoke(new AuraSharedFrameSliceContext());
        }
    }
}
