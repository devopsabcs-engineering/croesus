#!/usr/bin/env bash
#
# provision-app-registrations.sh
#
# Idempotent look-up-or-create of the two Entra app registrations that make the
# Croesus mock SaaS On-Behalf-Of (OBO) flow work:
#
#   Registration B (API)  - confidential client, exposes `access_as_user`,
#                           holds a certificate credential stored in Key Vault,
#                           and has Microsoft Graph User.Read (delegated) consented.
#   Registration A (SPA)   - public client, pre-authorized for the API scope,
#                           with a `spa` platform redirect URI.
#
# Re-running this script MUST NOT create duplicate registrations: every object is
# looked up by display name (apps) or name (certificate) before it is created.
#
# This script runs in CI on Linux with the Azure CLI already logged in
# (`az login` / OIDC). Live execution requires a tenant and is out of scope for
# static syntax checks.
#
# Secrets policy: the certificate private key never leaves Key Vault, and no
# credential, private key, or token is ever echoed to stdout/stderr.
#
# Configuration (env vars, with defaults):
#   KEY_VAULT_NAME       (required) Key Vault that stores the API certificate.
#   CERT_NAME            (default: croesus-api-cert) Key Vault certificate name.
#   API_DISPLAY_NAME     (default: "Croesus GPD Central API (mock)")
#   SPA_DISPLAY_NAME     (default: "Croesus GPD Central SPA (mock)")
#   SPA_REDIRECT_URI     (default: https://localhost:3000)
#   SIGN_IN_AUDIENCE     (default: AzureADMyOrg) single-tenant demo shape.
#
# Outputs (written to $GITHUB_OUTPUT when set, otherwise echoed):
#   api_client_id, spa_client_id, api_scope
#
set -euo pipefail

# --- Well-known constants -----------------------------------------------------
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"      # Microsoft Graph
GRAPH_USER_READ="e1fe6dd8-ba31-4d61-89e7-88639da4683d"   # User.Read (delegated)

# --- Configuration ------------------------------------------------------------
KEY_VAULT_NAME="${KEY_VAULT_NAME:?KEY_VAULT_NAME is required}"
CERT_NAME="${CERT_NAME:-croesus-api-cert}"
API_DISPLAY_NAME="${API_DISPLAY_NAME:-Croesus GPD Central API (mock)}"
SPA_DISPLAY_NAME="${SPA_DISPLAY_NAME:-Croesus GPD Central SPA (mock)}"
SPA_REDIRECT_URI="${SPA_REDIRECT_URI:-https://localhost:3000}"
SIGN_IN_AUDIENCE="${SIGN_IN_AUDIENCE:-AzureADMyOrg}"

log() { printf '>>> %s\n' "$*" >&2; }

# Portable UUID generator. Uses uuidgen when present (Linux CI), otherwise falls
# back to the kernel RNG or PowerShell so the script also runs under Git Bash on
# Windows. Output is a bare lowercase UUID with any trailing CR stripped.
gen_uuid() {
  if command -v uuidgen >/dev/null 2>&1; then
    uuidgen | tr -d '\r'
  elif [[ -r /proc/sys/kernel/random/uuid ]]; then
    tr -d '\r' < /proc/sys/kernel/random/uuid
  else
    powershell.exe -NoProfile -Command '[guid]::NewGuid().Guid' | tr -d '\r'
  fi
}

# Look up an app registration's appId by display name. Empty string if absent.
get_app_id_by_name() {
  az ad app list --display-name "$1" --query "[0].appId" -o tsv 2>/dev/null || true
}

# Ensure a service principal exists for the given appId (idempotent).
ensure_sp() {
  local app_id="$1"
  if ! az ad sp show --id "$app_id" >/dev/null 2>&1; then
    log "Creating service principal for $app_id"
    az ad sp create --id "$app_id" >/dev/null
  fi
}

