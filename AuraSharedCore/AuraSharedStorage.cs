using System;

namespace AuraShared.Core;

public static class AuraSharedStorage
{
    public static AuraSharedStorageResponse Read(string callerId, AuraSharedStorageRequest request)
    {
        return Invoke(callerId, "ReadStorageJson", request);
    }

    public static AuraSharedStorageResponse Write(string callerId, AuraSharedStorageRequest request)
    {
        return Invoke(callerId, "WriteStorageJson", request);
    }

    public static AuraSharedChangeFeed GetChanges(string callerId, long sinceSequence)
    {
        try
        {
            var json = AuraSharedRuntime.InvokeComponent(null, callerId, "GetChangesJson", sinceSequence) as string;
            return string.IsNullOrWhiteSpace(json)
                ? new AuraSharedChangeFeed()
                : AuraSharedJson.Deserialize<AuraSharedChangeFeed>(json!) ?? new AuraSharedChangeFeed();
        }
        catch
        {
            return new AuraSharedChangeFeed();
        }
    }

    private static AuraSharedStorageResponse Invoke(string callerId, string method, AuraSharedStorageRequest request)
    {
        try
        {
            var json = AuraSharedJson.Serialize(request);
            var resultJson = AuraSharedRuntime.InvokeComponent(null, callerId, method, json) as string;
            return string.IsNullOrWhiteSpace(resultJson)
                ? new AuraSharedStorageResponse { Success = false, Message = "Shared storage returned no response." }
                : AuraSharedJson.Deserialize<AuraSharedStorageResponse>(resultJson!)
                  ?? new AuraSharedStorageResponse { Success = false, Message = "Shared storage response is invalid." };
        }
        catch (Exception ex)
        {
            return new AuraSharedStorageResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }
}
