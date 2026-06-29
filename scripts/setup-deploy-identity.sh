#!/usr/bin/env bash
#
# setup-deploy-identity.sh
#
# Creates (idempotently) the GitHub -> Azure DEPLOY identity that the
# deploy-croesus workflow uses for OIDC login, then pushes the GitHub Actions
# repository variables the workflow reads. This is the piece that was missing
# when `azure/login@v2` failed with:
#
#   Login failed ... Ensure 'client-id' and 'tenant-id' are supplied.
#
# That error means the repo variables AZURE_CLIENT_ID / AZURE_TENANT_ID /
# AZURE_SUBSCRIPTION_ID were empty (this identity had not been created yet).
#
# This identity is DISTINCT from the SPA/API app registrations created by
# provision-app-registrations.sh and from the API's Key Vault certificate. It
# has NO stored secret: it authenticates with a federated credential (OIDC).
#
# CRITICAL: the deploy and evidence jobs run with `environment: production`, so
# the OIDC token subject is `repo:<owner>/<repo>:environment:production` (NOT a
# branch ref). The federated credential below matches that subject. A
# branch-scoped credential is also added so a non-environment run still works.
#
# Prerequisites (run locally, interactively):
#   * az login           (as a user who can create app registrations + assign RBAC)
#   * gh auth login       (with repo admin, to set Actions variables)
#
# Configuration (env vars):
#   REPO              (required) GitHub repo as "owner/name", e.g. devopsabcs-engineering/croesus
#   RESOURCE_GROUP    (required) Resource group the workflow deploys into
#   SUBSCRIPTION_ID   (default: current `az account show`) target subscription
#   APP_DISPLAY_NAME  (default: "Croesus Deploy Identity (mock)")
#   ENVIRONMENT_NAME  (default: production) must match the workflow `environment:`
#   DEFAULT_BRANCH    (default: main)
#   ASSIGN_RBAC       (default: true) assign Contributor at the resource-group scope
#   PUSH_GH_VARS      (default: true) set repo variables via the gh CLI
#
# Secrets policy: no secret is created or printed. OIDC needs no client secret.
#
set -euo pipefail

REPO="${REPO:?REPO is required (owner/name)}"
RESOURCE_GROUP="${RESOURCE_GROUP:?RESOURCE_GROUP is required}"
APP_DISPLAY_NAME="${APP_DISPLAY_NAME:-Croesus Deploy Identity (mock)}"
ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-production}"
DEFAULT_BRANCH="${DEFAULT_BRANCH:-main}"
ASSIGN_RBAC="${ASSIGN_RBAC:-true}"
PUSH_GH_VARS="${PUSH_GH_VARS:-true}"

log() { printf '>>> %s\n' "$*" >&2; }

TENANT_ID="$(az account show --query tenantId -o tsv)"
SUBSCRIPTION_ID="${SUBSCRIPTION_ID:-$(az account show --query id -o tsv)}"

# -----------------------------------------------------------------------------
# 1) Deploy app registration + service principal (idempotent by display name)
# -----------------------------------------------------------------------------
APP_ID="$(az ad app list --display-name "$APP_DISPLAY_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)"
if [[ -z "$APP_ID" ]]; then
  log "Creating deploy app registration: $APP_DISPLAY_NAME"
  APP_ID="$(az ad app create --display-name "$APP_DISPLAY_NAME" --query appId -o tsv)"
else
  log "Reusing deploy app registration: $APP_DISPLAY_NAME ($APP_ID)"
fi

if ! az ad sp show --id "$APP_ID" >/dev/null 2>&1; then
  log "Creating service principal for $APP_ID"
  az ad sp create --id "$APP_ID" >/dev/null
fi

