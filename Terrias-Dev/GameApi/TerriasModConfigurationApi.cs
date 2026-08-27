using System;
using Newtonsoft.Json.Linq;
using Witch.Core;
using Witch.Mod;

namespace Terrias.Dll.GameApi;

public static class TerriasModConfigurationApi
{
    public static bool TryGetBoolean(ModConfig modConfig, string key, out bool value, out string diagnostic)
    {
        value = false;
        diagnostic = "";
        if (modConfig == null)
        {
            diagnostic = "mod configuration is unavailable";
            return false;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            diagnostic = "configuration key is empty";
            return false;
        }

        try
        {
            var manager = Singleton<GameConfigManager>.Instance;
            if (manager == null || !manager.TryGetModOwnConfiguration(modConfig, out var configuration))
            {
                diagnostic = "Configuration.json was not loaded";
                return false;
            }
            if (configuration?.ExtensionData == null
                || !configuration.ExtensionData.TryGetValue(key, out var rawValue))
            {
                diagnostic = "missing key " + key;
                return false;
            }

            if (rawValue is bool boolean)
            {
                value = boolean;
                return true;
            }
            if (rawValue is JValue jsonValue && jsonValue.Type == JTokenType.Boolean)
            {
                value = jsonValue.Value<bool>();
                return true;
            }
            if (rawValue is JToken token && token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }
            if (bool.TryParse(Convert.ToString(rawValue), out boolean))
            {
                value = boolean;
                return true;
            }

            diagnostic = "key " + key + " must be a JSON boolean";
            return false;
        }
        catch (Exception ex)
        {
            diagnostic = ex.Message;
            return false;
        }
    }
}
