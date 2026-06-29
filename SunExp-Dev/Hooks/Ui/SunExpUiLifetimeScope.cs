using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SunExp.Dll.Hooks.Ui;

public sealed class SunExpUiLifetimeScope : IDisposable
{
    private readonly List<Action> cleanup = new();

    public void Listen(Button? button, UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.AddListener(action);
        cleanup.Add(() => button.onClick.RemoveListener(action));
    }

    public void Add(Action? release)
    {
        if (release != null)
        {
            cleanup.Add(release);
        }
    }

    public void Clear()
    {
        for (var i = cleanup.Count - 1; i >= 0; i--)
        {
            try
            {
                cleanup[i]();
            }
            catch
            {
                // Cleanup must never block UI teardown.
            }
        }

        cleanup.Clear();
    }

    public void Dispose()
    {
        Clear();
    }
}
