using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Pages.Admin;

public class WorkflowsModel : PageModel
{
    private readonly IDataService   _dataverse;

    public List<WorkflowDefinition> Definitions { get; set; } = new();
    public WorkflowDefinitionWithTiers? Selected  { get; set; }
    public string? Message { get; set; }
    public bool DemoMode { get; private set; }

    public WorkflowsModel(IDataService dataverse, IOptions<AppSettings> options)
    {
        _dataverse = dataverse;
        DemoMode   = options.Value.App.DemoMode;
    }

    public async Task OnGetAsync(Guid? id = null)
    {
        Definitions = await _dataverse.GetActiveWorkflowDefinitionsAsync();
        if (id.HasValue)
        {
            var tiers = await _dataverse.GetTiersForDefinitionAsync(id.Value);
            var wf    = Definitions.FirstOrDefault(w => w.Id == id.Value)
                        ?? await _dataverse.GetWorkflowDefinitionAsync(id.Value);
            if (wf is not null)
                Selected = new WorkflowDefinitionWithTiers { Definition = wf, Tiers = tiers };
        }
    }

    public async Task<IActionResult> OnPostUpdateTierAsync(
        Guid tierId, string approverEmails, int reminderHours, int expiryHours, Guid wfId)
    {
        await _dataverse.UpdateWorkflowTierAsync(tierId, new Dictionary<string, object>
        {
            ["cr_approveremails"]  = approverEmails,
            ["cr_reminderhours"]   = reminderHours,
            ["cr_expiryhours"]     = expiryHours
        });
        Message = "Tier updated.";
        return RedirectToPage(new { id = wfId });
    }

    public async Task<IActionResult> OnPostToggleWorkflowAsync(Guid wfId, bool active)
    {
        await _dataverse.UpdateWorkflowDefinitionAsync(wfId, new Dictionary<string, object>
        {
            ["cr_active"] = active
        });
        return RedirectToPage(new { id = wfId });
    }
}
