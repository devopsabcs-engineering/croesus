#!/usr/bin/env bash
#
# verify-app-registrations.sh
#
# Asserts that the API registration is genuinely OBO-capable, matching the
# research verification table (OBO-capable vs broken baseline):
#
#   | Check                | OBO-capable (CORRECT)                          |
#   | -------------------- | ---------------------------------------------- |
#   | Exposed scope        | api.oauth2PermissionScopes has access_as_user  |
#   | Application ID URI   | identifierUris set (api://<API_CLIENT_ID>)     |
#   | Confidential cred    | passwordCredentials OR keyCredentials non-empty|
#   | SPA->API wiring      | preAuthorizedApplications + knownClientApps    |
#   | Downstream Graph     | Graph User.Read delegated + admin consent      |
#
# Exits non-zero if ANY OBO-capability check fails. Read-only: makes no changes.
#
# Configuration (env vars, with defaults matching the provisioning script):
#   API_DISPLAY_NAME  (default: "Croesus GPD Central API (mock)")
#   SPA_DISPLAY_NAME  (default: "Croesus GPD Central SPA (mock)")
# Optional explicit ids (skip display-name lookup):
#   API_CLIENT_ID, SPA_CLIENT_ID
#
set -euo pipefail

GRAPH_USER_READ="e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # User.Read (delegated)

API_DISPLAY_NAME="${API_DISPLAY_NAME:-Croesus GPD Central API (mock)}"
SPA_DISPLAY_NAME="${SPA_DISPLAY_NAME:-Croesus GPD Central SPA (mock)}"
API_CLIENT_ID="${API_CLIENT_ID:-}"
SPA_CLIENT_ID="${SPA_CLIENT_ID:-}"

FAILURES=0

pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }

get_app_id_by_name() {
  az ad app list --display-name "$1" --query "[0].appId" -o tsv 2>/dev/null || true
}

# -----------------------------------------------------------------------------
# Resolve appIds.
# -----------------------------------------------------------------------------
[[ -z "$API_CLIENT_ID" ]] && API_CLIENT_ID="$(get_app_id_by_name "$API_DISPLAY_NAME")"
[[ -z "$SPA_CLIENT_ID" ]] && SPA_CLIENT_ID="$(get_app_id_by_name "$SPA_DISPLAY_NAME")"

if [[ -z "$API_CLIENT_ID" ]]; then
  fail "API registration not found by name '$API_DISPLAY_NAME' and no API_CLIENT_ID provided"
  exit 1
fi
printf '>>> Verifying API %s\n' "$API_CLIENT_ID" >&2
[[ -n "$SPA_CLIENT_ID" ]] && printf '>>> Expecting SPA %s\n' "$SPA_CLIENT_ID" >&2

API_JSON="$(az ad app show --id "$API_CLIENT_ID" -o json)"

jq_query() { printf '%s' "$API_JSON" | jq -r "$1"; }

# -----------------------------------------------------------------------------
# 1) Exposed scope: access_as_user present.
# -----------------------------------------------------------------------------
if jq_query '.api.oauth2PermissionScopes[]?.value' | grep -qx "access_as_user"; then
  pass "Exposed scope access_as_user present"
else
  fail "Exposed scope access_as_user MISSING"
fi

# -----------------------------------------------------------------------------
# 2) Application ID URI set.
# -----------------------------------------------------------------------------
if [[ -n "$(jq_query '.identifierUris[]? // empty')" ]]; then
  pass "Application ID URI set: $(jq_query '.identifierUris | join(",")')"
else
  fail "Application ID URI (identifierUris) NOT set"
fi

# -----------------------------------------------------------------------------
# 3) Confidential credential present (passwordCredentials OR keyCredentials).
# -----------------------------------------------------------------------------
N_SECRETS="$(jq_query '(.passwordCredentials // []) | length')"
N_CERTS="$(jq_query '(.keyCredentials // []) | length')"
if [[ "${N_SECRETS:-0}" -gt 0 || "${N_CERTS:-0}" -gt 0 ]]; then
  pass "Confidential credential present (secrets=$N_SECRETS certs=$N_CERTS)"
else
  fail "No confidential credential (passwordCredentials and keyCredentials both empty)"
fi

# -----------------------------------------------------------------------------
# 4) SPA->API wiring: preAuthorizedApplications + knownClientApplications set.
# -----------------------------------------------------------------------------
N_PREAUTH="$(jq_query '(.api.preAuthorizedApplications // []) | length')"
N_KNOWN="$(jq_query '(.api.knownClientApplications // []) | length')"
if [[ "${N_PREAUTH:-0}" -gt 0 ]]; then
  pass "preAuthorizedApplications set ($N_PREAUTH)"
else
  fail "preAuthorizedApplications NOT set"
fi
if [[ "${N_KNOWN:-0}" -gt 0 ]]; then
  pass "knownClientApplications set ($N_KNOWN)"
else
  fail "knownClientApplications NOT set"
fi

# If we know the SPA id, confirm it is actually the wired client.
if [[ -n "$SPA_CLIENT_ID" ]]; then
  if jq_query '.api.preAuthorizedApplications[]?.appId' | grep -qx "$SPA_CLIENT_ID"; then
    pass "SPA $SPA_CLIENT_ID is pre-authorized for the API"
  else
    fail "SPA $SPA_CLIENT_ID NOT in preAuthorizedApplications"
  fi
  if jq_query '.api.knownClientApplications[]?' | grep -qx "$SPA_CLIENT_ID"; then
    pass "SPA $SPA_CLIENT_ID is in knownClientApplications"
  else
    fail "SPA $SPA_CLIENT_ID NOT in knownClientApplications"
  fi
fi

# -----------------------------------------------------------------------------
# 5) Downstream Graph User.Read delegated + admin consent.
# -----------------------------------------------------------------------------
PERMS_JSON="$(az ad app permission list --id "$API_CLIENT_ID" -o json 2>/dev/null || echo '[]')"
if printf '%s' "$PERMS_JSON" \
     | jq -e --arg id "$GRAPH_USER_READ" \
       'any(.[]?; (.resourceAccess // [.])[]?.id == $id or .id == $id)' >/dev/null 2>&1 \
   || printf '%s' "$PERMS_JSON" | grep -q "$GRAPH_USER_READ"; then
  pass "Microsoft Graph User.Read delegated permission requested"
else
  fail "Microsoft Graph User.Read delegated permission NOT requested"
fi

# Admin-consent grants actually present in the tenant for this app.
GRANTS_JSON="$(az ad app permission list-grants --id "$API_CLIENT_ID" -o json 2>/dev/null || echo '[]')"
if printf '%s' "$GRANTS_JSON" | jq -e 'length > 0' >/dev/null 2>&1; then
  pass "Admin-consent (OAuth2 permission) grants present"
else
  fail "No admin-consent grants found for the API (User.Read not consented)"
fi

# -----------------------------------------------------------------------------
# Result.
# -----------------------------------------------------------------------------
echo
if [[ "$FAILURES" -gt 0 ]]; then
  printf 'OBO-capability verification FAILED: %d check(s) failed.\n' "$FAILURES" >&2
  exit 1
fi
printf 'OBO-capability verification PASSED: all checks succeeded.\n'
