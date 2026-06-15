namespace ApprovalWorkflow.Web.Models;

/// <summary>Maps to cr_workflowtier. One row = one step in a workflow definition.</summary>
public class WorkflowTier
{
    public Guid   Id                   { get; set; }
    public Guid   WorkflowDefinitionId { get; set; }
    public int    TierNumber           { get; set; }
    public string TierName             { get; set; } = string.Empty;

    /// <summary>Semicolon-separated approver email addresses.</summary>
    public string ApproverEmails     { get; set; } = string.Empty;

    /// <summary>1 = Internal, 2 = External</summary>
    public int ApproverType          { get; set; } = 1;

    public int RequiredApprovals    { get; set; } = 1;
    public int ReminderHours        { get; set; } = 24;
    public int ExpiryHours          { get; set; } = 72;
}

public class WorkflowDefinitionWithTiers
{
    public WorkflowDefinition Definition { get; set; } = new();
    public List<WorkflowTier>  Tiers     { get; set; } = new();
}
