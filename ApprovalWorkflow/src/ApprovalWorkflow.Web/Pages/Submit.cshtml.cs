using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Pages;

public class SubmitModel : PageModel
{
    private readonly ApprovalService          _approvalService;
    private readonly IDataService             _dataverse;
    private readonly AppSettings              _settings;

    [BindProperty]
    public SubmitRequestModel Input { get; set; } = new();

    public SubmitResult?                  SubmitResult        { get; set; }
    public string?                        ErrorMessage        { get; set; }
    public List<WorkflowDefinition>       WorkflowDefinitions { get; set; } = new();
    public WorkflowDefinitionWithTiers?   PreviewWorkflow     { get; set; }
    public bool                           DemoMode            { get; private set; }

    public SubmitModel(
        ApprovalService approvalService,
        IDataService dataverse,
        IOptions<AppSettings> options)
    {
        _approvalService = approvalService;
        _dataverse       = dataverse;
        _settings        = options.Value;
        DemoMode         = options.Value.App.DemoMode;
    }

    public async Task OnGetAsync()
    {
        await PopulateAsync(null);
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await PopulateAsync(Input.WorkflowDefinitionId == Guid.Empty ? null : Input.WorkflowDefinitionId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await PopulateAsync(Input.WorkflowDefinitionId == Guid.Empty ? null : Input.WorkflowDefinitionId);

        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please fill in all required fields.";
            return Page();
        }

        var requester = _settings.DemoUsers.FirstOrDefault(u => u.Type == "Internal");
        Input.RequestedBy = requester?.Email ?? "requester@contoso.com";

        try
        {
            SubmitResult = await _approvalService.SubmitForApprovalAsync(Input);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Submission failed: {ex.Message}";
        }

        return Page();
    }

    private async Task PopulateAsync(Guid? wfId)
    {
        WorkflowDefinitions = await _dataverse.GetActiveWorkflowDefinitionsAsync();
        if (wfId.HasValue)
        {
            var tiers = await _dataverse.GetTiersForDefinitionAsync(wfId.Value);
            var wf    = WorkflowDefinitions.FirstOrDefault(w => w.Id == wfId.Value);
            if (wf is not null)
                PreviewWorkflow = new WorkflowDefinitionWithTiers { Definition = wf, Tiers = tiers };
        }
    }
}
