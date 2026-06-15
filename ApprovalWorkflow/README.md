# Approvals Demo

Production-ready multi-tier document approval system built on **.NET 8 Razor Pages**, **Azure Logic Apps**, and **Microsoft Dataverse**.

## Architecture Overview

Approval workflows are **data-driven**: tier sequences, approvers, reminder intervals, and expiry windows are all stored in Dataverse and reconfigurable without redeployment.

```
Submitter → [Web App] → LA-1 (Loop) → per-tier: email → approver reviews → decide
                            ↑                              ↓
                        LA-2 (relay)            /api/approval/decide
                                                        ↓
                                            LA-1 resumes → next tier or complete
                                                        ↓
LA-3 (hourly)  → /api/approval/expire      (marks expired requests)
LA-4 (every 4h) → /api/approval/log-reminder (sends reminders)
LA-5 (on demand) → /api/approval/reset     (cancel run, re-start)
```

## Dataverse Tables (5)

| Table | Purpose |
|---|---|
| `cr_workflowdefinition` | Named workflow templates (active/inactive) |
| `cr_workflowtier` | Tier configuration per workflow (approvers, timing) |
| `cr_approvalrequest` | One row per submitted document |
| `cr_approvaltier_progress` | One row per tier per request; holds token hash |
| `cr_approvalauditlog` | Immutable event log |

See [`dataverse/schema.md`](dataverse/schema.md) for full column definitions.

## Logic Apps (5)

| LA | Name | Role |
|---|---|---|
| LA-1 | `la-approval-initiate` | Initiate flow, loop through tiers, suspend/resume |
| LA-2 | `la-approval-callback-relay` | Stable HTTPS relay → LA-1 callback URL |
| LA-3 | `la-approval-expiry-check` | Hourly: expire overdue requests |
| LA-4 | `la-approval-reminder` | Every 4h: send reminders for pending tiers |
| LA-5 | `la-approval-reset-handler` | On-demand: cancel LA-1 run, re-start |

## API Endpoints

| Method | Endpoint | Called By |
|---|---|---|
| POST | `/api/approval/tier-callback` | LA-1 (stores suspend URL) |
| POST | `/api/approval/decide` | Review page (approver decision) |
| POST | `/api/approval/delegate` | Review page (delegation) |
| POST | `/api/approval/expire` | LA-3 |
| POST | `/api/approval/reset` | Admin/Reset page, LA-5 |
| POST | `/api/approval/log-reminder` | LA-4 |
| GET  | `/api/approval/audit/{id}` | Admin/Index page |
| GET  | `/api/approval/tiers/{id}` | Dashboard / Review page |

## Web Pages

| URL | Purpose |
|---|---|
| `/` | Dashboard — all requests, tier ladder summary |
| `/Submit` | Submit new document for approval |
| `/Review?token=xxx` | Approver decision page with full tier ladder |
| `/Admin` | Audit trail (filterable by request) |
| `/Admin/Workflows` | Tier configuration editor |
| `/Admin/Reset` | Reset a pending/rejected request |

## Configuration — `appsettings.json`

```json
{
  "Dataverse": {
    "Url":          "https://xxx.crm.dynamics.com",
    "ClientId":     "...",
    "ClientSecret": "...",
    "TenantId":     "..."
  },
  "LogicApps": {
    "InitiateUrl":      "https://prod-xx.logic.azure.com/...",
    "CallbackRelayUrl": "https://prod-xx.logic.azure.com/...",
    "ResetHandlerUrl":  "https://prod-xx.logic.azure.com/...",
    "SubscriptionId":   "...",
    "ResourceGroup":    "rg-Approvals Demo-prod",
    "LA1Name":          "la-approval-initiate"
  },
  "App": {
    "BaseUrl": "https://your-app.azurewebsites.net"
  },
  "DemoUsers": [
    { "Email": "alice@contoso.com",  "Name": "Alice",  "Type": "Internal" },
    { "Email": "bob@contoso.com",    "Name": "Bob",    "Type": "Internal" },
    { "Email": "carol@contoso.com",  "Name": "Carol",  "Type": "Internal" },
    { "Email": "david@partner.com",  "Name": "David",  "Type": "External" }
  ]
}
```

## Deployment

### 1. Provision Dataverse tables
```bash
./dataverse/provision-tables.sh https://xxx.crm.dynamics.com <client-id> <client-secret>
```

### 2. Deploy Logic Apps (Azure CLI)
```bash
export APP_BASE_URL=https://your-app.azurewebsites.net
./infra/deploy.sh prod rg-Approvals Demo-prod australiaeast
```
Copy the output trigger URLs into `appsettings.json`.

### 3. Deploy the web app
```bash
dotnet publish -c Release -o ./publish
az webapp deploy --resource-group rg-Approvals Demo-prod --name approvals-demo \
  --src-path ./publish --type zip
```

### 4. Grant LA-5 managed identity the Logic App Operator role
```bash
az role assignment create \
  --assignee <la5-principal-id> \
  --role "Logic App Operator" \
  --scope /subscriptions/<subscription-id>
```

## Local Development

```bash
# Start without Dataverse (seed data skipped gracefully)
cd ApprovalWorkflow/src/ApprovalWorkflow.Web
dotnet run
```

The app starts at `https://localhost:5001`. Seed data is only inserted when Dataverse is reachable and the workflow definitions table is empty.

## Token Flow

1. Submission creates one `cr_approvaltier_progress` row per tier.
2. Tier 1 row gets a SHA-256 token hash; raw token is embedded in the review URL.
3. Approver visits `/Review?token=<raw>` — token validated against hash on tier row.
4. On decision: tier row flagged `cr_tokenconsumed=true` (one-time use).
5. On advance: next tier row gets a fresh token hash; LA-1 is woken via callback URL.
