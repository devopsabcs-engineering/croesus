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
#   SPA_REDIRECT_URI     (default: https://localhost:3000) local dev SPA URI.
#   SPA_DEPLOYED_REDIRECT_URI (optional) deployed SPA origin, e.g.
#                        https://croesus-spa.azurewebsites.net. Registered
#                        alongside SPA_REDIRECT_URI when set.
#   SIGN_IN_AUDIENCE     (default: AzureADMyOrg) single-tenant demo shape.
#   STATE_FILE           (default: .demo-state.json) machine-readable record of
#                        created object ids used by the reversible teardown.
#   PERSIST_REPO_VARIABLES (default: auto) when "auto" or "true", persist the
#                        non-secret outputs as GitHub Actions repository
#                        variables (API_CLIENT_ID, SPA_CLIENT_ID, API_SCOPE) via
#                        the `gh` CLI when it is installed and authenticated. Set
#                        to "false" to skip. When `gh` is unavailable or not
#                        authenticated the exact `gh variable set` commands are
#                        printed instead, so nothing is silently missed.
#   GH_REPO / GITHUB_REPOSITORY (optional) target repo (owner/name) for the
#                        variable writes; inferred from the local git remote
#                        when unset.
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
SPA_DEPLOYED_REDIRECT_URI="${SPA_DEPLOYED_REDIRECT_URI:-}"
SIGN_IN_AUDIENCE="${SIGN_IN_AUDIENCE:-AzureADMyOrg}"
STATE_FILE="${STATE_FILE:-.demo-state.json}"
PERSIST_REPO_VARIABLES="${PERSIST_REPO_VARIABLES:-auto}"

log() { printf '>>> %s\n' "$*" >&2; }

# Merge a single key/value into the JSON state file so the reversible teardown
# can delete exactly the objects this script created. Seeds an empty object on
# first use; idempotent (re-recording the same key overwrites in place).
record_state() {
  local key="$1" value="$2" tmp
  [[ -f "$STATE_FILE" ]] || echo '{}' > "$STATE_FILE"
  tmp="$(mktemp)"
  jq --arg v "$value" ".$key = \$v" "$STATE_FILE" > "$tmp" && mv "$tmp" "$STATE_FILE"
}

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

# SPA platform redirect URIs (spa, not web). PATCH is authoritative/idempotent.
# Always register the local dev URI; also register the deployed SPA origin when
# SPA_DEPLOYED_REDIRECT_URI is provided, otherwise an interactive sign-in from
# the deployed app fails with AADSTS50011 (redirect URI mismatch).
SPA_REDIRECT_JSON="\"$SPA_REDIRECT_URI\""
if [[ -n "$SPA_DEPLOYED_REDIRECT_URI" ]]; then
  SPA_REDIRECT_JSON="$SPA_REDIRECT_JSON,\"$SPA_DEPLOYED_REDIRECT_URI\""
fi
az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/$SPA_OBJ" \
  --headers "Content-Type=application/json" \
  --body "{\"spa\":{\"redirectUris\":[$SPA_REDIRECT_JSON]},\"isFallbackPublicClient\":true}" >/dev/null

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
# 7) SPA -> Microsoft Graph User.Read (delegated) + admin consent (Tier 2).
#    The SPA acquires a REAL Graph token so the Tier 2a server-side replay has a
#    genuine downstream token to forward. This materializes as an
#    oauth2PermissionGrant on the SPA service principal; its id is recorded to
#    the state file so teardown-app-registrations.sh can revoke it exactly.
#    The existing API-only Graph grant (section 5) is intentionally kept.
# -----------------------------------------------------------------------------
log "Adding SPA -> Microsoft Graph User.Read (delegated) + admin consent (Tier 2)"
az ad app permission add \
  --id "$SPA_ID" \
  --api "$GRAPH_APP_ID" \
  --api-permissions "$GRAPH_USER_READ=Scope" >/dev/null 2>&1 || true
az ad app permission admin-consent --id "$SPA_ID" >/dev/null

# Resolve and record the SPA -> Graph delegated grant id for reversible teardown.
SPA_SP_ID="$(az ad sp show --id "$SPA_ID" --query id -o tsv 2>/dev/null || true)"
GRAPH_SP_ID="$(az ad sp show --id "$GRAPH_APP_ID" --query id -o tsv 2>/dev/null || true)"
if [[ -n "$SPA_SP_ID" && -n "$GRAPH_SP_ID" ]]; then
  SPA_GRAPH_GRANT_ID="$(az rest --method GET \
    --uri "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=clientId eq '$SPA_SP_ID'" \
    --query "value[?resourceId=='$GRAPH_SP_ID'] | [0].id" -o tsv 2>/dev/null || true)"
  if [[ -n "$SPA_GRAPH_GRANT_ID" && "$SPA_GRAPH_GRANT_ID" != "None" ]]; then
    log "Recording SPA -> Graph grant id to $STATE_FILE"
    record_state spaGraphGrant "$SPA_GRAPH_GRANT_ID"
  else
    log "SPA -> Graph grant id not resolved yet (consent may lag); teardown will fall back to a filter query"
  fi
fi

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

# -----------------------------------------------------------------------------
# Persist the non-secret outputs as GitHub Actions repository variables so the
# evidence workflow (and anyone reading the config contract) does not need a
# manual repo-settings step. Only public identifiers are written here; the API
# certificate/credential is never touched. Controlled by PERSIST_REPO_VARIABLES.
# -----------------------------------------------------------------------------
persist_repo_variables() {
  [[ "$PERSIST_REPO_VARIABLES" == "false" ]] && return 0

  local repo_args=()
  local target_repo="${GH_REPO:-${GITHUB_REPOSITORY:-}}"
  [[ -n "$target_repo" ]] && repo_args=(-R "$target_repo")

  if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    log "Persisting repository variables via gh (API_CLIENT_ID, SPA_CLIENT_ID, API_SCOPE)"
    gh variable set API_CLIENT_ID "${repo_args[@]}" --body "$API_ID" >/dev/null
    gh variable set SPA_CLIENT_ID "${repo_args[@]}" --body "$SPA_ID" >/dev/null
    gh variable set API_SCOPE "${repo_args[@]}" --body "$API_SCOPE" >/dev/null
    log "Repository variables set."
  else
    log "gh CLI not available or not authenticated; not writing repository variables."
    log "To set them manually, run (after 'gh auth login'):"
    printf '    gh variable set API_CLIENT_ID --body %q\n' "$API_ID" >&2
    printf '    gh variable set SPA_CLIENT_ID --body %q\n' "$SPA_ID" >&2
    printf '    gh variable set API_SCOPE --body %q\n' "$API_SCOPE" >&2
  fi
}

persist_repo_variables
