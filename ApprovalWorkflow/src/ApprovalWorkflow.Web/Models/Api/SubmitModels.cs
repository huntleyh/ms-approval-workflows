namespace ApprovalWorkflow.Web.Models;

// ── Form binding model for Submit.cshtml ────────────────────────────────────

public class SubmitRequestModel
{
    public string DocumentTitle      { get; set; } = string.Empty;
    public string? DocumentSummary   { get; set; }
    public string DocumentContent    { get; set; } = string.Empty;
    public Guid   WorkflowDefinitionId { get; set; }
    public string RequestedBy        { get; set; } = string.Empty;
}

public class SubmitResult
{
    public Guid   RequestId       { get; set; }
    public int    CurrentTier     { get; set; }
    public string Status          { get; set; } = string.Empty;
    public string ReviewUrl       { get; set; } = string.Empty;
    public string TierName        { get; set; } = string.Empty;
    public string ApproverEmails  { get; set; } = string.Empty;
    public List<WorkflowTier> AllTiers { get; set; } = new();
}

public class AdvanceResult
{
    public bool IsComplete        { get; set; }
    public bool TierStillPending  { get; set; }   // true = more approvers still needed on this tier
    public int? NextTierNumber    { get; set; }
    public string? NextTierName   { get; set; }
}
