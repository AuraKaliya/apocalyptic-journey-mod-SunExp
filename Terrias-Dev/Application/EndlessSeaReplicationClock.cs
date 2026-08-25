using System;

namespace Terrias.Dll.Application;

public sealed class EndlessSeaReplicationClock
{
    public string Session { get; private set; } = "";

    public int Generation { get; private set; } = -1;

    public bool CanAccept(string session, int generation)
    {
        if (string.IsNullOrWhiteSpace(session) || generation < 0)
        {
            return false;
        }
        return !string.Equals(Session, session, StringComparison.Ordinal)
               || generation >= Generation;
    }

    public bool Commit(string session, int generation)
    {
        if (!CanAccept(session, generation)) return false;
        if (!string.Equals(Session, session, StringComparison.Ordinal))
        {
            Session = session;
            Generation = generation;
            return true;
        }
        Generation = Math.Max(Generation, generation);
        return true;
    }
}
