#!/usr/bin/env bash
#
# smoke-test.sh
#
# Post-deploy smoke test for the Croesus mock SaaS OBO flow. Acquires a token for
# the API scope (token A, aud = API), calls the deployed API's /api/me endpoint,
# and prints the two-leg claim summary:
#
#   leg 1 (SPA -> API): the token the API received   (aud/scp/appid/jti)
#   leg 2 (API -> Graph): the OBO token the API used  (aud/scp/appid/jti)
#
# The API's /api/me is expected to return the decoded claim summary it logged for
# both legs. This script also decodes token A locally to corroborate leg 1.
#
# Token acquisition uses the resource-owner-password (ROPC) grant with a
# dedicated, non-MFA CI test user — the deterministic, headless path for CI. A
# Playwright/device-code interactive login is the alternative for human-driven
# runs but is intentionally not used here.
#
# Secrets policy: raw access tokens are never printed; only non-secret claims
# (aud, scp, appid, jti) are surfaced.
#
# Configuration (env vars, all required unless noted):
#   TENANT_ID        Microsoft Entra tenant ID.
#   SPA_CLIENT_ID    SPA (public client) app registration ID.
#   API_SCOPE        api://<API_CLIENT_ID>/access_as_user
#   API_BASE_URL     Base URL of the deployed API (no trailing slash needed).
#   TEST_USERNAME    CI test user UPN (non-MFA).
#   TEST_PASSWORD    CI test user password.
#
set -euo pipefail

TENANT_ID="${TENANT_ID:?TENANT_ID is required}"
SPA_CLIENT_ID="${SPA_CLIENT_ID:?SPA_CLIENT_ID is required}"
API_SCOPE="${API_SCOPE:?API_SCOPE is required}"
API_BASE_URL="${API_BASE_URL:?API_BASE_URL is required}"
TEST_USERNAME="${TEST_USERNAME:?TEST_USERNAME is required}"
TEST_PASSWORD="${TEST_PASSWORD:?TEST_PASSWORD is required}"

TOKEN_ENDPOINT="https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token"

log() { printf '>>> %s\n' "$*" >&2; }

# Base64url-decode a JWT payload and extract a claim with jq. Never prints the
# token itself. Usage: claim "<jwt>" "<claim>"
claim() {
  local jwt="$1" name="$2" payload pad
  payload="$(printf '%s' "$jwt" | cut -d. -f2)"
  # Pad base64url to a multiple of 4 (only when padding is actually needed) and
  # translate to standard base64. Guarding pad>0 avoids emitting a stray '='.
  pad=$(( (4 - ${#payload} % 4) % 4 ))
  while [[ "$pad" -gt 0 ]]; do payload="${payload}="; pad=$((pad - 1)); done
  printf '%s' "$payload" | tr '_-' '/+' | base64 -d 2>/dev/null \
    | jq -r --arg n "$name" '.[$n] // "n/a"'
}

# Acquire token A (aud = API) via ROPC. Token captured in memory, never printed.
log "Acquiring token A (aud = API) via ROPC for $API_SCOPE"
TOKEN_RESPONSE="$(curl -s -X POST "$TOKEN_ENDPOINT" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=password" \
  --data-urlencode "client_id=${SPA_CLIENT_ID}" \
  --data-urlencode "scope=${API_SCOPE} openid profile" \
  --data-urlencode "username=${TEST_USERNAME}" \
  --data-urlencode "password=${TEST_PASSWORD}")"
TOKEN_A="$(printf '%s' "$TOKEN_RESPONSE" | jq -r '.access_token // empty')"

if [[ -z "$TOKEN_A" ]]; then
  # Surface only the non-secret error fields (AADSTS code + description). The
  # token endpoint never echoes the password, so this is safe to print and is
  # the deterministic signal for triaging ROPC failures in CI.
  ERR_CODE="$(printf '%s' "$TOKEN_RESPONSE" | jq -r '.error // "unknown"')"
  ERR_DESC="$(printf '%s' "$TOKEN_RESPONSE" | jq -r '.error_description // "no description"' | head -n 1)"
  printf 'ERROR: failed to acquire token A for the API scope.\n' >&2
  printf '       error=%s\n' "$ERR_CODE" >&2
  printf '       error_description=%s\n' "$ERR_DESC" >&2
  exit 1
fi

# Local corroboration of leg 1 (the token the API will receive).
echo "leg 1 (SPA -> API), decoded locally from token A:"
printf '  aud=%s  scp=%s  appid=%s  jti=%s\n' \
  "$(claim "$TOKEN_A" aud)" \
  "$(claim "$TOKEN_A" scp)" \
  "$(claim "$TOKEN_A" appid)" \
  "$(claim "$TOKEN_A" jti)"

# Call the deployed API; expect 200 with the API's own claim summary.
log "Calling ${API_BASE_URL}/api/me"
BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT
HTTP_CODE="$(curl -s -o "$BODY_FILE" -w '%{http_code}' \
  -H "Authorization: Bearer ${TOKEN_A}" \
  "${API_BASE_URL%/}/api/me")"

if [[ "$HTTP_CODE" != "200" ]]; then
  printf 'ERROR: /api/me returned HTTP %s (expected 200).\n' "$HTTP_CODE" >&2
  cat "$BODY_FILE" >&2 || true
  exit 1
fi

echo "Two-leg claim summary returned by /api/me:"
# The API is expected to return a JSON object; pretty-print it. If it nests the
# legs, print as-is so the operator can read aud/scp/appid/jti for both legs.
if jq . "$BODY_FILE" >/dev/null 2>&1; then
  jq . "$BODY_FILE"
else
  cat "$BODY_FILE"
fi

# Optional GitHub step summary.
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "### Croesus OBO smoke test"
    echo ""
    echo "- API endpoint: \`${API_BASE_URL%/}/api/me\` -> HTTP $HTTP_CODE"
    echo "- leg 1 aud: \`$(claim "$TOKEN_A" aud)\`  scp: \`$(claim "$TOKEN_A" scp)\`"
    echo ""
    echo '```json'
    jq . "$BODY_FILE" 2>/dev/null || cat "$BODY_FILE"
    echo '```'
  } >> "$GITHUB_STEP_SUMMARY"
fi

unset TOKEN_A
log "Smoke test passed."
