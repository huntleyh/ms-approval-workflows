using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Controllers;

[ApiController]
[Route("api/approval")]
public class ApprovalApiController : ControllerBase
{
    private readonly IDataService                    _dataverse;
    private readonly TokenService                   _tokenService;
    private readonly ApprovalService                _approvalService;
    private readonly AuditService                   _audit;
    private readonly ResetService                   _resetService;
    private readonly AppSettings                    _settings;
    private readonly ILogger<ApprovalApiController> _logger;

    public ApprovalApiController(
        IDataService dataverse,
        TokenService tokenService,
        ApprovalService approvalService,
        AuditService audit,
        ResetService resetService,
        IOptions<AppSettings> options,
        ILogger<ApprovalApiController> logger)
    {
        _dataverse       = dataverse;
        _tokenService    = tokenService;
        _approvalService = approvalService;
        _audit           = audit;
        _resetService    = resetService;
        _settings        = options.Value;
        _logger          = logger;
    }

    // POST /api/approval/tier-callback
    // LA-1 calls this after suspending so we can wake it later.
    // Stores the callback URL on ALL pending rows for the tier so that whichever
    // approver is last to decide can always fire the webhook to unblock LA-1.
    [HttpPost("tier-callback")]
    public async Task<IActionResult> RegisterTierCallback([FromBody] TierCallbackRequest body)
    {
        if (body.ApprovalRequestId == Guid.Empty || string.IsNullOrWhiteSpace(body.CallbackUrl))
            return BadRequest(new { error = "approvalRequestId and callbackUrl are required" });

        var tierRows = await _dataverse.GetTierProgressRowsForTierAsync(
            body.ApprovalRequestId, body.TierNumber);

        ApprovalTierProgress? firstRow = null;
        foreach (var row in tierRows.Where(r => r.Status == TierStatus.Pending))
        {
            firstRow ??= row;
            await _dataverse.UpdateTierProgressAsync(row.Id, new Dictionary<string, object>
            {
                ["cr_callbackurl"] = body.CallbackUrl
            });
        }

        await _audit.LogCallbackReceivedAsync(
            body.ApprovalRequestId,
            body.TierNumber,
            "la-1", null, body.CallbackUrl,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return Ok(new
        {
            status        = "ok",
            tierProgressId = firstRow?.Id,
            reviewUrl      = firstRow?.ReviewUrl
        });
    }

    // POST /api/approval/decide
    [HttpPost("decide")]
    public async Task<IActionResult> Decide([FromBody] DecisionRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.RawToken))
            return BadRequest(new { error = "rawToken is required" });

        var tierProgress = await _tokenService.ValidateTokenAsync(model.RawToken);
        if (tierProgress is null)
            return BadRequest(new { error = "Invalid or expired token" });

        var newTierStatus   = model.Decision == "Approved" ? TierStatus.Approved   : TierStatus.Rejected;
        var newTierDecision = model.Decision == "Approved" ? TierDecision.Approved : TierDecision.Rejected;
        var ipAddress       = HttpContext.Connection.RemoteIpAddress?.ToString();

        await _dataverse.UpdateTierProgressAsync(tierProgress.Id, new Dictionary<string, object>
        {
            ["cr_status"]           = (int)newTierStatus,
            ["cr_decision"]         = (int)newTierDecision,
            ["cr_approvercomments"] = model.Comments ?? string.Empty,
            ["cr_decisiondate"]     = DateTime.UtcNow
        });

        await _tokenService.ConsumeTokenAsync(tierProgress.Id);

        await _audit.LogCallbackReceivedAsync(
            tierProgress.ApprovalRequestId,
            tierProgress.TierNumber,
            model.ApprovedBy,
            tierProgress.Status.ToString(),
            model.Decision,
            ipAddress);

