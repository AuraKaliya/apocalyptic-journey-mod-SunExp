using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine.InputSystem;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Contains the version-sensitive part of releasing the native ChatUI input
/// subscriptions. The game registers a compiler-generated Submit callback in
/// ChatUI.Awake but does not remove it from ChatUI.OnDestroy.
/// </summary>
internal static class ChatUiLifecycleApi
{
    private const BindingFlags CallbackMethodFlags = BindingFlags.Instance
                                                     | BindingFlags.NonPublic
                                                     | BindingFlags.DeclaredOnly;

    internal static bool TryDetachInputCallbacks(ChatUI chatUi, out string detail)
    {
        detail = "";
        if (chatUi == null)
        {
            detail = "ChatUI is unavailable.";
            return false;
        }

        try
        {
            var localSubmitDetached = TryDetachLocalSubmit(chatUi);
            var callbackMethod = ResolveGlobalSubmitCallback();
            if (callbackMethod == null)
            {
                detail = "global Submit callback method was not resolved; localSubmit="
                         + localSubmitDetached + ".";
                return false;
            }

            var callback = (Action<InputAction.CallbackContext>)Delegate.CreateDelegate(
                typeof(Action<InputAction.CallbackContext>),
                chatUi,
                callbackMethod,
                throwOnBindFailure: true);
            var submit = KeyManager.playerAction?.Main.Submit;
            if (submit == null)
            {
                detail = "global Submit input action is unavailable; localSubmit="
                         + localSubmitDetached + ".";
                return false;
            }

            submit.performed -= callback;
            detail = "callback=" + callbackMethod.Name + ", localSubmit=" + localSubmitDetached + ".";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    private static bool TryDetachLocalSubmit(ChatUI chatUi)
    {
        var input = chatUi.transform.Find("Input")?.GetComponent<TMP_InputField>();
        if (input == null)
        {
            return false;
        }

        input.onSubmit.RemoveListener(chatUi.SendChatMessage);
        return true;
    }

    private static MethodInfo? ResolveGlobalSubmitCallback()
    {
        var callbackParameter = typeof(InputAction.CallbackContext);
        var candidates = typeof(ChatUI)
            .GetMethods(CallbackMethodFlags)
            .Where(method => method.ReturnType == typeof(void))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == callbackParameter;
            })
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.FirstOrDefault(method =>
                   method.Name.IndexOf("Awake", StringComparison.OrdinalIgnoreCase) >= 0
                   && method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
               ?? candidates.FirstOrDefault(method =>
                   method.Name.IndexOf("Awake", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? (candidates.Count == 1 ? candidates[0] : null);
    }
}
