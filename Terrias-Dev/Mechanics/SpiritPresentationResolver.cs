using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class SpiritLocalizedPresentation
{
    public TerriasLocalizedText Name { get; set; } = new();

    public TerriasLocalizedText Description { get; set; } = new();

    public SpiritLocalizedPresentation Clone()
    {
        return new SpiritLocalizedPresentation
        {
            Name = (Name ?? new TerriasLocalizedText()).Clone(),
            Description = (Description ?? new TerriasLocalizedText()).Clone()
        };
    }
}

public static class SpiritPresentationResolver
{
    public static SpiritLocalizedPresentation Capture(CapturedEnemySnapshot? snapshot)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var row = ResolveEnemyRow(snapshot.EnemyId);
        var name = TerriasLocalizedText.FromRow(row, "Name", snapshot.DisplayName);
        var first = TerriasLocalizedText.FromRow(row, "Description1");
        var second = TerriasLocalizedText.FromRow(row, "Description2");
        return new SpiritLocalizedPresentation
        {
            Name = name,
            Description = Combine(first, second, snapshot.Description)
        };
    }

    public static string Name(SpiritInstance? instance, string? locale = null)
    {
        if (instance == null)
        {
            return "";
        }

        return Name(instance.Snapshot, instance.Presentation, locale);
    }

    public static string Description(SpiritInstance? instance, string? locale = null)
    {
        if (instance == null)
        {
            return "";
        }

        return Description(instance.Snapshot, instance.Presentation, locale);
    }

    public static string Name(CapturedEnemySnapshot? snapshot, string? locale = null)
    {
        return Name(snapshot, null, locale);
    }

    public static string Description(CapturedEnemySnapshot? snapshot, string? locale = null)
    {
        return Description(snapshot, null, locale);
    }

    public static string Name(SpiritDeploymentSnapshot? snapshot, string? locale = null)
        => snapshot == null ? "" : Name(snapshot.Source, snapshot.Presentation, locale);

    public static string Description(SpiritDeploymentSnapshot? snapshot, string? locale = null)
        => snapshot == null ? "" : Description(snapshot.Source, snapshot.Presentation, locale);

    public static TerriasLocalizedText Names(CapturedEnemySnapshot? snapshot)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var live = Capture(snapshot).Name;
        return Merge(live, null, snapshot.DisplayName, snapshot.EnemyId);
    }

    public static TerriasLocalizedText Descriptions(CapturedEnemySnapshot? snapshot)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var live = Capture(snapshot).Description;
        return Merge(live, null, snapshot.Description, "");
    }

    public static TerriasLocalizedText Names(SpiritDeploymentSnapshot? snapshot)
    {
        snapshot ??= new SpiritDeploymentSnapshot();
        var source = snapshot.Source ?? new CapturedEnemySnapshot();
        return Merge(Capture(source).Name, snapshot.Presentation?.Name, source.DisplayName, source.EnemyId);
    }

    public static TerriasLocalizedText Descriptions(SpiritDeploymentSnapshot? snapshot)
    {
        snapshot ??= new SpiritDeploymentSnapshot();
        var source = snapshot.Source ?? new CapturedEnemySnapshot();
        return Merge(Capture(source).Description, snapshot.Presentation?.Description, source.Description, "");
    }

    private static string Name(
        CapturedEnemySnapshot? snapshot,
        SpiritLocalizedPresentation? persisted,
        string? locale)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var live = Capture(snapshot).Name;
        var value = Merge(live, persisted?.Name, snapshot.DisplayName, snapshot.EnemyId)
            .Resolve(locale ?? TerriasLanguageApi.CurrentLocale, snapshot.EnemyId);
        return string.IsNullOrWhiteSpace(value) ? snapshot.EnemyId : value;
    }

    private static string Description(
        CapturedEnemySnapshot? snapshot,
        SpiritLocalizedPresentation? persisted,
        string? locale)
    {
        snapshot ??= new CapturedEnemySnapshot();
        var live = Capture(snapshot).Description;
        return Merge(live, persisted?.Description, snapshot.Description, "")
            .Resolve(locale ?? TerriasLanguageApi.CurrentLocale);
    }

    private static Dictionary<string, string>? ResolveEnemyRow(string enemyId)
    {
        var id = (enemyId ?? "").Trim().TrimStart('*');
        if (id.Length == 0)
        {
            return null;
        }

        try
        {
            return TerriasConfigIndex.Row(DataType.Enemy, id)
                   ?? TerriasConfigIndex.Row(DataType.Enemy, TerriasContentIdCompatibility.Canonicalize(id));
        }
        catch
        {
            return null;
        }
    }

    private static TerriasLocalizedText Combine(
        TerriasLocalizedText first,
        TerriasLocalizedText second,
        string legacyFallback)
    {
        return new TerriasLocalizedText
        {
            ZhHans = Join(first.ZhHans, second.ZhHans),
            ZhHant = Join(first.ZhHant, second.ZhHant),
            English = Join(first.English, second.English),
            Japanese = Join(first.Japanese, second.Japanese),
            LegacyFallback = legacyFallback ?? ""
        };
    }

    private static TerriasLocalizedText Merge(
        TerriasLocalizedText? primary,
        TerriasLocalizedText? secondary,
        string legacyFallback,
        string finalFallback)
    {
        primary ??= new TerriasLocalizedText();
        secondary ??= new TerriasLocalizedText();
        return new TerriasLocalizedText
        {
            ZhHans = First(primary.ZhHans, secondary.ZhHans),
            ZhHant = First(primary.ZhHant, secondary.ZhHant),
            English = First(primary.English, secondary.English),
            Japanese = First(primary.Japanese, secondary.Japanese),
            LegacyFallback = First(primary.LegacyFallback, secondary.LegacyFallback, legacyFallback, finalFallback)
        };
    }

    private static string Join(string left, string right)
    {
        var first = (left ?? "").Trim();
        var second = (right ?? "").Trim();
        if (first.Length == 0)
        {
            return second;
        }

        return second.Length == 0 ? first : first + "\n" + second;
    }

    private static string First(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }
}
