using UnityEngine;

namespace SunExp.Dll.Hooks.Ui;

public sealed class PooledCombatCardViewMarker : MonoBehaviour
{
    public int Generation { get; set; }

    public string Bucket { get; set; } = "";

    public string ConfigInstanceId { get; set; } = "";

    public bool InUse { get; set; }

    public bool ReleasePending { get; set; }

    public int ReleaseAttempts { get; set; }
}
