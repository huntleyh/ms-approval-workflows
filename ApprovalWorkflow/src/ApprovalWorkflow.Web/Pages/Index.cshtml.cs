using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApprovalWorkflow.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IDataService      _dataverse;

    public List<ApprovalRequest>           Requests  { get; set; } = new();
    public Dictionary<Guid, List<ApprovalTierProgress>> TierMap { get; set; } = new();
    public int TotalCount   { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount{ get; set; }
    public int RejectedCount{ get; set; }
    public int ResetCount   { get; set; }
    public int ExpiredCount { get; set; }

    public IndexModel(IDataService dataverse) => _dataverse = dataverse;

    public async Task OnGetAsync()
    {
        Requests      = await _dataverse.GetAllApprovalRequestsAsync();
        TotalCount    = Requests.Count;
        PendingCount  = Requests.Count(r => r.Status == ApprovalStatus.Pending);
        ApprovedCount = Requests.Count(r => r.Status == ApprovalStatus.Approved);
        RejectedCount = Requests.Count(r => r.Status == ApprovalStatus.Rejected);
        ResetCount    = Requests.Count(r => r.Status == ApprovalStatus.Reset);
        ExpiredCount  = Requests.Count(r => r.Status == ApprovalStatus.Expired);

        foreach (var r in Requests)
        {
            TierMap[r.Id] = await _dataverse.GetTierProgressForRequestAsync(r.Id);
        }
    }
}
