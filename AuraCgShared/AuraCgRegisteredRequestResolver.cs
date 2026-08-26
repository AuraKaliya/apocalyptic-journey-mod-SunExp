using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AuraCg.Shared;

internal sealed class AuraCgRegisteredRequestResolver
{
    private readonly Func<string, IReadOnlyList<AuraCgRegistryEntry>> registeredEntries;
    private readonly Func<AuraCgRegistryEntry, bool> effectiveEnabled;
    private readonly Func<string, string, string> imagePathResolver;
    private readonly Func<float> clock;
    private readonly Action<string, string>? warnOnce;
    private readonly int maximumIdentifierLength;

    public AuraCgRegisteredRequestResolver(
        Func<string, IReadOnlyList<AuraCgRegistryEntry>> registeredEntries,
        Func<AuraCgRegistryEntry, bool> effectiveEnabled,
        Func<string, string, string> imagePathResolver,
        Func<float> clock,
        Action<string, string>? warnOnce,
        string skillKind,
        string cardUseKind,
        int maximumIdentifierLength)
    {
        this.registeredEntries = registeredEntries ?? throw new ArgumentNullException(nameof(registeredEntries));
        this.effectiveEnabled = effectiveEnabled ?? throw new ArgumentNullException(nameof(effectiveEnabled));
        this.imagePathResolver = imagePathResolver ?? throw new ArgumentNullException(nameof(imagePathResolver));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.warnOnce = warnOnce;
        this.maximumIdentifierLength = Math.Max(1, maximumIdentifierLength);
    }

    public SkillCgRequest? BuildRequest(
        AuraCgRegistryEntry entry,
        string kind,
        SkillCgTriggerContext context,
        bool consumerCanPlay,
        bool disableSync,
        bool warnWhenMediaMissing = true)
    {
        var signal = AuraCgSignalContext.FromLegacy(context);
        if (!string.Equals(signal.SignalId, SignalForLegacyKind(kind), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return BuildSignalRequest(entry, signal, consumerCanPlay, disableSync, warnWhenMediaMissing);
    }

    public SkillCgRequest? BuildSignalRequest(
        AuraCgRegistryEntry entry,
        AuraCgSignalContext context,
        bool consumerCanPlay,
        bool disableSync,
        bool warnWhenMediaMissing = true)
    {
        if (!AuraCgRegistryQueryService.MatchesSignal(entry, context, consumerCanPlay))
        {
            return null;
        }

        if (string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase))
        {
            var plan = context.ScenePlan ?? AuraCgTeamScenePlanner.Build(
                context.SceneSource,
                entry.Scene,
                context.SignalId);
            if (plan == null || !plan.IsValid(maximumIdentifierLength))
            {
                return null;
            }

            return CreateRequest(entry, "", "", CopyWithScenePlan(context, plan), disableSync);
        }

        var imageResource = AuraCgRegistryQueryService.ResolveImageResource(entry);
        var imagePath = imagePathResolver(entry.OwnerModId, imageResource);
        if (!MediaExists(entry.Media.Type, imagePath, entry.Media.BundlePath))
        {
            if (warnWhenMediaMissing)
            {
                warnOnce?.Invoke(
                    "registered-media-missing:" + entry.QualifiedCgId,
                    "Registered CG media is missing: " + entry.QualifiedCgId + ", resource=" + imageResource);
            }

            return null;
        }

        return CreateRequest(entry, imageResource, imagePath, context, disableSync);
    }

    private static AuraCgSignalContext CopyWithScenePlan(
        AuraCgSignalContext source,
        AuraCgScenePlan plan)
    {
        return new AuraCgSignalContext
        {
            SignalId = source.SignalId,
            SubjectType = source.SubjectType,
            SubjectId = source.SubjectId,
            ActionSequence = source.ActionSequence,
            EventToken = source.EventToken,
            Action = source.Action,
            RoleId = source.RoleId,
            CardId = source.CardId,
            SkillId = source.SkillId,
            OwnerInstanceId = source.OwnerInstanceId,
            BattleId = source.BattleId,
            ModeId = source.ModeId,
            Outcome = source.Outcome,
            CreatedAt = source.CreatedAt,
            ScenePlan = plan,
            Facts = new Dictionary<string, string>(source.Facts, StringComparer.OrdinalIgnoreCase),
            Metrics = new Dictionary<string, double>(source.Metrics, StringComparer.OrdinalIgnoreCase),
            ConfigureResolvedRequest = source.ConfigureResolvedRequest
        };
    }

    public SkillCgRequest CreateRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        SkillCgTriggerContext context,
        bool disableSync)
    {
        return AuraCgRegistryQueryService.CreateRequest(
            entry,
            imageResource,
            imagePath,
            context,
            disableSync,
            clock());
    }

