using System;
using System.Buffers.Binary;
using System.IO;
using Newtonsoft.Json.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Settle(FieldPresentationState state, float duration = 2f)
    {
        for (var time = 0f; time < duration; time += 1f / 60f) state.Advance(1f / 60f, false);
    }

    private static void Main(string[] args)
    {
        var state = new FieldPresentationState();
        state.Apply(TerriasFieldId.MoonDomain, 1, 1);
        Settle(state);
        Check(state.Weight(TerriasFieldId.MoonDomain) == 1f, "Initial field reaches full visibility.");
        Check(!state.Apply(TerriasFieldId.MoonDomain, 1, 1), "Repeated snapshots must not replay entry.");
        Check(state.Pulse == 0f, "Repeated snapshots must not restart a completed pulse.");

        state.Apply(TerriasFieldId.ScorchingCanopy, 1, 9);
        state.Advance(0.1f, false);
        Check(state.Weight(TerriasFieldId.MoonDomain) > 0f && state.Weight(TerriasFieldId.ScorchingCanopy) > 0f,
            "Replacement crossfades directly between fields.");
        Check(Math.Abs(state.Visibility - 1f) < 0.001f, "Replacement preserves total blend visibility.");
        state.Apply(TerriasFieldId.SamsaraGarden, 5, 5);
        Settle(state);
        Check(state.Weight(TerriasFieldId.ScorchingCanopy) == 0f && state.Weight(TerriasFieldId.MoonDomain) == 0f,
            "Rapid replacement retires every earlier field.");
        Check(state.Weight(TerriasFieldId.SamsaraGarden) == 1f, "Latest field owns the settled view.");

        state.Apply(TerriasFieldId.ScorchingCanopy, 2, 9);
        Settle(state);
        var strength = state.Strength(TerriasFieldId.ScorchingCanopy);
        state.Apply(TerriasFieldId.ScorchingCanopy, 99, 9);
        Check(state.Stacks == 9 && state.Strength(TerriasFieldId.ScorchingCanopy) <= 1f, "Stacks and intensity are capped.");
        Check(state.Strength(TerriasFieldId.ScorchingCanopy) > strength, "Stack increase strengthens the field.");
        Check(state.Weight(TerriasFieldId.ScorchingCanopy) == 1f && state.Pulse < 1f,
            "Stack increase does not restart the background transition.");
        state.Trigger();
        state.Advance(0.016f, true);
        Check(state.Pulse == 0f, "Reduced motion suppresses transient pulses.");

        state.Apply(TerriasFieldId.None, 0, 0);
        Check(state.Visibility > 0f, "Ordinary removal has an exit transition.");
        Settle(state);
        Check(state.Visibility == 0f, "Removed field completely disappears.");
        state.Apply(TerriasFieldId.SamsaraGarden, 1, 5, false);
        Check(state.Visibility == 1f && state.Pulse == 0f, "Hydration can restore a field without entry replay.");
        state.Reset();
        Check(state.Visibility == 0f && state.Field == TerriasFieldId.None, "Battle reset immediately drops all visible state.");
        state.Apply((TerriasFieldId)100, 2, 3);
        state.Advance(float.NaN, false);
        state.Advance(float.PositiveInfinity, false);
        Check(state.Visibility == 0f, "Unknown fields and invalid time cannot corrupt presentation.");

        var options = new FieldPresentationOptions { Intensity = float.NaN, Quality = " invalid " };
        options.Normalize();
        Check(options.Intensity == 0.8f && options.Quality == "standard", "Invalid options normalize deterministically.");
        var spec = new FieldVisualSpec { Id = " MOON_DOMAIN ", PrimaryColor = "#??????", ParticleCount = int.MaxValue,
            BackgroundOpacity = float.PositiveInfinity };
        spec.Normalize();
        Check(spec.Id == "moon_domain" && spec.ParticleCount == 48 && spec.BackgroundOpacity == 1f,
            "Registry values cannot create unbounded rendering work.");
        Check(spec.PrimaryColor == "#A7BFF4", "Malformed colors have a readable fallback.");

        var delivered = 0;
        Action<TerriasFieldId> fail = _ => throw new InvalidOperationException("fixture");
        Action<TerriasFieldId> success = field => { if (field == TerriasFieldId.MoonDomain) delivered++; };
        FieldPresentationSignals.Triggered += fail;
        FieldPresentationSignals.Triggered += success;
        try { FieldPresentationSignals.Trigger(TerriasFieldId.MoonDomain); }
        finally
        {
            FieldPresentationSignals.Triggered -= fail;
            FieldPresentationSignals.Triggered -= success;
        }
        Check(delivered == 1, "A failed visual listener cannot suppress remaining feedback or fail combat.");

        var repo = args.Length > 0 ? args[0] : Path.GetFullPath("..");
        var registry = JObject.Parse(File.ReadAllText(Path.Combine(repo, "Terrias", "visual.registry.json")));
        var entries = registry["fields"]!.ToObject<FieldVisualSpec[]>()!;
        Check(entries.Length == 3, "All three current fields have shipped presentation entries.");
        Span<byte> header = stackalloc byte[24];
        foreach (var expected in FieldVisualSpec.Defaults())
        {
            var entry = Array.Find(entries, item => item.Id == expected.Id);
            Check(entry != null && entry.Enabled, "Field entry is active: " + expected.Id);
            var file = Path.Combine(repo, "Terrias", entry!.BackgroundPath.Substring("Mods/Terrias/".Length));
            using var stream = File.OpenRead(file);
            stream.ReadExactly(header);
            Check(header[0] == 137 && header[1] == 80, "Backdrop is a PNG: " + entry.Id);
            var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            Check(width >= 1280 && height >= 720 && Math.Abs((double)width / height - 16d / 9d) < 0.01,
                "Backdrop has adequate landscape resolution: " + entry.Id);
        }
        Console.WriteLine($"Field presentation behavior and asset checks passed: {assertions}.");
    }
}

namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog { public static void Warn(string message) { } }
}
