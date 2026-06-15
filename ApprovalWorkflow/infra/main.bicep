// ─────────────────────────────────────────────────────────────────────────────
// Approvals Demo — main.bicep
// Deploys 5 Logic Apps + API Connections + Managed Identity for LA-5
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region for all resources')
param location string = resourceGroup().location

@description('Environment tag — dev | test | prod')
@allowed(['dev', 'test', 'prod'])
param environment string = 'dev'

@description('Base URL of the deployed ApprovalWorkflow web app (leave empty to derive from App Service hostname)')
param appBaseUrl string = ''

@description('Name of the App Service hosting the web app (hostname = {appName}-{environment}.azurewebsites.net)')
param appName string = 'approvals-demo'

@description('Teams Group ID (GUID) — find via: Teams channel > ... > Get link to channel, groupId= query param)')
param teamsGroupId string = ''

@description('Teams Channel ID — find via: Teams channel > ... > Get link to channel, channel ID in URL path)')
param teamsChannelId string = ''

@description('Email address the O365 connection is authorised as (for display purposes)')
param notifyFromEmail string = ''

// ── Resolved App Base URL ─────────────────────────────────────────────────────
// If appBaseUrl is not provided, derive from the App Service hostname.
var resolvedAppBaseUrl = !empty(appBaseUrl) ? appBaseUrl : 'https://${appName}-${environment}.azurewebsites.net'

// ── Tags ─────────────────────────────────────────────────────────────────────
var tags = {
  app: 'approvals-demo'
  environment: environment
}

// ── Office 365 Outlook API Connection ────────────────────────────────────────
// After deployment run:
//   az rest --method post \
//     --url "https://management.azure.com/subscriptions/.../resourceGroups/.../providers/Microsoft.Web/connections/office365/listConsentLinks?api-version=2016-06-01" \
//     --body '{"parameters":[{"parameterName":"token","redirectUrl":"https://portal.azure.com"}]}'
// Then open the consentLink URL and sign in with the sending M365 account.
resource office365Connection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'office365'
  location: location
  tags: tags
  properties: {
    displayName: 'Approvals Demo Notifications${notifyFromEmail != '' ? ' (${notifyFromEmail})' : ''}'
    api: {
      id: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Web/locations/${location}/managedApis/office365'
    }
    parameterValues: {}
  }
}

// ── Teams API Connection ──────────────────────────────────────────────────────
// After deployment: Azure Portal → rg-Approvals Demo-dev → teams (API Connection)
// → Edit API connection → Sign in with the M365 account that will post to Teams
resource teamsConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'teams'
  location: location
  tags: tags
  properties: {
    displayName: 'Approvals Demo Teams Notifications'
    api: {
      id: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Web/locations/${location}/managedApis/teams'
    }
    parameterValues: {}
  }
}

// ── LA-6: Teams Notification Dispatcher ──────────────────────────────────────
resource la6 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-teams-notify'
  location: location
  tags: tags
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la6-teams-notify.json'))
    parameters: {
      '$connections': {
        value: {
          teams: {
            connectionId:   teamsConnection.id
            connectionName: 'teams'
            id: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Web/locations/${location}/managedApis/teams'
          }
        }
      }
      teamsGroupId:   { value: teamsGroupId }
      teamsChannelId: { value: teamsChannelId }
    }
  }
}

// ── LA-1: Multi-Tier Initiate & Loop ─────────────────────────────────────────
resource la1 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-initiate'
  location: location
  tags: tags
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la1-initiate.json'))
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId:   office365Connection.id
            connectionName: 'office365'
            id: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Web/locations/${location}/managedApis/office365'
          }
        }
      }
      appBaseUrl:      { value: resolvedAppBaseUrl }
      tierCallbackUrl: { value: '${appBaseUrl}/api/approval/tier-callback' }
      teamsWebhookUrl: { value: listCallbackUrl('${la6.id}/triggers/manual', la6.apiVersion).value }
      notifyFromEmail: { value: notifyFromEmail }
    }
  }
}

// ── LA-2: Callback Relay (stable HTTPS endpoint) ──────────────────────────────
resource la2 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-callback-relay'
  location: location
  tags: tags
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la2-callback-relay.json'))
    parameters: {
      appBaseUrl: { value: resolvedAppBaseUrl }
    }
  }
}

// ── LA-3: Expiry Checker (hourly recurrence) ──────────────────────────────────
resource la3 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-expiry-check'
  location: location
  tags: tags
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la3-expiry-check.json'))
    parameters: {
      appBaseUrl: { value: resolvedAppBaseUrl }
      expireUrl: { value: '${appBaseUrl}/api/approval/expire' }
    }
  }
}