# -----------------------------------------------------------------------------
# 1) Registration B (API, confidential client) — look up or create
# -----------------------------------------------------------------------------
API_ID="$(get_app_id_by_name "$API_DISPLAY_NAME")"
if [[ -z "$API_ID" ]]; then
  log "Creating API registration: $API_DISPLAY_NAME"
  API_ID="$(az ad app create \
    --display-name "$API_DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --query appId -o tsv)"
else
  log "Reusing existing API registration: $API_DISPLAY_NAME ($API_ID)"
fi
ensure_sp "$API_ID"
API_OBJ="$(az ad app show --id "$API_ID" --query id -o tsv)"

# Application ID URI api://<API_CLIENT_ID> (idempotent set).
az ad app update --id "$API_ID" --identifier-uris "api://$API_ID" >/dev/null

# Reuse an existing access_as_user scope id so preAuthorizedApplications stays
# valid across re-runs; only mint a fresh uuid the first time.
SCOPE_ID="$(az ad app show --id "$API_ID" \
  --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv)"
if [[ -z "$SCOPE_ID" || "$SCOPE_ID" == "None" ]]; then
  SCOPE_ID="$(gen_uuid)"
  log "Minting new access_as_user scope id: $SCOPE_ID"
else
  log "Reusing existing access_as_user scope id: $SCOPE_ID"
fi

# -----------------------------------------------------------------------------
# 2) Registration A (SPA, public client) — look up or create
# -----------------------------------------------------------------------------
SPA_ID="$(get_app_id_by_name "$SPA_DISPLAY_NAME")"
if [[ -z "$SPA_ID" ]]; then
  log "Creating SPA registration: $SPA_DISPLAY_NAME"
  SPA_ID="$(az ad app create \
    --display-name "$SPA_DISPLAY_NAME" \
    --sign-in-audience "$SIGN_IN_AUDIENCE" \
    --query appId -o tsv)"
else
  log "Reusing existing SPA registration: $SPA_DISPLAY_NAME ($SPA_ID)"
fi
ensure_sp "$SPA_ID"
SPA_OBJ="$(az ad app show --id "$SPA_ID" --query id -o tsv)"

# SPA platform redirect URI (spa, not web). PATCH is authoritative/idempotent.
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$SPA_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{\"spa\":{\"redirectUris\":[\"$SPA_REDIRECT_URI\"]}}" >/dev/null

# -----------------------------------------------------------------------------
# 3) Expose access_as_user + knownClientApplications + preAuthorizedApplications
#    (all on the API). PATCH is authoritative so re-runs converge.
# -----------------------------------------------------------------------------
log "Configuring exposed scope, knownClientApplications, preAuthorizedApplications on API"
# Graph validates preAuthorizedApplications.delegatedPermissionIds against the
# scopes that ALREADY exist on the app, so the scope must be committed first.
# PATCH the scope (+ knownClientApplications) in one call, then PATCH
# preAuthorizedApplications referencing the now-existing scope id in a second.
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{
    \"api\": {
      \"oauth2PermissionScopes\": [
        {
          \"id\": \"$SCOPE_ID\",
          \"adminConsentDescription\": \"Access the Croesus GPD Central API as the signed-in user\",
          \"adminConsentDisplayName\": \"Access Croesus GPD Central API\",
          \"isEnabled\": true,
          \"type\": \"User\",
          \"userConsentDescription\": \"Access the Croesus GPD Central API on your behalf\",
          \"userConsentDisplayName\": \"Access Croesus GPD Central API\",
          \"value\": \"access_as_user\"
        }
      ],
      \"knownClientApplications\": [\"$SPA_ID\"]
    }
  }" >/dev/null

az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$API_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{
    \"api\": {
      \"preAuthorizedApplications\": [
        { \"appId\": \"$SPA_ID\", \"delegatedPermissionIds\": [\"$SCOPE_ID\"] }
      ]
    }
  }" >/dev/null

