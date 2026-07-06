#!/usr/bin/env bash
#
# teardown-ca-policy.sh
#
# Reversible delete of the Croesus Tier 2b Token Protection Conditional Access
# policy created by provision-ca-policy.sh. Running this script twice MUST NOT
# error: the delete is guarded and tolerates an already-removed policy.
#
# Two removal strategies, applied in order:
#   1) Preferred — delete the EXACT policy id recorded in the state file
#      (deletes what we created, not "whatever currently matches the name").
#   2) Fallback  — sweep every CA policy whose displayName starts with the demo
#      prefix (`croesus-demo-`), so strays are removed even if the state file
#      was lost. CA policies have no free-text `tags`, so the prefix IS the tag.
#
# After a successful delete the caPolicyId is cleared from the state file.
#
# Graph DELETE returns 204 No Content; a repeat returns 404, which the guard
# swallows. Requires the Azure CLI logged in as a principal holding
# `Policy.ReadWrite.ConditionalAccess`.
#
# Configuration (env vars):
#   POLICY_PREFIX  (default: croesus-demo-) sweep prefix for the fallback.
#   STATE_FILE     (default: .demo-state.json) source of the recorded id.
#
set -euo pipefail

POLICY_PREFIX="${POLICY_PREFIX:-croesus-demo-}"
STATE_FILE="${STATE_FILE:-.demo-state.json}"

GRAPH_BETA="https://graph.microsoft.com/beta/identity/conditionalAccess/policies"

log() { printf '>>> %s\n' "$*" >&2; }

# Delete a CA policy by id, tolerating an already-gone policy (404).
del_policy() {
  local id="$1"
  if az rest --method DELETE --uri "$GRAPH_BETA/$id" >/dev/null 2>&1; then
    log "Deleted CA policy: $id"
  else
    log "CA policy not found (already deleted): $id"
  fi
}

# Remove a key from the state file if the file exists (idempotent).
clear_state() {
  local key="$1" tmp
  [[ -f "$STATE_FILE" ]] || return 0
  tmp="$(mktemp)"
  jq "del(.$key)" "$STATE_FILE" > "$tmp" && mv "$tmp" "$STATE_FILE"
}

# -----------------------------------------------------------------------------
# 1) Preferred: delete the exact recorded policy id.
# -----------------------------------------------------------------------------
RECORDED_ID=""
if [[ -f "$STATE_FILE" ]]; then
  RECORDED_ID="$(jq -r '.caPolicyId // empty' "$STATE_FILE")"
fi
if [[ -n "$RECORDED_ID" ]]; then
  log "Removing recorded CA policy id from state file: $RECORDED_ID"
  del_policy "$RECORDED_ID"
  clear_state caPolicyId
else
  log "No caPolicyId recorded in $STATE_FILE; relying on prefix sweep"
fi

# -----------------------------------------------------------------------------
# 2) Fallback sweep: delete any CA policy whose displayName starts with the demo
#    prefix (catches strays / lost state file). No-op when none remain.
# -----------------------------------------------------------------------------
log "Sweeping for CA policies with displayName prefix '$POLICY_PREFIX'"
SWEEP_IDS="$(az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies?\$select=id,displayName" \
  --query "value[?starts_with(displayName,'$POLICY_PREFIX')].id" -o tsv 2>/dev/null || true)"
if [[ -n "$SWEEP_IDS" ]]; then
  while IFS= read -r id; do
    [[ -z "$id" ]] && continue
    del_policy "$id"
  done <<< "$SWEEP_IDS"
else
  log "No demo CA policies found in sweep (nothing to delete)"
fi

log "CA policy teardown complete."
