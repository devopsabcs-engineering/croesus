#!/usr/bin/env bash
#
# teardown-app-registrations.sh
#
# Idempotent delete of the two Croesus mock SaaS app registrations and the
# Key Vault certificate created by provision-app-registrations.sh. Running this
# script twice MUST NOT error: every delete is guarded by an existence check and
# tolerates an already-removed object.
#
# Tier 2 also reverses the additional tenant changes so the demo tenant returns
# to its EXACT prior state:
#   * revokes the SPA -> Microsoft Graph delegated grant(s) (both Principal and
#     AllPrincipals consent types) created for the Tier 2a replay flow;
#   * resets the live `Demo__EnableReplay` App Service setting to false (the
#     code-only deploy path does not reapply the Bicep default);
#   * removes the `replay-lab` federated credential from the deploy identity.
# All Tier 2 actions are idempotent and tolerate already-removed state.
#
# Configuration (env vars, with defaults matching the provisioning script):
#   KEY_VAULT_NAME    (required) Key Vault that holds the API certificate.
#   CERT_NAME         (default: croesus-api-cert)
#   API_DISPLAY_NAME  (default: "Croesus GPD Central API (mock)")
#   SPA_DISPLAY_NAME  (default: "Croesus GPD Central SPA (mock)")
#   PURGE_CERT        (default: false) when "true", also purge the soft-deleted
#                     certificate so the name is immediately reusable.
#   STATE_FILE        (default: .demo-state.json) source of recorded object ids.
#   SPA_CLIENT_ID     (optional) SPA appId; resolved by display name when unset.
#   API_APP_NAME      (optional) deployed API Web App name; when set (with
#                     RESOURCE_GROUP) the live Demo__EnableReplay gate is reset.
#   RESOURCE_GROUP    (optional) resource group of the API Web App.
#   DEPLOY_APP_DISPLAY_NAME (default: "Croesus Deploy Identity (mock)") deploy
#                     identity that holds the replay-lab federated credential.
#
set -euo pipefail

# --- Well-known constants -----------------------------------------------------
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"      # Microsoft Graph

KEY_VAULT_NAME="${KEY_VAULT_NAME:?KEY_VAULT_NAME is required}"
CERT_NAME="${CERT_NAME:-croesus-api-cert}"
API_DISPLAY_NAME="${API_DISPLAY_NAME:-Croesus GPD Central API (mock)}"
SPA_DISPLAY_NAME="${SPA_DISPLAY_NAME:-Croesus GPD Central SPA (mock)}"
PURGE_CERT="${PURGE_CERT:-false}"
STATE_FILE="${STATE_FILE:-.demo-state.json}"
SPA_CLIENT_ID="${SPA_CLIENT_ID:-}"
API_APP_NAME="${API_APP_NAME:-}"
RESOURCE_GROUP="${RESOURCE_GROUP:-}"
DEPLOY_APP_DISPLAY_NAME="${DEPLOY_APP_DISPLAY_NAME:-Croesus Deploy Identity (mock)}"

log() { printf '>>> %s\n' "$*" >&2; }

get_app_id_by_name() {
  az ad app list --display-name "$1" --query "[0].appId" -o tsv 2>/dev/null || true
}

# Delete a Graph object by relative path (beta), tolerating already-gone (404).
del_graph() {
  local path="$1"
  if az rest --method DELETE --uri "https://graph.microsoft.com/beta/$path" >/dev/null 2>&1; then
    log "Revoked/deleted: $path"
  else
    log "Already gone (skip): $path"
  fi
}

# Remove a key from the state file if present (idempotent).
clear_state() {
  local key="$1" tmp
  [[ -f "$STATE_FILE" ]] || return 0
  tmp="$(mktemp)"
  jq "del(.$key)" "$STATE_FILE" > "$tmp" && mv "$tmp" "$STATE_FILE"
}

# Delete an app registration by display name if it exists.
delete_app() {
  local name="$1" app_id
  app_id="$(get_app_id_by_name "$name")"
  if [[ -n "$app_id" ]]; then
    log "Deleting app registration: $name ($app_id)"
    az ad app delete --id "$app_id" >/dev/null
  else
    log "App registration not found (already deleted): $name"
  fi
}

# -----------------------------------------------------------------------------
# 0) Revoke the SPA -> Microsoft Graph delegated grant(s) BEFORE deleting the
#    registrations (resolving the SPA service principal needs the SPA app to
#    still exist). Prefer the recorded grant id; fall back to a filter query so
#    BOTH Principal and AllPrincipals consent types are revoked. Idempotent.
# -----------------------------------------------------------------------------
RECORDED_GRANT=""
if [[ -f "$STATE_FILE" ]]; then
  RECORDED_GRANT="$(jq -r '.spaGraphGrant // empty' "$STATE_FILE" 2>/dev/null || true)"
fi
if [[ -n "$RECORDED_GRANT" ]]; then
  log "Revoking recorded SPA -> Graph grant: $RECORDED_GRANT"
  del_graph "oauth2PermissionGrants/$RECORDED_GRANT"
  clear_state spaGraphGrant
