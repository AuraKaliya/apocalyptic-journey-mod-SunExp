using System.Runtime.CompilerServices;

namespace Terrias.Dll.Mechanics;

// A native hand view is created for each successful draw, including redraws of
// the same deck instance. Failed/queued draws and catalogue views never enter here.
public sealed class ColumbinaCardGainLedger
{
    private ConditionalWeakTable<object, object> materializations = new();

    public bool TryRecord(object? handView)
    {
        if (handView == null || materializations.TryGetValue(handView, out _)) return false;
        materializations.Add(handView, new object());
        return true;
    }

    public void Clear() => materializations = new ConditionalWeakTable<object, object>();
}
