using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public enum StarScoreNote
{
    Opening,
    Sustain,
    Turn,
    Close
}

public static class StarScoreNoteCodes
{
    public const string Opening = "S";
    public const string Sustain = "U";
    public const string Turn = "T";
    public const string Close = "C";

    public static bool TryFromCardId(string id, out StarScoreNote note)
    {
        switch (NormalizeId(id))
        {
            case "stellar_overture_start":
            case TerriasIds.StellarOvertureStartCardId:
                note = StarScoreNote.Opening;
                return true;
            case "stellar_overture_sustain":
            case TerriasIds.StellarOvertureSustainCardId:
                note = StarScoreNote.Sustain;
                return true;
            case "stellar_overture_turn":
            case TerriasIds.StellarOvertureTurnCardId:
                note = StarScoreNote.Turn;
                return true;
            case "stellar_overture_close":
            case TerriasIds.StellarOvertureCloseCardId:
                note = StarScoreNote.Close;
                return true;
            default:
                note = default;
                return false;
        }
    }

    public static bool TryFromPatternCode(string code, out StarScoreNote note)
    {
        switch ((code ?? "").Trim())
        {
            case Opening:
                note = StarScoreNote.Opening;
                return true;
            case Sustain:
                note = StarScoreNote.Sustain;
                return true;
            case Turn:
                note = StarScoreNote.Turn;
                return true;
            case Close:
                note = StarScoreNote.Close;
                return true;
            default:
                note = default;
                return false;
        }
    }

    public static string PatternCode(StarScoreNote note)
    {
        return note switch
        {
            StarScoreNote.Opening => Opening,
            StarScoreNote.Sustain => Sustain,
            StarScoreNote.Turn => Turn,
            StarScoreNote.Close => Close,
            _ => ""
        };
    }

    public static string PatternFromNotes(System.Collections.Generic.IEnumerable<StarScoreNote> notes)
    {
        var result = "";
        foreach (var note in notes)
        {
            result += PatternCode(note);
        }

        return result;
    }

    private static string NormalizeId(string id)
    {
        return (id ?? "").Replace("*", "").Trim();
    }
}
