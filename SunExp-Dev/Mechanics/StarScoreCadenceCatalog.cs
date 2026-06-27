using System.Collections.Generic;
using System.Linq;

namespace SunExp.Dll.Mechanics;

public sealed class StarScoreCadenceInfo
{
    public StarScoreCadenceInfo(string pattern, string combination, string title, string effect, bool isDefault = false)
    {
        Pattern = pattern ?? "";
        Combination = combination ?? "";
        Title = title ?? "";
        Effect = effect ?? "";
        IsDefault = isDefault;
    }

    public string Pattern { get; }

    public string Combination { get; }

    public string Title { get; }

    public string Effect { get; }

    public bool IsDefault { get; }

    public string DisplayText => Combination + "\uff1a" + Title + "\u3002" + Effect;
}

public static class StarScoreCadenceCatalog
{
    private static readonly StarScoreNote[] NoteOrder =
    {
        StarScoreNote.Opening,
        StarScoreNote.Sustain,
        StarScoreNote.Turn,
        StarScoreNote.Close
    };

    private static readonly Dictionary<string, StarScoreCadenceInfo> NamedCadences = new()
    {
        [StarScoreNoteCodes.Opening + StarScoreNoteCodes.Opening + StarScoreNoteCodes.Opening] =
            Create("SSS", "\u6025\u677f", "\u62bd1\u5f20\u724c\uff1b\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1"),
        [StarScoreNoteCodes.Sustain + StarScoreNoteCodes.Sustain + StarScoreNoteCodes.Sustain] =
            Create("UUU", "\u957f\u97f3", "\u53cb\u65b9\u5168\u4f53\u62a4\u76fe+8\uff1b\u968f\u673a\u6b63\u9762Buff+2"),
        [StarScoreNoteCodes.Turn + StarScoreNoteCodes.Turn + StarScoreNoteCodes.Turn] =
            Create("TTT", "\u5931\u8c03", "\u654c\u65b9\u5168\u4f53\u8d1f\u9762Buff\u5c42\u6570+2"),
        [StarScoreNoteCodes.Close + StarScoreNoteCodes.Close + StarScoreNoteCodes.Close] =
            Create("CCC", "\u7ec8\u6b62\u5f0f", "\u7ed9\u654c\u65b9\u5168\u4f538+\u53cc\u65b9Buff\u79cd\u7c7b\u603b\u6570\u4f24\u5bb3"),
        [StarScoreNoteCodes.Opening + StarScoreNoteCodes.Sustain + StarScoreNoteCodes.Turn] =
            Create("SUT", "\u8c03\u5f8b", "\u9b54\u80fd+1\uff1b\u53cb\u65b9\u5168\u4f53\u4f59\u97f3+1"),
        [StarScoreNoteCodes.Sustain + StarScoreNoteCodes.Turn + StarScoreNoteCodes.Close] =
            Create("UTC", "\u5408\u594f", "\u7ed9\u654c\u65b9\u5168\u4f53\u9020\u621010\u4f24\u5bb3\uff1b\u83b7\u5f9710*\u654c\u4eba\u6570\u91cf\u62a4\u76fe"),
        [StarScoreNoteCodes.Turn + StarScoreNoteCodes.Sustain + StarScoreNoteCodes.Opening] =
            Create("TUS", "\u56de\u65f6", "\u6e05\u9664\u53cb\u65b9\u5168\u4f53\u8d1f\u9762\u6548\u679c\uff1b\u81ea\u8eab\u6bcf\u6e05\u9664\u4e00\u4e2a\u62bd1\u5f20\u724c")
    };

    public static IReadOnlyList<StarScoreCadenceInfo> CandidatesForPrefix(IReadOnlyList<StarScoreNote> notes)
    {
        var prefix = NormalizePrefix(notes);
        if (prefix.Count >= 3)
        {
            return new[] { Resolve(prefix) };
        }

        if (prefix.Count == 2)
        {
            return NoteOrder
                .Select(note => Resolve(prefix.Concat(new[] { note }).ToList()))
                .ToList();
        }

        if (prefix.Count == 1)
        {
            var prefixPattern = StarScoreNoteCodes.PatternFromNotes(prefix);
            var named = NamedCadences.Values
                .Where(cadence => cadence.Pattern.StartsWith(prefixPattern, System.StringComparison.Ordinal))
                .OrderBy(cadence => cadence.Pattern, System.StringComparer.Ordinal)
                .ToList();
            named.Add(DefaultForPrefix(prefix));
            return named;
        }

        var all = NamedCadences.Values
            .OrderBy(cadence => cadence.Pattern, System.StringComparer.Ordinal)
            .ToList();
        all.Add(DefaultForPrefix(prefix));
        return all;
    }

    public static StarScoreCadenceInfo Resolve(IReadOnlyList<StarScoreNote> notes)
    {
        var normalized = NormalizePrefix(notes);
        var pattern = StarScoreNoteCodes.PatternFromNotes(normalized);
        return NamedCadences.TryGetValue(pattern, out var cadence)
            ? cadence
            : DefaultForPattern(pattern);
    }

    public static string CurrentStateText(IReadOnlyList<StarScoreNote> notes)
    {
        var normalized = NormalizePrefix(notes);
        if (normalized.Count == 0)
        {
            return "\u65e0";
        }

        return string.Join("", normalized.Select(DisplayName));
    }

    public static string DisplayName(StarScoreNote note)
    {
        return note switch
        {
            StarScoreNote.Opening => "\u542f",
            StarScoreNote.Sustain => "\u627f",
            StarScoreNote.Turn => "\u8f6c",
            StarScoreNote.Close => "\u5408",
            _ => ""
        };
    }

    private static StarScoreCadenceInfo Create(string pattern, string title, string effect)
    {
        return new StarScoreCadenceInfo(pattern, CombinationForPattern(pattern), title, effect);
    }

    private static StarScoreCadenceInfo DefaultForPattern(string pattern)
    {
        return new StarScoreCadenceInfo(
            pattern,
            string.IsNullOrWhiteSpace(pattern) ? "\u5176\u5b83" : CombinationForPattern(pattern),
            "\u4e09\u58f0\u548c\u5f26",
            "\u4f59\u97f3+1\uff1b\u62bd1\u5f20\u724c",
            isDefault: true);
    }

    private static StarScoreCadenceInfo DefaultForPrefix(IReadOnlyList<StarScoreNote> prefix)
    {
        var pattern = StarScoreNoteCodes.PatternFromNotes(prefix);
        var combination = string.IsNullOrWhiteSpace(pattern)
            ? "\u5176\u5b83"
            : CombinationForPattern(pattern) + "\u5176\u5b83";
        return new StarScoreCadenceInfo(
            pattern,
            combination,
            "\u4e09\u58f0\u548c\u5f26",
            "\u4f59\u97f3+1\uff1b\u62bd1\u5f20\u724c",
            isDefault: true);
    }

    private static string CombinationForPattern(string pattern)
    {
        return string.Join("", (pattern ?? "").Select(NoteNameForCode));
    }

    private static string NoteNameForCode(char code)
    {
        return code switch
        {
            'S' => "\u542f",
            'U' => "\u627f",
            'T' => "\u8f6c",
            'C' => "\u5408",
            _ => ""
        };
    }

    private static List<StarScoreNote> NormalizePrefix(IReadOnlyList<StarScoreNote>? notes)
    {
        return notes == null
            ? new List<StarScoreNote>()
            : notes.Take(3).ToList();
    }
}
