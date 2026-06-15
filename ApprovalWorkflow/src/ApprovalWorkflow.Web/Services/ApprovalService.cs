using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;

namespace ApprovalWorkflow.Web.Services;

public class ApprovalService
{
    private readonly IDataService                _dataverse;
    private readonly WorkflowDefinitionService   _wfService;
    private readonly TokenService                _tokenService;
    private readonly AuditService                _audit;
    private readonly IHttpClientFactory          _httpFactory;
    private readonly AppSettings                 _settings;
    private readonly ILogger<ApprovalService>    _logger;

    public ApprovalService(
        IDataService dataverse,
        WorkflowDefinitionService wfService,
        TokenService tokenService,
        AuditService audit,
        IHttpClientFactory httpFactory,
        IOptions<AppSettings> options,
        ILogger<ApprovalService> logger)
    {
        _dataverse    = dataverse;
        _wfService    = wfService;
        _tokenService = tokenService;
        _audit        = audit;
        _httpFactory  = httpFactory;
        _settings     = options.Value;
        _logger       = logger;
    }

    /// <summary>
    /// Full multi-tier submission:
    /// 1. Load WorkflowDefinition + tiers
    /// 2. Create ApprovalRequest (Pending, tier 1)
    /// 3. Create one ApprovalTierProgress row per tier
    /// 4. Generate token for tier 1 — store hash on tier 1 progress row
    /// 5. Fire LA-1 trigger (fire-and-forget)
    /// 6. Store LA-1 run ID + URL returned in trigger response header
    /// 7. Audit: Submitted, TierStarted
    /// </summary>
    public async Task<SubmitResult> SubmitForApprovalAsync(SubmitRequestModel model)
    {
        var wf = await _wfService.GetDefinitionWithTiersAsync(model.WorkflowDefinitionId)
                 ?? throw new InvalidOperationException("Workflow definition not found.");

        var tier1    = wf.Tiers.FirstOrDefault(t => t.TierNumber == 1)
                       ?? throw new InvalidOperationException("Workflow has no tier 1.");

        // Overall expiry = max expiry across all tiers.
        // In DemoMode, the ExpiryHours value is treated as minutes.
        var maxExpiryUnits = wf.Tiers.Max(t => t.ExpiryHours);
        var demoMode       = _settings.App.DemoMode;

        // 1. Create the top-level request
        var request = new ApprovalRequest
        {
            Title                = model.DocumentTitle,
            RequestedBy          = model.RequestedBy,
            WorkflowDefinitionId = model.WorkflowDefinitionId,
            Status               = ApprovalStatus.Pending,
            CurrentTierNumber    = 1,
            DocumentTitle        = model.DocumentTitle,
            DocumentSummary      = model.DocumentSummary,
            DocumentContent      = model.DocumentContent,
            ExpiryDate           = demoMode
                                   ? DateTime.UtcNow.AddMinutes(maxExpiryUnits)
                                   : DateTime.UtcNow.AddHours(maxExpiryUnits),
            ResetCount           = 0
        };
        var requestId = await _dataverse.CreateApprovalRequestAsync(request);

        // 2. Create tier progress rows: one per approver per tier
        //    (tier1AssignedApprover is unused by the new multi-row implementation but kept for signature compat)
        await _wfService.CreateTierProgressRowsAsync(requestId, model.WorkflowDefinitionId, string.Empty);

        // 3. Issue one token per tier-1 approver row
        var tier1Rows = await _dataverse.GetTierProgressRowsForTierAsync(requestId, 1);
        string firstReviewUrl = string.Empty;

        foreach (var row in tier1Rows)
        {
            var (rawToken, tokenHash) = _tokenService.GenerateToken();
            var reviewUrl = _tokenService.BuildReviewUrl(rawToken);
            if (firstReviewUrl.Length == 0) firstReviewUrl = reviewUrl;

            await _dataverse.UpdateTierProgressAsync(row.Id, new Dictionary<string, object>
            {
                ["cr_approvaltokenhash"] = tokenHash,
                ["cr_reviewurl"]        = reviewUrl
            });
        }

        // 4. Fire LA-1 (async — LA-1 sends email/Teams to all approverEmails in the list)
        _ = FireAndForgetLA1Async(requestId, wf, tier1, model, firstReviewUrl);

        // 5. Audit
        await _audit.LogSubmittedAsync(requestId, model.RequestedBy,
            AuditService.Snapshot(new { request.Title, request.WorkflowDefinitionId, TierCount = wf.Tiers.Count }));
        await _audit.LogTierStartedAsync(requestId, 1, tier1.TierName, tier1.ApproverEmails);

        return new SubmitResult
        {
            RequestId      = requestId,
            CurrentTier    = 1,
            Status         = "Pending",
            ReviewUrl      = firstReviewUrl,
            TierName       = tier1.TierName,
            ApproverEmails = tier1.ApproverEmails,
            AllTiers       = wf.Tiers
        };
    }

