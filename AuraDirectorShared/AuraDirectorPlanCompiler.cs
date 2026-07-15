using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AuraDirector.Shared;

public static class AuraDirectorPlanCompiler
{
    public const int CurrentProtocolVersion = 1;
    public const int MaximumActorCount = 32;
    public const string AlternatingPortraitStrategyId = "alternating-portrait-v1";
    public const int AlternatingPortraitStrategyVersion = 1;
    public const string DefaultOpeningProfileId = "opening-default-v1";

    private const double FocusBarRatio = 0.13d;
    private const double StartOutsideRatio = 1.15d;
    private const double EndOutsideRatio = -0.15d;
    private const double FocusXRatio = 0.5d;

    public static AuraDirectorCompileResult Compile(AuraDirectorRequest? request)
    {
        if (request == null)
        {
            return AuraDirectorCompileResult.Rejected("request-null");
        }

        var ownerModId = AuraDirectorResourceRef.Clean(request.OwnerModId);
        var requestId = AuraDirectorResourceRef.Clean(request.RequestId);
        if (ownerModId.Length == 0)
        {
            return AuraDirectorCompileResult.Rejected("owner-mod-id-empty");
        }
        if (requestId.Length == 0)
        {
            return AuraDirectorCompileResult.Rejected("request-id-empty");
        }

        var sourceActors = request.Actors ?? new List<AuraDirectorActorRef>();
        if (sourceActors.Count == 0)
        {
            return AuraDirectorCompileResult.Rejected("actors-empty");
        }
        if (sourceActors.Count > MaximumActorCount)
        {
            return AuraDirectorCompileResult.Rejected("actors-over-limit");
        }

        var strategy = (request.Strategy ?? new AuraDirectorStrategyRef()).Normalized();
        if (!string.Equals(strategy.StrategyId, AlternatingPortraitStrategyId, StringComparison.Ordinal)
            || strategy.StrategyVersion != AlternatingPortraitStrategyVersion
            || !string.Equals(strategy.ProfileId, DefaultOpeningProfileId, StringComparison.Ordinal))
        {
            return AuraDirectorCompileResult.Rejected("strategy-unsupported");
        }

        var actors = new List<AuraDirectorActorRef>(sourceActors.Count);
        var actorKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceActors)
        {
            var actor = (source ?? new AuraDirectorActorRef()).Normalized();
            if (actor.ActorKey.Length == 0)
            {
                return AuraDirectorCompileResult.Rejected("actor-key-empty");
            }
            if (!actorKeys.Add(actor.ActorKey))
            {
                return AuraDirectorCompileResult.Rejected("actor-key-duplicate");
            }
            if (actor.ContentId.Length == 0)
            {
                return AuraDirectorCompileResult.Rejected("actor-content-id-empty");
            }
            if (actor.Resource.ProviderId.Length == 0
                || actor.Resource.OwnerModId.Length == 0
                || actor.Resource.ResourceId.Length == 0)
            {
                return AuraDirectorCompileResult.Rejected("actor-resource-unregistered");
            }
            actors.Add(actor);
        }

        var compact = actors.Count > 8;
        var enter = compact ? 0.25d : 0.35d;
        var hold = compact ? 0.15d : 0.45d;
        var exit = compact ? 0.25d : 0.35d;
        var gap = compact ? 0.05d : 0.10d;
        var cursor = 0d;
        var cues = new List<AuraDirectorCue>(actors.Count * 4);

