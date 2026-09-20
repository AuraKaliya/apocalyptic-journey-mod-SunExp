using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terrias.Dll.GameApi;
using UnityEngine;

internal static class Program
{
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            var file = Path.Combine(root, "Managed", new AssemblyName(request.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try
        {
            Run(root);
            Console.WriteLine("Native map preview regression passed: both third-layer boss folders rejected by the exact native Texture2D probe; original IndexOutOfRange reproduced.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string root)
    {
        foreach (var boss in new[] { "WuNa_e", "SecondSunWeel_e" })
        {
            var directory = Path.Combine(root, "Terrias", "ModResource", "AnimationLib", boss);
            foreach (var state in new[] { "Map", "Idle" })
            {
                var path = Path.Combine(directory, state);
                Require(Directory.GetFiles(path, "*.png").Length > 0, "real boss PNG fixture missing: " + path);
                // Raw: is a native path route. It reaches the same CustomLoadAll
                // used by Mods/ without booting Steam or a Unity scene.
                var frames = ResourceLoader.LoadAll<Texture2D>("Raw:" + path);
                Require(frames != null && frames.Length == 0,
                    "revalidate the adapter: host now accepts MOD Texture2D frames");
                var reproduced = false;
                try { GC.KeepAlive(frames![0]); }
                catch (IndexOutOfRangeException) { reproduced = true; }
                Require(reproduced, "native frame-zero failure was not reproduced");
            }

            SolarMemoryMapPreviewApi.ClearProbeCache();
            Require(!SolarMemoryMapPreviewApi.HasNativePreviewFrames("Raw:" + directory),
                "native probe must not inherit shared Texture-to-Texture2D adaptation");
            Require(!SolarMemoryMapPreviewApi.HasNativePreviewFrames("Raw:" + directory),
                "cached native capability must remain false for unreadable MOD frames");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
