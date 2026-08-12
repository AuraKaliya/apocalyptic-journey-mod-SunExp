using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Terrias.Dll.Mechanics;

public sealed class ProjectionDeckCardRecipe
{
    public ProjectionDeckCardRecipe(
        string cardId,
        string definitionType = "Card",
        string attachmentId = "",
        string attachmentType = "EnchTag")
    {
        CardId = (cardId ?? "").Trim();
        DefinitionType = string.IsNullOrWhiteSpace(definitionType)
            ? "Card"
            : definitionType.Trim();
        AttachmentId = (attachmentId ?? "").Trim();
        AttachmentType = string.IsNullOrWhiteSpace(attachmentType)
            ? "EnchTag"
            : attachmentType.Trim();
    }

    public string CardId { get; }

    public string DefinitionType { get; }

    public string AttachmentId { get; }

    public string AttachmentType { get; }

    internal string Identity => CardId
                                + "\u001f"
                                + DefinitionType
                                + "\u001f"
                                + AttachmentId
                                + "\u001f"
                                + AttachmentType;
}

public sealed class ProjectionDeckRecipe
{
    public const int MaximumCards = 512;
    public const int DefaultMaxPower = 3;
    public const int DefaultDrawCount = 5;

    public ProjectionDeckRecipe(IEnumerable<ProjectionDeckCardRecipe>? cards)
    {
        Cards = (cards ?? Array.Empty<ProjectionDeckCardRecipe>())
            .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId))
            .GroupBy(card => card.Identity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group)
            .Take(MaximumCards)
            .ToArray();
        Hash = ProjectionDeckRecipeHash.Compute(Cards);
        BaseHash = ProjectionDeckRecipeHash.ComputeBase(Cards);
    }

    public IReadOnlyList<ProjectionDeckCardRecipe> Cards { get; }

    public string Hash { get; }

    /// <summary>
    /// Diagnostic-only identity sent by the client. The host never trusts it
    /// as executable state and always builds the projection from RoleTable.
    /// </summary>
    public string BaseHash { get; }

    public int ShuffleSeed
    {
        get
        {
            if (Hash.Length < 8
                || !uint.TryParse(
                    Hash.Substring(0, 8),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
            {
                return Cards.Count;
            }
            return unchecked((int)value);
        }
    }
}

public static class ProjectionDeckRecipeHash
{
    public static string Compute(IEnumerable<ProjectionDeckCardRecipe>? cards)
    {
        var canonical = string.Join(
            "\n",
            (cards ?? Array.Empty<ProjectionDeckCardRecipe>())
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId))
                .GroupBy(card => card.Identity, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key + "\u001e" + group.Count()));
        using var sha = SHA256.Create();
        return string.Concat(
            sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))
                .Select(value => value.ToString("x2")));
    }

    public static string ComputeBase(IEnumerable<ProjectionDeckCardRecipe>? cards)
    {
        var canonical = string.Join(
            "\n",
            (cards ?? Array.Empty<ProjectionDeckCardRecipe>())
                .Where(card => card != null && !string.IsNullOrWhiteSpace(card.CardId))
                .GroupBy(card => card.CardId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key + "\u001e" + group.Count()));
        using var sha = SHA256.Create();
        return string.Concat(
            sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))
                .Select(value => value.ToString("x2")));
    }
}

public enum ProjectionRoleDeckSourceKind
{
    None,
    LocalRole,
    ServerRole
}

public static class ProjectionRoleDeckSourcePolicy
{
    public static ProjectionRoleDeckSourceKind Select(
        bool multiplayer,
        bool ownerIsLocalStatus,
        bool localRoleMatchesOwner,
        bool serverRoleAvailable)
    {
        if (!multiplayer)
        {
            return localRoleMatchesOwner
                ? ProjectionRoleDeckSourceKind.LocalRole
                : ProjectionRoleDeckSourceKind.None;
        }
        if (ownerIsLocalStatus && localRoleMatchesOwner)
        {
            return ProjectionRoleDeckSourceKind.LocalRole;
        }
        return serverRoleAvailable
            ? ProjectionRoleDeckSourceKind.ServerRole
            : ProjectionRoleDeckSourceKind.None;
    }
}
