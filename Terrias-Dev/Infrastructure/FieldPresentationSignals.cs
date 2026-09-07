using System;

namespace Terrias.Dll.Infrastructure;

/// <summary>Optional local feedback after a committed field-related mechanic.</summary>
public static class FieldPresentationSignals
{
    public static event Action<TerriasFieldId>? Triggered;

    public static void Trigger(TerriasFieldId field)
    {
        var subscribers = Triggered;
        if (subscribers == null) return;
        foreach (Action<TerriasFieldId> subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(field); }
            catch (Exception ex) { TerriasLog.Warn("[FieldPresentation] feedback listener failed: " + ex.Message); }
        }
    }
}