# -----------------------------------------------------------------------------
# 4) Certificate credential on the API — create/import in Key Vault, then attach
#    only the PUBLIC certificate to the app registration. The private key never
#    leaves Key Vault and is never printed.
# -----------------------------------------------------------------------------
if ! az keyvault certificate show --vault-name "$KEY_VAULT_NAME" --name "$CERT_NAME" >/dev/null 2>&1; then
  log "Creating self-signed certificate $CERT_NAME in Key Vault $KEY_VAULT_NAME"
  POLICY_FILE="$(mktemp)"
  trap 'rm -f "${POLICY_FILE:-}" "${PUBLIC_CER:-}"' EXIT
  az keyvault certificate get-default-policy > "$POLICY_FILE"
  # Subject is non-secret metadata; safe to set.
  az keyvault certificate create \
    --vault-name "$KEY_VAULT_NAME" \
    --name "$CERT_NAME" \
    --policy "@$POLICY_FILE" >/dev/null
else
  log "Reusing existing Key Vault certificate $CERT_NAME"
  trap 'rm -f "${PUBLIC_CER:-}"' EXIT
fi

# Attach the certificate to the API registration only if it has no key
# credentials yet (keeps re-runs from accumulating duplicate credentials).
HAS_KEY_CRED="$(az ad app show --id "$API_ID" \
  --query "length(keyCredentials)" -o tsv 2>/dev/null || echo 0)"
if [[ "${HAS_KEY_CRED:-0}" == "0" ]]; then
  log "Attaching public certificate to API registration"
  PUBLIC_CER="$(mktemp --suffix=.cer)"
  # az keyvault certificate download refuses to overwrite an existing file, and
  # mktemp already created it, so remove the placeholder before downloading.
  rm -f "$PUBLIC_CER"
  # Download ONLY the public certificate (DER), base64-encode for --cert.
  az keyvault certificate download \
    --vault-name "$KEY_VAULT_NAME" \
    --name "$CERT_NAME" \
    --encoding DER \
    --file "$PUBLIC_CER" >/dev/null
  CERT_B64="$(base64 -w0 "$PUBLIC_CER" 2>/dev/null || base64 "$PUBLIC_CER" | tr -d '\n')"
  az ad app credential reset \
    --id "$API_ID" \
    --cert "$CERT_B64" \
    --append \
    --years 1 >/dev/null
  unset CERT_B64
else
  log "API registration already has a certificate credential; skipping attach"
fi

# -----------------------------------------------------------------------------
# 5) Downstream Graph User.Read (delegated) on the API + admin consent
# -----------------------------------------------------------------------------
log "Adding Microsoft Graph User.Read (delegated) on API + admin consent"
az ad app permission add \
  --id "$API_ID" \
  --api "$GRAPH_APP_ID" \
  --api-permissions "$GRAPH_USER_READ=Scope" >/dev/null 2>&1 || true
az ad app permission admin-consent --id "$API_ID" >/dev/null

# -----------------------------------------------------------------------------
# 6) SPA -> API delegated permission (access_as_user) + admin consent
# -----------------------------------------------------------------------------
log "Adding SPA -> API access_as_user delegated permission + admin consent"
az ad app permission add \
  --id "$SPA_ID" \
  --api "$API_ID" \
  --api-permissions "$SCOPE_ID=Scope" >/dev/null 2>&1 || true
az ad app permission admin-consent --id "$SPA_ID" >/dev/null

# -----------------------------------------------------------------------------
# Outputs (non-secret). Certificate/credential is intentionally never emitted.
# -----------------------------------------------------------------------------
API_SCOPE="api://$API_ID/access_as_user"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  {
    echo "api_client_id=$API_ID"
    echo "spa_client_id=$SPA_ID"
    echo "api_scope=$API_SCOPE"
  } >> "$GITHUB_OUTPUT"
fi

log "Provisioning complete."
echo "api_client_id=$API_ID"
echo "spa_client_id=$SPA_ID"
echo "api_scope=$API_SCOPE"