        if (model.Decision == "Approved")
        {
            await _audit.LogApprovedAsync(tierProgress.ApprovalRequestId, tierProgress.TierNumber,
                model.ApprovedBy, model.Comments);

            var advanceResult = await _approvalService.AdvanceTierAsync(
                tierProgress.ApprovalRequestId,
                tierProgress.TierNumber,
                tierProgress.CallbackUrl ?? string.Empty);

            // Only update overall request status when the tier actually completes
            if (!advanceResult.TierStillPending)
            {
                var finalStatus = advanceResult.IsComplete ? ApprovalStatus.Approved : ApprovalStatus.Pending;
                await _dataverse.UpdateApprovalRequestAsync(tierProgress.ApprovalRequestId,
                    new Dictionary<string, object> { ["cr_status"] = (int)finalStatus });
            }
        }
        else
        {
            await _audit.LogRejectedAsync(tierProgress.ApprovalRequestId, tierProgress.TierNumber,
                model.ApprovedBy, model.Comments);

            // Cancel all remaining Pending sibling rows for this tier (fail-fast on rejection)
            var siblingRows = await _dataverse.GetTierProgressRowsForTierAsync(
                tierProgress.ApprovalRequestId, tierProgress.TierNumber);

            foreach (var sibling in siblingRows.Where(p =>
                         p.Id != tierProgress.Id && p.Status == TierStatus.Pending))
            {
                await _dataverse.UpdateTierProgressAsync(sibling.Id, new Dictionary<string, object>
                {
                    ["cr_status"]   = (int)TierStatus.Skipped,
                    ["cr_decision"] = (int)TierDecision.Rejected
                });
                await _tokenService.ConsumeTokenAsync(sibling.Id);
            }

            // If there's a stored LA-1 callback for this tier, complete it so the run doesn't hang
            var callbackRow = siblingRows.FirstOrDefault(p => !string.IsNullOrEmpty(p.CallbackUrl))
                              ?? tierProgress;
            if (!string.IsNullOrEmpty(callbackRow.CallbackUrl))
                _ = _approvalService.PostTierCallbackAsync(callbackRow.CallbackUrl, new { decision = "complete" });

            await _dataverse.UpdateApprovalRequestAsync(tierProgress.ApprovalRequestId,
                new Dictionary<string, object> { ["cr_status"] = (int)ApprovalStatus.Rejected });
        }