fi

# Fallback / completeness sweep: find any remaining SPA -> Graph grants.
[[ -z "$SPA_CLIENT_ID" ]] && SPA_CLIENT_ID="$(get_app_id_by_name "$SPA_DISPLAY_NAME")"
if [[ -n "$SPA_CLIENT_ID" ]]; then
  SPA_SP_ID="$(az ad sp show --id "$SPA_CLIENT_ID" --query id -o tsv 2>/dev/null || true)"
  GRAPH_SP_ID="$(az ad sp show --id "$GRAPH_APP_ID" --query id -o tsv 2>/dev/null || true)"
  if [[ -n "$SPA_SP_ID" && -n "$GRAPH_SP_ID" ]]; then
    GRANT_IDS="$(az rest --method GET \
      --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'" \
      --query "value[?resourceId=='$GRAPH_SP_ID'].id" -o tsv 2>/dev/null || true)"
    if [[ -n "$GRANT_IDS" ]]; then
      while IFS= read -r gid; do
        [[ -z "$gid" ]] && continue
        log "Revoking SPA -> Graph grant (sweep): $gid"
        del_graph "oauth2PermissionGrants/$gid"
      done <<< "$GRANT_IDS"
    else
      log "No remaining SPA -> Graph grants found (nothing to revoke)"
    fi
  fi
else
  log "SPA registration not found; skipping Graph grant revocation"
fi

# -----------------------------------------------------------------------------
# 1) Delete both registrations (SPA first, then API).
# -----------------------------------------------------------------------------
delete_app "$SPA_DISPLAY_NAME"
delete_app "$API_DISPLAY_NAME"

# -----------------------------------------------------------------------------
# 2) Delete the Key Vault certificate.
# -----------------------------------------------------------------------------
if az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" >/dev/null 2>&1; then
  log "Deleting Key Vault certificate: $CERT_NAME"
  az keyvault certificate delete --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" >/dev/null
else
  log "Key Vault certificate not found (already deleted): $CERT_NAME"
fi

if [[ "$PURGE_CERT" == "true" ]]; then
  if az keyvault certificate list-deleted --vault-name "$KEY_VAULT_NAME" \
       --query "[?name=='$CERT_NAME'] | [0].name" -o tsv 2>/dev/null | grep -q .; then
    log "Purging soft-deleted certificate: $CERT_NAME"
    az keyvault certificate purge --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" >/dev/null || true
  fi
fi

# -----------------------------------------------------------------------------
# 3) Reset the live Demo__EnableReplay gate to false on the API Web App. A
#    code-only deploy does NOT reapply the Bicep `false` default, so an
#    enforcement run that flipped it live must be reset here. Guarded on the
#    app actually existing; a missing app or setting is a no-op.
# -----------------------------------------------------------------------------
if [[ -n "$API_APP_NAME" && -n "$RESOURCE_GROUP" ]]; then
  if az webapp show --name "$API_APP_NAME" --resource-group "$RESOURCE_GROUP" >/dev/null 2>&1; then
    log "Resetting live Demo__EnableReplay=false on $API_APP_NAME"
    az webapp config appsettings set \
      --name "$API_APP_NAME" \
      --resource-group "$RESOURCE_GROUP" \
      --settings Demo__EnableReplay=false >/dev/null || true
  else
    log "API Web App '$API_APP_NAME' not found; skipping live gate reset"
  fi
else
  log "API_APP_NAME/RESOURCE_GROUP not set; skipping live Demo__EnableReplay reset"
fi

# -----------------------------------------------------------------------------
# 4) Remove the replay-lab federated credential from the deploy identity so the
#    tenant returns to its exact prior state. Resolved by the
#    ':environment:replay-lab' subject suffix (name-agnostic), guarded on the
#    deploy identity + credential existing. Idempotent.
# -----------------------------------------------------------------------------
DEPLOY_APP_ID="$(get_app_id_by_name "$DEPLOY_APP_DISPLAY_NAME")"
if [[ -n "$DEPLOY_APP_ID" ]]; then
  FIC_IDS="$(az ad app federated-credential list --id "$DEPLOY_APP_ID" \
    --query "[?ends_with(subject, ':environment:replay-lab')].id" -o tsv 2>/dev/null || true)"
  if [[ -n "$FIC_IDS" ]]; then
    while IFS= read -r fic; do
      [[ -z "$fic" ]] && continue
      log "Removing replay-lab federated credential: $fic"
      az ad app federated-credential delete --id "$DEPLOY_APP_ID" --federated-credential-id "$fic" >/dev/null 2>&1 || true
    done <<< "$FIC_IDS"
  else
    log "No replay-lab federated credential found (nothing to remove)"
  fi
else
  log "Deploy identity '$DEPLOY_APP_DISPLAY_NAME' not found; skipping replay-lab FIC removal"
fi

log "Teardown complete."
