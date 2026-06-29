// main.bicepparam
// Non-secret parameters only. Every value here is a public identifier or a
// resource name sourced from a GitHub Actions repository variable (vars.*) at
// deploy time. No secret or certificate material belongs in this file - the API
// confidential-client certificate lives in Key Vault only (croesus-api-cert).
//
// The placeholder GUIDs below are overridden in CI by `--parameters` /
// environment substitution from vars.AZURE_TENANT_ID, vars.SPA_CLIENT_ID, and
// vars.API_CLIENT_ID. Replace them locally only for ad-hoc validation.

using './main.bicep'

param tenantId = '00000000-0000-0000-0000-000000000000'
param spaClientId = '11111111-1111-1111-1111-111111111111'
param apiClientId = '22222222-2222-2222-2222-222222222222'

param spaAppName = 'croesus-spa'
param apiAppName = 'croesus-api'
param appServicePlanName = 'croesus-asp'
param keyVaultName = 'croesus-kv-demo'
param logAnalyticsWorkspaceName = 'croesus-law'
param appInsightsName = 'croesus-appi'
param certSecretName = 'croesus-api-cert'
param appInsightsConnectionStringSecretName = 'appinsights-connection-string'
