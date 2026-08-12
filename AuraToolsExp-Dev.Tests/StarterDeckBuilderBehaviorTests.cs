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
    public static void TestStarterDeckDeckBuilder()
    {
        var valid = new HashSet<string>(new[] { "a", "b", "c", "d" }, StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(new[] { "b" }, StringComparer.OrdinalIgnoreCase);
        var deck = StarterDeckDeckBuilder.Build(
            new[] { "", "a", "missing", "b" },
            3,
            valid.Contains,
            excluded.Contains,
            new[] { "c", "d" });
        Assert(deck.SequenceEqual(new[] { "a", "c", "d" }),
            "starter deck builder preserves configured order and fills only valid non-excluded cards");
    
        var bounded = StarterDeckDeckBuilder.Build(
            new[] { "a", "c", "d" },
            2,
            valid.Contains,
            excluded.Contains,
            new[] { "d" });
        Assert(bounded.SequenceEqual(new[] { "a", "c" }),
            "starter deck builder enforces deck size before fallback expansion");
    
        var compatible = StarterDeckDeckBuilder.Build(
            new[] { "short_a", "legacy_b" },
            2,
            valid.Contains,
            excluded.Contains,
            Array.Empty<string>(),
            id => id == "short_a" ? "a" : id == "legacy_b" ? "b" : id);
        Assert(compatible.SequenceEqual(new[] { "a" }),
            "starter deck builder validates and applies resolved runtime card ids");
    }
    
    internal sealed class TestCaptureFrame : IDamageCaptureFrame
    {
        public int Frame { get; set; }
    
        public int Value { get; set; }
    
        public void Reset()
        {
            Frame = 0;
            Value = 0;
        }
    }
    
    internal sealed class TestDisposable : IDisposable
    {
        private readonly Action dispose;
    
        internal TestDisposable(Action dispose) => this.dispose = dispose;
    
        public void Dispose() => dispose();
    }
}
