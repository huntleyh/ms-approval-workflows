#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Approvals Demo — deploy.sh
# Usage: ./infra/deploy.sh [environment] [resource-group] [location]
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ENVIRONMENT="${1:-dev}"
RESOURCE_GROUP="${2:-rg-Approvals Demo-${ENVIRONMENT}}"
LOCATION="${3:-australiaeast}"
APP_BASE_URL="${APP_BASE_URL:?Set APP_BASE_URL env var before running}"

echo "Deploying Approvals Demo to ${RESOURCE_GROUP} (${ENVIRONMENT})"

# Ensure resource group exists
az group create \
  --name "${RESOURCE_GROUP}" \
  --location "${LOCATION}" \
  --tags app=approvals-demo environment="${ENVIRONMENT}" \
  --output none

# Deploy Bicep
DEPLOY_OUTPUT=$(az deployment group create \
  --resource-group "${RESOURCE_GROUP}" \
  --template-file "$(dirname "$0")/main.bicep" \
  --parameters environment="${ENVIRONMENT}" appBaseUrl="${APP_BASE_URL}" \
  --output json)

# Extract trigger URLs
LA1_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.la1TriggerUrl.value')
LA2_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.la2TriggerUrl.value')
LA5_URL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.la5TriggerUrl.value')
LA5_PRINCIPAL=$(echo "$DEPLOY_OUTPUT" | jq -r '.properties.outputs.la5PrincipalId.value')

echo ""
echo "Deployment complete!"
echo "LA-1 (Initiate)        : $LA1_URL"
echo "LA-2 (Callback Relay)  : $LA2_URL"
echo "LA-5 (Reset Handler)   : $LA5_URL"
echo "LA-5 Principal ID      : $LA5_PRINCIPAL"
echo ""
echo "Update appsettings.json LogicApps section:"
echo "  InitiateUrl      = $LA1_URL"
echo "  CallbackRelayUrl = $LA2_URL"
echo "  ResetHandlerUrl  = $LA5_URL"
echo ""
echo "Grant LA-5 managed identity the 'Logic App Operator' role on the subscription:"
echo "  az role assignment create --assignee $LA5_PRINCIPAL \\"
echo "    --role 'Logic App Operator' \\"
echo "    --scope /subscriptions/\$(az account show --query id -o tsv)"