// ── LA-4: Reminder Sender (every 4 hours) ────────────────────────────────────
resource la4 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-reminder'
  location: location
  tags: tags
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la4-reminder.json'))
    parameters: {
      '$connections': {
        value: {
          office365: {
            connectionId:   office365Connection.id
            connectionName: 'office365'
            id: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Web/locations/${location}/managedApis/office365'
          }
        }
      }
      appBaseUrl:      { value: resolvedAppBaseUrl }
      logReminderUrl:  { value: '${appBaseUrl}/api/approval/log-reminder' }
      teamsWebhookUrl: { value: listCallbackUrl('${la6.id}/triggers/manual', la6.apiVersion).value }
    }
  }
}

// ── LA-5: Reset Handler ───────────────────────────────────────────────────────
resource la5 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'la-approval-reset-handler'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: 'Enabled'
    definition: json(loadTextContent('./logic-apps/la5-reset-handler.json'))
    parameters: {
      appBaseUrl: { value: resolvedAppBaseUrl }
      resetUrl: { value: '${appBaseUrl}/api/approval/reset' }
    }
  }
}

// ── App Service Plan ──────────────────────────────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: 'plan-${appName}-${environment}'
  location: location
  tags: tags
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  kind: 'linux'
  properties: {
    reserved: true  // required for Linux
  }
}

// ── App Service ───────────────────────────────────────────────────────────────
resource appService 'Microsoft.Web/sites@2022-09-01' = {
  name: '${appName}-${environment}'
  location: location
  tags: tags
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT',        value: 'Production' }
        { name: 'App__BaseUrl',                  value: resolvedAppBaseUrl }
        { name: 'App__DefaultExpiryHours',       value: '72' }
        { name: 'Dataverse__Url',                value: 'YOUR_DATAVERSE_URL' }  // keeps SQLite mode
        { name: 'LogicApps__InitiateUrl',        value: listCallbackUrl('${la1.id}/triggers/manual', la1.apiVersion).value }
        { name: 'LogicApps__CallbackRelayUrl',   value: listCallbackUrl('${la2.id}/triggers/manual', la2.apiVersion).value }
        { name: 'LogicApps__ResetHandlerUrl',    value: listCallbackUrl('${la5.id}/triggers/manual', la5.apiVersion).value }
        { name: 'LogicApps__SubscriptionId',     value: subscription().subscriptionId }
        { name: 'LogicApps__ResourceGroup',      value: resourceGroup().name }
        { name: 'LogicApps__LA1Name',            value: 'la-approval-initiate' }
        { name: 'DemoUsers__0__Email',           value: notifyFromEmail != '' ? notifyFromEmail : 'approver@example.com' }
        { name: 'DemoUsers__0__Name',            value: 'Huntley H' }
        { name: 'DemoUsers__0__Type',            value: 'Internal' }
        { name: 'DemoUsers__1__Email',           value: notifyFromEmail != '' ? notifyFromEmail : 'approver@example.com' }
        { name: 'DemoUsers__1__Name',            value: 'Bob Martinez' }
        { name: 'DemoUsers__1__Type',            value: 'Internal' }
        { name: 'DemoUsers__2__Email',           value: notifyFromEmail != '' ? notifyFromEmail : 'approver@example.com' }
        { name: 'DemoUsers__2__Name',            value: 'Carol Singh' }
        { name: 'DemoUsers__2__Type',            value: 'Internal' }
        { name: 'DemoUsers__3__Email',           value: notifyFromEmail != '' ? notifyFromEmail : 'approver@example.com' }
        { name: 'DemoUsers__3__Name',            value: 'David Okafor' }
        { name: 'DemoUsers__3__Type',            value: 'External' }
      ]
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output la1TriggerUrl    string = listCallbackUrl('${la1.id}/triggers/manual', la1.apiVersion).value
output la2TriggerUrl    string = listCallbackUrl('${la2.id}/triggers/manual', la2.apiVersion).value
output la5TriggerUrl    string = listCallbackUrl('${la5.id}/triggers/manual', la5.apiVersion).value
output la5PrincipalId   string = la5.identity.principalId
output la6TriggerUrl    string = listCallbackUrl('${la6.id}/triggers/manual', la6.apiVersion).value
output office365ConnId  string = office365Connection.id
output teamsConnId      string = teamsConnection.id
output appServiceUrl    string = 'https://${appService.properties.defaultHostName}'

