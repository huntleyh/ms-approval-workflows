namespace ApprovalWorkflow.Web.Models;

/// <summary>Maps to cr_approvalrequest. One row per submitted document.</summary>
public class ApprovalRequest
{
    public Guid   Id                    { get; set; }
    public string Title                 { get; set; } = string.Empty;
    public string RequestedBy           { get; set; } = string.Empty;

    // Lookup → cr_workflowdefinition
    public Guid   WorkflowDefinitionId  { get; set; }
    public string? WorkflowDefinitionName { get; set; } // denormalised

    public ApprovalStatus Status           { get; set; } = ApprovalStatus.Pending;
    public int            CurrentTierNumber { get; set; } = 1;

    public string? LogicAppRunId  { get; set; }
    public string? LogicAppRunUrl { get; set; }

    public string  DocumentTitle   { get; set; } = string.Empty;
    public string? DocumentSummary { get; set; }
    public string? DocumentContent { get; set; }

    public DateTime ExpiryDate  { get; set; }
    public int      ResetCount  { get; set; }
    public DateTime? CreatedOn  { get; set; }
}

public enum ApprovalStatus
{
    Pending  = 1,
    Approved = 2,
    Rejected = 3,
    Reset    = 4,
    Expired  = 5
}
