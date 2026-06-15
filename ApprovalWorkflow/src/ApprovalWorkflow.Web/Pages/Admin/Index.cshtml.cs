using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApprovalWorkflow.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly IDataService   _dataverse;

    public List<ApprovalRequest>        Requests     { get; set; } = new();
    public List<AuditLogEntry>          AuditLog     { get; set; } = new();
    public List<ApprovalTierProgress>   TierProgress { get; set; } = new();
    public ApprovalRequest?             SelectedRequest { get; set; }
    public Guid?                        FilterId     { get; set; }

    public IndexModel(IDataService dataverse) => _dataverse = dataverse;

    public async Task OnGetAsync(Guid? requestId = null)
    {
        Requests = await _dataverse.GetAllApprovalRequestsAsync();
        FilterId = requestId;

        if (requestId.HasValue)
        {
            AuditLog     = await _dataverse.GetAuditLogForRequestAsync(requestId.Value);
            TierProgress = await _dataverse.GetTierProgressForRequestAsync(requestId.Value);
            SelectedRequest = Requests.FirstOrDefault(r => r.Id == requestId.Value);
        }
        else
        {
            AuditLog = await _dataverse.GetAllAuditLogsAsync();
        }
    }
}
