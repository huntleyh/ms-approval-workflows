# Power Automate — Connection References

Before importing `ApprovalWorkflowSolution.zip`, create the following connections
in Power Automate. The import wizard will prompt you to map them.

---

## Required Connections

### 1. Office 365 Outlook

| Field | Value |
|---|---|
| Connector | Office 365 Outlook |
| Display name | `Office 365 Outlook — Approvals` |
| Authentication | Sign in with the M365 account that will send approval/decision emails |

**Used by:** Flow 1 (send approval emails to external approvers, decision notification to requester), Flow 3 (expiry notification emails).

**How to create:**
1. Go to [Power Automate](https://make.powerautomate.com) → **Data** → **Connections** → **+ New connection**
2. Search for **Office 365 Outlook**
3. Click **Create** → sign in with your sending account
4. Note the connection name — you will map this during solution import

---

### 2. Microsoft Dataverse

| Field | Value |
|---|---|
| Connector | Microsoft Dataverse |
| Display name | `Microsoft Dataverse — ApprovalWorkflow` |
| Authentication | Sign in with an account that has read access to the `cr_approvalrequest` table |

**Used by:** Flow 2 (look up the stored callback URL for a given request ID), Flow 3 (list pending expired requests).

**How to create:**
1. Go to **Data** → **Connections** → **+ New connection**
2. Search for **Microsoft Dataverse**
3. Click **Create** → sign in
4. Note the connection name

---

### 3. HTTP (no auth)

The HTTP connector used in Flow 1 (Teams webhook POST) and Flow 3 (app expiry-notify POST) does not require a named connection — it is used inline with a URI and no authentication. No pre-creation step needed.

---

## After Import — Update Placeholders in Flows

After importing, open each flow in the designer and replace:

| Placeholder | Where | How to find |
|---|---|---|
| `YOUR_TEAMS_WEBHOOK_URL` | Flow 1 → `Post_Teams_Adaptive_Card` action → URI | Teams channel → `...` → Connectors → Incoming Webhook → copy URL |
| `YOUR_APP_BASE_URL` | Flow 1 → `Wait_For_Approver_Callback` subscribe URI; Flow 3 → `Notify_App_of_Expiry` URI | ngrok HTTPS URL or your deployed app URL |
| `YOUR_DATAVERSE_ENV_ID` | Flow 2 → `List_Dataverse_Row`; Flow 3 → `List_Pending_Expired_Requests` | Power Platform admin centre → Environments → your env → copy the env ID from the URL |

> **Tip:** Run `create-solution-zip.ps1 -AppBaseUrl "https://..." -TeamsWebhook "https://..." -DataverseEnvId "..."` to pre-bake these values into the ZIP before import.