        for (var i = 0; i < actors.Count; i++)
        {
            var actor = actors[i];
            var direction = i % 2 == 0 ? AuraDirectorDirection.RightToLeft : AuraDirectorDirection.LeftToRight;
            var startX = direction == AuraDirectorDirection.RightToLeft ? StartOutsideRatio : EndOutsideRatio;
            var endX = direction == AuraDirectorDirection.RightToLeft ? EndOutsideRatio : StartOutsideRatio;
            var prefix = "actor-" + i.ToString(CultureInfo.InvariantCulture);

            cues.Add(new AuraDirectorCue
            {
                CueId = prefix + "-focus",
                TrackId = "letterbox",
                CueKind = AuraDirectorCueKind.Letterbox,
                ActorKey = actor.ActorKey,
                StartSeconds = cursor,
                DurationSeconds = enter,
                Layer = 20,
                FocusBarRatio = FocusBarRatio
            });
            cues.Add(new AuraDirectorCue
            {
                CueId = prefix + "-portrait",
                TrackId = "portrait",
                CueKind = AuraDirectorCueKind.PortraitSlide,
                ActorKey = actor.ActorKey,
                StartSeconds = cursor,
                DurationSeconds = enter + hold + exit,
                Layer = 10,
                Direction = direction,
                Resource = actor.Resource.Normalized(),
                EnterSeconds = enter,
                HoldSeconds = hold,
                ExitSeconds = exit,
                StartXRatio = startX,
                FocusXRatio = FocusXRatio,
                EndXRatio = endX
            });
            cues.Add(new AuraDirectorCue
            {
                CueId = prefix + "-relax",
                TrackId = "letterbox",
                CueKind = AuraDirectorCueKind.Letterbox,
                ActorKey = actor.ActorKey,
                StartSeconds = cursor + enter + hold,
                DurationSeconds = exit,
                Layer = 20,
                FocusBarRatio = 0d
            });
            cues.Add(new AuraDirectorCue
            {
                CueId = prefix + "-gap",
                TrackId = "markers",
                CueKind = AuraDirectorCueKind.Wait,
                ActorKey = actor.ActorKey,
                StartSeconds = cursor + enter + hold + exit,
                DurationSeconds = gap,
                Layer = 0
            });
            cursor += enter + hold + exit + gap;
        }

        var descriptor = new AuraDirectorPlanDescriptor
        {
            OwnerModId = ownerModId,
            RequestId = requestId,
            BattleSessionId = request.BattleSessionId,
            Actors = actors,
            Strategy = strategy,
            BlockingMode = request.BlockingMode,
            FailurePolicy = request.FailurePolicy,
            HardTimeoutSeconds = Clamp(request.HardTimeoutSeconds, 5d, 60d, 20d),
            DurationSeconds = cursor
        };
        descriptor.PlanHash = ComputeHash(descriptor, cues);
        return AuraDirectorCompileResult.Accepted(descriptor, cues);
    }

    private static string ComputeHash(AuraDirectorPlanDescriptor descriptor, IReadOnlyList<AuraDirectorCue> cues)
    {
        var canonical = new StringBuilder(1024);
        Append(canonical, descriptor.ProtocolVersion);
        Append(canonical, descriptor.OwnerModId);
        Append(canonical, descriptor.RequestId);
        Append(canonical, descriptor.BattleSessionId);
        Append(canonical, descriptor.Strategy.StrategyId);
        Append(canonical, descriptor.Strategy.StrategyVersion);
        Append(canonical, descriptor.Strategy.ProfileId);
        Append(canonical, (int)descriptor.BlockingMode);
        Append(canonical, (int)descriptor.FailurePolicy);
        Append(canonical, descriptor.HardTimeoutSeconds);
        foreach (var actor in descriptor.Actors)
        {
            Append(canonical, actor.ActorKey);
            Append(canonical, (int)actor.ActorKind);
            Append(canonical, (int)actor.Side);
            Append(canonical, actor.OwnerPlayerId);
            Append(canonical, actor.ContentOwnerModId);
            Append(canonical, actor.ContentId);
            Append(canonical, actor.Resource.ProviderId);
            Append(canonical, actor.Resource.OwnerModId);
            Append(canonical, actor.Resource.ResourceId);
            Append(canonical, actor.Resource.VariantId);
        }
        foreach (var cue in cues)
        {
            Append(canonical, cue.CueId);
            Append(canonical, cue.TrackId);
            Append(canonical, (int)cue.CueKind);
            Append(canonical, cue.ActorKey);
            Append(canonical, cue.StartSeconds);
            Append(canonical, cue.DurationSeconds);
            Append(canonical, cue.Layer);
            Append(canonical, (int)cue.Direction);
            Append(canonical, cue.Resource.ProviderId);
            Append(canonical, cue.Resource.OwnerModId);
            Append(canonical, cue.Resource.ResourceId);
            Append(canonical, cue.Resource.VariantId);
            Append(canonical, cue.EnterSeconds);
            Append(canonical, cue.HoldSeconds);
            Append(canonical, cue.ExitSeconds);
            Append(canonical, cue.FocusBarRatio);
            Append(canonical, cue.StartXRatio);
            Append(canonical, cue.FocusXRatio);
            Append(canonical, cue.EndXRatio);
        }

        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => "",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text).Append('|');
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}