    /// <summary>
    /// Called after an individual approver's decision is Approved.
    /// Checks unanimous gate — if other approvers for this tier are still pending, returns TierStillPending=true.
    /// Otherwise advances to the next tier (generating tokens for each approver) or completes the flow.
    /// POSTs to the stored LA-1 callback URL.
    /// </summary>
    public async Task<AdvanceResult> AdvanceTierAsync(
        Guid requestId,
        int completedTierNumber,
        string callbackUrl)
    {
        var nextTier = await _wfService.AdvanceToNextTierAsync(requestId, completedTierNumber);

        // AdvanceToNextTierAsync returns null either because the tier is still pending
        // (other approvers haven't decided yet) or because the flow is complete.
        // Distinguish the two by checking if any Pending rows remain for this tier.
        if (nextTier is null)
        {
            var thisTierRows = await _dataverse.GetTierProgressRowsForTierAsync(requestId, completedTierNumber);
            bool stillPending = thisTierRows.Any(p => p.Status == TierStatus.Pending);

            if (stillPending)
            {
                // Unanimous gate not yet met — do not callback LA-1 yet
                return new AdvanceResult { IsComplete = false, TierStillPending = true };
            }

            // Flow complete — all tiers approved.
            // Resolve callbackUrl from any sibling row in case the current row's URL is empty
            // (can happen when a non-first approver is last to decide).
            var resolvedCallback = !string.IsNullOrEmpty(callbackUrl)
                ? callbackUrl
                : thisTierRows.FirstOrDefault(r => !string.IsNullOrEmpty(r.CallbackUrl))?.CallbackUrl ?? string.Empty;

            await _audit.LogFlowCompletedAsync(requestId, "Approved");
            _ = PostCallbackAsync(resolvedCallback, new { decision = "complete" });
            return new AdvanceResult { IsComplete = true };
        }

        // Issue one token per approver row for the next tier
        var nextTierRows = await _dataverse.GetTierProgressRowsForTierAsync(requestId, nextTier.TierNumber);
        string firstReviewUrl = string.Empty;

        foreach (var row in nextTierRows)
        {
            var (rawToken, tokenHash) = _tokenService.GenerateToken();
            var reviewUrl = _tokenService.BuildReviewUrl(rawToken);
            if (firstReviewUrl.Length == 0) firstReviewUrl = reviewUrl;

            await _dataverse.UpdateTierProgressAsync(row.Id, new Dictionary<string, object>
            {
                ["cr_approvaltokenhash"] = tokenHash,
                ["cr_assignedapprover"]  = row.AssignedApprover,
                ["cr_reviewurl"]        = reviewUrl
            });
        }

        await _audit.LogTierStartedAsync(requestId, nextTier.TierNumber, nextTier.TierName, nextTier.ApproverEmails);

        // Close this LA-1 run; fire a new LA-1 run for the next tier.
        // Resolve callbackUrl from sibling rows in case this row's URL is empty.
        var thisTierRowsForCallback = await _dataverse.GetTierProgressRowsForTierAsync(requestId, completedTierNumber);
        var resolvedCallbackForAdvance = !string.IsNullOrEmpty(callbackUrl)
            ? callbackUrl
            : thisTierRowsForCallback.FirstOrDefault(r => !string.IsNullOrEmpty(r.CallbackUrl))?.CallbackUrl ?? string.Empty;

        _ = PostCallbackAsync(resolvedCallbackForAdvance, new { decision = "complete" });
        _ = FireLA1ForNextTierAsync(requestId, nextTier, firstReviewUrl);

        return new AdvanceResult
        {
            IsComplete     = false,
            TierStillPending = false,
            NextTierNumber = nextTier.TierNumber,
            NextTierName   = nextTier.TierName
        };
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task FireAndForgetLA1Async(
        Guid requestId,
        WorkflowDefinitionWithTiers wf,
        WorkflowTier tier1,
        SubmitRequestModel model,
        string reviewUrl)
    {
        try
        {
            // Build per-approver array so LA-1 can send each person their own unique review URL
            var tier1Rows = await _dataverse.GetTierProgressRowsForTierAsync(requestId, 1);
            var approvers = tier1Rows
                .Select(r => new { email = r.AssignedApprover, reviewUrl = r.ReviewUrl ?? reviewUrl })
                .ToArray();

            var payload = new
            {
                requestId            = requestId.ToString(),
                workflowDefinitionId = model.WorkflowDefinitionId.ToString(),
                currentTierNumber    = 1,
                approvers            = approvers,
                approverEmails       = tier1.ApproverEmails,   // for Teams card display
                approverType         = tier1.ApproverType == 2 ? "External" : "Internal",
                documentUrl          = reviewUrl,              // for Teams card link (first approver)
                tierName             = tier1.TierName,
                title                = model.DocumentTitle,
                requestedBy          = model.RequestedBy
            };

            var client  = _httpFactory.CreateClient();
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_settings.LogicApps.InitiateUrl, content);
            response.EnsureSuccessStatusCode();

            // Store the LA-1 run ID from the response header
            var runId  = response.Headers.TryGetValues("x-ms-workflow-run-id", out var vals)
                         ? vals.FirstOrDefault() : null;
            var runUrl = $"https://management.azure.com/subscriptions/{_settings.LogicApps.SubscriptionId}" +
                         $"/resourceGroups/{_settings.LogicApps.ResourceGroup}" +
                         $"/providers/Microsoft.Logic/workflows/{_settings.LogicApps.LA1Name}" +
                         $"/runs/{runId}";

            if (!string.IsNullOrEmpty(runId))
            {
                await _dataverse.UpdateApprovalRequestAsync(requestId, new Dictionary<string, object>
                {
                    ["cr_logicapprunid"]  = runId,
                    ["cr_logicapprunurl"] = runUrl
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger LA-1 for request {RequestId}", requestId);
        }
    }

    private async Task FireLA1ForNextTierAsync(Guid requestId, WorkflowTier nextTier, string reviewUrl)
    {
        try
        {
            var request = await _dataverse.GetApprovalRequestAsync(requestId);
            if (request is null)
            {
                _logger.LogWarning("FireLA1ForNextTierAsync: request {RequestId} not found", requestId);
                return;
            }

            // Build per-approver array so each approver gets their own unique review URL
            var tierRows = await _dataverse.GetTierProgressRowsForTierAsync(requestId, nextTier.TierNumber);
            var approvers = tierRows
                .Select(r => new { email = r.AssignedApprover, reviewUrl = r.ReviewUrl ?? reviewUrl })
                .ToArray();

            var payload = new
            {
                requestId            = requestId.ToString(),
                workflowDefinitionId = request.WorkflowDefinitionId.ToString(),
                currentTierNumber    = nextTier.TierNumber,
                approvers            = approvers,
                approverEmails       = nextTier.ApproverEmails,   // for Teams card display
                approverType         = nextTier.ApproverType == 2 ? "External" : "Internal",
                documentUrl          = reviewUrl,                 // for Teams card link (first approver)
                tierName             = nextTier.TierName,
                title                = request.Title,
                requestedBy          = request.RequestedBy
            };

            var client   = _httpFactory.CreateClient();
            var content  = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_settings.LogicApps.InitiateUrl, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("LA-1 fired for tier {Tier} on request {RequestId}", nextTier.TierNumber, requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fire LA-1 for tier {Tier} on request {RequestId}", nextTier.TierNumber, requestId);
        }
    }

    private async Task PostCallbackAsync(string callbackUrl, object body)
    {
        try
        {
            var client  = _httpFactory.CreateClient();
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            await client.PostAsync(callbackUrl, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to POST to LA-1 callback URL");
        }
    }

    // Exposed for controller use (e.g. reject fast-path needs to fire callback)
    public Task PostTierCallbackAsync(string callbackUrl, object body) => PostCallbackAsync(callbackUrl, body);
}
