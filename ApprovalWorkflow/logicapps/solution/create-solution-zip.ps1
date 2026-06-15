#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds ApprovalWorkflowSolution.zip — the importable Power Automate solution package.

.DESCRIPTION
    Run this script AFTER updating all YOUR_* placeholders in the flow JSON files.
    The resulting ZIP is placed in powerautomate/solution/ApprovalWorkflowSolution.zip.

.EXAMPLE
    cd ApprovalWorkflow/powerautomate/solution
    ./create-solution-zip.ps1
#>

param (
    [string]$AppBaseUrl   = "YOUR_APP_BASE_URL",
    [string]$TeamsWebhook = "YOUR_TEAMS_WEBHOOK_URL",
    [string]$DataverseEnvId = "YOUR_DATAVERSE_ENV_ID"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir  = $PSScriptRoot
$contentsDir = Join-Path $scriptDir "contents"
$flowsDir   = Join-Path $scriptDir "..\flows"
$workflowsDir = Join-Path $contentsDir "Workflows"
$outputZip  = Join-Path $scriptDir "ApprovalWorkflowSolution.zip"

Write-Host "Building Power Automate solution ZIP..." -ForegroundColor Cyan

# ── 1. Create Workflows directory inside contents ─────────────────────────────
if (-not (Test-Path $workflowsDir)) {
    New-Item -ItemType Directory -Path $workflowsDir | Out-Null
}

# ── 2. Copy and patch flow files ──────────────────────────────────────────────
$flowFiles = @{
    "flow1-initiate.json" = "Flow1-InitiateApproval.json"
    "flow2-callback.json" = "Flow2-ProcessCallback.json"
    "flow3-expiry.json"   = "Flow3-ExpiryCheck.json"
}

foreach ($srcName in $flowFiles.Keys) {
    $src  = Join-Path $flowsDir $srcName
    $dest = Join-Path $workflowsDir $flowFiles[$srcName]

    if (-not (Test-Path $src)) {
        Write-Error "Flow file not found: $src"
        exit 1
    }

    $content = Get-Content $src -Raw -Encoding UTF8

    # Substitute placeholders if parameters were provided
    if ($AppBaseUrl   -ne "YOUR_APP_BASE_URL")   { $content = $content -replace "YOUR_APP_BASE_URL",   $AppBaseUrl }
    if ($TeamsWebhook -ne "YOUR_TEAMS_WEBHOOK_URL") { $content = $content -replace "YOUR_TEAMS_WEBHOOK_URL", $TeamsWebhook }
    if ($DataverseEnvId -ne "YOUR_DATAVERSE_ENV_ID") { $content = $content -replace "YOUR_DATAVERSE_ENV_ID", $DataverseEnvId }

    Set-Content -Path $dest -Value $content -Encoding UTF8
    Write-Host "  ✓ Copied $srcName → Workflows/$($flowFiles[$srcName])"
}

# ── 3. Remove old ZIP if present ──────────────────────────────────────────────
if (Test-Path $outputZip) {
    Remove-Item $outputZip -Force
}

# ── 4. Create the ZIP ─────────────────────────────────────────────────────────
Add-Type -Assembly System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($contentsDir, $outputZip)

Write-Host ""
Write-Host "✅ Solution ZIP created: $outputZip" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open Power Automate → Solutions → Import solution"
Write-Host "  2. Upload: $outputZip"
Write-Host "  3. Map connection references when prompted"
Write-Host "  4. After import, turn all three flows ON"
Write-Host "  5. Copy Flow 1 and Flow 2 trigger URLs into appsettings.json"
Write-Host ""
Write-Host "See powerautomate/solution/README-import.md for full instructions."
