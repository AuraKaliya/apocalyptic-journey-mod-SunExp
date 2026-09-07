using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.GameApi;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;

/// <summary>
/// Owns an isolated native-prefab replay world. Visible state is projected into
/// sanitized HUD/combatant views; observed presentation tracks supply temporal motion.
/// Gameplay managers, scripts, rewards, hooks, and networking are never initialized.
/// </summary>
internal sealed class ReplayBattleSceneRuntimeV17 : IDisposable
{
    private readonly GameObject root;
    private readonly ReplayAssetCacheV17 assets;
    private readonly ReplayRenderHostV17 renderHost;
    private readonly ReplayVisibleBattleViewV17 view;
    private readonly ReplayEffectRuntimeV17 effects;
    private readonly ReplayAudioRuntimeV17 audio;
    private bool hudVisible;
    private bool visualDirty = true;
    private bool disposed;
    private long logicalTicks;

    internal ReplayBattleSceneRuntimeV17(ReplayDocumentV17 document, bool includeHud)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        var extensionIntents = new ReplayExtensionIntentVisualsV17(
            document.PresentationEvents, ReplayIntentVisualCompatibilityApi.Exists);
        root = new GameObject("AuraToolsReplayBattleWorldV17");
        Object.DontDestroyOnLoad(root);
        ReplayRenderHostV17? createdHost = null;
        ReplayAssetCacheV17? createdAssets = null;
        ReplayVisibleBattleViewV17? createdView = null;
        try
        {
            createdAssets = new ReplayAssetCacheV17(document.Assets);
            assets = createdAssets;
            createdHost = new ReplayRenderHostV17(root.transform, document.Presentation.Scene);
            renderHost = createdHost;
            createdView = new ReplayVisibleBattleViewV17(
                document.Presentation,
                renderHost.CaptureRoot,
                renderHost.CaptureCanvas.transform,
                renderHost.Camera,
                includeHud,
                extensionIntents);
            view = createdView;
            effects = new ReplayEffectRuntimeV17(
                renderHost.CaptureRoot,
                assets,
                document.Presentation.Effects.ToDictionary(item => item.DescriptorId, StringComparer.Ordinal));
            audio = new ReplayAudioRuntimeV17(renderHost.CaptureRoot, assets);
            hudVisible = includeHud;
        }
        catch
        {
            try { createdView?.Dispose(); }
            finally
            {
                try { createdHost?.Dispose(); }
                finally
                {
                    try { createdAssets?.Dispose(); }
                    finally { Object.Destroy(root); }
                }
            }
            throw;
        }
    }

    internal bool HudVisible => hudVisible;
    internal bool IsPreflighted => renderHost.IsPreflighted;
    internal bool IsActivationReady => renderHost.IsActivationReady;

    internal void SetHudVisible(bool visible)
    {
        if (hudVisible == visible) return;
        hudVisible = visible;
        view.SetHudVisible(visible);
        visualDirty = true;
    }

    internal void PreflightRender()
    {
        renderHost.PreflightRender();
        visualDirty = false;
    }

    internal void ConfirmFrameBarrier() => renderHost.ConfirmFrameBarrier();

    internal void ActivateDisplay(bool visible) => renderHost.ActivateDisplay(visible);

    internal void RenderInteractive()
    {
        if (renderHost.RenderInteractive(visualDirty)) visualDirty = false;
    }

    internal ReplayRenderExportLeaseV17 AcquireExportTarget(RenderTexture target) =>
        renderHost.AcquireExportTarget(target);

    internal void SetPlaybackSpeed(float speed) => audio.SetTransportSpeed(speed);

    internal void SetPaused(bool paused) => audio.SetPaused(paused);

    internal void Restore(ReplayVisibleStateV17 state, ReplayPresentationCheckpointV17? checkpoint)
    {
        effects.Clear();
        audio.StopAll();
        view.Restore(state, checkpoint);
        visualDirty = true;
    }

    internal void ApplyState(ReplayVisibleStateV17 state)
    {
        view.ApplyState(state);
        visualDirty = true;
    }

    internal void RestoreTimedPresentationsAt(
        IEnumerable<ReplayJournalEventV17> presentationEvents,
        long targetTicks,
        bool includeAudio)
    {
        effects.Clear();
        audio.StopAll();
        view.ClearTransientPresentation();
        var ordered = (presentationEvents ?? Array.Empty<ReplayJournalEventV17>())
                     .Where(item => ReplayPresentationTimingV17.EffectiveTimeTicks(item) <= targetTicks)
                     .OrderBy(ReplayPresentationTimingV17.EffectiveTimeTicks)
                     .ThenBy(item => item.Sequence)
                     .ToList();
        var cameraState = ordered.LastOrDefault(item => item.Presentation?.HasCameraState == true);
        if (cameraState?.Presentation != null) view.ApplyCamera(cameraState.Presentation);
        foreach (var value in ordered)
        {
            var message = value.Presentation;
            if (message == null) continue;
            var start = ReplayPresentationTimingV17.EffectiveTimeTicks(value);
            var end = start + Math.Max(1L, message.DurationTicks);
            if (value.EventType == ReplayEventTypesV17.CardMotionPresented && targetTicks < end)
                view.PresentCardMotion(message, start);
            else if ((value.EventType == ReplayEventTypesV17.ActorAnimationPresented
                      || value.EventType == ReplayEventTypesV17.HitReactionPresented) && targetTicks < end)
                view.PlayEntityAnimation(message, start);
            else if (value.EventType == ReplayEventTypesV17.DamageTextPresented && targetTicks < end)
                view.PresentDamageText(message, start);
            else if (value.EventType == ReplayEventTypesV17.TurnTransitionPresented && targetTicks < end)
                view.PresentTurnTransition(message, start);
            else if (value.EventType == ReplayEventTypesV17.ExtensionPresented
                     && (message.Persistent || targetTicks < end))
                view.PresentExtension(message, start);
            else if (value.EventType == ReplayEventTypesV17.EffectPresented)
            {
                var duration = Math.Max(120_000L, message.DurationTicks);
                if (start <= targetTicks && targetTicks < start + duration)
                    effects.Play(message, view.PositionForEntity(
                        message.TargetIds.FirstOrDefault() ?? message.ActorId), start);
            }
            else if (includeAudio && value.EventType == ReplayEventTypesV17.AudioPresented && message.Audio != null)
            {
                var durationTicks = message.Audio.DurationSamples
                                    * ReplayProtocolV17.TimebaseTicksPerSecond / 48_000L;
                if (durationTicks > 0 && targetTicks < start + durationTicks)
                    audio.Play(message.Audio, start, targetTicks);
            }
        }
        Tick(targetTicks);
    }

    internal void ApplyPresentation(
        ReplayJournalEventV17 value,
        ReplayVisibleStateV17 state,
        bool suppressAudio)
    {
        var message = value.Presentation;
        if (message == null) return;
        var start = ReplayPresentationTimingV17.EffectiveTimeTicks(value);
        switch (value.EventType)
        {
            case ReplayEventTypesV17.EntityPresented:
                if (message.EntityBinding != null) view.BindEntity(message.EntityBinding, state);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.CardMotionPresented:
                view.PresentCardMotion(message, start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.ActorAnimationPresented:
            case ReplayEventTypesV17.HitReactionPresented:
                view.PlayEntityAnimation(message, start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.DamageTextPresented:
                view.PresentDamageText(message, start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.TurnTransitionPresented:
                view.PresentTurnTransition(message, start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.ExtensionPresented:
                view.PresentExtension(message, start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.EffectPresented:
                effects.Play(message, view.PositionForEntity(
                    message.TargetIds.FirstOrDefault() ?? message.ActorId), start);
                visualDirty = true;
                break;
            case ReplayEventTypesV17.AudioPresented:
                if (!suppressAudio && message.Audio != null) audio.Play(message.Audio, start);
                break;
        }
    }

    internal void Tick(long logicalTicks)
    {
        if (this.logicalTicks != logicalTicks) visualDirty = true;
        this.logicalTicks = logicalTicks;
        view.Tick(logicalTicks);
        effects.Tick(logicalTicks);
        audio.Tick(logicalTicks);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Exception? failure = null;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception ex) { failure ??= ex; }
        }

        Cleanup(view.Dispose);
        Cleanup(effects.Dispose);
        Cleanup(audio.Dispose);
        Cleanup(assets.Dispose);
        Cleanup(renderHost.Dispose);
        Cleanup(() => { if (root != null) Object.Destroy(root); });
        if (failure != null)
            throw new InvalidOperationException("Replay battle world teardown was incomplete.", failure);
    }
}
