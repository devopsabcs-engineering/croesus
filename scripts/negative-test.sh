#!/usr/bin/env bash
#
# negative-test.sh
#
# Gated negative control for the Croesus mock SaaS OBO flow. Proves the API and
# Microsoft Graph each reject a token whose audience does not match them, so a
# stolen/replayed token bound to the wrong resource is useless.
#
# Per design decision DR-06, this test does NOT reuse a real interactive user
# token as the negative control. It constructs the mismatched-audience tokens
# deterministically with a dedicated CI test identity:
#
#   Attempt 1 - Graph-audience token presented to the API:
#     Mint a Microsoft Graph token via the CI test service principal's
#     client-credentials grant (scope=https://graph.microsoft.com/.default).
#     Its aud = Graph, not the API. Present it to /api/me  ->  expect HTTP 401.
#
#   Attempt 2 - API-audience token presented to Graph:
#     Acquire token A (aud = API) the same way smoke-test.sh does, then present
#     it directly to https://graph.microsoft.com/v1.0/me  ->  expect HTTP 401.
#
# Exits non-zero if EITHER mismatched-audience attempt is accepted (HTTP 2xx).
#
# Secrets policy: no token, client secret, or password is ever printed.
#
# Configuration (env vars, all required):
#   TENANT_ID            Microsoft Entra tenant ID.
#   API_BASE_URL         Base URL of the deployed API.
#   TEST_SP_CLIENT_ID    CI test service principal (deploy/test identity) appId.
#   TEST_SP_CLIENT_SECRET CI test service principal client secret.
#   SPA_CLIENT_ID        SPA (public client) app registration ID (for token A).
#   API_SCOPE            api://<API_CLIENT_ID>/access_as_user (for token A).
#   TEST_USERNAME        CI test user UPN (non-MFA, for token A).
#   TEST_PASSWORD        CI test user password (for token A).
#
set -euo pipefail

TENANT_ID="${TENANT_ID:?TENANT_ID is required}"
API_BASE_URL="${API_BASE_URL:?API_BASE_URL is required}"
TEST_SP_CLIENT_ID="${TEST_SP_CLIENT_ID:?TEST_SP_CLIENT_ID is required}"
TEST_SP_CLIENT_SECRET="${TEST_SP_CLIENT_SECRET:?TEST_SP_CLIENT_SECRET is required}"
SPA_CLIENT_ID="${SPA_CLIENT_ID:?SPA_CLIENT_ID is required}"
API_SCOPE="${API_SCOPE:?API_SCOPE is required}"
TEST_USERNAME="${TEST_USERNAME:?TEST_USERNAME is required}"
TEST_PASSWORD="${TEST_PASSWORD:?TEST_PASSWORD is required}"

TOKEN_ENDPOINT="https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token"
GRAPH_ME="https://graph.microsoft.com/v1.0/me"

FAILURES=0
log()  { printf '>>> %s\n' "$*" >&2; }
pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }

# HTTP status of a Bearer GET (body discarded). Token passed in memory only.
http_status() {
  local url="$1" token="$2"
  curl -s -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer ${token}" "$url"
}

# -----------------------------------------------------------------------------
# Attempt 1: Graph-audience token (CI test SP client-credentials) -> API = 401
# -----------------------------------------------------------------------------
log "Minting Graph-audience token via CI test SP client-credentials grant"
GRAPH_TOKEN="$(curl -s -X POST "$TOKEN_ENDPOINT" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=${TEST_SP_CLIENT_ID}" \
  --data-urlencode "client_secret=${TEST_SP_CLIENT_SECRET}" \
  --data-urlencode "scope=https://graph.microsoft.com/.default" \
  | jq -r '.access_token // empty')"

if [[ -z "$GRAPH_TOKEN" ]]; then
  fail "Could not mint Graph-audience token (cannot run attempt 1)"
else
  CODE="$(http_status "${API_BASE_URL%/}/api/me" "$GRAPH_TOKEN")"
  if [[ "$CODE" == "401" ]]; then
    pass "Graph-audience token rejected by API (HTTP 401)"
  else
    fail "API accepted a Graph-audience token (HTTP $CODE; expected 401)"
  fi
fi
unset GRAPH_TOKEN

# -----------------------------------------------------------------------------
# Attempt 2: API-audience token (token A) presented to Graph = 401
# -----------------------------------------------------------------------------
log "Acquiring token A (aud = API) via ROPC for the Graph-rejection check"
TOKEN_A="$(curl -s -X POST "$TOKEN_ENDPOINT" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=password" \
  --data-urlencode "client_id=${SPA_CLIENT_ID}" \
  --data-urlencode "scope=${API_SCOPE} openid profile" \
  --data-urlencode "username=${TEST_USERNAME}" \
  --data-urlencode "password=${TEST_PASSWORD}" \
  | jq -r '.access_token // empty')"

if [[ -z "$TOKEN_A" ]]; then
  fail "Could not acquire token A (cannot run attempt 2)"
else
  CODE="$(http_status "$GRAPH_ME" "$TOKEN_A")"
  if [[ "$CODE" == "401" ]]; then
    pass "API-audience token rejected by Microsoft Graph (HTTP 401)"
  else
    fail "Microsoft Graph accepted an API-audience token (HTTP $CODE; expected 401)"
  fi
fi
unset TOKEN_A

# -----------------------------------------------------------------------------
# Result.
# -----------------------------------------------------------------------------
echo
if [[ "$FAILURES" -gt 0 ]]; then
  printf 'Negative test FAILED: %d mismatched-audience attempt(s) were not correctly rejected.\n' "$FAILURES" >&2
  exit 1
fi
printf 'Negative test PASSED: both mismatched-audience tokens were rejected with 401.\n'
