namespace SunExp.Dll.GameApi;

public static class CombatVarApi
{
    public static int GetInt(string key, int fallback = 0)
    {
        var map = FightManager.Instance?.TempVarsMap;
        if (map == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        return map.TryGetValue(key, out var value) ? value : fallback;
    }

    public static int SetInt(string key, int value)
    {
        var map = FightManager.Instance?.TempVarsMap;
        if (map == null || string.IsNullOrWhiteSpace(key))
        {
            return value;
        }

        map[key] = value;
        return value;
    }

    public static int AddInt(string key, int amount)
    {
        return SetInt(key, GetInt(key) + amount);
    }
}
