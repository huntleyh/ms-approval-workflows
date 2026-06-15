using ApprovalWorkflow.Web.Models;
using Microsoft.Xrm.Sdk;

namespace ApprovalWorkflow.Web.Services;

/// <summary>
/// Reads and manages workflow definitions and tier configurations.
/// Handles creating per-request tier progress rows and advancing tiers.
/// </summary>
public class WorkflowDefinitionService
{
    private readonly IDataService _dataverse;

    public WorkflowDefinitionService(IDataService dataverse)
    {
        _dataverse = dataverse;
    }

    /// <summary>Returns a workflow definition with its ordered list of tiers.</summary>
    public async Task<WorkflowDefinitionWithTiers?> GetDefinitionWithTiersAsync(Guid definitionId)
    {
        var def = await _dataverse.GetWorkflowDefinitionAsync(definitionId);
        if (def is null) return null;

        var tiers = await _dataverse.GetTiersForDefinitionAsync(definitionId);
        return new WorkflowDefinitionWithTiers { Definition = def, Tiers = tiers };
    }

    /// <summary>
    /// Creates one ApprovalTierProgress row per approver per tier.
    /// Tier 1 rows get Status=Pending; all others get Status=Waiting.
    /// Tokens for tier 1 rows are set separately by ApprovalService.
    /// </summary>
    public async Task CreateTierProgressRowsAsync(
        Guid requestId,
        Guid definitionId,
        string tier1AssignedApprover)
    {
        var tiers = await _dataverse.GetTiersForDefinitionAsync(definitionId);
        foreach (var tier in tiers)
        {
            var approvers = tier.ApproverEmails
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var email in approvers)
            {
                var progress = new ApprovalTierProgress
                {
                    ApprovalRequestId = requestId,
                    WorkflowTierId    = tier.Id,
                    TierNumber        = tier.TierNumber,
                    TierName          = tier.TierName,
                    Status            = tier.TierNumber == 1 ? TierStatus.Pending : TierStatus.Waiting,
                    AssignedApprover  = email,
                    ApprovalTokenHash = string.Empty,   // set by ApprovalService for tier 1 rows
                    TokenConsumed     = false,
                    Decision          = TierDecision.Pending,
                    ExpiryHours       = tier.ExpiryHours
                };
                await _dataverse.CreateTierProgressAsync(progress);
            }
        }
    }

    /// <summary>
    /// Returns the next WorkflowTier after the completed tier number,
    /// or null if that was the last tier.
    /// </summary>
    public async Task<WorkflowTier?> GetNextTierAsync(Guid requestId, int completedTierNumber)
    {
        // Get the workflow definition from the request
        var request = await _dataverse.GetApprovalRequestAsync(requestId);
        if (request is null) return null;

        var tiers = await _dataverse.GetTiersForDefinitionAsync(request.WorkflowDefinitionId);
        return tiers.FirstOrDefault(t => t.TierNumber == completedTierNumber + 1);
    }

    /// <summary>
    /// Called after an individual approver's decision is Approved.
    /// Checks whether ALL approvers for this tier have approved (unanimous gate).
    /// If not yet unanimous, returns null with IsComplete=false and TierStillPending=true.
    /// If unanimous, marks this tier's rows Approved, activates the next tier's rows (Pending),
    /// updates ApprovalRequest.cr_currenttiernumber, and returns the next WorkflowTier.
    /// Returns null with IsComplete=true when the last tier completes.
    /// </summary>
    public async Task<WorkflowTier?> AdvanceToNextTierAsync(Guid requestId, int completedTierNumber)
    {
        var allProgress = await _dataverse.GetTierProgressForRequestAsync(requestId);

        // Check if ALL rows for this tier are now Approved
        var thisTierRows = allProgress.Where(p => p.TierNumber == completedTierNumber).ToList();
        bool allApproved  = thisTierRows.All(p => p.Decision == TierDecision.Approved);

        if (!allApproved)
        {
            // Tier still waiting for other approvers — do not advance
            return null;
        }

        // All approvers for this tier have approved — move to next tier
        var nextTier = await GetNextTierAsync(requestId, completedTierNumber);
        if (nextTier is null)
        {
            // Flow complete — mark request as Approved
            await _dataverse.UpdateApprovalRequestAsync(requestId, new Dictionary<string, object>
            {
                ["cr_status"] = new OptionSetValue((int)ApprovalStatus.Approved)
            });
            return null;
        }

        // Activate ALL next-tier progress rows (one per approver)
        var nextTierRows = allProgress.Where(p => p.TierNumber == nextTier.TierNumber).ToList();
        foreach (var row in nextTierRows)
        {
            await _dataverse.UpdateTierProgressAsync(row.Id, new Dictionary<string, object>
            {
                ["cr_status"] = new OptionSetValue((int)TierStatus.Pending)
            });
        }

        // Update request's current tier
        await _dataverse.UpdateApprovalRequestAsync(requestId, new Dictionary<string, object>
        {
            ["cr_currenttiernumber"] = nextTier.TierNumber
        });

        return nextTier;
    }
}
