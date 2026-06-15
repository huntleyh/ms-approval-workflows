using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ApprovalWorkflow.Web.Models;

namespace ApprovalWorkflow.Web.Services;

/// <summary>
/// All Dataverse reads and writes for the 5 Option-B tables.
/// Uses ServiceClient with ClientSecret authentication.
/// </summary>
public class DataverseService : IDataService
{
    private readonly ServiceClient _client;

    private const string TableWorkflowDef  = "cr_workflowdefinition";
    private const string TableWorkflowTier = "cr_workflowtier";
    private const string TableRequest      = "cr_approvalrequest";
    private const string TableTierProgress = "cr_approvaltier_progress";
    private const string TableAuditLog     = "cr_approvalauditlog";

    public DataverseService(ServiceClient client) => _client = client;

    // ── WorkflowDefinitions ─────────────────────────────────────────────────

    public async Task<List<WorkflowDefinition>> GetActiveWorkflowDefinitionsAsync()
    {
        var query = new QueryExpression(TableWorkflowDef)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression()
        };
        query.Criteria.AddCondition("cr_active", ConditionOperator.Equal, true);
        query.AddOrder("cr_name", OrderType.Ascending);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToWorkflowDefinition).ToList();
    }

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(Guid id)
    {
        try { var e = await _client.RetrieveAsync(TableWorkflowDef, id, new ColumnSet(true)); return e is null ? null : MapToWorkflowDefinition(e); }
        catch { return null; }
    }

    public async Task<Guid> CreateWorkflowDefinitionAsync(WorkflowDefinition def)
    {
        var e = new Entity(TableWorkflowDef)
        {
            ["cr_name"]         = def.Name,
            ["cr_documenttype"] = def.DocumentType,
            ["cr_active"]       = def.Active
        };
        if (!string.IsNullOrEmpty(def.Description)) e["cr_description"] = def.Description;
        return await _client.CreateAsync(e);
    }

    public async Task UpdateWorkflowDefinitionAsync(Guid id, Dictionary<string, object> updates)
    {
        var e = new Entity(TableWorkflowDef, id);
        foreach (var kv in updates) e[kv.Key] = kv.Value;
        await _client.UpdateAsync(e);
    }

    // ── WorkflowTiers ───────────────────────────────────────────────────────

    public async Task<List<WorkflowTier>> GetTiersForDefinitionAsync(Guid definitionId)
    {
        var query = new QueryExpression(TableWorkflowTier)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression()
        };
        query.Criteria.AddCondition("cr_workflowdefinitionid", ConditionOperator.Equal, definitionId);
        query.AddOrder("cr_tiernumber", OrderType.Ascending);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToWorkflowTier).ToList();
    }

    public async Task<Guid> CreateWorkflowTierAsync(WorkflowTier tier)
    {
        var e = new Entity(TableWorkflowTier)
        {
            ["cr_workflowdefinitionid"] = new EntityReference(TableWorkflowDef, tier.WorkflowDefinitionId),
            ["cr_tiernumber"]           = tier.TierNumber,
            ["cr_tiername"]             = tier.TierName,
            ["cr_approveremails"]       = tier.ApproverEmails,
            ["cr_approvertype"]         = new OptionSetValue(tier.ApproverType),
            ["cr_requiredapprovals"]    = tier.RequiredApprovals,
            ["cr_reminderhours"]        = tier.ReminderHours,
            ["cr_expiryhours"]          = tier.ExpiryHours
        };
        return await _client.CreateAsync(e);
    }

    public async Task UpdateWorkflowTierAsync(Guid tierId, Dictionary<string, object> updates)
    {
        var e = new Entity(TableWorkflowTier, tierId);
        foreach (var kv in updates) e[kv.Key] = kv.Value;
        await _client.UpdateAsync(e);
    }

    public async Task DeleteWorkflowTierAsync(Guid tierId)
        => await _client.DeleteAsync(TableWorkflowTier, tierId);

    // ── ApprovalRequests ────────────────────────────────────────────────────

    public async Task<Guid> CreateApprovalRequestAsync(ApprovalRequest req)
    {
        var e = new Entity(TableRequest)
        {
            ["cr_title"]               = req.Title,
            ["cr_requestedby"]         = req.RequestedBy,
            ["cr_workflowdefinitionid"] = new EntityReference(TableWorkflowDef, req.WorkflowDefinitionId),
            ["cr_status"]              = new OptionSetValue((int)req.Status),
            ["cr_currenttiernumber"]   = req.CurrentTierNumber,
            ["cr_documenttitle"]       = req.DocumentTitle,
            ["cr_expirydate"]          = req.ExpiryDate,
            ["cr_resetcount"]          = req.ResetCount
        };
        if (!string.IsNullOrEmpty(req.DocumentSummary)) e["cr_documentsummary"] = req.DocumentSummary;
        if (!string.IsNullOrEmpty(req.DocumentContent)) e["cr_documentcontent"] = req.DocumentContent;
        if (!string.IsNullOrEmpty(req.LogicAppRunId))   e["cr_logicapprunid"]   = req.LogicAppRunId;
        if (!string.IsNullOrEmpty(req.LogicAppRunUrl))  e["cr_logicapprunurl"]  = req.LogicAppRunUrl;
        return await _client.CreateAsync(e);
    }

    public async Task<ApprovalRequest?> GetApprovalRequestAsync(Guid id)
    {
        try { var e = await _client.RetrieveAsync(TableRequest, id, new ColumnSet(true)); return e is null ? null : MapToApprovalRequest(e); }
        catch { return null; }
    }

    public async Task<List<ApprovalRequest>> GetAllApprovalRequestsAsync()
    {
        var query = new QueryExpression(TableRequest) { ColumnSet = new ColumnSet(true) };
        query.AddOrder("createdon", OrderType.Descending);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToApprovalRequest).ToList();
    }

    public async Task<List<ApprovalRequest>> GetExpiredPendingRequestsAsync()
    {
        var query = new QueryExpression(TableRequest)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition("cr_status",     ConditionOperator.Equal,    1);
        query.Criteria.AddCondition("cr_expirydate", ConditionOperator.LessThan, DateTime.UtcNow);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToApprovalRequest).ToList();
    }

    public async Task UpdateApprovalRequestAsync(Guid id, Dictionary<string, object> updates)
    {
        var e = new Entity(TableRequest, id);
        foreach (var kv in updates) e[kv.Key] = kv.Value;
        await _client.UpdateAsync(e);
    }

    // ── ApprovalTierProgress ────────────────────────────────────────────────

    public async Task<Guid> CreateTierProgressAsync(ApprovalTierProgress tp)
    {
        var e = new Entity(TableTierProgress)
        {
            ["cr_approvalrequestid"] = new EntityReference(TableRequest,      tp.ApprovalRequestId),
            ["cr_workflowtierid"]    = new EntityReference(TableWorkflowTier, tp.WorkflowTierId),
            ["cr_tiernumber"]        = tp.TierNumber,
            ["cr_tiername"]          = tp.TierName,
            ["cr_status"]            = new OptionSetValue((int)tp.Status),
            ["cr_assignedapprover"]  = tp.AssignedApprover,
            ["cr_approvaltokenhash"] = tp.ApprovalTokenHash,
            ["cr_tokenconsumed"]     = tp.TokenConsumed,
            ["cr_decision"]          = new OptionSetValue((int)tp.Decision)
        };
        if (!string.IsNullOrEmpty(tp.CallbackUrl))      e["cr_callbackurl"]      = tp.CallbackUrl;
        if (!string.IsNullOrEmpty(tp.ReviewUrl))         e["cr_reviewurl"]        = tp.ReviewUrl;
        if (!string.IsNullOrEmpty(tp.ApproverComments)) e["cr_approvercomments"] = tp.ApproverComments;
        if (tp.DecisionDate.HasValue)                   e["cr_decisiondate"]     = tp.DecisionDate.Value;
        if (tp.LastRemindedAt.HasValue)                 e["cr_lastreminded_at"]  = tp.LastRemindedAt.Value;
        if (!string.IsNullOrEmpty(tp.DelegatedFrom))    e["cr_delegatedfrom"]    = tp.DelegatedFrom;
        return await _client.CreateAsync(e);
    }

    public async Task<ApprovalTierProgress?> GetTierProgressByTokenHashAsync(string tokenHash)
    {
        var query = new QueryExpression(TableTierProgress)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression()
        };
        query.Criteria.AddCondition("cr_approvaltokenhash", ConditionOperator.Equal, tokenHash);
        var r = await _client.RetrieveMultipleAsync(query);
        var e = r.Entities.FirstOrDefault();
        return e is null ? null : MapToTierProgress(e);
    }

    public async Task<List<ApprovalTierProgress>> GetTierProgressForRequestAsync(Guid requestId)
    {
        var query = new QueryExpression(TableTierProgress)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression()
        };
        query.Criteria.AddCondition("cr_approvalrequestid", ConditionOperator.Equal, requestId);
        query.AddOrder("cr_tiernumber", OrderType.Ascending);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToTierProgress).ToList();
    }

    public async Task<ApprovalTierProgress?> GetCurrentPendingTierAsync(Guid requestId)
    {
        var query = new QueryExpression(TableTierProgress)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition("cr_approvalrequestid", ConditionOperator.Equal, requestId);
        query.Criteria.AddCondition("cr_status",            ConditionOperator.Equal, 2); // Pending
        query.AddOrder("cr_tiernumber", OrderType.Ascending);
        var r = await _client.RetrieveMultipleAsync(query);
        var e = r.Entities.FirstOrDefault();
        return e is null ? null : MapToTierProgress(e);
    }

    public async Task<List<ApprovalTierProgress>> GetOverdueRemindersAsync()
    {
        var query = new QueryExpression(TableTierProgress)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression(LogicalOperator.And)
        };
        query.Criteria.AddCondition("cr_status", ConditionOperator.Equal, 2);
        var orFilter = new FilterExpression(LogicalOperator.Or);
        orFilter.AddCondition("cr_lastreminded_at", ConditionOperator.Null);
        orFilter.AddCondition("cr_lastreminded_at", ConditionOperator.LessThan, DateTime.UtcNow.AddHours(-24));
        query.Criteria.AddFilter(orFilter);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToTierProgress).ToList();
    }

    public async Task UpdateTierProgressAsync(Guid tierId, Dictionary<string, object> updates)
    {
        var e = new Entity(TableTierProgress, tierId);
        foreach (var kv in updates) e[kv.Key] = kv.Value;
        await _client.UpdateAsync(e);
    }

    public async Task<ApprovalTierProgress?> GetTierProgressByIdAsync(Guid tierId)
    {
        var e = await _client.RetrieveAsync(TableTierProgress, tierId, new ColumnSet(true));
        return e is null ? null : MapToTierProgress(e);
    }

    public async Task<List<ApprovalTierProgress>> GetTierProgressRowsForTierAsync(Guid requestId, int tierNumber)
    {
        var all = await GetTierProgressForRequestAsync(requestId);
        return all.Where(p => p.TierNumber == tierNumber).ToList();
    }

    public async Task ResetTierProgressForRequestAsync(Guid requestId)
    {
        var tiers = await GetTierProgressForRequestAsync(requestId);
        foreach (var tier in tiers)
            await _client.DeleteAsync(TableTierProgress, tier.Id);
    }

    // ── ApprovalAuditLog ────────────────────────────────────────────────────

    public async Task WriteAuditLogAsync(AuditLogEntry entry)
    {
        var e = new Entity(TableAuditLog)
        {
            ["cr_approvalrequestid"] = new EntityReference(TableRequest, entry.ApprovalRequestId),
            ["cr_tiernumber"]        = entry.TierNumber,
            ["cr_eventtype"]         = new OptionSetValue((int)entry.EventType),
            ["cr_performedby"]       = entry.PerformedBy,
            ["cr_timestamp"]         = DateTime.UtcNow
        };
        if (!string.IsNullOrEmpty(entry.PreviousValue)) e["cr_previousvalue"] = entry.PreviousValue;
        if (!string.IsNullOrEmpty(entry.NewValue))      e["cr_newvalue"]      = entry.NewValue;
        if (!string.IsNullOrEmpty(entry.IpAddress))     e["cr_ipaddress"]     = entry.IpAddress;
        if (!string.IsNullOrEmpty(entry.Notes))         e["cr_notes"]         = entry.Notes;
        await _client.CreateAsync(e);
    }

    public async Task<List<AuditLogEntry>> GetAuditLogForRequestAsync(Guid requestId)
    {
        var query = new QueryExpression(TableAuditLog)
        {
            ColumnSet = new ColumnSet(true),
            Criteria  = new FilterExpression()
        };
        query.Criteria.AddCondition("cr_approvalrequestid", ConditionOperator.Equal, requestId);
        query.AddOrder("cr_timestamp", OrderType.Ascending);
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(MapToAuditLogEntry).ToList();
    }

    public async Task<List<AuditLogEntry>> GetAllAuditLogsAsync()
    {
        var query = new QueryExpression(TableAuditLog) { ColumnSet = new ColumnSet(true) };
        query.AddOrder("cr_timestamp", OrderType.Descending);
        var link = query.AddLink(TableRequest, "cr_approvalrequestid", "cr_approvalrequestid", JoinOperator.LeftOuter);
        link.Columns     = new ColumnSet("cr_title");
        link.EntityAlias  = "req";
        var r = await _client.RetrieveMultipleAsync(query);
        return r.Entities.Select(e =>
        {
            var entry = MapToAuditLogEntry(e);
            if (e.Contains("req.cr_title") && e["req.cr_title"] is AliasedValue av)
                entry.RequestTitle = av.Value?.ToString();
            return entry;
        }).ToList();
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    private static WorkflowDefinition MapToWorkflowDefinition(Entity e) => new()
    {
        Id           = e.Id,
        Name         = e.GetAttributeValue<string>("cr_name")         ?? string.Empty,
        DocumentType = e.GetAttributeValue<string>("cr_documenttype") ?? string.Empty,
        Description  = e.GetAttributeValue<string>("cr_description"),
        Active       = e.GetAttributeValue<bool>("cr_active"),
        CreatedOn    = e.GetAttributeValue<DateTime?>("createdon")
    };

    private static WorkflowTier MapToWorkflowTier(Entity e) => new()
    {
        Id                   = e.Id,
        WorkflowDefinitionId = e.GetAttributeValue<EntityReference>("cr_workflowdefinitionid")?.Id ?? Guid.Empty,
        TierNumber           = e.GetAttributeValue<int>("cr_tiernumber"),
        TierName             = e.GetAttributeValue<string>("cr_tiername")       ?? string.Empty,
        ApproverEmails       = e.GetAttributeValue<string>("cr_approveremails") ?? string.Empty,
        ApproverType         = e.GetAttributeValue<OptionSetValue>("cr_approvertype")?.Value ?? 1,
        RequiredApprovals    = e.GetAttributeValue<int>("cr_requiredapprovals"),
        ReminderHours        = e.GetAttributeValue<int>("cr_reminderhours"),
        ExpiryHours          = e.GetAttributeValue<int>("cr_expiryhours")
    };

    private static ApprovalRequest MapToApprovalRequest(Entity e) => new()
    {
        Id                     = e.Id,
        Title                  = e.GetAttributeValue<string>("cr_title")       ?? string.Empty,
        RequestedBy            = e.GetAttributeValue<string>("cr_requestedby") ?? string.Empty,
        WorkflowDefinitionId   = e.GetAttributeValue<EntityReference>("cr_workflowdefinitionid")?.Id   ?? Guid.Empty,
        WorkflowDefinitionName = e.GetAttributeValue<EntityReference>("cr_workflowdefinitionid")?.Name,
        Status                 = (ApprovalStatus)(e.GetAttributeValue<OptionSetValue>("cr_status")?.Value ?? 1),
        CurrentTierNumber      = e.GetAttributeValue<int>("cr_currenttiernumber"),
        LogicAppRunId          = e.GetAttributeValue<string>("cr_logicapprunid"),
        LogicAppRunUrl         = e.GetAttributeValue<string>("cr_logicapprunurl"),
        DocumentTitle          = e.GetAttributeValue<string>("cr_documenttitle")   ?? string.Empty,
        DocumentSummary        = e.GetAttributeValue<string>("cr_documentsummary"),
        DocumentContent        = e.GetAttributeValue<string>("cr_documentcontent"),
        ExpiryDate             = e.GetAttributeValue<DateTime>("cr_expirydate"),
        ResetCount             = e.GetAttributeValue<int>("cr_resetcount"),
        CreatedOn              = e.GetAttributeValue<DateTime?>("createdon")
    };

    private static ApprovalTierProgress MapToTierProgress(Entity e) => new()
    {
        Id                = e.Id,
        ApprovalRequestId = e.GetAttributeValue<EntityReference>("cr_approvalrequestid")?.Id ?? Guid.Empty,
        WorkflowTierId    = e.GetAttributeValue<EntityReference>("cr_workflowtierid")?.Id    ?? Guid.Empty,
        TierNumber        = e.GetAttributeValue<int>("cr_tiernumber"),
        TierName          = e.GetAttributeValue<string>("cr_tiername")          ?? string.Empty,
        Status            = (TierStatus)(e.GetAttributeValue<OptionSetValue>("cr_status")?.Value ?? 1),
        AssignedApprover  = e.GetAttributeValue<string>("cr_assignedapprover")  ?? string.Empty,
        ApprovalTokenHash = e.GetAttributeValue<string>("cr_approvaltokenhash") ?? string.Empty,
        TokenConsumed     = e.GetAttributeValue<bool>("cr_tokenconsumed"),
        CallbackUrl       = e.GetAttributeValue<string>("cr_callbackurl"),
        ReviewUrl         = e.GetAttributeValue<string>("cr_reviewurl"),
        Decision          = (TierDecision)(e.GetAttributeValue<OptionSetValue>("cr_decision")?.Value ?? 1),
        ApproverComments  = e.GetAttributeValue<string>("cr_approvercomments"),
        DecisionDate      = e.GetAttributeValue<DateTime?>("cr_decisiondate"),
        LastRemindedAt    = e.GetAttributeValue<DateTime?>("cr_lastreminded_at"),
        DelegatedFrom     = e.GetAttributeValue<string>("cr_delegatedfrom"),
        CreatedOn         = e.GetAttributeValue<DateTime?>("createdon")
    };

    private static AuditLogEntry MapToAuditLogEntry(Entity e) => new()
    {
        Id                = e.Id,
        ApprovalRequestId = e.GetAttributeValue<EntityReference>("cr_approvalrequestid")?.Id ?? Guid.Empty,
        TierNumber        = e.GetAttributeValue<int>("cr_tiernumber"),
        EventType         = (AuditEventType)(e.GetAttributeValue<OptionSetValue>("cr_eventtype")?.Value ?? 1),
        PerformedBy       = e.GetAttributeValue<string>("cr_performedby") ?? string.Empty,
        PreviousValue     = e.GetAttributeValue<string>("cr_previousvalue"),
        NewValue          = e.GetAttributeValue<string>("cr_newvalue"),
        IpAddress         = e.GetAttributeValue<string>("cr_ipaddress"),
        Timestamp         = e.GetAttributeValue<DateTime>("cr_timestamp"),
        Notes             = e.GetAttributeValue<string>("cr_notes")
    };
}
