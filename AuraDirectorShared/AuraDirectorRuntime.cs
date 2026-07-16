using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Witch.Mod;

namespace AuraDirector.Shared;

public static class AuraDirectorRuntime
{
    private const string GlobalObjectName = "AuraDirector.Global";
    private const string ComponentFullName = "AuraDirector.Shared.AuraDirectorRuntime+AuraDirectorComponent";

    public const int CurrentRuntimeProtocolVersion = 2;
    public const string NativeBattleSpriteProviderId = "AuraDirector.NativeBattleSprite.v1";

    public static void Initialize(ModConfig modConfig, string ownerModId)
    {
        AuraSharedRuntime.Initialize(modConfig, ownerModId);
        EnsureComponent(ownerModId);
    }

    public static bool RegisterRequestSource(string ownerModId, IAuraDirectorRequestSource source)
    {
        return EnsureComponent(ownerModId)?.RegisterRequestSource(ownerModId, source) == true;
    }

    public static AuraDirectorCapabilityProbeResult RegisterStartGateProvider(
        string ownerModId,
        IAuraDirectorStartGateProvider provider)
    {
        var component = EnsureComponent(ownerModId);
        return component == null
            ? Unsupported("director-runtime-unavailable", "The global AuraDirector runtime is unavailable.")
            : component.RegisterStartGateProvider(ownerModId, provider);
    }

    private static AuraDirectorComponent? EnsureComponent(string ownerModId)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            foreach (var component in gameObject.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().FullName != ComponentFullName)
                {
                    continue;
                }

                if (component is AuraDirectorComponent compatible
                    && compatible.ProtocolVersion == CurrentRuntimeProtocolVersion)
                {
                    return compatible;
                }

