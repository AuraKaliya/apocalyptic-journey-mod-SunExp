using System;
using System.Collections.Generic;

namespace Terrias.Dll.Mechanics;

public static class SpiritAdventurePartyRules
{
    public static bool Remove(IList<string>? slots, ref string activeSpiritUid, string uid)
    {
        var target = (uid ?? "").Trim();
        if (target.Length == 0)
        {
            return false;
        }

        var changed = false;
        if (slots != null)
        {
            for (var index = 0; index < slots.Count; index++)
            {
                if (!string.Equals(slots[index], target, StringComparison.Ordinal))
                {
                    continue;
                }

                slots[index] = "";
                changed = true;
            }
        }

        if (string.Equals(activeSpiritUid, target, StringComparison.Ordinal))
        {
            activeSpiritUid = "";
            changed = true;
        }

        return changed;
    }
}
