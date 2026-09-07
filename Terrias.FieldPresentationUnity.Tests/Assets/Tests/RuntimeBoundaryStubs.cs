using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

public static class FieldFixtureRuntime
{
    public static FieldPresentationScene? Scene;
    public static FieldPresentationOptions Options = new();
    public static List<FieldVisualSpec> Specs = FieldVisualSpec.Defaults();
    public static bool Enabled = true;
    public static bool MissingTextures;
    public static readonly Dictionary<string, Texture2D> Textures = new();

    public static Texture2D? Load(string path)
    {
        if (MissingTextures) return null;
        var key = Path.GetFileName(path);
        if (Textures.TryGetValue(key, out var texture)) return texture;
        var file = Path.Combine(Application.dataPath, "Fixtures", key);
        if (!File.Exists(file)) return null;
        texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!texture.LoadImage(File.ReadAllBytes(file))) throw new InvalidDataException(file);
        Textures.Add(key, texture);
        return texture;
    }
}

namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog
    {
        public static void Warn(string message) => Debug.LogWarning(message);
        public static void WarnOnce(string key, string message) => Debug.LogWarning(message);
        public static void Error(string message, Exception ex) => Debug.LogError(message + ": " + ex);
    }
    public static class TerriasPerformanceSettings
    {
        public static bool FieldVisualsEnabled => FieldFixtureRuntime.Enabled;
        public static float FieldVisualGeometryInterval(bool low, bool reduced) => reduced ? 1f / 12 : low ? 1f / 15 : 1f / 30;
        public static int FieldVisualParticleBudget(bool low) => low ? 12 : 36;
    }
    public static class TerriasPerformanceCounters { public static void Record(string name) { } }
    public static class TerriasFrameScheduler
    {
        private static readonly Dictionary<string, (int frame, Action action)> Pending = new();
        public static void RunOnceNextFrame(string key, Action action) => RunOnceAfterFrames(key, 1, action);
        public static void RunOnceAfterFrames(string key, int frames, Action action)
        {
            if (!Pending.ContainsKey(key)) Pending.Add(key, (Time.frameCount + frames, action));
        }
        public static void Flush()
        {
            foreach (var key in Pending.Where(pair => pair.Value.frame <= Time.frameCount).Select(pair => pair.Key).ToArray())
            {
                var action = Pending[key].action;
                Pending.Remove(key);
                action();
            }
        }
    }
}

namespace Terrias.Dll.GameApi
{
    public sealed class FieldBuffSnapshot
    {
        public TerriasFieldId Field;
        public int Stacks;
        public int MaxStacks;
        public bool IsActive => Field != TerriasFieldId.None && Stacks > 0;
    }
    public static class FieldApi
    {
        public static event Action<FieldBuffSnapshot>? Changed;
        private static FieldBuffSnapshot snapshot = new();
        public static FieldBuffSnapshot ActiveFieldSnapshot() => snapshot;
        public static void Set(TerriasFieldId field, int stacks = 1)
        {
            snapshot = new FieldBuffSnapshot { Field = field, Stacks = stacks,
                MaxStacks = field == TerriasFieldId.ScorchingCanopy ? 9 : field == TerriasFieldId.SamsaraGarden ? 5 : 1 };
            Changed?.Invoke(snapshot);
        }
    }
    public static class FieldPresentationSceneApi
    {
        public static bool TryGet(FieldPresentationScene? cached, out FieldPresentationScene? scene)
        {
            scene = FieldFixtureRuntime.Scene;
            return scene != null && scene.IsAlive;
        }
    }
    public static class TerriasResourceCache
    {
        public static T? Load<T>(string path, bool fromMod, string category) where T : UnityEngine.Object
        {
            // Native ResourceLoader.CustomLoad returns PNG textures only for typeof(Texture).
            if (typeof(T) != typeof(Texture)) throw new InvalidOperationException("Native PNG loading requires Texture, not Texture2D.");
            return FieldFixtureRuntime.Load(path) as T;
        }
    }
}

namespace Terrias.Dll.Mechanics
{
    public static class VisualRegistry
    {
        public static FieldPresentationOptions FieldPresentation => FieldFixtureRuntime.Options;
        public static IReadOnlyList<FieldVisualSpec> FieldVisuals() => FieldFixtureRuntime.Specs;
    }
    public sealed class FieldEffectDefinition { public bool HasRoundStartHandler; }
    public static class FieldEffectRegistry
    {
        public static FieldEffectDefinition DefinitionFor(TerriasFieldId field) => new()
            { HasRoundStartHandler = field is TerriasFieldId.ScorchingCanopy or TerriasFieldId.SamsaraGarden };
    }
}

namespace Terrias.Dll.Hooks
{
    public sealed class TerriasBattleLifecycleSubscription
    {
        public Action<object>? BattleInitializing, BattleMaterialized, BattleOpening, BattleReady,
            BattleRestarting, BattleRestarted, OutcomeEntering, BattleSettling, BattleEnded, PlayerTurnEntering;
    }
    public static class TerriasBattleLifecycleRouter
    {
        public static TerriasBattleLifecycleSubscription? Subscription;
        public static void Register(string id, TerriasBattleLifecycleSubscription value) => Subscription = value;
    }
}

namespace Terrias.Dll.Hooks.Ui
{
    public static class TerriasTransientUiRegistry
    {
        public static void Register(string key, Action<string> close) { }
        public static void Unregister(string key) { }
    }
    public static class TerriasUiSafety
    {
        public static void CloseTransient(GameObject root, string source, string prefix)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
    }
}

namespace Terrias.Dll.Hooks.Visual
{
    public static class EffectMaterialFactory
    {
        public static Material CreateMaterial(string effect, string shaderId, string fallback, string prefix) => new(Shader.Find(fallback));
    }
}

public sealed class FieldFramePump : MonoBehaviour
{
    private void Update() => TerriasFrameScheduler.Flush();
}
