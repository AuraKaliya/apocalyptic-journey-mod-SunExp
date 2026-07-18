using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class CardRefreshSettings
{
    [JsonProperty("enabled")]
    public bool Enabled { get; set; }
}
