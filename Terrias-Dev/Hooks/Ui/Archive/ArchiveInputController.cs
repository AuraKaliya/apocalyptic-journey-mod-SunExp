using System;
using Terrias.Dll.GameApi;
using UnityEngine;

namespace Terrias.Dll.Hooks.Ui.Archive;

public sealed class ArchiveInputController : MonoBehaviour
{
    private Action? close;
    private Action<int>? moveCharacter;
    private Action<int>? moveSection;

    public void Initialize(Action close, Action<int> moveCharacter, Action<int> moveSection)
    {
        this.close = close;
        this.moveCharacter = moveCharacter;
        this.moveSection = moveSection;
    }

    private void Update()
    {
        if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.Escape))
        {
            close?.Invoke();
            return;
        }

        if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.Q)
            || KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.LeftArrow))
        {
            moveCharacter?.Invoke(-1);
        }
        else if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.E)
                 || KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.RightArrow))
        {
            moveCharacter?.Invoke(1);
        }

        if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.W)
            || KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.UpArrow))
        {
            moveSection?.Invoke(-1);
        }
        else if (KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.S)
                 || KeyboardInputApi.WasPressedThisFrame(TerriasKeyboardKey.DownArrow))
        {
            moveSection?.Invoke(1);
        }
    }
}