# -----------------------------------------------------------------------------
# 2) Federated credentials (OIDC, no secret). One per subject, idempotent by name.
#    The `environment:` subject is the one the gated jobs actually present.
# -----------------------------------------------------------------------------
add_fic() {
  local name="$1" subject="$2"
  if az ad app federated-credential list --id "$APP_ID" \
       --query "[?name=='$name'] | [0].name" -o tsv 2>/dev/null | grep -q "$name"; then
    log "Federated credential '$name' already exists; skipping"
    return
  fi
  log "Adding federated credential '$name' (subject: $subject)"
  az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"$name\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$subject\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" >/dev/null
}

add_fic "github-${ENVIRONMENT_NAME}"   "repo:${REPO}:environment:${ENVIRONMENT_NAME}"
add_fic "github-branch-${DEFAULT_BRANCH}" "repo:${REPO}:ref:refs/heads/${DEFAULT_BRANCH}"

# -----------------------------------------------------------------------------
# 3) RBAC: Contributor at the resource-group scope so the workflow can deploy
#    code to App Service and run read-only Log Analytics queries. Tighten later
#    if least privilege is required.
# -----------------------------------------------------------------------------
if [[ "$ASSIGN_RBAC" == "true" ]]; then
  SP_OBJECT_ID="$(az ad sp show --id "$APP_ID" --query id -o tsv)"
  SCOPE="/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}"
  if az role assignment list --assignee "$SP_OBJECT_ID" --scope "$SCOPE" \
       --query "[?roleDefinitionName=='Contributor'] | [0].id" -o tsv 2>/dev/null | grep -q .; then
    log "Contributor role already assigned at $SCOPE; skipping"
  else
    log "Assigning Contributor at $SCOPE"
    az role assignment create \
      --assignee-object-id "$SP_OBJECT_ID" \
      --assignee-principal-type ServicePrincipal \
      --role Contributor \
      --scope "$SCOPE" >/dev/null
  fi
fi

# -----------------------------------------------------------------------------
# 4) Push the three login variables the workflow reads (gh CLI).
#    Other vars (SPA_CLIENT_ID, API_CLIENT_ID, API_SCOPE, API_BASE_URL,
#    KEY_VAULT_NAME, LOG_ANALYTICS_WORKSPACE_ID) come from the provisioning and
#    Bicep outputs; set those after running provision-app-registrations.sh and
#    deploying the Bicep. See docs/configuration-contract.md.
# -----------------------------------------------------------------------------
if [[ "$PUSH_GH_VARS" == "true" ]]; then
  log "Setting GitHub Actions repository variables on $REPO"
  gh variable set AZURE_CLIENT_ID       --repo "$REPO" --body "$APP_ID"
  gh variable set AZURE_TENANT_ID       --repo "$REPO" --body "$TENANT_ID"
  gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUBSCRIPTION_ID"
  gh variable set RESOURCE_GROUP        --repo "$REPO" --body "$RESOURCE_GROUP"
fi

cat >&2 <<EOF

Deploy identity ready.
  AZURE_CLIENT_ID       = $APP_ID
  AZURE_TENANT_ID       = $TENANT_ID
  AZURE_SUBSCRIPTION_ID = $SUBSCRIPTION_ID
  RESOURCE_GROUP        = $RESOURCE_GROUP
  Federated subjects:
    repo:${REPO}:environment:${ENVIRONMENT_NAME}
    repo:${REPO}:ref:refs/heads/${DEFAULT_BRANCH}

Next:
  1. Create the GitHub 'production' environment (Settings > Environments) so the
     environment-scoped federated credential is presented.
  2. Run provision-app-registrations.sh, then set SPA_CLIENT_ID, API_CLIENT_ID,
     API_SCOPE per docs/configuration-contract.md.
  3. Deploy infra/main.bicep, then set API_BASE_URL, KEY_VAULT_NAME,
     LOG_ANALYTICS_WORKSPACE_ID, SPA_APP_NAME, API_APP_NAME.
  4. Set the CI test secrets: TEST_SP_CLIENT_ID, TEST_SP_CLIENT_SECRET,
     TEST_USERNAME, TEST_PASSWORD (gh secret set ...).
EOF
