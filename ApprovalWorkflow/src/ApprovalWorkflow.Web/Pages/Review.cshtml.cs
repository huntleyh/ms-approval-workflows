using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Pages;

public class ReviewModel : PageModel
{
    private readonly TokenService    _tokenService;
    private readonly IDataService    _dataverse;
    private readonly AuditService    _audit;
    private readonly AppSettings     _settings;

    public ApprovalTierProgress? TierProgress { get; set; }
    public new ApprovalRequest?  Request      { get; set; }
    public List<ApprovalTierProgress> AllTiers { get; set; } = new();
    public string?   TokenError   { get; set; }
    public string    RawToken     { get; set; } = string.Empty;
    public List<DemoUser> OtherUsers { get; set; } = new();

    public ReviewModel(
        TokenService tokenService,
        IDataService dataverse,
        AuditService audit,
        IOptions<AppSettings> options)
    {
        _tokenService = tokenService;
        _dataverse    = dataverse;
        _audit        = audit;
        _settings     = options.Value;
    }

    // /Review?token=xxx  — normal approver link
    public async Task OnGetAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TokenError = "No approval token was provided in the URL.";
            return;
        }

        RawToken     = token;
        TierProgress = await _tokenService.ValidateTokenAsync(token);

        if (TierProgress is null)
        {
            var hash     = TokenService.ComputeSha256(token);
            var existing = await _dataverse.GetTierProgressByTokenHashAsync(hash);

            if (existing is null)
                TokenError = "This approval link is invalid or does not exist.";
            else if (existing.TokenConsumed)
                TokenError = "This approval link has already been used.";
            else
                TokenError = "This approval link is not valid (the request may have expired).";

            if (existing is not null)
                Request = await _dataverse.GetApprovalRequestAsync(existing.ApprovalRequestId);
            return;
        }

        Request  = await _dataverse.GetApprovalRequestAsync(TierProgress.ApprovalRequestId);
        AllTiers = await _dataverse.GetTierProgressForRequestAsync(TierProgress.ApprovalRequestId);

        OtherUsers = _settings.DemoUsers
            .Where(u => !u.Email.Equals(TierProgress.AssignedApprover, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _audit.LogPageVisitedAsync(TierProgress.ApprovalRequestId, TierProgress.TierNumber,
            TierProgress.AssignedApprover, ipAddress);
    }
}
