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
    public static void TestSkinRemoteSelectionPolicy()
    {
        var snapshot = new SkinSelectionSnapshot
        {
            PlayerId = "remote-player",
            CareerId = "Terrias_loneer_loneer",
            SkinId = "night"
        };
        Assert(SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult
            {
                Success = false,
                DefaultSkin = false
            }),
            "missing remote skin resources retain the latest state for reconciliation");
        Assert(!SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult
            {
                Success = true,
                DefaultSkin = true
            }),
            "explicit default skin clears a previous remote override");
        snapshot.SkinId = "";
        Assert(!SkinRemoteSelectionPolicy.ShouldRetain(snapshot, new SkinSelectionResolveResult()),
            "invalid remote skin identity is not retained");
    }
    
    public static void TestSkillCgPresentationNormalization()
    {
        var settings = new AuraToolsSkillCgSettings
        {
            DefaultPresentation = new SkillCgPresentationSettings
            {
                Mode = "fullscreenFade",
                Fit = "cover",
                FadeIn = 0.2f,
                Hold = 2f,
                FadeOut = 0.3f,
                FocusX = 0.4f,
                FocusY = 0.6f,
                SafeScale = 1.1f
            },
            Roles = new Dictionary<string, SkillCgRoleSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["career_1"] = new()
                {
                    RoleId = "career_1",
                    Rules =
                    {
                        new SkillCgRuleSettings
                        {
                            CardId = "careercard_1",
                            Image = "CG/Roles/1/skill_cg.png"
                        },
                        new SkillCgRuleSettings
                        {
                            CardId = "careercard_2",
                            Image = "CG/AuraToolsExp/Roles/1/skill_cg_2.png",
                            Presentation = new SkillCgPresentationSettings
                            {
                                Mode = "centerFade",
                                Fit = "stretch",
                                Hold = 1.25f,
                                FocusX = 2f,
                                SafeScale = 0.5f
                            }
                        }
                    }
                }
            }
        };
    
        settings.Normalize();
        var rules = settings.Roles["career_1"].Rules;
        Assert(settings.SchemaVersion == 4
               && rules[0].Image == "CG/Roles/1/skill_cg.png"
               && rules[0].EffectivePresentation.Mode == "fullscreenFade"
               && rules[0].EffectivePresentation.Fit == "cover"
               && Math.Abs(rules[0].EffectivePresentation.Hold - 2f) < 0.001f
               && Math.Abs(rules[0].EffectivePresentation.FocusX - 0.4f) < 0.001f
               && Math.Abs(rules[0].EffectivePresentation.FocusY - 0.6f) < 0.001f
               && Math.Abs(rules[0].EffectivePresentation.SafeScale - 1.1f) < 0.001f,
            "skill CG preserves imported paths and presentation inherits global defaults");
        Assert(rules[1].EffectivePresentation.Mode == "centerFade"
               && rules[1].EffectivePresentation.Fit == "stretch"
               && Math.Abs(rules[1].EffectivePresentation.FadeIn - 0.2f) < 0.001f
               && Math.Abs(rules[1].EffectivePresentation.Hold - 1.25f) < 0.001f
               && Math.Abs(rules[1].EffectivePresentation.FocusX - 1f) < 0.001f
               && Math.Abs(rules[1].EffectivePresentation.FocusY - 0.6f) < 0.001f
               && Math.Abs(rules[1].EffectivePresentation.SafeScale - 1f) < 0.001f,
            "skill CG rule presentation overrides selected fields");
    }
    
    public static void TestRpcPayloadBudgetUsesUtf8Bytes()
    {
        var small = new { Kind = "small", Payload = "ok" };
        Assert(AuraToolsRpcPayloadGuard.FitsSoftLimit(
                small,
                AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                out var smallBytes,
                out var smallError)
               && smallBytes > 0
               && smallError == "",
            "small RPC payload fits the soft budget");
    
        var oversized = new { Kind = "oversized", Payload = new string('界', 23000) };
        Assert(AuraToolsRpcPayloadGuard.TryMeasureUtf8Json(oversized, out var oversizedBytes, out var oversizedError)
               && oversizedError == ""
               && oversizedBytes > AuraToolsRpcPayloadGuard.MirrorStringLimitBytes,
            "oversized RPC payload is measured by UTF-8 bytes past Mirror's string limit");
        Assert(!AuraToolsRpcPayloadGuard.FitsSoftLimit(
                oversized,
                AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes,
                out _,
                out _),
            "oversized RPC payload is rejected before Mirror serialization");
        Assert(AuraToolsRpcPayloadGuard.DefaultSoftLimitBytes < AuraToolsRpcPayloadGuard.MirrorStringLimitBytes,
            "soft RPC budget keeps headroom below Mirror's hard string limit");
    }
    
    public static void TestDamageMeterAuthorityPolicy()
    {
        var host = new AuraToolsRpcSender("host", "Host", true, true, "test", true);
        var client = new AuraToolsRpcSender("client", "Client", true, false, "test", true);
        Assert(DamageMeterAuthorityPolicy.RequireHostControl(host, out var hostReject)
               && hostReject == "",
            "host control accepted");
        Assert(!DamageMeterAuthorityPolicy.RequireHostControl(client, out var nonHostReject)
               && nonHostReject == "control issuer is not host",
            "non-host control rejected");
        Assert(!DamageMeterAuthorityPolicy.RequireLobbyMember(AuraToolsRpcSender.Unbound, out var missingReject)
               && missingReject == "missing server sender",
            "missing sender rejected");
    
        var candidate = Event(NewLedger(), 1, "source", 10, 0, DamageTeam.Friendly, "detail");
        candidate.ReporterPlayerId = "spoofed-host";
        Assert(DamageMeterAuthorityPolicy.TryBindReporter(candidate, client, out var bound, out var bindReject)
               && bindReject == "",
            "reporter binding accepted");
        Assert(bound.ReporterPlayerId == "client" && candidate.ReporterPlayerId == "spoofed-host",
            "reporter binding uses server sender and leaves original untouched");
    
        var outsider = new AuraToolsRpcSender("outsider", "", false, false, "test", true);
        Assert(!DamageMeterAuthorityPolicy.TryBindReporter(candidate, outsider, out _, out var outsideReject)
               && outsideReject == "sender not in lobby",
            "non-lobby sender rejected");
    }
    
    public static void TestFeastRoleResourceIdentity()
    {
        var first = AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.FolderName("Mod/Role:A");
        var second = AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.FolderName("Mod\\Role:A");
        Assert(first.StartsWith("Mod_Role_A--", StringComparison.Ordinal)
               && second.StartsWith("Mod_Role_A--", StringComparison.Ordinal)
               && first != second,
            "feast role folders stay readable while hash suffixes prevent sanitized id collisions");
        Assert(AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.CgId("RoleA")
               == AuraToolsExp.Dll.Features.Feast.FeastRoleResourceIdentity.CgId("rolea"),
            "feast generated CG identity is stable across case-insensitive role ids");
    }
    
    public static void TestDamageCaptureFrameWindow()
    {
        var released = 0;
        var window = new DamageFrameWindow<TestCaptureFrame>(2, _ => released++);
        var first = window.Rent(1);
        first.Value = 11;
        window.Add(first);
        var second = window.Rent(2);
        second.Value = 22;
        window.Add(second);
        var third = window.Rent(3);
        third.Value = 33;
        window.Add(third);
    
        Assert(window.Count == 2 && released == 1, "capture window evicts oldest frame at capacity");
        Assert(first.Frame == 0 && first.Value == 0, "evicted capture frame is reset before pooling");
    
        window.PruneOlderThan(8, 4);
        Assert(window.Count == 0 && released == 3, "capture window prunes every expired frame");
    
        var reused = window.Rent(9);
        Assert(reused.Frame == 9 && reused.Value == 0, "capture frame pool returns reset state");
        window.Add(reused);
        window.Clear();
        Assert(window.Count == 0 && released == 4 && reused.Frame == 0,
            "capture window clear releases and resets remaining frames");
    }
    
    public static void TestDamageCaptureMatchingPolicy()
    {
        Assert(DamageCaptureMatchingPolicy.IsHitMatch("target", "source", "target", "source"),
            "damage text pairs with the exact hit frame");
        Assert(DamageCaptureMatchingPolicy.IsHitMatch("target", "source", "target", ""),
            "damage text without a source can pair by target");
        Assert(!DamageCaptureMatchingPolicy.IsHitMatch("target", "source-a", "target", "source-b"),
            "damage text rejects a conflicting source");
        Assert(!DamageCaptureMatchingPolicy.IsHitMatch("target-a", "source", "target-b", "source"),
            "damage text rejects a conflicting target");
        Assert(DamageCaptureMatchingPolicy.Loss(100, 35) == 65
               && DamageCaptureMatchingPolicy.Loss(35, 100) == 0,
            "capture loss is positive-only for damage and healing boundaries");
    }
    
    public static void TestDamageEventFactory()
    {
        var damage = DamageEventFactory.Create(new ResolvedDamageInput
        {
            SourceInstanceId = "  ",
            SourceDisplayName = "",
            TargetInstanceId = " target ",
            SourceDataId = new string('x', DamageMeterProtocol.MaxStringLength + 10),
            DetailLabel = " detail ",
            DamageType = " ",
            HpDamage = -1,
            ShieldDamage = DamageMeterProtocol.MaxDamagePerEvent + 1,
            FinalDamage = 12,
            AttributionConfidence = DamageAttributionConfidence.Exact
        });
    
        Assert(damage.SourceInstanceId == "unknown" && damage.SourceDisplayName == "unknown",
            "damage event factory supplies stable source fallbacks");
        Assert(damage.TargetInstanceId == "target" && damage.DetailLabel == "detail"
               && damage.DamageType == "Unknown",
            "damage event factory trims and normalizes labels");
        Assert(damage.SourceDataId.Length == DamageMeterProtocol.MaxStringLength
               && damage.HpDamage == 0
               && damage.ShieldDamage == DamageMeterProtocol.MaxDamagePerEvent
               && damage.FinalDamage == 12,
            "damage event factory enforces protocol budgets");
    }
    
    public static void TestDamageMeterHookRegistrationSet()
    {
        var registrations = new DamageMeterHookRegistrationSet();
        var disposed = 0;
        Assert(registrations.Register("before:Hit", () => new TestDisposable(() => disposed++)),
            "hook registration accepts a new key");
        Assert(!registrations.Register("before:Hit", () => new TestDisposable(() => disposed += 100)),
            "hook registration is idempotent by route key");
        Assert(registrations.Register("after:Hit", () => new TestDisposable(() => throw new InvalidOperationException("busy"))),
            "hook registration accepts an independent phase");
        var failures = new List<string>();
        Assert(registrations.DisposeAll((key, _) => failures.Add(key)) == 1,
            "failed hook disposal remains registered for retry");
        Assert(disposed == 1 && failures.SequenceEqual(new[] { "after:Hit" }),
            "hook disposal releases healthy handles and reports the failing key");
    }
    
    public static void TestDamageMeterHudPresenter()
    {
        var settings = new DamageMeterSettings();
        settings.Normalize();
        var idle = DamageMeterHudPresenter.Build(
            new DamageLedger(),
            new DamageRunLedger(),
            new DamageHistoryStore(),
            settings,
            "offline");
        Assert(!idle.ShowStats && idle.Height == 250f && idle.VisibleRows.Count == 0,
            "damage HUD presenter builds the idle layout without runtime UI state");
        Assert(idle.Title.Contains("DPT", StringComparison.Ordinal)
               && idle.Footer.Contains("offline", StringComparison.Ordinal),
            "damage HUD presenter keeps idle title and network state");
    
        var ledger = NewLedger();
        ledger.StartRound(1);
        Apply(ledger, 1, "hero", 30, 5, DamageTeam.Friendly, "card");
        Apply(ledger, 2, "ally", 15, 0, DamageTeam.Friendly, "card");
        Apply(ledger, 3, "enemy", 50, 0, DamageTeam.Enemy, "attack");
        settings.DisplayMode = DamageMeterDisplayModes.Bars;
        var active = DamageMeterHudPresenter.Build(
            ledger,
            new DamageRunLedger(),
            new DamageHistoryStore(),
            settings,
            "host");
        Assert(active.ShowStats && active.VisibleRows.Count == 3 && active.BarsMode,
            "damage HUD presenter builds active progress-bar rows for every selected team");
        Assert(active.VisibleRows[0].Stat.InstanceId == "hero"
               && Math.Abs(active.VisibleRows[0].Share - 0.7d) < 0.001d
               && Math.Abs(active.VisibleRows[0].BarFraction - 1d) < 0.001d
               && Math.Abs(active.VisibleRows[1].BarFraction - (15d / 35d)) < 0.001d
               && active.VisibleRows[2].Stat.InstanceId == "enemy"
               && Math.Abs(active.VisibleRows[2].Share - 1d) < 0.001d,
            "bar widths and shares are normalized independently inside each team group");
        Assert(active.Title.Contains("回合 1", StringComparison.Ordinal)
               && active.Footer.Contains("本场战斗合计 100", StringComparison.Ordinal),
            "damage HUD presenter formats active fight summary");

        settings.TeamFilter = DamageMeterTeamFilters.Enemy;
        Assert(DamageMeterHudPresenter.Build(
                   ledger,
                   new DamageRunLedger(),
                   new DamageHistoryStore(),
                   settings,
                   "host").VisibleRows.Single().Stat.Team == DamageTeam.Enemy,
            "team selector filters the live view to enemies");

        var run = new DamageRunLedger();
        run.BeginAdventure("run", "start");
        var aggregateEvent = Event(ledger, 4, "hero", 20, 0, DamageTeam.Friendly, "run-card");
        Assert(run.Apply(aggregateEvent), "HUD test run aggregate accepts damage");
        settings.TeamFilter = DamageMeterTeamFilters.All;
        settings.DisplayScope = DamageMeterDisplayScopes.Adventure;
        var adventure = DamageMeterHudPresenter.Build(
            new DamageLedger(),
            run,
            new DamageHistoryStore(),
            settings,
            "host");
        Assert(adventure.ShowStats
               && adventure.ScopeLabel == "本轮冒险"
               && adventure.VisibleRows.Single().CurrentRound == 0,
            "adventure scope reads the persisted run aggregate rather than the current fight");
    }
}
