<#
.SYNOPSIS
    Deploys the Approvals Demo Logic Apps and API connections to Azure.

.DESCRIPTION
    Creates:
      - Resource group (if it doesn't exist)
      - Office 365 Outlook API connection
      - Microsoft Dataverse API connection
      - Three Logic Apps (flow1-initiate, flow2-callback, flow3-expiry)

    After deployment you must MANUALLY AUTHORIZE the two API connections in the
    Azure portal (one-time OAuth consent). The script prints direct authorisation URLs.

.EXAMPLE
    .\deploy.ps1 `
        -ResourceGroup    "rg-Approvals Demo-demo" `
        -Location         "australiaeast" `
        -AppName          "approvals-demo" `
        -AppBaseUrl       "https://myapp.azurewebsites.net" `
        -DataverseEnvUrl  "https://myorg.crm.dynamics.com" `
        -TeamsWebhookUrl  "https://myorg.webhook.office.com/..."

.NOTES
    Requires: Azure CLI  (az)   – https://aka.ms/installazurecliwindows
    Run once before this script: az login
#>

[CmdletBinding()]
param (
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $AppBaseUrl,
    [Parameter(Mandatory)] [string] $DataverseEnvUrl,
    [string] $Location        = "australiaeast",
    [string] $AppName         = "approvals-demo",
    [string] $TeamsWebhookUrl = "",
    [string] $SubscriptionId  = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── 0. Validate prerequisites ────────────────────────────────────────────────
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI not found. Install from https://aka.ms/installazurecliwindows then re-run."
    exit 1
}

$templateFile = Join-Path $PSScriptRoot "deploy.json"
if (-not (Test-Path $templateFile)) {
    Write-Error "deploy.json not found at $templateFile"
    exit 1
}

# ── 1. Set subscription (optional) ───────────────────────────────────────────
if ($SubscriptionId) {
    Write-Host "`n[1/6] Setting subscription to: $SubscriptionId" -ForegroundColor Cyan
    az account set --subscription $SubscriptionId
}
else {
    Write-Host "`n[1/6] Using current default subscription." -ForegroundColor Cyan
    $SubscriptionId = (az account show --query id -o tsv)
}

Write-Host "      Subscription: $SubscriptionId" -ForegroundColor Gray

# ── 2. Create / verify resource group ────────────────────────────────────────
Write-Host "`n[2/6] Ensuring resource group: $ResourceGroup ($Location)" -ForegroundColor Cyan
$rgExists = (az group exists --name $ResourceGroup)
if ($rgExists -eq "false") {
    Write-Host "      Creating resource group..." -ForegroundColor Gray
    az group create --name $ResourceGroup --location $Location | Out-Null
}
else {
    Write-Host "      Resource group already exists." -ForegroundColor Gray
}

# ── 3. Deploy ARM template ────────────────────────────────────────────────────
Write-Host "`n[3/6] Deploying ARM template (connections + 3 Logic Apps)..." -ForegroundColor Cyan
Write-Host "      This takes ~2 minutes." -ForegroundColor Gray

$deploymentName = "$AppName-deploy-$(Get-Date -Format 'yyyyMMddHHmmss')"

$deployResult = az deployment group create `
    --resource-group $ResourceGroup `
    --name $deploymentName `
    --template-file $templateFile `
    --parameters `
        appName="$AppName" `
        appBaseUrl="$AppBaseUrl" `
        dataverseEnvironmentUrl="$DataverseEnvUrl" `
        teamsWebhookUrl="$TeamsWebhookUrl" `
        location="$Location" `
    --output json 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "ARM deployment failed:`n$deployResult"
    exit 1
}

$deployOutput = $deployResult | ConvertFrom-Json
$outputs      = $deployOutput.properties.outputs

$flow1Name            = $outputs.flow1LogicAppName.value
$flow2Name            = $outputs.flow2LogicAppName.value
$flow3Name            = $outputs.flow3LogicAppName.value
$office365ConnName    = $outputs.office365ConnectionName.value
$dataverseConnName    = $outputs.dataverseConnectionName.value

Write-Host "      Deployed: $flow1Name, $flow2Name, $flow3Name" -ForegroundColor Green

# ── 4. Retrieve Logic App trigger URLs ────────────────────────────────────────
Write-Host "`n[4/6] Retrieving Logic App HTTP trigger URLs..." -ForegroundColor Cyan

function Get-TriggerUrl([string]$rg, [string]$workflowName, [string]$triggerName) {
    $result = az logic workflow trigger list-callback-url `
        --resource-group $rg `
        --workflow-name  $workflowName `
        --trigger-name   $triggerName `
        --output json 2>&1
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($result | ConvertFrom-Json).value
}

$flow1TriggerUrl = Get-TriggerUrl $ResourceGroup $flow1Name "When_a_HTTP_request_is_received"
$flow2TriggerUrl = Get-TriggerUrl $ResourceGroup $flow2Name "When_a_HTTP_request_is_received"

if (-not $flow1TriggerUrl) {
    Write-Warning "Could not retrieve Flow 1 trigger URL automatically. Get it from the Azure portal."
}
if (-not $flow2TriggerUrl) {
    Write-Warning "Could not retrieve Flow 2 trigger URL automatically. Get it from the Azure portal."
}

# ── 5. Print connection authorisation links ───────────────────────────────────
Write-Host "`n[5/6] CONNECTION AUTHORISATION REQUIRED" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host "Both API connections require one-time OAuth consent in the portal." -ForegroundColor Yellow
Write-Host ""

$portalBase = "https://portal.azure.com/#blade/Microsoft_Azure_ManagementGroups/AzureResourceManagerTemplateBlade"
$office365EditUrl  = "https://portal.azure.com/#resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$office365ConnName/edit"
$dataverseEditUrl  = "https://portal.azure.com/#resource/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$dataverseConnName/edit"

Write-Host "  1. Office 365 Outlook connection – sign in with your M365 account:" -ForegroundColor White
Write-Host "     $office365EditUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "  2. Microsoft Dataverse connection – sign in with your Dataverse account:" -ForegroundColor White
Write-Host "     $dataverseEditUrl" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Click the connection, then 'Authorize' / 'Edit API connection' > 'Save'." -ForegroundColor White
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Yellow

# ── 6. Print appsettings values ───────────────────────────────────────────────
Write-Host "`n[6/6] UPDATE appsettings.json with these values:" -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host '  "LogicApps": {' -ForegroundColor White
Write-Host "    `"Flow1TriggerUrl`": `"$flow1TriggerUrl`"," -ForegroundColor Yellow
Write-Host "    `"Flow2TriggerUrl`": `"$flow2TriggerUrl`"" -ForegroundColor Yellow
Write-Host '  },' -ForegroundColor White
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Deployment complete. Next steps:" -ForegroundColor Green
Write-Host "  1. Authorize both API connections (URLs above)" -ForegroundColor White
Write-Host "  2. Paste the trigger URLs into appsettings.json" -ForegroundColor White
Write-Host "  3. Provision Dataverse tables: cd ../dataverse && ./provision-tables.ps1" -ForegroundColor White
Write-Host "  4. Run the .NET app: cd ../src/ApprovalWorkflow.Web && dotnet run" -ForegroundColor White
Write-Host ""