                AuraSharedLog.Error(
                    "AuraDirector",
                    "Incompatible global director runtime; initialization disabled for " + ownerModId + ".");
                return null;
            }
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var created = gameObject.AddComponent<AuraDirectorComponent>();
        AuraSharedLog.InfoOnce(
            "AuraDirector",
            "runtime-created",
            "Created local AuraDirector runtime, protocol=" + CurrentRuntimeProtocolVersion + ".",
            false);
        return created;
    }

    private static AuraDirectorCapabilityProbeResult Unsupported(string code, string detail)
    {
        return new AuraDirectorCapabilityProbeResult
        {
            Supported = false,
            Code = code,
            Detail = detail
        };
    }

    public sealed class AuraDirectorComponent : MonoBehaviour, IAuraDirectorNativeStartHoldSink
    {
        private const float SkipDebounceSeconds = 0.3f;

        private readonly object gate = new();
        private readonly Dictionary<string, SourceRegistration> sources = new(StringComparer.OrdinalIgnoreCase);
        private readonly AuraDirectorOverlayPresenter overlay = new();
        private IAuraDirectorStartGateProvider? startGateProvider;
        private ActiveSession? activeSession;
        private int generation;
        private bool skipInputPollingFaulted;

        public int ProtocolVersion => CurrentRuntimeProtocolVersion;

        public bool RegisterRequestSource(string ownerModId, IAuraDirectorRequestSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.SourceId))
            {
                return false;
            }

            var owner = Clean(ownerModId, "UnknownOwner");
            var key = owner + ":" + source.SourceId.Trim();
            lock (gate)
            {
                sources[key] = new SourceRegistration(owner, source);
            }
            AuraSharedLog.DebugLog("AuraDirector", "Request source registered: " + key, false);
            return true;
        }

        public AuraDirectorCapabilityProbeResult RegisterStartGateProvider(
            string ownerModId,
            IAuraDirectorStartGateProvider provider)
        {
            if (provider == null)
            {
                return Unsupported("start-gate-provider-null", "The start-gate provider is null.");
            }

            lock (gate)
            {
                if (startGateProvider != null)
                {
                    if (string.Equals(startGateProvider.ProviderId, provider.ProviderId, StringComparison.Ordinal))
                    {
                        return Supported("start-gate-provider-reused", "The compatible start-gate provider is already installed.");
                    }

                    return Unsupported(
                        "start-gate-provider-conflict",
                        "Another start-gate provider already owns the director runtime: " + startGateProvider.ProviderId);
                }
            }

            var capability = provider.ProbeCapability();
            if (!capability.Supported)
            {
                AuraSharedLog.Warn(
                    "AuraDirector",
                    "Start-gate provider rejected for " + ownerModId + ": " + capability.Code + " -> " + capability.Detail,
                    false);
                return capability;
            }

            var installed = provider.Install(this);
            if (!installed.Supported)
            {
                return installed;
            }

            lock (gate)
            {
                startGateProvider = provider;
            }
            AuraSharedLog.Info(
                "AuraDirector",
                "Start-gate provider installed: " + provider.ProviderId + ", owner=" + ownerModId + ".",
                false);
            return installed;
        }

        public bool TryAccept(IAuraDirectorNativeStartHold hold)
        {
            if (hold?.NativeTarget is not FightManager fightManager || fightManager == null)
            {
                return false;
            }

            ActiveSession? current;
            lock (gate)
            {
                current = activeSession;
            }
            if (current != null && !current.State.IsReleased)
            {
                AuraSharedLog.Warn("AuraDirector", "A second native start hold was rejected while a session is active.", false);
                return false;
            }

            try
            {
                var battleSessionId = AuraBattleLifecycleRouter.EnsureBattleSession();
                if (!TryCompileRequest(fightManager, battleSessionId, out var compileResult, out var sourceId))
                {
                    return false;
                }

                if (!overlay.EnsureCreated())
                {
                    return false;
                }

                var session = new ActiveSession(
                    ++generation,
                    fightManager,
                    hold,
                    compileResult.Descriptor!,
                    compileResult.Cues,
                    sourceId);
                session.State.TryAdvance(AuraDirectorSessionState.Preparing);
                session.State.TryAdvance(AuraDirectorSessionState.Ready);
                session.State.TryAdvance(AuraDirectorSessionState.Scheduled);

                lock (gate)
                {
                    activeSession = session;
                }

                session.Coroutine = StartCoroutine(PlaySession(session));
                AuraSharedLog.Info(
                    "AuraDirector",
                    "Local opening accepted: source=" + sourceId
                    + ", battleSession=" + battleSessionId
                    + ", actors=" + compileResult.Descriptor!.Actors.Count
                    + ", duration=" + compileResult.Descriptor.DurationSeconds.ToString("0.###")
                    + ", hash=" + compileResult.Descriptor.PlanHash + ".",
                    false);
                return true;
            }
            catch (Exception ex)
            {
                AuraSharedLog.Error("AuraDirector", "Local opening setup failed open.", ex, false);
                lock (gate)
                {
                    activeSession = null;
                }
                overlay.Hide();
                return false;
            }
        }

        private bool TryCompileRequest(
            FightManager fightManager,
            long battleSessionId,
            out AuraDirectorCompileResult compileResult,
            out string sourceId)
        {
            SourceRegistration[] snapshot;
            lock (gate)
            {
                snapshot = sources.Values
                    .OrderByDescending(item => item.Source.Priority)
                    .ThenBy(item => item.OwnerModId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Source.SourceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            foreach (var item in snapshot)
            {
                try
                {
                    var request = item.Source.BuildRequest(fightManager, battleSessionId);
                    if (request == null)
                    {
                        continue;
                    }

                    var result = AuraDirectorPlanCompiler.Compile(request);
                    if (result.Success && result.Descriptor != null)
                    {
                        compileResult = result;
                        sourceId = item.OwnerModId + ":" + item.Source.SourceId;
                        return true;
                    }

                    AuraSharedLog.Warn(
                        "AuraDirector",
                        "Opening request rejected: source=" + item.OwnerModId + ":" + item.Source.SourceId
                        + ", code=" + result.RejectionCode + ".",
                        false);
                }
                catch (Exception ex)
                {
                    AuraSharedLog.Error(
                        "AuraDirector",
                        "Opening request source failed: " + item.OwnerModId + ":" + item.Source.SourceId + ".",
                        ex,
                        false);
                }
            }

            compileResult = AuraDirectorCompileResult.Rejected("no-local-request");
            sourceId = "";
            return false;
        }

        private IEnumerator PlaySession(ActiveSession session)
        {
            if (!IsCurrent(session) || !session.State.TryAdvance(AuraDirectorSessionState.Playing))
            {
                yield break;
            }

            overlay.Show();
            var portraitCues = session.Cues
                .Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide)
                .OrderBy(cue => cue.StartSeconds)
                .ThenBy(cue => cue.CueId, StringComparer.Ordinal)
                .ToArray();
            var letterboxCues = session.Cues
                .Where(cue => cue.CueKind == AuraDirectorCueKind.Letterbox)
                .OrderBy(cue => cue.StartSeconds)
                .ThenBy(cue => cue.CueId, StringComparer.Ordinal)
                .ToArray();
            var waitCues = session.Cues
                .Where(cue => cue.CueKind == AuraDirectorCueKind.Wait)
                .OrderBy(cue => cue.StartSeconds)
                .ThenBy(cue => cue.CueId, StringComparer.Ordinal)
                .ToArray();

            foreach (var cue in portraitCues)
            {
                if (!IsCurrent(session))
                {
                    yield break;
                }

                var actor = session.Descriptor.Actors.FirstOrDefault(item =>
                    string.Equals(item.ActorKey, cue.ActorKey, StringComparison.Ordinal));
                var actorLetterboxCues = letterboxCues
                    .Where(item => string.Equals(item.ActorKey, cue.ActorKey, StringComparison.Ordinal))
                    .ToArray();
                var focusBarRatio = actorLetterboxCues.FirstOrDefault()?.FocusBarRatio ?? 0.13d;
                var relaxBarRatio = actorLetterboxCues.LastOrDefault()?.FocusBarRatio ?? 0d;
                var portrait = ResolveNativePortrait(session.Target, actor)
                               ?? CreatePortraitSnapshot(overlay.SilhouetteSprite, false, false);
                yield return overlay.PlayPortrait(
                    cue,
                    actor,
                    portrait,
                    focusBarRatio,
                    relaxBarRatio,
                    () => IsCurrent(session));

                var gap = waitCues.FirstOrDefault(item =>
                    string.Equals(item.ActorKey, cue.ActorKey, StringComparison.Ordinal));
                if (gap != null && gap.DurationSeconds > 0d)
                {
                    yield return overlay.Wait(gap.DurationSeconds, () => IsCurrent(session));
                }
            }

            if (!IsCurrent(session))
            {
                yield break;
            }

            session.State.TryAdvance(AuraDirectorSessionState.Completing);
            Finish(session, "completed");
        }

        private void Update()
        {
            ActiveSession? session;
            lock (gate)
            {
                session = activeSession;
            }
            if (session == null || session.State.IsReleased)
            {
                return;
            }

            if (session.Target == null)
            {
                Finish(session, "fight-manager-destroyed");
                return;
            }

            if (Time.unscaledTime >= session.Deadline)
            {
                Finish(session, "hard-timeout");
                return;
            }

            if (Time.unscaledTime - session.StartedAt >= SkipDebounceSeconds
                && WasSkipPressedThisFrame())
            {
                Finish(session, "user-skip");
            }
        }

        private bool WasSkipPressedThisFrame()
        {
            if (skipInputPollingFaulted)
            {
                return false;
            }

            try
            {
                var keyboard = Keyboard.current;
                if (keyboard != null
                    && (keyboard.escapeKey.wasPressedThisFrame
                        || keyboard.spaceKey.wasPressedThisFrame
                        || keyboard.enterKey.wasPressedThisFrame
                        || keyboard.numpadEnterKey.wasPressedThisFrame))
                {
                    return true;
                }

                var mouse = Mouse.current;
                return mouse != null && mouse.leftButton.wasPressedThisFrame;
            }
            catch (Exception ex)
            {
                skipInputPollingFaulted = true;
                AuraSharedLog.WarnOnce(
                    "AuraDirector",
                    "skip-input-polling-failed",
                    "Director skip input polling disabled after an Input System failure: " + ex.Message,
                    false);
                return false;
            }
        }

        private void Finish(ActiveSession session, string reason)
        {
            if (!session.State.TryBeginRelease(reason))
            {
                return;
            }

            lock (gate)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                }
            }

            overlay.Hide();
            var released = false;
            try
            {
                released = session.Hold.TryRelease(reason);
            }
            catch (Exception ex)
            {
                AuraSharedLog.Error("AuraDirector", "Native start hold release failed.", ex, false);
            }
            finally
            {
                session.State.TryMarkReleased();
            }

            AuraSharedLog.Info(
                "AuraDirector",
                "Local opening released: reason=" + reason
                + ", source=" + session.SourceId
                + ", elapsed=" + (Time.unscaledTime - session.StartedAt).ToString("0.###")
                + ", nativeReleased=" + released + ".",
                false);
        }

        private bool IsCurrent(ActiveSession session)
        {
            lock (gate)
            {
                return ReferenceEquals(activeSession, session) && !session.State.IsReleased;
            }
        }

        private static NativePortraitSnapshot? ResolveNativePortrait(
            FightManager fightManager,
            AuraDirectorActorRef? actor)
        {
            if (actor == null
                || !string.Equals(actor.Resource.ProviderId, NativeBattleSpriteProviderId, StringComparison.Ordinal)
                || fightManager.statuses == null)
            {
                return null;
            }

            var statusId = string.IsNullOrWhiteSpace(actor.Resource.ResourceId)
                ? actor.ActorKey
                : actor.Resource.ResourceId;
            if (!fightManager.statuses.TryGetValue(statusId, out var status) || status == null)
            {
                return null;
            }

            var body = status.transform.Find("body")?.GetComponent<SpriteRenderer>();
            if (body?.sprite == null)
            {
                return null;
            }

            var lossyScale = body.transform.lossyScale;
            return CreatePortraitSnapshot(
                body.sprite,
                body.flipX ^ (lossyScale.x < 0f),
                body.flipY ^ (lossyScale.y < 0f));
        }

        private void OnDestroy()
        {
            ActiveSession? session;
            IAuraDirectorStartGateProvider? provider;
            lock (gate)
            {
                session = activeSession;
                provider = startGateProvider;
                startGateProvider = null;
            }

            if (session != null)
            {
                Finish(session, "runtime-destroyed");
            }

            try
            {
                provider?.Uninstall("runtime-destroyed");
            }
            catch (Exception ex)
            {
                AuraSharedLog.Error("AuraDirector", "Start-gate provider uninstall failed.", ex, false);
            }
            overlay.Dispose();
        }

        private static string Clean(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static AuraDirectorCapabilityProbeResult Supported(string code, string detail)
        {
            return new AuraDirectorCapabilityProbeResult
            {
                Supported = true,
                Code = code,
                Detail = detail
            };
        }

        private static AuraDirectorCapabilityProbeResult Unsupported(string code, string detail)
        {
            return new AuraDirectorCapabilityProbeResult
            {
                Supported = false,
                Code = code,
                Detail = detail
            };
        }

        private sealed class SourceRegistration
        {
            public SourceRegistration(string ownerModId, IAuraDirectorRequestSource source)
            {
                OwnerModId = ownerModId;
                Source = source;
            }

            public string OwnerModId { get; }

            public IAuraDirectorRequestSource Source { get; }
        }

        private sealed class ActiveSession
        {
            public ActiveSession(
                int generation,
                FightManager target,
                IAuraDirectorNativeStartHold hold,
                AuraDirectorPlanDescriptor descriptor,
                IReadOnlyList<AuraDirectorCue> cues,
                string sourceId)
            {
                Generation = generation;
                Target = target;
                Hold = hold;
                Descriptor = descriptor;
                Cues = cues;
                SourceId = sourceId;
                StartedAt = Time.unscaledTime;
                Deadline = StartedAt + (float)descriptor.HardTimeoutSeconds;
            }

            public int Generation { get; }

            public FightManager Target { get; }

            public IAuraDirectorNativeStartHold Hold { get; }

            public AuraDirectorPlanDescriptor Descriptor { get; }

            public IReadOnlyList<AuraDirectorCue> Cues { get; }

            public string SourceId { get; }

            public float StartedAt { get; }

            public float Deadline { get; }

            public AuraDirectorSessionStateMachine State { get; } = new();

            public Coroutine? Coroutine { get; set; }
        }
    }

    private readonly struct NativePortraitSnapshot
    {
        public NativePortraitSnapshot(
            Sprite sprite,
            bool flipX,
            bool flipY,
            Vector2 sourceMin,
            Vector2 sourceMax)
        {
            Sprite = sprite;
            FlipX = flipX;
            FlipY = flipY;
            SourceMin = sourceMin;
            SourceMax = sourceMax;
        }

        public Sprite Sprite { get; }

        public bool FlipX { get; }

        public bool FlipY { get; }

        public Vector2 SourceMin { get; }

        public Vector2 SourceMax { get; }
    }

    private static NativePortraitSnapshot CreatePortraitSnapshot(Sprite sprite, bool flipX, bool flipY)
    {
        var bounds = sprite.bounds;
        var minimum = new Vector2(bounds.min.x, bounds.min.y);
        var maximum = new Vector2(bounds.max.x, bounds.max.y);
        var vertices = sprite.vertices;
        if (vertices != null && vertices.Length > 0)
        {
            minimum = vertices[0];
            maximum = vertices[0];
            for (var i = 1; i < vertices.Length; i++)
            {
                minimum = Vector2.Min(minimum, vertices[i]);
                maximum = Vector2.Max(maximum, vertices[i]);
            }
        }

        if (maximum.x - minimum.x <= 0.0001f || maximum.y - minimum.y <= 0.0001f)
        {
            minimum = new Vector2(-0.5f, -0.5f);
            maximum = new Vector2(0.5f, 0.5f);
        }
        return new NativePortraitSnapshot(sprite, flipX, flipY, minimum, maximum);
    }

    private sealed class AuraDirectorOverlayPresenter : IDisposable
    {
        private const int SortingOrder = 32740;
        private GameObject? root;
        private CanvasGroup? group;
        private Image? blocker;
        private AuraDirectorPortraitGraphic? portrait;
        private Image? topBar;
        private Image? bottomBar;
        private Sprite? silhouetteSprite;
        private Texture2D? silhouetteTexture;
        private NativePortraitSnapshot? activePortrait;
        private double activeFocusBarRatio;
        private int layoutScreenWidth;
        private int layoutScreenHeight;

        public Sprite SilhouetteSprite => silhouetteSprite ??= CreateSilhouette();

        public bool EnsureCreated()
        {
            if (root != null && group != null && blocker != null && portrait != null && topBar != null && bottomBar != null)
            {
                return true;
            }

            Dispose();
            try
            {
                root = new GameObject(
                    "AuraDirector.Overlay",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasGroup),
                    typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(root);
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;

                group = root.GetComponent<CanvasGroup>();
                group.alpha = 1f;
                group.blocksRaycasts = true;
                group.interactable = true;

                blocker = CreateImage("Blocker", root.transform, Color.black);
                blocker.color = new Color(0.025f, 0.03f, 0.04f, 0.94f);
                blocker.raycastTarget = true;

                portrait = CreatePortraitGraphic(root.transform);

                topBar = CreateImage("Letterbox.Top", root.transform, Color.black);
                bottomBar = CreateImage("Letterbox.Bottom", root.transform, Color.black);
                ConfigureBar(topBar.rectTransform, top: true);
                ConfigureBar(bottomBar.rectTransform, top: false);

                root.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                AuraSharedLog.Error("AuraDirector", "Director overlay creation failed.", ex, false);
                Dispose();
                return false;
            }
        }

        public void Show()
        {
            if (!EnsureCreated() || root == null || group == null)
            {
                return;
            }

            root.SetActive(true);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;
            SetLetterboxRatio(0d);
        }

        public void Hide()
        {
            if (root == null)
            {
                return;
            }

            if (portrait != null)
            {
                portrait.enabled = false;
                portrait.ClearSprite();
            }
            activePortrait = null;
            activeFocusBarRatio = 0d;
            layoutScreenWidth = 0;
            layoutScreenHeight = 0;
            SetLetterboxRatio(0d);
            if (group != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            root.SetActive(false);
        }

        public IEnumerator PlayPortrait(
            AuraDirectorCue cue,
            AuraDirectorActorRef? actor,
            NativePortraitSnapshot snapshot,
            double focusBarRatio,
            double relaxBarRatio,
            Func<bool> isCurrent)
        {
            if (portrait == null || root == null)
            {
                yield break;
            }

            activePortrait = snapshot;
            activeFocusBarRatio = focusBarRatio;
            portrait.color = actor?.Side == AuraDirectorActorSide.Hostile
                ? new Color(1f, 0.84f, 0.82f, 1f)
                : new Color(0.88f, 0.95f, 1f, 1f);
            portrait.enabled = true;
            ApplyPortraitLayout(snapshot, focusBarRatio);

            yield return MovePortraitAndLetterbox(
                cue.StartXRatio,
                cue.FocusXRatio,
                relaxBarRatio,
                focusBarRatio,
                cue.EnterSeconds,
                isCurrent);
            yield return WaitUnscaled(cue.HoldSeconds, isCurrent);
            yield return MovePortraitAndLetterbox(
                cue.FocusXRatio,
                cue.EndXRatio,
                focusBarRatio,
                relaxBarRatio,
                cue.ExitSeconds,
                isCurrent);
            portrait.enabled = false;
            portrait.ClearSprite();
            activePortrait = null;
            activeFocusBarRatio = 0d;
            layoutScreenWidth = 0;
            layoutScreenHeight = 0;
        }

        public IEnumerator Wait(double seconds, Func<bool> isCurrent)
        {
            return WaitUnscaled(seconds, isCurrent);
        }

        public void Dispose()
        {
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
            }
            if (silhouetteSprite != null)
            {
                UnityEngine.Object.Destroy(silhouetteSprite);
            }
            if (silhouetteTexture != null)
            {
                UnityEngine.Object.Destroy(silhouetteTexture);
            }

            root = null;
            group = null;
            blocker = null;
            portrait = null;
            topBar = null;
            bottomBar = null;
            silhouetteSprite = null;
            silhouetteTexture = null;
        }

        private IEnumerator MovePortraitAndLetterbox(
            double fromRatio,
            double toRatio,
            double fromBarRatio,
            double toBarRatio,
            double seconds,
            Func<bool> isCurrent)
        {
            if (portrait == null)
            {
                yield break;
            }

            var duration = Mathf.Max(0f, (float)seconds);
            if (duration <= 0f)
            {
                SetPortraitX((float)toRatio);
                SetLetterboxRatio(toBarRatio);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration && isCurrent())
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                SetPortraitX(Mathf.Lerp((float)fromRatio, (float)toRatio, progress));
                SetLetterboxRatio(Mathf.Lerp((float)fromBarRatio, (float)toBarRatio, progress));
                yield return null;
            }
            SetPortraitX((float)toRatio);
            SetLetterboxRatio(toBarRatio);
        }

        private static IEnumerator WaitUnscaled(double seconds, Func<bool> isCurrent)
        {
            var remaining = Mathf.Max(0f, (float)seconds);
            while (remaining > 0f && isCurrent())
            {
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void ApplyPortraitLayout(NativePortraitSnapshot snapshot, double focusBarRatio)
        {
            if (portrait == null)
            {
                return;
            }

            var layout = AuraDirectorPortraitLayout.Calculate(
                Screen.height,
                focusBarRatio,
                snapshot.SourceMin.x,
                snapshot.SourceMin.y,
                snapshot.SourceMax.x,
                snapshot.SourceMax.y);
            portrait.Configure(snapshot, layout);
            layoutScreenWidth = Screen.width;
            layoutScreenHeight = Screen.height;
        }

        private void SetLetterboxRatio(double ratio)
        {
            var layout = AuraDirectorPortraitLayout.Calculate(
                Screen.height,
                ratio,
                0d,
                0d,
                1d,
                1d);
            var barHeight = (float)layout.BarHeight;
            if (topBar != null)
            {
                topBar.rectTransform.sizeDelta = new Vector2(0f, barHeight);
            }
            if (bottomBar != null)
            {
                bottomBar.rectTransform.sizeDelta = new Vector2(0f, barHeight);
            }
        }

        private void SetPortraitX(float ratio)
        {
            if (portrait == null)
            {
                return;
            }
            if (activePortrait.HasValue)
            {
                if (layoutScreenWidth != Screen.width || layoutScreenHeight != Screen.height)
                {
                    ApplyPortraitLayout(activePortrait.Value, activeFocusBarRatio);
                }
            }
            var x = AuraDirectorPortraitLayout.ResolveAnchoredX(
                ratio,
                Screen.width,
                portrait.rectTransform.sizeDelta.x);
            portrait.rectTransform.anchoredPosition = new Vector2((float)x, 0f);
        }

        private Sprite CreateSilhouette()
        {
            const int width = 256;
            const int height = 384;
            var pixels = new Color32[width * height];
            var fill = new Color32(188, 198, 212, 255);
            var shadow = new Color32(88, 98, 114, 255);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var head = Circle(x, y, 128, 286, 52);
                    var torso = Ellipse(x, y, 128, 128, 104, 138) && y < 238;
                    var neck = x >= 104 && x <= 152 && y >= 210 && y <= 255;
                    if (!head && !torso && !neck)
                    {
                        pixels[y * width + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var edge = Circle(x, y, 128, 286, 48)
                               || Ellipse(x, y, 128, 128, 98, 132)
                               || neck;
                    pixels[y * width + x] = edge ? fill : shadow;
                }
            }

            silhouetteTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "AuraDirector.GenericSilhouette",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            silhouetteTexture.SetPixels32(pixels);
            silhouetteTexture.Apply(false, false);
            var sprite = Sprite.Create(
                silhouetteTexture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = "AuraDirector.GenericSilhouette.Sprite";
            return sprite;
        }

        private static bool Circle(int x, int y, int centerX, int centerY, int radius)
        {
            var dx = x - centerX;
            var dy = y - centerY;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool Ellipse(int x, int y, int centerX, int centerY, int radiusX, int radiusY)
        {
            var dx = (x - centerX) / (float)radiusX;
            var dy = (y - centerY) / (float)radiusY;
            return dx * dx + dy * dy <= 1f;
        }

        private static AuraDirectorPortraitGraphic CreatePortraitGraphic(Transform parent)
        {
            var gameObject = new GameObject(
                "Portrait",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(AuraDirectorPortraitGraphic));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            var graphic = gameObject.GetComponent<AuraDirectorPortraitGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var rect = gameObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void ConfigureBar(RectTransform rect, bool top)
        {
            rect.anchorMin = top ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            rect.anchorMax = top ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
            rect.pivot = top ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }

    private sealed class AuraDirectorPortraitGraphic : MaskableGraphic
    {
        private Sprite? sprite;
        private Vector2 sourceCenter;
        private float unitsToPixels = 1f;
        private bool flipX;
        private bool flipY;

        public override Texture mainTexture => sprite?.texture ?? s_WhiteTexture;

        public void Configure(
            NativePortraitSnapshot snapshot,
            AuraDirectorPortraitLayoutResult layout)
        {
            sprite = snapshot.Sprite;
            sourceCenter = new Vector2((float)layout.SourceCenterX, (float)layout.SourceCenterY);
            unitsToPixels = Mathf.Max(0.0001f, (float)layout.UnitsToPixels);
            flipX = snapshot.FlipX;
            flipY = snapshot.FlipY;
            rectTransform.sizeDelta = new Vector2(
                Mathf.Max(1f, (float)layout.DisplayWidth),
                Mathf.Max(1f, (float)layout.DisplayHeight));
            SetAllDirty();
        }

        public void ClearSprite()
        {
            sprite = null;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (sprite == null)
            {
                return;
            }

            var vertices = sprite.vertices;
            var uv = sprite.uv;
            var triangles = sprite.triangles;
            if (vertices == null
                || uv == null
                || triangles == null
                || vertices.Length == 0
                || vertices.Length != uv.Length)
            {
                return;
            }

            var vertexColor = (Color32)color;
            for (var i = 0; i < vertices.Length; i++)
            {
                var position = vertices[i] - sourceCenter;
                if (flipX)
                {
                    position.x = -position.x;
                }
                if (flipY)
                {
                    position.y = -position.y;
                }
                vertexHelper.AddVert(position * unitsToPixels, vertexColor, uv[i]);
            }

            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                vertexHelper.AddTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
            }
        }
    }
}
