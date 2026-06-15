using ApprovalWorkflow.Web.Models;
using ApprovalWorkflow.Web.Services;

/// <summary>
/// Seeds two WorkflowDefinitions with tier configurations and two pending ApprovalRequests.
/// Runs automatically on first startup when Dataverse is empty.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(IDataService dvService, IConfiguration config)
    {
        var demoMode = bool.TryParse(config["App:DemoMode"], out var dm) && dm;

        // ── Workflow Definition 1: Internal 3-Tier ──────────────────────────
        var wf1Id = await dvService.CreateWorkflowDefinitionAsync(new WorkflowDefinition
        {
            Name         = "Standard Internal Approval",
            DocumentType = "Vendor Contract",
            Description  = "Three-tier internal approval: Manager, Finance, then Legal.",
            Active       = true
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf1Id,
            TierNumber           = 1,
            TierName             = "Manager Review",
            ApproverEmails       = config["DemoUsers:0:Email"] ?? "alice@contoso.com",
            ApproverType         = 1,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 3 : 24,
            ExpiryHours          = demoMode ? 30 : 72
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf1Id,
            TierNumber           = 2,
            TierName             = "Finance Sign-Off",
            ApproverEmails       = config["DemoUsers:1:Email"] ?? "bob@contoso.com",
            ApproverType         = 1,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 3 : 24,
            ExpiryHours          = demoMode ? 30 : 72
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf1Id,
            TierNumber           = 3,
            TierName             = "Legal Approval",
            ApproverEmails       = config["DemoUsers:0:Email"] ?? "alice@contoso.com",
            ApproverType         = 1,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 3 : 24,
            ExpiryHours          = demoMode ? 30 : 72
        });

        // ── Workflow Definition 2: External 3-Tier ──────────────────────────
        var wf2Id = await dvService.CreateWorkflowDefinitionAsync(new WorkflowDefinition
        {
            Name         = "External Partner Approval",
            DocumentType = "Partnership Agreement",
            Description  = "Three-tier approval: Internal commercial review, external partner sign-off, then executive approval.",
            Active       = true
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf2Id,
            TierNumber           = 1,
            TierName             = "Commercial Review",
            ApproverEmails       = config["DemoUsers:1:Email"] ?? "bob@contoso.com",
            ApproverType         = 1,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 3 : 24,
            ExpiryHours          = demoMode ? 30 : 72
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf2Id,
            TierNumber           = 2,
            TierName             = "Partner Sign-Off",
            ApproverEmails       = config["DemoUsers:3:Email"] ?? "david@partner.com",
            ApproverType         = 2,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 5 : 48,
            ExpiryHours          = demoMode ? 45 : 96
        });

        await dvService.CreateWorkflowTierAsync(new WorkflowTier
        {
            WorkflowDefinitionId = wf2Id,
            TierNumber           = 3,
            TierName             = "Executive Approval",
            ApproverEmails       = config["DemoUsers:0:Email"] ?? "alice@contoso.com",
            ApproverType         = 1,
            RequiredApprovals    = 1,
            ReminderHours        = demoMode ? 3 : 24,
            ExpiryHours          = demoMode ? 30 : 72
        });

        // ── Seed Requests ───────────────────────────────────────────────────
        // (In a real deployment these are not pre-seeded; users submit via the
        //  form. We add them here so the demo dashboard is not empty.)

        var requester  = config["DemoUsers:2:Email"] ?? "carol@contoso.com";
        var appBaseUrl = config["App:BaseUrl"] ?? string.Empty;

        await SeedRequest(dvService, new ApprovalRequest
        {
            Title                = "Q3 Vendor Contract — Acme Corp",
            RequestedBy          = requester,
            WorkflowDefinitionId = wf1Id,
            Status               = ApprovalStatus.Pending,
            CurrentTierNumber    = 1,
            DocumentTitle        = "Vendor Services Agreement Q3 2026",
            DocumentSummary      = "Annual renewal of the Acme Corp managed services contract. Value: $85,000.",
            DocumentContent      = SampleContent.VendorContract,
            ExpiryDate           = demoMode ? DateTime.UtcNow.AddMinutes(30) : DateTime.UtcNow.AddHours(72),
            ResetCount           = 0
        }, wf1Id, dvService, appBaseUrl);

        await SeedRequest(dvService, new ApprovalRequest
        {
            Title                = "Partnership Agreement — Beta Ltd",
            RequestedBy          = requester,
            WorkflowDefinitionId = wf2Id,
            Status               = ApprovalStatus.Pending,
            CurrentTierNumber    = 1,
            DocumentTitle        = "Strategic Partnership Agreement 2026",
            DocumentSummary      = "New reseller partnership with Beta Ltd for APAC market expansion.",
            DocumentContent      = SampleContent.PartnerAgreement,
            ExpiryDate           = demoMode ? DateTime.UtcNow.AddMinutes(45) : DateTime.UtcNow.AddHours(96),
            ResetCount           = 0
        }, wf2Id, dvService, appBaseUrl);
    }

    private static async Task SeedRequest(
        IDataService dvService,
        ApprovalRequest request,
        Guid workflowDefinitionId,
        IDataService dv,
        string appBaseUrl)
    {
        var requestId = await dvService.CreateApprovalRequestAsync(request);
        var tiers     = await dvService.GetTiersForDefinitionAsync(workflowDefinitionId);

        foreach (var tier in tiers.OrderBy(t => t.TierNumber))
        {
            var approvers = tier.ApproverEmails
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var email in approvers)
            {
                string tokenHash = string.Empty;
                string? reviewUrl = null;

                if (tier.TierNumber == 1)
                {
                    // Generate a proper raw token so ReviewUrl can be stored and used by reminders
                    var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                    tokenHash = TokenService.ComputeSha256(rawToken);
                    reviewUrl = $"{appBaseUrl.TrimEnd('/')}/Review?token={rawToken}";
                }

                await dvService.CreateTierProgressAsync(new ApprovalTierProgress
                {
                    ApprovalRequestId = requestId,
                    WorkflowTierId    = tier.Id,
                    TierNumber        = tier.TierNumber,
                    TierName          = tier.TierName,
                    Status            = tier.TierNumber == 1 ? TierStatus.Pending : TierStatus.Waiting,
                    AssignedApprover  = email,
                    ApprovalTokenHash = tokenHash,
                    ReviewUrl         = reviewUrl,
                    TokenConsumed     = false,
                    Decision          = TierDecision.Pending
                });
            }
        }
    }
}

internal static class SampleContent
{
    public static readonly string VendorContract = @"VENDOR SERVICES AGREEMENT

This agreement is entered into between Contoso Ltd (""Client"") and Acme Corp
(""Vendor"") for the provision of managed IT services for the period
1 July 2026 to 30 June 2027.

SCOPE OF SERVICES
The Vendor shall provide tier-2 helpdesk support, infrastructure monitoring,
and monthly security patching across all Client production environments.

FEES
The annual fee is $85,000 payable quarterly in advance.

TERMINATION
Either party may terminate with 90 days written notice.

Please review and approve or reject this renewal.";

    public static readonly string PartnerAgreement = @"STRATEGIC PARTNERSHIP AGREEMENT

This Partnership Agreement is made between Contoso Ltd and Beta Ltd
for the purpose of establishing a reseller arrangement covering the APAC region.

APPOINTMENT
Contoso Ltd appoints Beta Ltd as a non-exclusive reseller in Australia,
New Zealand, and Singapore.

REVENUE SHARE
Beta Ltd shall receive a 20% commission on all net new ARR generated
through its sales channel.

TERM
24 months commencing 1 May 2026, with automatic renewal on 60 days notice.

Please review all terms carefully before approving.";
}
