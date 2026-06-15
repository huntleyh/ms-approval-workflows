namespace ApprovalWorkflow.Web.Models;

// ── Request body: LA-1 trigger (your app → Logic App) ───────────────────────

public class InitiateRequest
{
    public string RequestId            { get; set; } = string.Empty;
    public string WorkflowDefinitionId { get; set; } = string.Empty;
    public int    CurrentTierNumber    { get; set; }
    public string ApproverEmails       { get; set; } = string.Empty;
    public string ApproverType         { get; set; } = string.Empty;
    public string DocumentUrl          { get; set; } = string.Empty;
    public string TierName             { get; set; } = string.Empty;
    public string Title                { get; set; } = string.Empty;
    public string RequestedBy          { get; set; } = string.Empty;
}

// ── Request body: LA-1 → /api/approval/tier-callback ────────────────────────

public class TierCallbackRequest
{
    public Guid   ApprovalRequestId { get; set; }
    public int    TierNumber        { get; set; }
    public string CallbackUrl       { get; set; } = string.Empty;
}

// ── Request body: Review page → /api/approval/decide ────────────────────────

public class DecisionRequest
{
    public string  RawToken  { get; set; } = string.Empty;
    public string  Decision  { get; set; } = string.Empty;  // "Approved" | "Rejected"
    public string? Comments  { get; set; }
    public string  ApprovedBy { get; set; } = string.Empty;
}

// ── Request body: Review page → /api/approval/delegate ──────────────────────

public class DelegationRequest
{
    public string RawToken      { get; set; } = string.Empty;
    public string DelegateTo    { get; set; } = string.Empty;
    public string DelegateType  { get; set; } = string.Empty;  // "Internal" | "External"
    public string Reason        { get; set; } = string.Empty;
}

// ── Request body: Admin → /api/approval/reset ────────────────────────────────

public class ResetRequest
{
    public Guid                ApprovalRequestId { get; set; }
    public string              Reason            { get; set; } = string.Empty;
    public string              TriggeredBy       { get; set; } = string.Empty;
    public List<TierUpdateDto>? TierUpdates      { get; set; }
}

public class TierUpdateDto
{
    public int    TierNumber        { get; set; }
    public string NewApproverEmails { get; set; } = string.Empty;
}

// ── Request body: LA-3 → /api/approval/expire ───────────────────────────────

public class ExpireRequest
{
    public Guid ApprovalRequestId { get; set; }
}

// ── Request body: LA-4 → /api/approval/log-reminder ─────────────────────────

public class LogReminderRequest
{
    public Guid   TierProgressId { get; set; }
    public string ApproverEmail  { get; set; } = string.Empty;
}
