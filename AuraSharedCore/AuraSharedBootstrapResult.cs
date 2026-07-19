using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraShared.Core;

public sealed class AuraSharedBootstrapResult
{
    private readonly List<AuraSharedInstallResponse> responses = new();

    public bool Success => responses.Count > 0 && responses.All(response => response.Success);

    public bool Changed => responses.Any(response => response.Success && response.Changed);

    public int Installed => CountStatus("Installed");

    public int Repaired => CountStatus("Repaired");

    public int Updated => CountStatus("Updated");

    public int Deduplicated => CountStatus("Deduplicated");

    public int PreservedLocal => CountStatus("PreservedLocal");

    public int Conflicts => responses.Count(response => response.Conflict);

    public int Failures => responses.Count(response => !response.Success && !response.Conflict);

    public IReadOnlyList<AuraSharedInstallResponse> Responses => responses;

    public string Summary =>
        "installed=" + Installed
        + ", repaired=" + Repaired
        + ", updated=" + Updated
        + ", deduplicated=" + Deduplicated
        + ", preservedLocal=" + PreservedLocal
        + ", conflicts=" + Conflicts
        + ", failures=" + Failures;

    public static AuraSharedBootstrapResult FromResponses(
        IEnumerable<AuraSharedInstallResponse>? installResponses)
    {
        var result = new AuraSharedBootstrapResult();
        if (installResponses != null)
        {
            result.responses.AddRange(installResponses.Where(response => response != null));
        }

        if (result.responses.Count == 0)
        {
            result.responses.Add(new AuraSharedInstallResponse
            {
                Success = false,
                Status = "Failed",
                Message = "Resource bootstrap returned no install responses."
            });
        }

        return result;
    }

    public static AuraSharedBootstrapResult Failed(string message)
    {
        return FromResponses(new[]
        {
            new AuraSharedInstallResponse
            {
                Success = false,
                Status = "Failed",
                Message = message ?? ""
            }
        });
    }

    private int CountStatus(string status)
    {
        return responses.Count(response =>
            response.Success
            && string.Equals(response.Status, status, StringComparison.OrdinalIgnoreCase));
    }
}
