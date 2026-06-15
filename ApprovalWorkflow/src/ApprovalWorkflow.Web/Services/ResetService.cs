using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Services;

public class ResetService
{
    private readonly IDataService                _dataverse;
    private readonly WorkflowDefinitionService    _wfService;
    private readonly AuditService                 _audit;
    private readonly IHttpClientFactory           _httpFactory;
    private readonly AppSettings                  _settings;
    private readonly ILogger<ResetService>        _logger;

    public ResetService(
        IDataService dataverse,
        WorkflowDefinitionService wfService,
        AuditService audit,
        IHttpClientFactory httpFactory,
        IOptions<AppSettings> options,
        ILogger<ResetService> logger)
    {
        _dataverse   = dataverse;
        _wfService   = wfService;
        _audit       = audit;
        _httpFactory = httpFactory;
        _settings    = options.Value;
        _logger      = logger;
    }

    /// <summary>
    /// Cancels the current LA-1 run, resets all Dataverse state,
    /// optionally overrides tier approver emails, then re-submits.
    /// </summary>
    public async Task<bool> ResetApprovalAsync(
        Guid   requestId,
        string performedBy,
        string reason,
        IEnumerable<TierUpdateDto>? tierUpdates = null)
    {
        // 1. Load the request
        var request = await _dataverse.GetApprovalRequestAsync(requestId);
        if (request is null)
        {
            _logger.LogWarning("Reset failed — request {Id} not found", requestId);
            return false;
        }

        var previousStatus = request.Status.ToString();

        // 2. Cancel LA-1 run if we have a run ID
        if (!string.IsNullOrEmpty(request.LogicAppRunId))
        {
            await CancelLogicAppRunAsync(request.LogicAppRunId);
        }

        // 3. Apply any tier overrides before re-seeding progress rows
        if (tierUpdates is not null)
        {
            foreach (var update in tierUpdates)
            {
                var tiers = await _dataverse.GetTiersForDefinitionAsync(request.WorkflowDefinitionId);
                var tier  = tiers.FirstOrDefault(t => t.TierNumber == update.TierNumber);
                if (tier is not null)
                {
                    await _dataverse.UpdateWorkflowTierAsync(tier.Id, new Dictionary<string, object>
                    {
                        ["cr_approveremails"] = update.NewApproverEmails
                    });
                }
            }
        }

        // 4. Delete all existing TierProgress rows for this request
        await _dataverse.ResetTierProgressForRequestAsync(requestId);

        // 5. Increment reset count and bump status back to Pending at tier 1
        var newResetCount = request.ResetCount + 1;
        await _dataverse.UpdateApprovalRequestAsync(requestId, new Dictionary<string, object>
        {
            ["cr_status"]           = (int)ApprovalStatus.Pending,
            ["cr_currenttiernumber"] = 1,
            ["cr_resetcount"]        = newResetCount,
            ["cr_logicapprunid"]     = string.Empty,
            ["cr_logicapprunurl"]    = string.Empty
        });

        // 6. Audit reset
        await _audit.LogResetAsync(requestId, performedBy, reason,
            previousValue: previousStatus,
            newValue: "Pending / Tier 1");

        // 7. Re-submit as a fresh flow (reuses same requestId, just re-fires LA-1)
        var submitModel = new SubmitRequestModel
        {
            DocumentTitle        = request!.DocumentTitle,
            DocumentSummary      = request.DocumentSummary,
            DocumentContent      = request!.DocumentContent ?? string.Empty,
            WorkflowDefinitionId = request.WorkflowDefinitionId,
            RequestedBy          = performedBy   // resetter becomes the (re)submitter
        };

        await ResubmitExistingAsync(requestId, submitModel);
        return true;
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    /// Re-submit an existing request (skips creating a new ApprovalRequest row).
    private async Task ResubmitExistingAsync(Guid requestId, SubmitRequestModel model)
    {
        try
        {
            // Reload updated request so we have current WorkflowDefinitionId
            var request  = await _dataverse.GetApprovalRequestAsync(requestId);
            var wf       = await _dataverse.GetTiersForDefinitionAsync(model.WorkflowDefinitionId);
            var tier1    = wf.FirstOrDefault(t => t.TierNumber == 1);
            if (request is null || tier1 is null) return;

            var firstApprover = tier1.ApproverEmails.Split(';')[0].Trim();

            // Recreate tier progress rows
            await _wfService.CreateTierProgressRowsAsync(requestId, model.WorkflowDefinitionId, firstApprover);

            // Trigger LA-1 again
            var la1Payload = new
            {
                requestId            = requestId.ToString(),
                workflowDefinitionId = model.WorkflowDefinitionId.ToString(),
                currentTierNumber    = 1,
                approverEmails       = tier1.ApproverEmails,
                approverType         = tier1.ApproverType == 2 ? "External" : "Internal",
                documentUrl          = $"{_settings.App.BaseUrl}/Review",
                tierName             = tier1.TierName,
                title                = model.DocumentTitle,
                requestedBy          = model.RequestedBy
            };

            var client   = _httpFactory.CreateClient();
            var content  = new StringContent(JsonSerializer.Serialize(la1Payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_settings.LogicApps.InitiateUrl, content);
            response.EnsureSuccessStatusCode();

            var runId = response.Headers.TryGetValues("x-ms-workflow-run-id", out var vals)
                        ? vals.FirstOrDefault() : null;

            if (!string.IsNullOrEmpty(runId))
            {
                var runUrl = $"https://management.azure.com/subscriptions/{_settings.LogicApps.SubscriptionId}" +
                             $"/resourceGroups/{_settings.LogicApps.ResourceGroup}" +
                             $"/providers/Microsoft.Logic/workflows/{_settings.LogicApps.LA1Name}" +
                             $"/runs/{runId}";

                await _dataverse.UpdateApprovalRequestAsync(requestId, new Dictionary<string, object>
                {
                    ["cr_logicapprunid"]  = runId,
                    ["cr_logicapprunurl"] = runUrl
                });
            }

            await _audit.LogTierStartedAsync(requestId, 1, tier1.TierName, tier1.ApproverEmails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-submit request {RequestId} after reset", requestId);
        }
    }

    private async Task CancelLogicAppRunAsync(string runId)
    {
        try
        {
            var mgmtToken = await GetManagementTokenAsync();
            var url = $"https://management.azure.com/subscriptions/{_settings.LogicApps.SubscriptionId}" +
                      $"/resourceGroups/{_settings.LogicApps.ResourceGroup}" +
                      $"/providers/Microsoft.Logic/workflows/{_settings.LogicApps.LA1Name}" +
                      $"/runs/{runId}?api-version=2016-06-01";

            var client  = _httpFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mgmtToken);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cancel LA-1 run returned {Status} — may already be complete", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel LA-1 run {RunId}", runId);
        }
    }

    private async Task<string> GetManagementTokenAsync()
    {
        var tokenUrl = $"https://login.microsoftonline.com/{_settings.Dataverse.TenantId}/oauth2/v2.0/token";
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = _settings.Dataverse.ClientId,
            ["client_secret"] = _settings.Dataverse.ClientSecret,
            ["scope"]         = "https://management.azure.com/.default"
        });

        var client   = _httpFactory.CreateClient();
        var response = await client.PostAsync(tokenUrl, body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()
               ?? throw new InvalidOperationException("Empty access_token in management token response.");
    }
}
