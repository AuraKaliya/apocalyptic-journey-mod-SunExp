using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.GameApi;

public static class EndlessAbyssCardPreviewApi
{
    public static DataConfig Snapshot(IDataConfig card)
    {
        var data = new Dictionary<string, string>(card.data, StringComparer.Ordinal);
        foreach (var key in data.Keys.Where(key => key.EndsWith("Script", StringComparison.Ordinal)).ToArray()) data[key] = "";
        var snapshot = new DataConfig(data, new Dictionary<string, string>(card.Vars), ifPreCompile: false, type: DataType.Card);
        snapshot.Vars["InstanceID"] = card.InstanceID;
        // SetCardMsg normally executes InitScript. A read-only picker must not
        // run gameplay scripts, reset instance tags or register battle listeners.
        snapshot.scriptExecutor.ScriptDict["InitScript"] = (Action)(() => { });
        return snapshot;
    }
}
