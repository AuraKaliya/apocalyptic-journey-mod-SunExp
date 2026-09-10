using System;
using System.Collections.Generic;
using System.IO;
using Data.Save;
using Newtonsoft.Json;
using Terrias.Dll.GameApi;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssChoiceStore
{
    public const string Shock = "shock";
    public const string Milestone = "milestone";

    public static EndlessAbyssChoiceState Load(string kind, string key, IReadOnlyList<string> pool)
    {
        var save = GameSaveManager.GetNowSave() ?? throw new InvalidOperationException("Adventure save is unavailable.");
        if (save.GameVars.TryGetValue(StorageKey(kind), out var json) && !string.IsNullOrWhiteSpace(json))
        {
            var state = JsonConvert.DeserializeObject<EndlessAbyssChoiceState>(json)
                ?? throw new InvalidDataException("Abyss choice state is missing.");
            state.Validate(pool);
            if (state.Key == key) return state;
        }
        var created = EndlessAbyssChoiceState.Create(key, pool, PickIndex);
        Save(kind, created);
        return created;
    }

    public static void Save(string kind, EndlessAbyssChoiceState state)
    {
        var save = GameSaveManager.GetNowSave() ?? throw new InvalidOperationException("Adventure save is unavailable.");
        save.SetValue(StorageKey(kind), JsonConvert.SerializeObject(state));
    }

    public static int PickIndex(int count)
    {
        if (count <= 1) return 0;
        return (int)((uint)(MapManager.Instance?.NowDice ?? Dice.Default).Roll().Value % (uint)count);
    }

    private static string StorageKey(string kind)
    {
        var player = PlayerApi.LocalNetworkPlayerId();
        if (string.IsNullOrWhiteSpace(player)) player = RoleTable.Instance?.Id ?? "solo";
        return "Terrias_EndlessAbyssChoicesV1:" + kind + ":" + player;
    }
}
