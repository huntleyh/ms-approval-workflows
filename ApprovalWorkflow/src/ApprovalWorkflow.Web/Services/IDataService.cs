using ApprovalWorkflow.Web.Models;

namespace ApprovalWorkflow.Web.Services;

/// <summary>
/// Abstraction over the data store.
/// Implemented by DataverseService (production) and SqliteDataService (demo / local).
/// </summary>
public interface IDataService
{
    // ── WorkflowDefinitions ─────────────────────────────────────────────────
    Task<List<WorkflowDefinition>>  GetActiveWorkflowDefinitionsAsync();
    Task<WorkflowDefinition?>       GetWorkflowDefinitionAsync(Guid id);
    Task<Guid>                      CreateWorkflowDefinitionAsync(WorkflowDefinition def);
    Task                            UpdateWorkflowDefinitionAsync(Guid id, Dictionary<string, object> updates);

    // ── WorkflowTiers ───────────────────────────────────────────────────────
    Task<List<WorkflowTier>> GetTiersForDefinitionAsync(Guid definitionId);
    Task<Guid>               CreateWorkflowTierAsync(WorkflowTier tier);
    Task                     UpdateWorkflowTierAsync(Guid tierId, Dictionary<string, object> updates);
    Task                     DeleteWorkflowTierAsync(Guid tierId);

    // ── ApprovalRequests ────────────────────────────────────────────────────
    Task<Guid>                    CreateApprovalRequestAsync(ApprovalRequest req);
    Task<ApprovalRequest?>        GetApprovalRequestAsync(Guid id);
    Task<List<ApprovalRequest>>   GetAllApprovalRequestsAsync();
    Task<List<ApprovalRequest>>   GetExpiredPendingRequestsAsync();
    Task                          UpdateApprovalRequestAsync(Guid id, Dictionary<string, object> updates);

    // ── ApprovalTierProgress ────────────────────────────────────────────────
    Task<Guid>                        CreateTierProgressAsync(ApprovalTierProgress tp);
    Task<ApprovalTierProgress?>       GetTierProgressByTokenHashAsync(string tokenHash);
    Task<List<ApprovalTierProgress>>  GetTierProgressForRequestAsync(Guid requestId);
    Task<ApprovalTierProgress?>       GetCurrentPendingTierAsync(Guid requestId);
    Task<List<ApprovalTierProgress>>  GetOverdueRemindersAsync();
    Task                              UpdateTierProgressAsync(Guid tierId, Dictionary<string, object> updates);
    Task<ApprovalTierProgress?>       GetTierProgressByIdAsync(Guid tierId);
    Task<List<ApprovalTierProgress>>  GetTierProgressRowsForTierAsync(Guid requestId, int tierNumber);
    Task                              ResetTierProgressForRequestAsync(Guid requestId);

    // ── AuditLog ─────────────────────────────────────────────────────────────
    Task                     WriteAuditLogAsync(AuditLogEntry entry);
    Task<List<AuditLogEntry>> GetAuditLogForRequestAsync(Guid requestId);
    Task<List<AuditLogEntry>> GetAllAuditLogsAsync();
}
