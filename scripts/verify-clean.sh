#!/usr/bin/env bash
#
# verify-clean.sh
#
# Asserts the demo tenant has been restored to its exact prior state after the
# Tier 2 teardown (teardown-ca-policy.sh + teardown-app-registrations.sh). It is
# READ-ONLY: it makes no changes. It exits non-zero if ANY residue is found, so
# it can gate a "the tenant is clean" claim in CI or a runbook.
#
# Five checks (all must be clean):
#   1) No Conditional Access policy with displayName starting `croesus-demo-`.
#   2) No SPA -> Microsoft Graph oauth2PermissionGrant.
#   3) No demo app registrations by display name (SPA + API).
#   4) The live `Demo__EnableReplay` App Service setting is false or absent
#      (skipped gracefully if the API Web App is gone).
#   5) No `replay-lab` federated credential on the deploy identity (skipped
#      gracefully if the deploy identity is gone).
#
# Configuration (env vars, with defaults matching the provisioning scripts):
#   API_DISPLAY_NAME  (default: "Croesus GPD Central API (mock)")
#   SPA_DISPLAY_NAME  (default: "Croesus GPD Central SPA (mock)")
#   SPA_CLIENT_ID     (optional) SPA appId; resolved by display name when unset.
#   POLICY_PREFIX     (default: croesus-demo-) demo CA policy displayName prefix.
#   API_APP_NAME      (optional) deployed API Web App name for the gate check.
#   RESOURCE_GROUP    (optional) resource group of the API Web App.
#   DEPLOY_APP_DISPLAY_NAME (default: "Croesus Deploy Identity (mock)")
#
set -euo pipefail

GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"      # Microsoft Graph

API_DISPLAY_NAME="${API_DISPLAY_NAME:-Croesus GPD Central API (mock)}"
SPA_DISPLAY_NAME="${SPA_DISPLAY_NAME:-Croesus GPD Central SPA (mock)}"
SPA_CLIENT_ID="${SPA_CLIENT_ID:-}"
POLICY_PREFIX="${POLICY_PREFIX:-croesus-demo-}"
API_APP_NAME="${API_APP_NAME:-}"
RESOURCE_GROUP="${RESOURCE_GROUP:-}"
DEPLOY_APP_DISPLAY_NAME="${DEPLOY_APP_DISPLAY_NAME:-Croesus Deploy Identity (mock)}"

FAILURES=0

pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
skip() { printf 'SKIP  %s\n' "$*"; }

get_app_id_by_name() {
  az ad app list --display-name "$1" --query "[0].appId" -o tsv 2>/dev/null || true
}

# -----------------------------------------------------------------------------
# 1) No demo CA policy remains.
# -----------------------------------------------------------------------------
CA_LEFT="$(az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName" \
  --query "value[?starts_with(displayName,'$POLICY_PREFIX')].id" -o tsv 2>/dev/null || true)"
if [[ -z "$CA_LEFT" ]]; then
  pass "No Conditional Access policy with prefix '$POLICY_PREFIX'"
else
  fail "Conditional Access policy/policies still present: $(echo "$CA_LEFT" | tr '\n' ' ')"
fi

# -----------------------------------------------------------------------------
# 2) No SPA -> Graph oauth2PermissionGrant. If the SPA app is gone, the grant is
#    gone with it — treat as clean.
# -----------------------------------------------------------------------------
[[ -z "$SPA_CLIENT_ID" ]] && SPA_CLIENT_ID="$(get_app_id_by_name "$SPA_DISPLAY_NAME")"
if [[ -z "$SPA_CLIENT_ID" ]]; then
  pass "No SPA -> Graph grant (SPA registration absent)"
