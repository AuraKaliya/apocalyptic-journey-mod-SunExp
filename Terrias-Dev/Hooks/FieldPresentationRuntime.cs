using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;

namespace Terrias.Dll.Hooks;

public static class FieldPresentationRuntime
{
    private static bool initialized;
    private static bool accepting;
    private static TerriasFieldId pendingPulse;
    private static int retryCount;
    private static int generation;
    private static FieldPresentationView? view;
    private static FieldPresentationScene? scene;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        FieldApi.Changed += _ => RequestRefresh();
        FieldPresentationSignals.Triggered += OnTrigger;
        TerriasBattleLifecycleRouter.Register("FieldPresentation", new TerriasBattleLifecycleSubscription
        {
            BattleInitializing = _ => BeginBattle(),
            BattleMaterialized = _ => RequestRefresh(),
            BattleOpening = _ => RequestRefresh(),
            BattleReady = _ => RequestRefresh(),
            BattleRestarting = _ => Stop("BattleRestarting"),
            BattleRestarted = _ => BeginBattle(),
            OutcomeEntering = _ => Stop("OutcomeEntering"),
            BattleSettling = _ => Stop("BattleSettling"),
            BattleEnded = _ => Stop("BattleEnded"),
            PlayerTurnEntering = _ =>
            {
                var snapshot = FieldApi.ActiveFieldSnapshot();
                if (snapshot.IsActive && FieldEffectRegistry.DefinitionFor(snapshot.Field)?.HasRoundStartHandler == true)
                {
                    pendingPulse = snapshot.Field;
                    RequestRefresh();
                }
            }
        });
    }

    private static void BeginBattle()
    {
        Stop("BattleInitializing");
        accepting = true;
        RequestRefresh();
    }

    private static void OnTrigger(TerriasFieldId field)
    {
        if (!accepting || FieldApi.ActiveFieldSnapshot().Field != field) return;
        pendingPulse = field;
        RequestRefresh();
    }

    private static void RequestRefresh()
    {
        if (!accepting) return;
        var current = generation;
        TerriasFrameScheduler.RunOnceNextFrame("FieldPresentation.Refresh." + current, () =>
        {
            if (current == generation && accepting) Refresh();
        });
    }

    private static void Refresh()
    {
        try
        {
            var snapshot = FieldApi.ActiveFieldSnapshot();
            if (view == null && !snapshot.IsActive) return;
            if (view == null)
            {
                if (!FieldPresentationSceneApi.TryGet(scene, out var next))
                {
                    if (++retryCount <= 30)
                    {
                        var current = generation;
                        TerriasFrameScheduler.RunOnceAfterFrames("FieldPresentation.WaitForScene." + current, 2, () =>
                        {
                            if (current == generation && accepting) Refresh();
                        });
                    }
                    return;
                }
                scene = next;
                var root = new GameObject("Terrias_FieldPresentation", typeof(RectTransform));
                var rect = root.GetComponent<RectTransform>();
                rect.SetParent(scene!.FightUi, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                view = root.AddComponent<FieldPresentationView>();
                view.Configure(CurrentScene, VisualRegistry.FieldPresentation, VisualRegistry.FieldVisuals(),
                    () => EffectMaterialFactory.CreateMaterial("field.environment", "field.environment.unlit", "Sprites/Default", "[FieldPresentation]"),
                    path => TerriasResourceCache.Load<Texture>(path, true, "field-presentation") as Texture2D);
                TerriasTransientUiRegistry.Register("FieldPresentation", Stop);
            }
            retryCount = 0;
            view.SetField(snapshot.Field, snapshot.Stacks, snapshot.MaxStacks);
            if (pendingPulse == snapshot.Field && pendingPulse != TerriasFieldId.None) view.Pulse();
            pendingPulse = TerriasFieldId.None;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Field presentation refresh failed", ex);
            CloseView("RefreshFailed");
        }
    }

    private static FieldPresentationScene? CurrentScene()
    {
        if (!accepting || !TerriasPerformanceSettings.FieldVisualsEnabled
            || !FieldPresentationSceneApi.TryGet(scene, out var next)) return null;
        scene = next;
        return scene;
    }

    private static void Stop(string source)
    {
        accepting = false;
        generation++;
        retryCount = 0;
        pendingPulse = TerriasFieldId.None;
        CloseView(source);
    }

    private static void CloseView(string source)
    {
        if (view != null) TerriasUiSafety.CloseTransient(view.gameObject, source, "[FieldPresentation]");
        view = null;
        scene = null;
        TerriasTransientUiRegistry.Unregister("FieldPresentation");
    }
}
