using System;
using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class NativeContentVisibilityLease<T>
    where T : class
{
    private sealed class Entry
    {
        public T Target { get; set; } = null!;

        public bool WasVisible { get; set; }
    }

    private readonly List<Entry> entries = new();
    private Action<T, bool>? setVisible;
    private Action<Exception>? onError;

    internal bool IsActive { get; private set; }

    internal int Count => entries.Count;

    internal bool Acquire(
        IEnumerable<T> targets,
        Func<T, bool> getVisible,
        Action<T, bool> applyVisible,
        Action<Exception>? error = null)
    {
        if (IsActive)
        {
            return false;
        }

        if (targets == null) throw new ArgumentNullException(nameof(targets));
        if (getVisible == null) throw new ArgumentNullException(nameof(getVisible));
        if (applyVisible == null) throw new ArgumentNullException(nameof(applyVisible));

        setVisible = applyVisible;
        onError = error;
        var seen = new HashSet<T>();
        foreach (var target in targets)
        {
            if (target == null || !seen.Add(target))
            {
                continue;
            }

            try
            {
                var wasVisible = getVisible(target);
                entries.Add(new Entry
                {
                    Target = target,
                    WasVisible = wasVisible
                });
                if (wasVisible)
                {
                    applyVisible(target, false);
                }
            }
            catch (Exception ex)
            {
                error?.Invoke(ex);
            }
        }

        IsActive = true;
        return true;
    }

    internal bool Release()
    {
        if (!IsActive)
        {
            return false;
        }

        var restore = entries.ToArray();
        var applyVisible = setVisible;
        var error = onError;
        entries.Clear();
        setVisible = null;
        onError = null;
        IsActive = false;

        if (applyVisible == null)
        {
            return true;
        }

        foreach (var entry in restore)
        {
            try
            {
                applyVisible(entry.Target, entry.WasVisible);
            }
            catch (Exception ex)
            {
                error?.Invoke(ex);
            }
        }

        return true;
    }
}