else
  SPA_SP_ID="$(az ad sp show --id "$SPA_CLIENT_ID" --query id -o tsv 2>/dev/null || true)"
  GRAPH_SP_ID="$(az ad sp show --id "$GRAPH_APP_ID" --query id -o tsv 2>/dev/null || true)"
  if [[ -z "$SPA_SP_ID" ]]; then
    pass "No SPA -> Graph grant (SPA service principal absent)"
  else
    GRANTS_LEFT="$(az rest --method GET \
      --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'" \
      --query "value[?resourceId=='$GRAPH_SP_ID'].id" -o tsv 2>/dev/null || true)"
    if [[ -z "$GRANTS_LEFT" ]]; then
      pass "No SPA -> Graph oauth2PermissionGrant"
    else
      fail "SPA -> Graph grant(s) still present: $(echo "$GRANTS_LEFT" | tr '\n' ' ')"
    fi
  fi
fi

# -----------------------------------------------------------------------------
# 3) No demo app registrations by display name.
# -----------------------------------------------------------------------------
SPA_LEFT="$(get_app_id_by_name "$SPA_DISPLAY_NAME")"
API_LEFT="$(get_app_id_by_name "$API_DISPLAY_NAME")"
if [[ -z "$SPA_LEFT" && -z "$API_LEFT" ]]; then
  pass "No demo app registrations (SPA + API removed)"
else
  [[ -n "$SPA_LEFT" ]] && fail "SPA registration still present: $SPA_LEFT"
  [[ -n "$API_LEFT" ]] && fail "API registration still present: $API_LEFT"
fi

# -----------------------------------------------------------------------------
# 4) Live Demo__EnableReplay is false or absent (skip if the app is gone).
# -----------------------------------------------------------------------------
if [[ -n "$API_APP_NAME" && -n "$RESOURCE_GROUP" ]]; then
  if az webapp show --name "$API_APP_NAME" --resource-group "$RESOURCE_GROUP" >/dev/null 2>&1; then
    GATE_VALUE="$(az webapp config appsettings list \
      --name "$API_APP_NAME" --resource-group "$RESOURCE_GROUP" \
      --query "[?name=='Demo__EnableReplay'] | [0].value" -o tsv 2>/dev/null || true)"
    if [[ -z "$GATE_VALUE" || "$GATE_VALUE" == "None" ]]; then
      pass "Live Demo__EnableReplay is absent"
    elif [[ "$GATE_VALUE" == "false" ]]; then
      pass "Live Demo__EnableReplay is false"
    else
      fail "Live Demo__EnableReplay is '$GATE_VALUE' (expected false or absent)"
    fi
  else
    skip "API Web App '$API_APP_NAME' not found; live gate check skipped"
  fi
else
  skip "API_APP_NAME/RESOURCE_GROUP not set; live gate check skipped"
fi

# -----------------------------------------------------------------------------
# 5) No replay-lab federated credential on the deploy identity (skip if gone).
# -----------------------------------------------------------------------------
DEPLOY_APP_ID="$(get_app_id_by_name "$DEPLOY_APP_DISPLAY_NAME")"
if [[ -z "$DEPLOY_APP_ID" ]]; then
  skip "Deploy identity '$DEPLOY_APP_DISPLAY_NAME' not found; replay-lab FIC check skipped"
else
  FIC_LEFT="$(az ad app federated-credential list --id "$DEPLOY_APP_ID" \
    --query "[?ends_with(subject, ':environment:replay-lab')].id" -o tsv 2>/dev/null || true)"
  if [[ -z "$FIC_LEFT" ]]; then
    pass "No replay-lab federated credential on the deploy identity"
  else
    fail "replay-lab federated credential still present: $(echo "$FIC_LEFT" | tr '\n' ' ')"
  fi
fi

# -----------------------------------------------------------------------------
# Result.
# -----------------------------------------------------------------------------
echo
if [[ "$FAILURES" -gt 0 ]]; then
  printf 'Clean-tenant verification FAILED: %d check(s) found residue.\n' "$FAILURES" >&2
  exit 1
fi
printf 'Clean-tenant verification PASSED: tenant restored to prior state.\n'