    public SkillCgRequest CreateRequest(
        AuraCgRegistryEntry entry,
        string imageResource,
        string imagePath,
        AuraCgSignalContext context,
        bool disableSync)
    {
        return AuraCgRegistryQueryService.CreateRequest(
            entry,
            imageResource,
            imagePath,
            context,
            disableSync,
            clock());
    }

    // Network playback carries registered ids only. Every peer resolves its own local resource declaration.
    public SkillCgRequest? ResolveNetworkRequest(SkillCgNetworkEvent item, bool requireLocalActivation)
    {
        if (!AuraCgNetworkPolicy.HasValidEventIdentity(item, maximumIdentifierLength))
        {
            return null;
        }

        var ownerModId = item.OwnerModId.Trim();
        var entry = registeredEntries(ownerModId)
            .FirstOrDefault(candidate => string.Equals(candidate.CgId, item.CgId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry == null
            || !AuraCgRegistryQueryService.IsRegisteredSignalEntry(entry)
            || !MatchesNetworkTarget(entry, item)
            || !string.Equals(item.ProviderId.Trim(), ProviderIdentity(entry), StringComparison.Ordinal))
        {
            return null;
        }

        // Activation is a recipient-local effective-state overlay. The host
        // validates registered identity without applying another peer's local override.
        if (requireLocalActivation && !effectiveEnabled(entry))
        {
            return null;
        }

        var scene = string.Equals(entry.Media.Type, SkillCgMediaTypes.Scene, StringComparison.OrdinalIgnoreCase);
        var imageResource = scene ? "" : AuraCgRegistryQueryService.ResolveImageResource(entry);
        var imagePath = scene ? "" : imagePathResolver(entry.OwnerModId, imageResource);
        if (!scene && !MediaExists(entry.Media.Type, imagePath, entry.Media.BundlePath))
        {
            return null;
        }

        var signal = new AuraCgSignalContext
        {
            SignalId = item.SignalId,
            SubjectType = item.SubjectType,
            SubjectId = item.SubjectId,
            CardId = item.CardId,
            SkillId = string.Equals(item.SignalId, AuraCgSignals.RoleSkillCommitted, StringComparison.OrdinalIgnoreCase)
                ? item.CardId : "",
            RoleId = string.Equals(item.SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase)
                ? item.SubjectId : "",
            OwnerInstanceId = item.OwnerInstanceId,
            ActionSequence = item.ActionSequence,
            EventToken = item.EventToken,
            ScenePlan = item.ScenePlan
        };
        signal.Normalize();
        var request = CreateRequest(entry, imageResource, imagePath, signal, disableSync: true);
        request.IssuerPlayerId = item.IssuerPlayerId;
        request.SkillCgPlayId = item.SkillCgPlayId;
        return request;
    }

    private static bool MatchesNetworkTarget(AuraCgRegistryEntry entry, SkillCgNetworkEvent item)
    {
        // The producing peer has already evaluated rich local facts and metrics.
        // Peers receive only the resolved registered identity and minimum scene
        // plan, so network validation deliberately does not require source data.
        return AuraCgRegistryQueryService.MatchesResolvedIdentity(
            entry,
            item.SignalId,
            item.SubjectType,
            item.SubjectId);
    }

    internal static bool MediaExists(string mediaType, string path, string bundlePath)
    {
        if (!string.IsNullOrWhiteSpace(bundlePath))
        {
            return true;
        }

        if (string.Equals(mediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
        {
            return Directory.Exists(path) || File.Exists(path);
        }

        return File.Exists(path);
    }

    private static string SignalForLegacyKind(string kind)
    {
        if (string.Equals(kind, "cardUse", StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSignals.CardUsePresentationCommitted;
        }

        if (string.Equals(kind, "feast", StringComparison.OrdinalIgnoreCase))
        {
            return AuraCgSignals.RoleFeastCompleted;
        }

        return AuraCgSignals.RoleSkillCommitted;
    }

    private static string ProviderIdentity(AuraCgRegistryEntry entry)
    {
        return entry.OwnerModId + ".SkillCG." + entry.CgId;
    }
}
