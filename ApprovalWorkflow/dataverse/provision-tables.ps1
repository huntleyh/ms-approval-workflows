<#
.SYNOPSIS
    Provisions all 5 Dataverse tables for the Approvals Demo (Option B).
.DESCRIPTION
    Creates: WorkflowDefinitions, WorkflowTiers, ApprovalRequests,
             ApprovalTierProgress, ApprovalAuditLog
.PARAMETER OrgUrl
    The Dataverse environment URL, e.g. https://xxx.crm.dynamics.com
.PARAMETER ClientId
    App registration client ID (must have System Customizer role)
.PARAMETER ClientSecret
    App registration client secret
#>
param(
    [Parameter(Mandatory)][string]$OrgUrl,
    [Parameter(Mandatory)][string]$ClientId,
    [Parameter(Mandatory)][string]$ClientSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Auth ──────────────────────────────────────────────────────────────────────
$tenantId = (Invoke-RestMethod "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration").issuer -replace "https://login.microsoftonline.com/|/v2.0", ""
$tokenResp = Invoke-RestMethod "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" -Method Post -Body @{
    grant_type    = "client_credentials"
    client_id     = $ClientId
    client_secret = $ClientSecret
    scope         = "$OrgUrl/.default"
}
$headers = @{
    Authorization  = "Bearer $($tokenResp.access_token)"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

function Create-Table($displayName, $logicalName, $columns) {
    $body = @{
        "@odata.type"             = "Microsoft.Dynamics.CRM.EntityMetadata"
        DisplayName               = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = $displayName; LanguageCode = 1033 }) }
        DisplayCollectionName     = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = "${displayName}s"; LanguageCode = 1033 }) }
        SchemaName                = $logicalName
        OwnershipType             = "UserOwned"
        HasActivities             = $false
        IsActivity                = $false
        PrimaryNameAttribute      = "${logicalName}_name"
        Attributes                = $columns
    }
    try {
        Invoke-RestMethod "$OrgUrl/api/data/v9.2/EntityDefinitions" -Method Post -Headers $headers -Body ($body | ConvertTo-Json -Depth 20)
        Write-Host "  Created table: $logicalName" -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode -eq 409) { Write-Host "  Table exists: $logicalName" -ForegroundColor Yellow }
        else { throw }
    }
}

function String-Attr($name, $maxLen = 255) {
    @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        SchemaName              = $name
        MaxLength               = $maxLen
        DisplayName             = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = $name; LanguageCode = 1033 }) }
        RequiredLevel           = @{ "@odata.type" = "Microsoft.Dynamics.CRM.AttributeRequiredLevelManagedProperty"; Value = "None" }
    }
}

function Int-Attr($name) {
    @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        SchemaName              = $name
        DisplayName             = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = $name; LanguageCode = 1033 }) }
        RequiredLevel           = @{ "@odata.type" = "Microsoft.Dynamics.CRM.AttributeRequiredLevelManagedProperty"; Value = "None" }
    }
}

function Bool-Attr($name) {
    @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        SchemaName              = $name
        DisplayName             = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = $name; LanguageCode = 1033 }) }
        RequiredLevel           = @{ "@odata.type" = "Microsoft.Dynamics.CRM.AttributeRequiredLevelManagedProperty"; Value = "None" }
        OptionSet               = @{ "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"; TrueOption = @{ Value = 1; Label = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = "Yes"; LanguageCode = 1033 }) } }; FalseOption = @{ Value = 0; Label = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; LocalizedLabels = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; Label = "No"; LanguageCode = 1033 }) } } }
    }
}

Write-Host "Provisioning Dataverse tables..." -ForegroundColor Cyan

# 1. WorkflowDefinitions
Create-Table "Workflow Definition" "cr_workflowdefinition" @(
    (String-Attr "cr_workflowdefinition_name"),
    (String-Attr "cr_documenttype"),
    (String-Attr "cr_description" 2000),
    (Bool-Attr   "cr_active")
)

# 2. WorkflowTiers
Create-Table "Workflow Tier" "cr_workflowtier" @(
    (String-Attr "cr_workflowtier_name"),
    (String-Attr "cr_workflowdefinitionid" 100),
    (Int-Attr    "cr_tiernumber"),
    (String-Attr "cr_tiername"),
    (String-Attr "cr_approveremails" 1000),
    (Int-Attr    "cr_approvertype"),
    (Int-Attr    "cr_requiredapprovals"),
    (Int-Attr    "cr_reminderhours"),
    (Int-Attr    "cr_expiryhours")
)

# 3. ApprovalRequests
Create-Table "Approval Request" "cr_approvalrequest" @(
    (String-Attr "cr_approvalrequest_name"),
    (String-Attr "cr_requestedby"),
    (String-Attr "cr_workflowdefinitionid" 100),
    (String-Attr "cr_workflowdefinitionname"),
    (Int-Attr    "cr_status"),
    (Int-Attr    "cr_currenttiernumber"),
    (String-Attr "cr_logicapprunid"),
    (String-Attr "cr_logicapprunurl" 1000),
    (String-Attr "cr_documenttitle"),
    (String-Attr "cr_documentsummary" 2000),
    (String-Attr "cr_documentcontent" 8192),
    (Int-Attr    "cr_resetcount")
)

# 4. ApprovalTierProgress
Create-Table "Approval Tier Progress" "cr_approvaltier_progress" @(
    (String-Attr "cr_approvaltier_progress_name"),
    (String-Attr "cr_approvalrequestid" 100),
    (String-Attr "cr_workflowtierid" 100),
    (Int-Attr    "cr_tiernumber"),
    (String-Attr "cr_tiername"),
    (Int-Attr    "cr_status"),
    (String-Attr "cr_assignedapprover"),
    (String-Attr "cr_approvaltokenhash"),
    (Bool-Attr   "cr_tokenconsumed"),
    (String-Attr "cr_callbackurl" 2000),
    (String-Attr "cr_reviewurl"   2000),
    (Int-Attr    "cr_decision"),
    (String-Attr "cr_approvercomments" 2000),
    (String-Attr "cr_delegatedfrom")
)

# 5. ApprovalAuditLog
Create-Table "Approval Audit Log" "cr_approvalauditlog" @(
    (String-Attr "cr_approvalauditlog_name"),
    (String-Attr "cr_approvalrequestid" 100),
    (Int-Attr    "cr_tiernumber"),
    (Int-Attr    "cr_eventtype"),
    (String-Attr "cr_performedby"),
    (String-Attr "cr_previousvalue" 4000),
    (String-Attr "cr_newvalue" 4000),
    (String-Attr "cr_ipaddress"),
    (String-Attr "cr_notes" 2000)
)

Write-Host ""
Write-Host "All 5 tables provisioned." -ForegroundColor Green
