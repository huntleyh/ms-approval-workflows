namespace ApprovalWorkflow.Web.Models;

/// <summary>
/// Maps to cr_approvaltier_progress.
/// One row per tier per request — created when a request is submitted.
/// </summary>
public class ApprovalTierProgress
{
    public Guid   Id                { get; set; }
    public Guid   ApprovalRequestId { get; set; }
    public Guid   WorkflowTierId    { get; set; }
    public int    TierNumber        { get; set; }
    public string TierName          { get; set; } = string.Empty;

    public TierStatus   Status           { get; set; } = TierStatus.Waiting;
    public string       AssignedApprover { get; set; } = string.Empty;

    public string ApprovalTokenHash { get; set; } = string.Empty;
    public bool   TokenConsumed     { get; set; }

    public string? CallbackUrl       { get; set; }
    public string? ReviewUrl         { get; set; }  // Signed review link for current token
    public TierDecision Decision     { get; set; } = TierDecision.Pending;
    public string? ApproverComments  { get; set; }
    public DateTime? DecisionDate    { get; set; }
    public DateTime? LastRemindedAt  { get; set; }
    public string? DelegatedFrom     { get; set; }
    public DateTime? CreatedOn       { get; set; }

    // Expiry pulled from the workflow tier config (hours) — resolved at runtime
    public int ExpiryHours { get; set; } = 72;
}

public enum TierStatus
{
    Waiting  = 1,
    Pending  = 2,
    Approved = 3,
    Rejected = 4,
    Expired  = 5,
    Skipped  = 6
}

public enum TierDecision
{
    Pending  = 1,
    Approved = 2,
    Rejected = 3
}
