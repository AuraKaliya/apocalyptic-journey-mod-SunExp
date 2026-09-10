using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssCardRewardTransaction
{
    public static bool Apply(IDataConfig card, Func<bool> mutate, Action commit, Action? rollbackAttachment = null)
    {
        var previous = new Dictionary<string, string>(card.Vars);
        void Restore()
        {
            card.Vars.Clear();
            foreach (var pair in previous) card.Vars[pair.Key] = pair.Value;
            rollbackAttachment?.Invoke();
        }
        try
        {
            if (!mutate()) { Restore(); return false; }
            commit();
            return true;
        }
        catch
        {
            Restore();
            throw;
        }
    }
}
