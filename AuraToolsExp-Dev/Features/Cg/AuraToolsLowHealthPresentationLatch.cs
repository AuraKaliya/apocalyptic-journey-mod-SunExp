using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.Cg;

internal sealed class AuraToolsLowHealthPresentationLatch
{
    private readonly HashSet<string> enteredStatusIds = new(StringComparer.Ordinal);

    internal bool TryEnter(string statusInstanceId)
    {
        var id = (statusInstanceId ?? "").Trim();
        return id.Length > 0 && enteredStatusIds.Add(id);
    }

    internal void Reset()
    {
        enteredStatusIds.Clear();
    }
}
