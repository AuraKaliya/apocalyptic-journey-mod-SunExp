namespace AuraToolsExp.Dll.Config;

/// <summary>
/// Product availability, not a player preference. Keep event configuration and
/// resources for later development, but do not allow this release to enable it.
/// Role/skill and card CG have independent availability and settings.
/// </summary>
public static class AuraToolsEventCgAvailability
{
    public static bool IsAvailable => false;
    public const string Status = "暂时停用";
    public const string Description = "事件 CG 暂时停用，待后续调整后开放。";
}
