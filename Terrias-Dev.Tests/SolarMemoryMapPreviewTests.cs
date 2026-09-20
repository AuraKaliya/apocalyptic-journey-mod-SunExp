using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Terrias.Dll.GameApi;

internal static partial class Program
{
    private static void TestSolarMemoryMapPreviewRestoration()
    {
        var row = new Dictionary<string, string>
        {
            ["Animation"] = "Mods/Terrias/ModResource/AnimationLib/WuNa_e",
            ["Hp"] = "320"
        };
        var original = row["Animation"];
        // Native DataConfig.data is a read-only view of this table row.
        var nativeView = new ReadOnlyDictionary<string, string>(row);
        var preview = new SolarMemoryMapPreviewOverride(row, "saint", "AnimationLib/失心魔女");
        Equal("AnimationLib/失心魔女", nativeView["Animation"], "native map sees the temporary preview");
        Equal("320", nativeView["Hp"], "preview preserves combat attributes");
        True(preview.Restore(), "successful native initialization restores its animation");
        Equal(original, nativeView["Animation"], "battle reads the original custom animation");
        False(preview.Restore(), "duplicate restore is harmless");

        var failed = new SolarMemoryMapPreviewOverride(row, "saint", "AnimationLib/失心魔女");
        try
        {
            throw new InvalidOperationException("native initialization failed");
        }
        catch (InvalidOperationException)
        {
            failed.Restore();
        }
        Equal(original, nativeView["Animation"], "failure cleanup restores the same backing row");
        var next = new SolarMemoryMapPreviewOverride(row, "saint", "AnimationLib/失心魔女");
        False(failed.Restore(), "stale failure cleanup does not affect the next map item");
        Equal("AnimationLib/失心魔女", nativeView["Animation"], "next map item retains its own preview");
        next.Restore();
        Equal(original, nativeView["Animation"], "next map item also restores before combat");

        var replaced = new SolarMemoryMapPreviewOverride(row, "saint", "AnimationLib/失心魔女");
        row["Animation"] = "Mods/AnotherOwner/animation";
        False(replaced.Restore(), "cleanup preserves an external animation change");
        Equal("Mods/AnotherOwner/animation", row["Animation"], "external writer is not overwritten");

        var missing = new Dictionary<string, string>();
        var missingPreview = new SolarMemoryMapPreviewOverride(missing, "missing", "fallback");
        True(missingPreview.Restore(), "missing-field preview can be restored");
        False(missing.ContainsKey("Animation"), "restore preserves original field absence");
    }
}
