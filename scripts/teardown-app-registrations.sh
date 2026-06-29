#!/usr/bin/env bash
#
# teardown-app-registrations.sh
#
# Idempotent delete of the two Croesus mock SaaS app registrations and the
# Key Vault certificate created by provision-app-registrations.sh. Running this
# script twice MUST NOT error: every delete is guarded by an existence check and
# tolerates an already-removed object.
#
# Configuration (env vars, with defaults matching the provisioning script):
#   KEY_VAULT_NAME    (required) Key Vault that holds the API certificate.
#   CERT_NAME         (default: croesus-api-cert)
#   API_DISPLAY_NAME  (default: "Croesus GPD Central API (mock)")
#   SPA_DISPLAY_NAME  (default: "Croesus GPD Central SPA (mock)")
#   PURGE_CERT        (default: false) when "true", also purge the soft-deleted
#                     certificate so the name is immediately reusable.
#
set -euo pipefail

KEY_VAULT_NAME="${KEY_VAULT_NAME:?KEY_VAULT_NAME is required}"
CERT_NAME="${CERT_NAME:-croesus-api-cert}"
API_DISPLAY_NAME="${API_DISPLAY_NAME:-Croesus GPD Central API (mock)}"
SPA_DISPLAY_NAME="${SPA_DISPLAY_NAME:-Croesus GPD Central SPA (mock)}"
PURGE_CERT="${PURGE_CERT:-false}"

log() { printf '>>> %s\n' "$*" >&2; }

get_app_id_by_name() {
  az ad app list --display-name "$1" --query "[0].appId" -o tsv 2>/dev/null || true
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

log "Teardown complete."
