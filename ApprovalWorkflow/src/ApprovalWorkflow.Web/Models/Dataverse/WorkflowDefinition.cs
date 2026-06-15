namespace ApprovalWorkflow.Web.Models;

/// <summary>Maps to cr_workflowdefinition.</summary>
public class WorkflowDefinition
{
    public Guid   Id           { get; set; }
    public string Name         { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool   Active       { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
}
