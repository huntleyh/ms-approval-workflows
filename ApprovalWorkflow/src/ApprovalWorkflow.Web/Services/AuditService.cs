using System.Text.Json;
using ApprovalWorkflow.Web.Models;

namespace ApprovalWorkflow.Web.Services;

public class AuditService
{
    private readonly IDataService          _dataverse;
    private readonly ILogger<AuditService>  _logger;

    public AuditService(IDataService dataverse, ILogger<AuditService> logger)
    {
        _dataverse = dataverse;
        _logger    = logger;
    }

    public Task LogSubmittedAsync(Guid requestId, string performedBy, string? newValue = null) =>
        WriteAsync(requestId, 0, AuditEventType.Submitted, performedBy, newValue: newValue);

    public Task LogTierStartedAsync(Guid requestId, int tierNumber, string tierName, string approverEmails) =>
        WriteAsync(requestId, tierNumber, AuditEventType.TierStarted, "system",
            newValue: Snapshot(new { tierName, approverEmails }));

    public Task LogNotifiedAsync(Guid requestId, int tierNumber, string channel, string approverEmail) =>
        WriteAsync(requestId, tierNumber, AuditEventType.Notified, "system",
            notes: $"Channel={channel}; To={approverEmail}");

    public Task LogReminderSentAsync(Guid requestId, int tierNumber, string approverEmail) =>
        WriteAsync(requestId, tierNumber, AuditEventType.ReminderSent, "system",
            notes: $"Reminded={approverEmail}");

    public Task LogPageVisitedAsync(Guid requestId, int tierNumber, string performedBy, string? ipAddress) =>
        WriteAsync(requestId, tierNumber, AuditEventType.PageVisited, performedBy, ipAddress: ipAddress);

    public Task LogCallbackReceivedAsync(
        Guid requestId, int tierNumber, string performedBy,
        string? previousValue, string? newValue, string? ipAddress) =>
        WriteAsync(requestId, tierNumber, AuditEventType.CallbackReceived, performedBy,
            previousValue, newValue, ipAddress);

    public Task LogApprovedAsync(Guid requestId, int tierNumber, string performedBy, string? comments) =>
        WriteAsync(requestId, tierNumber, AuditEventType.Approved, performedBy, notes: comments);

    public Task LogRejectedAsync(Guid requestId, int tierNumber, string performedBy, string? comments) =>
        WriteAsync(requestId, tierNumber, AuditEventType.Rejected, performedBy, notes: comments);

    public Task LogDelegatedAsync(
        Guid requestId, int tierNumber, string fromApprover, string toApprover, string? reason) =>
        WriteAsync(requestId, tierNumber, AuditEventType.Delegated, fromApprover,
            previousValue: fromApprover, newValue: toApprover, notes: reason);

    public Task LogTierExpiredAsync(Guid requestId, int tierNumber) =>
        WriteAsync(requestId, tierNumber, AuditEventType.TierExpired, "system");

    public Task LogRequestExpiredAsync(Guid requestId) =>
        WriteAsync(requestId, 0, AuditEventType.RequestExpired, "system");

    public Task LogResetAsync(
        Guid requestId, string performedBy, string reason,
        string? previousValue, string? newValue) =>
        WriteAsync(requestId, 0, AuditEventType.Reset, performedBy,
            previousValue, newValue, notes: reason);

    public Task LogFlowCompletedAsync(Guid requestId, string finalDecision) =>
        WriteAsync(requestId, 0, AuditEventType.FlowCompleted, "system",
            newValue: finalDecision);

    public Task LogTokenConsumedAsync(Guid requestId, int tierNumber, string performedBy) =>
        WriteAsync(requestId, tierNumber, AuditEventType.TokenConsumed, performedBy);

    // ── Helpers ─────────────────────────────────────────────────────────────

    public static string Snapshot(object obj) => JsonSerializer.Serialize(obj);

    private async Task WriteAsync(
        Guid requestId,
        int tierNumber,
        AuditEventType eventType,
        string performedBy,
        string? previousValue = null,
        string? newValue      = null,
        string? ipAddress     = null,
        string? notes         = null)
    {
        try
        {
            var entry = new AuditLogEntry
            {
                ApprovalRequestId = requestId,
                TierNumber        = tierNumber,
                EventType         = eventType,
                PerformedBy       = performedBy,
                PreviousValue     = previousValue,
                NewValue          = newValue,
                IpAddress         = ipAddress,
                Notes             = notes,
                Timestamp         = DateTime.UtcNow
            };
            await _dataverse.WriteAuditLogAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log {EventType} for request {RequestId}",
                eventType, requestId);
        }
    }
}
