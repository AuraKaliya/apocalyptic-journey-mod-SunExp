using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;


namespace Terrias.Dll.Contracts;

[Serializable]
public class ProjectionSummonResultSnapshot
{
    public int ServerProtocolVersion { get; set; } = TerriasProtocolContract.ProjectionVersion;
    public int ServerBattleEpoch { get; set; }
    public string ServerCardModelVersion { get; set; } = TerriasProtocolContract.ProjectionCardModel;
    public string Token { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string OwnerPlayerId { get; set; } = "";
    public string StatusId { get; set; } = "";
    public string Generation { get; set; } = "";
    public bool Accepted { get; set; }
    public bool Terminal { get; set; } = true;
    public ProjectionSummonFailureCode FailureCode { get; set; }
    public ProjectionSummonFailureCategory FailureCategory { get; set; }
    public bool Retryable { get; set; }
    public bool RefundCard { get; set; }
    public string Detail { get; set; } = "";
}
