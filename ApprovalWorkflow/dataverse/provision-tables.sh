#!/usr/bin/env bash
# provision-tables.sh — wrapper that calls the PowerShell script via pwsh
# Usage: ./provision-tables.sh <org-url> <client-id> <client-secret>
set -euo pipefail
pwsh -File "$(dirname "$0")/provision-tables.ps1" \
  -OrgUrl "$1" -ClientId "$2" -ClientSecret "$3"
