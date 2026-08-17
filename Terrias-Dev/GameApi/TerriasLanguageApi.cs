using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class TerriasLanguageApi
{
    private static readonly string EventName = LanguageEvent.LanguageChange.ToString();

    public static string CurrentLocale
    {
        get
        {
            try
            {
                return TerriasLocale.Normalize(Globals.Language);
            }
            catch
            {
                return TerriasLocale.ZhHans;
            }
        }
    }

    public static void Subscribe(object owner, Action refresh)
    {
        if (owner == null || refresh == null)
        {
            return;
        }

        try
        {
            Singleton<EventCenter>.Instance.RemoveEventListener(EventName, owner);
            Singleton<EventCenter>.Instance.AddEventListener(EventName, refresh, owner);
        }
        catch (Exception ex)
        {
            TerriasLog.WarnOnce("Localization.LanguageSubscribe", "[Localization] language listener unavailable: " + ex.Message);
        }
    }

    public static void Unsubscribe(object owner)
    {
        if (owner == null)
        {
            return;
        }

        try
        {
            Singleton<EventCenter>.Instance.RemoveEventListener(EventName, owner);
        }
        catch
        {
            // The game event center may already be unavailable during teardown.
        }
    }
}
