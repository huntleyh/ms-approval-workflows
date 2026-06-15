namespace ApprovalWorkflow.Web.Models;

/// <summary>Maps to cr_approvalauditlog. Append-only.</summary>
public class AuditLogEntry
{
    public Guid   Id                { get; set; }
    public Guid   ApprovalRequestId { get; set; }
    public int    TierNumber        { get; set; }  // 0 = request-level
    public AuditEventType EventType { get; set; }
    public string PerformedBy       { get; set; } = string.Empty;
    public string? PreviousValue    { get; set; }
    public string? NewValue         { get; set; }
    public string? IpAddress        { get; set; }
    public DateTime Timestamp       { get; set; } = DateTime.UtcNow;
    public string? Notes            { get; set; }

    // Denormalised for display in Admin page
    public string? RequestTitle { get; set; }
}

public enum AuditEventType
{
    Submitted        = 1,
    TierStarted      = 2,
    Notified         = 3,
    ReminderSent     = 4,
    PageVisited      = 5,
    CallbackReceived = 6,
    Approved         = 7,
    Rejected         = 8,
    Delegated        = 9,
    TierExpired      = 10,
    RequestExpired   = 11,
    Reset            = 12,
    FlowResumed      = 13,
    FlowCompleted    = 14,
    TokenConsumed    = 15
}