        await _audit.LogTokenConsumedAsync(tierProgress.ApprovalRequestId, tierProgress.TierNumber, model.ApprovedBy);
        return Ok(new { status = "ok", decision = model.Decision });
    }

    // POST /api/approval/delegate
    [HttpPost("delegate")]
    public async Task<IActionResult> Delegate([FromBody] DelegationRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.RawToken))
            return BadRequest(new { error = "rawToken is required" });

        var tierProgress = await _tokenService.ValidateTokenAsync(model.RawToken);
        if (tierProgress is null)
            return BadRequest(new { error = "Invalid or expired token" });

        var fromApprover = tierProgress.AssignedApprover;

        if (string.IsNullOrWhiteSpace(model.DelegateTo) || !model.DelegateTo.Contains('@'))
            return BadRequest(new { error = "A valid delegate email address is required" });

        // Determine type: Internal if email matches a DemoUser, otherwise External
        var delegateUser = _settings.DemoUsers
            .FirstOrDefault(u => u.Email.Equals(model.DelegateTo, StringComparison.OrdinalIgnoreCase));
        var delegateType = delegateUser?.Type ?? model.DelegateType ?? "Internal";

        var (rawToken, tokenHash) = _tokenService.GenerateToken();
        var reviewUrl             = _tokenService.BuildReviewUrl(rawToken);

        // Update row with new token + delegate info; reset TokenConsumed so the new token is live
        await _dataverse.UpdateTierProgressAsync(tierProgress.Id, new Dictionary<string, object>
        {
            ["cr_assignedapprover"]  = model.DelegateTo,
            ["cr_approvaltokenhash"] = tokenHash,
            ["cr_reviewurl"]         = reviewUrl,
            ["cr_tokenconsumed"]     = false,
            ["cr_delegatedfrom"]     = fromApprover
        });

        await _audit.LogDelegatedAsync(tierProgress.ApprovalRequestId, tierProgress.TierNumber,
            fromApprover, model.DelegateTo, model.Reason);
        await _audit.LogTokenConsumedAsync(tierProgress.ApprovalRequestId, tierProgress.TierNumber, fromApprover);

        return Ok(new { status = "ok", reviewUrl });
    }

    // POST /api/approval/expire  (called by LA-3)
    [HttpPost("expire")]
    public async Task<IActionResult> Expire([FromBody] ExpireRequest body)
    {
        if (body.ApprovalRequestId == Guid.Empty)
            return BadRequest(new { error = "approvalRequestId is required" });

        var request = await _dataverse.GetApprovalRequestAsync(body.ApprovalRequestId);
        if (request is null) return NotFound();

        await _dataverse.UpdateApprovalRequestAsync(body.ApprovalRequestId,
            new Dictionary<string, object> { ["cr_status"] = (int)ApprovalStatus.Expired });

        var tiers = await _dataverse.GetTierProgressForRequestAsync(body.ApprovalRequestId);
        foreach (var t in tiers.Where(t => t.Status is TierStatus.Pending or TierStatus.Waiting))
        {
            await _dataverse.UpdateTierProgressAsync(t.Id,
                new Dictionary<string, object> { ["cr_status"] = (int)TierStatus.Expired });
            await _audit.LogTierExpiredAsync(body.ApprovalRequestId, t.TierNumber);
        }

        await _audit.LogRequestExpiredAsync(body.ApprovalRequestId);
        return Ok(new { status = "ok" });
    }

    // POST /api/approval/reset
    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] ResetRequest body)
    {
        if (body.ApprovalRequestId == Guid.Empty)
            return BadRequest(new { error = "approvalRequestId is required" });

        var success = await _resetService.ResetApprovalAsync(
            body.ApprovalRequestId, body.TriggeredBy, body.Reason, body.TierUpdates);

        return success ? Ok(new { status = "reset" }) : NotFound();
    }

    // POST /api/approval/log-reminder
    [HttpPost("log-reminder")]
    public async Task<IActionResult> LogReminder([FromBody] LogReminderRequest body)
    {
        if (body.TierProgressId == Guid.Empty)
            return BadRequest(new { error = "tierProgressId is required" });

        var tier = await _dataverse.GetTierProgressByIdAsync(body.TierProgressId);
        if (tier is null) return NotFound();

        await _dataverse.UpdateTierProgressAsync(tier.Id,
            new Dictionary<string, object> { ["cr_lastreminderat"] = DateTime.UtcNow });

        await _audit.LogReminderSentAsync(tier.ApprovalRequestId, tier.TierNumber, body.ApproverEmail);
        return Ok(new { status = "ok" });
    }

    // GET /api/approval/overdue-reminders
    // Called by LA-4 to get all pending tiers that need reminders.
    [HttpGet("overdue-reminders")]
    public async Task<IActionResult> GetOverdueReminders()
    {
        var tiers = await _dataverse.GetOverdueRemindersAsync();
        var results = tiers.Select(t => new
        {
            id              = t.Id,
            tierProgressId  = t.Id,
            approvalRequestId = t.ApprovalRequestId,
            assignedApprover = t.AssignedApprover,
            tierName        = t.TierName,
            tierNumber      = t.TierNumber,
            reviewUrl       = t.ReviewUrl
        });
        return Ok(results);
    }

    // GET /api/approval/audit/{requestId}
    [HttpGet("audit/{requestId:guid}")]
    public async Task<IActionResult> GetAuditLog(Guid requestId)
    {
        var logs = await _dataverse.GetAuditLogForRequestAsync(requestId);
        return Ok(logs);
    }

    // GET /api/approval/tiers/{requestId}
    [HttpGet("tiers/{requestId:guid}")]
    public async Task<IActionResult> GetTierProgress(Guid requestId)
    {
        var tiers = await _dataverse.GetTierProgressForRequestAsync(requestId);
        return Ok(tiers);
    }
}
