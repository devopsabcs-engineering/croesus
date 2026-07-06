#!/usr/bin/env bash
#
# provision-ca-policy.sh
#
# Idempotent look-up-or-create of a REPORT-ONLY Microsoft Entra Conditional
# Access "Token Protection" policy used by the Croesus Tier 2b demo to surface
# the real Token Protection "unbound / 1008" telemetry on a SUPPORTED resource.
#
# Token protection is a SESSION control (not a grant control). Its Graph field
# is `sessionControls.secureSignInSession = { "isEnabled": true }`, which exists
# ONLY in the beta endpoint, so this script targets:
#   https://graph.microsoft.com/beta/identity/conditionalAccess/policies
#
# The policy is created in report-only mode (state =
# `enabledForReportingButNotEnforced`) so it emits the token-binding evaluation
# into the sign-in logs WITHOUT blocking anyone — safe to demo and trivial to
# reverse (see teardown-ca-policy.sh). Flip to `enabled` only for the
# enforcement beat via the commented PATCH at the end of this script.
#
# Re-running this script MUST NOT create a duplicate: an existing policy whose
# displayName matches POLICY_DISPLAY_NAME is PATCHed in place instead. The demo
# `croesus-demo-` prefix doubles as the strays-finding tag for teardown, because
# CA policies have no free-text `tags` field.
#
# This script runs on Linux with the Azure CLI already logged in (`az login` /
# OIDC) as a principal holding `Policy.ReadWrite.ConditionalAccess`
# (Conditional Access Administrator or Security Administrator). Live execution
# requires a tenant and is out of scope for static syntax checks.
#
# Configuration (env vars):
#   TEST_USER_OBJECT_ID        (required) object id of the scoped test user.
#   BREAK_GLASS_USER_OBJECT_ID (required) object id of the excluded break-glass
#                              account (never lock everyone out).
#   RESOURCE_APP_ID            (default: Exchange Online
#                              00000002-0000-0ff1-ce00-000000000000) a resource
#                              token protection actually enforces on.
#   POLICY_DISPLAY_NAME        (default: croesus-demo-token-protection) MUST keep
#                              the `croesus-demo-` prefix for teardown sweeps.
#   STATE_FILE                 (default: .demo-state.json) records caPolicyId for
#                              reversible teardown.
#
# Outputs (written to $GITHUB_OUTPUT when set, otherwise echoed):
#   ca_policy_id
#
set -euo pipefail

# --- Configuration ------------------------------------------------------------
TEST_USER_OBJECT_ID="${TEST_USER_OBJECT_ID:?TEST_USER_OBJECT_ID is required}"
BREAK_GLASS_USER_OBJECT_ID="${BREAK_GLASS_USER_OBJECT_ID:?BREAK_GLASS_USER_OBJECT_ID is required}"
RESOURCE_APP_ID="${RESOURCE_APP_ID:-00000002-0000-0ff1-ce00-000000000000}"   # Exchange Online
POLICY_DISPLAY_NAME="${POLICY_DISPLAY_NAME:-croesus-demo-token-protection}"
STATE_FILE="${STATE_FILE:-.demo-state.json}"

GRAPH_BETA="https://graph.microsoft.com/beta/identity/conditionalAccess/policies"

log() { printf '>>> %s\n' "$*" >&2; }

# Merge a single key/value into the JSON state file (see provision-app-registrations.sh).
record_state() {
  local key="$1" value="$2" tmp
  [[ -f "$STATE_FILE" ]] || echo '{}' > "$STATE_FILE"
  tmp="$(mktemp)"
  jq --arg v "$value" ".$key = \$v" "$STATE_FILE" > "$tmp" && mv "$tmp" "$STATE_FILE"
}

# The policy body. Report-only + native-client + supported resource + narrowly
# scoped to the test user with a break-glass exclude. secureSignInSession is the
# Graph representation of "Require token protection for sign-in sessions".
POLICY_BODY="$(cat <<JSON
{
  "displayName": "$POLICY_DISPLAY_NAME",
  "state": "enabledForReportingButNotEnforced",
  "conditions": {
    "clientAppTypes": ["mobileAppsAndDesktopClients"],
    "platforms": { "includePlatforms": ["windows", "macOS", "iOS"] },
    "applications": { "includeApplications": ["$RESOURCE_APP_ID"] },
    "users": {
      "includeUsers": ["$TEST_USER_OBJECT_ID"],
      "excludeUsers": ["$BREAK_GLASS_USER_OBJECT_ID"]
    }
  },
  "sessionControls": {
    "secureSignInSession": { "isEnabled": true }
  }
}
JSON
)"

# -----------------------------------------------------------------------------
# 1) Idempotency guard: look up an existing policy by displayName. PATCH it in
#    place if found; only POST (create) when absent.
# -----------------------------------------------------------------------------
EXISTING_ID="$(az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName" \
  --query "value[?displayName=='$POLICY_DISPLAY_NAME'] | [0].id" -o tsv 2>/dev/null || true)"
EXISTING_ID="${EXISTING_ID%$'\r'}"

if [[ -n "$EXISTING_ID" && "$EXISTING_ID" != "None" ]]; then
  log "Reusing existing CA policy '$POLICY_DISPLAY_NAME' ($EXISTING_ID) — patching in place"
  az rest --method PATCH \
    --uri "$GRAPH_BETA/$EXISTING_ID" \
    --headers "Content-Type=application/json" \
    --body "$POLICY_BODY" >/dev/null
  CA_POLICY_ID="$EXISTING_ID"
else
  log "Creating report-only Token Protection CA policy: $POLICY_DISPLAY_NAME"
  CA_POLICY_ID="$(az rest --method POST \
    --uri "$GRAPH_BETA" \
    --headers "Content-Type=application/json" \
    --body "$POLICY_BODY" \
    --query id -o tsv)"
  CA_POLICY_ID="${CA_POLICY_ID%$'\r'}"
fi

# -----------------------------------------------------------------------------
# 2) Record the policy id for reversible teardown.
# -----------------------------------------------------------------------------
log "Recording CA policy id to $STATE_FILE"
record_state caPolicyId "$CA_POLICY_ID"

# -----------------------------------------------------------------------------
# 3) OPTIONAL — enforcement beat. Uncomment to flip the policy from report-only
#    to ENFORCED for the demo's "block the unbound token" moment, then re-run
#    this script (or teardown-ca-policy.sh) to revert. Leave commented so the
#    default provision path never enforces / never locks anyone out.
# -----------------------------------------------------------------------------
# az rest --method PATCH \
#   --uri "$GRAPH_BETA/$CA_POLICY_ID" \
#   --headers "Content-Type=application/json" \
#   --body '{ "state": "enabled" }' >/dev/null
# log "CA policy $CA_POLICY_ID flipped to ENFORCED (state=enabled)"

# -----------------------------------------------------------------------------
# Outputs (non-secret).
# -----------------------------------------------------------------------------
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "ca_policy_id=$CA_POLICY_ID" >> "$GITHUB_OUTPUT"
fi

log "CA policy provisioning complete."
echo "ca_policy_id=$CA_POLICY_ID"
