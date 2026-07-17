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
    private readonly string skillKind;
    private readonly string cardUseKind;
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
        this.skillKind = skillKind ?? "";
        this.cardUseKind = cardUseKind ?? "";
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
        if (!AuraCgRegistryQueryService.MatchesTrigger(entry, kind, context, consumerCanPlay))
        {
            return null;
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
            || !IsSupportedNetworkKind(entry)
            || !AuraCgRegistryQueryService.MatchesCard(entry, item.CardId)
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

        var imageResource = AuraCgRegistryQueryService.ResolveImageResource(entry);
        var imagePath = imagePathResolver(entry.OwnerModId, imageResource);
        if (!MediaExists(entry.Media.Type, imagePath, entry.Media.BundlePath))
        {
            return null;
        }

        var request = CreateRequest(entry, imageResource, imagePath, new SkillCgTriggerContext
        {
            CardId = item.CardId,
            OwnerInstanceId = item.OwnerInstanceId,
            ActionSequence = item.ActionSequence,
            EventToken = item.EventToken
        }, disableSync: true);
        request.IssuerPlayerId = item.IssuerPlayerId;
        request.SkillCgPlayId = item.SkillCgPlayId;
        return request;
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

    private bool IsSupportedNetworkKind(AuraCgRegistryEntry entry)
    {
        return AuraCgRegistryQueryService.IsRegisteredEntry(entry, skillKind)
               || AuraCgRegistryQueryService.IsRegisteredEntry(entry, cardUseKind);
    }

    private static string ProviderIdentity(AuraCgRegistryEntry entry)
    {
        return entry.OwnerModId + ".SkillCG." + entry.CgId;
    }
}
