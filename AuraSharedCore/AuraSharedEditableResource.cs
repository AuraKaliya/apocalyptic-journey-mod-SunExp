namespace AuraShared.Core;

public static class AuraSharedEditableResource
{
    public static string StageTemporary(
        string callerId,
        string logicalId,
        string extension,
        byte[] content)
    {
        AuraSharedRuntime.Initialize(null, callerId);
        var coordinator = new AuraSharedEditableResourceCoordinator(AuraSharedPaths.RootDirectory);
        var path = coordinator.StageTemporary(callerId, logicalId, extension, content);
        AuraSharedLog.Info("AuraShared.EditableResource", "staged temporary resource: owner=" + callerId
            + ", logicalId=" + logicalId
            + ", bytes=" + (content?.Length ?? 0));
        return path;
    }

    public static void ReleaseTemporary(string callerId, string path)
    {
        AuraSharedRuntime.Initialize(null, callerId);
        var coordinator = new AuraSharedEditableResourceCoordinator(AuraSharedPaths.RootDirectory);
        coordinator.ReleaseTemporary(path);
    }

    public static AuraSharedEditableResourceResult Seed(string callerId, AuraSharedEditableResourceRequest request)
    {
        AuraSharedRuntime.Initialize(null, callerId);
        var coordinator = new AuraSharedEditableResourceCoordinator(AuraSharedPaths.RootDirectory);
        var result = coordinator.Seed(request);
        var message = "owner=" + request.OwnerModId
                      + ", system=" + request.System
                      + ", logicalId=" + request.LogicalId
                      + ", destination=" + request.DestinationRelativePath
                      + ", status=" + result.Status
                      + ", changed=" + result.Changed
                      + ", customized=" + result.Customized
                      + ", seedHash=" + Display(result.SeedHash)
                      + ", contentHash=" + Display(result.ContentHash);
        if (result.Success)
        {
            AuraSharedLog.Info("AuraShared.EditableResource", message);
        }
        else
        {
            AuraSharedLog.Warn("AuraShared.EditableResource", message + ", failure=" + result.Message);
        }

        return result;
    }

    private static string Display(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
    }
}
