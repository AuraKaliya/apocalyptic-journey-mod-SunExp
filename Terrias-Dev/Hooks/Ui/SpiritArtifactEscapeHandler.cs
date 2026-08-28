using System;
using Terrias.Dll.GameApi;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui;

internal sealed class SpiritArtifactEscapeHandler : MonoBehaviour
{
    private Action? close;

    public void Configure(Action action)
    {
        close = action;
        enabled = action != null;
    }

    private void Update()
    {
        if (close == null || !KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.Escape)) return;
        close();
    }
}
