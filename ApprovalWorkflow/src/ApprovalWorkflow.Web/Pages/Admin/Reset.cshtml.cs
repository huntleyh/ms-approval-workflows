using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Pages.Admin;

public class ResetModel : PageModel
{
    private readonly ResetService    _resetService;
    private readonly IDataService    _dataverse;
    private readonly AppSettings     _settings;

    public List<ApprovalRequest>      Requests    { get; set; } = new();
    public ApprovalRequest?           Selected    { get; set; }
    public List<ApprovalTierProgress> TierProgress{ get; set; } = new();
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage   { get; set; }

    public ResetModel(ResetService resetService, IDataService dataverse, IOptions<AppSettings> options)
    {
        _resetService = resetService;
        _dataverse    = dataverse;
        _settings     = options.Value;
    }

    public async Task OnGetAsync(Guid? id = null)
    {
        Requests = (await _dataverse.GetAllApprovalRequestsAsync())
                    .Where(r => r.Status is ApprovalStatus.Pending or ApprovalStatus.Rejected)
                    .ToList();

        if (id.HasValue)
        {
            Selected     = await _dataverse.GetApprovalRequestAsync(id.Value);
            TierProgress = await _dataverse.GetTierProgressForRequestAsync(id.Value);
        }
    }

    public async Task<IActionResult> OnPostAsync(
        Guid requestId, string reason, string? overrideTier1Emails)
    {
        var triggeredBy = _settings.DemoUsers.FirstOrDefault()?.Email ?? "admin@contoso.com";

        IEnumerable<TierUpdateDto>? tierUpdates = null;
        if (!string.IsNullOrWhiteSpace(overrideTier1Emails))
        {
            tierUpdates = new[] { new TierUpdateDto { TierNumber = 1, NewApproverEmails = overrideTier1Emails } };
        }

        var ok = await _resetService.ResetApprovalAsync(requestId, triggeredBy, reason, tierUpdates);

        if (ok)
            SuccessMessage = $"Request {requestId} has been reset and re-submitted.";
        else
            ErrorMessage = "Reset failed — request not found.";

        await OnGetAsync(requestId);
        return Page();
    }
}
