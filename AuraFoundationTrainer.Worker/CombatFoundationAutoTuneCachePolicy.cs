using AuraCombatAi.Shared;

namespace AuraFoundationTrainer.Worker;

internal sealed class CombatFoundationAutoTuneCachePolicy
{
    public CombatFoundationAutoTuneCachePolicy(bool reuseEnabled)
    {
        ReuseEnabled = reuseEnabled;
    }

    public bool ReuseEnabled { get; }

    public bool ShouldLoad(string path)
    {
        return ReuseEnabled && File.Exists(path);
    }

    public bool ShouldPersist(CombatFoundationAutoTuneResult? result)
    {
        return ReuseEnabled && result is { LowConfidence: false };
    }
}
