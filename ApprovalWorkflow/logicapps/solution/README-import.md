# Power Automate Solution — Import Guide

## Prerequisites

- [ ] A Power Platform environment with Dataverse enabled
- [ ] An M365 account with Power Automate (per-user or per-flow licence)
- [ ] Office 365 Outlook connection created (see `connection-references.md`)
- [ ] Microsoft Dataverse connection created (see `connection-references.md`)
- [ ] Dataverse tables already provisioned (run `dataverse/provision-tables.ps1` first)
- [ ] Your app is reachable at `YOUR_APP_BASE_URL` (use ngrok for local dev)

---

## Step 1 — Build the ZIP (if not already done)

```powershell
cd ApprovalWorkflow/powerautomate/solution

# Option A: with placeholder values (recommended — bakes URLs in before import)
./create-solution-zip.ps1 `
    -AppBaseUrl      "https://YOUR-NGROK-URL.ngrok-free.app" `
    -TeamsWebhook    "https://contoso.webhook.office.com/webhookb2/YOUR_GUID/..." `
    -DataverseEnvId  "YOUR_DATAVERSE_ENV_ID_GUID"

# Option B: plain ZIP (update URLs in each flow after import)
./create-solution-zip.ps1
```

---

## Step 2 — Import the Solution

1. Open [Power Automate](https://make.powerautomate.com)
2. Select your environment (top-right corner)
3. Click **Solutions** in the left navigation
4. Click **Import solution**
5. Click **Browse** → select `ApprovalWorkflowSolution.zip`
6. Click **Next**

---

## Step 3 — Map Connection References

You will be prompted to map two connection references:

| Connection Reference | Map To |
|---|---|
| Office 365 Outlook — Approvals | Your Office 365 Outlook connection |
| Microsoft Dataverse — ApprovalWorkflow | Your Dataverse connection |

Select the connections you created in the Prerequisites step.
Click **Import**.

---

## Step 4 — Get the Flow Trigger URLs

After import succeeds:

### Flow 1 — Initiate Approval
1. Go to **Solutions** → **ApprovalWorkflowSolution** → find **Flow1 - Initiate Approval**
2. Click **Edit** to open the designer
3. Click the trigger step: **When an HTTP request is received**
4. Copy the **HTTP POST URL** (e.g. `https://prod-xx.westus.logic.azure.com:443/workflows/...`)
5. Paste into `appsettings.json` → `PowerAutomate:InitiateFlowUrl`

### Flow 2 — Process Callback
1. Open **Flow2 - Process Callback** → designer → trigger
2. Copy the HTTP POST URL
3. Paste into `appsettings.json` → `PowerAutomate:ProcessCallbackUrl`

> **Note:** Flow 2's URL is currently stored in config but is not called by the .NET app in this build — the app posts directly to the callback URL stored in Dataverse (which is Flow 1's per-run callback URL). Flow 2 exists as a stable public endpoint for future use. See `README.md §Production Upgrade Path`.

---

## Step 5 — Turn All Three Flows ON

Flows default to **Off** after solution import.

For each flow:
1. In the solution, click the flow name
2. Click **Turn on**

Verify all three show a green **On** status badge.

---

## Step 6 — Update Flow 3's App URL (if not using create-solution-zip.ps1 params)

If you didn't pre-bake the URL with script parameters:
1. Open **Flow3 - Expiry Check** → edit
2. Expand **For Each Expired Request** → **Notify App of Expiry**
3. Update the URI from `YOUR_APP_BASE_URL/api/approval/expiry-notify` to your real URL
4. Save the flow

---

## Step 7 — Test Flow 1 Manually (Postman)

Before running the app, verify Flow 1 starts successfully:

```
POST https://prod-xx.westus.logic.azure.com:443/workflows/... (your Flow 1 URL)
Content-Type: application/json

{
  "requestId":     "00000000-0000-0000-0000-000000000001",
  "title":         "Test Request",
  "requestedBy":   "test@contoso.com",
  "approverEmail": "YOUR_APPROVER_EMAIL",
  "approverType":  "Internal",
  "documentUrl":   "https://YOUR_APP_BASE_URL/Review?token=test",
  "expiryHours":   72,
  "rawToken":      "test-token"
}
```

Expected: HTTP 202 Accepted. Check the flow run history in Power Automate — it should show as **Running** (suspended waiting for callback).

---

## Troubleshooting

| Issue | Fix |
|---|---|
| Import fails with "connection reference not found" | Ensure connections are created before import and map them correctly in step 3 |
| Flow 1 fails immediately on notify step | Check Teams webhook URL is correct; for emails check the Outlook connection has send permission |
| Flow 1 stays running but app never gets callbackUrl | Ensure `YOUR_APP_BASE_URL` is reachable from the internet (check ngrok is running) |
| Flow 3 doesn't trigger | Ensure it's turned ON and the recurrence is set to every 1 hour |
| `cr_approvalrequestid` filter not working in Flow 2/3 | Confirm Dataverse tables were provisioned with exact logical names from `dataverse/schema.md` |
