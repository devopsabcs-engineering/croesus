// appservice.bicep
// Provisions one Linux App Service plan hosting two Web Apps:
//   * croesus-spa  - the static MSAL.js public-client SPA
//   * croesus-api  - the confidential-client OBO middle-tier API
// The API Web App carries a system-assigned Managed Identity and reads its
// confidential-client certificate and the Application Insights connection
// string exclusively through App Service Key Vault references. No secret or
// certificate material is ever written inline.

@description('Azure region for the App Service plan and Web Apps.')
param location string

@description('Name of the shared Linux App Service plan.')
param appServicePlanName string

@description('Name of the SPA Web App (public-client front end).')
param spaAppName string

@description('Name of the API Web App (confidential-client middle tier).')
param apiAppName string

@description('Microsoft Entra tenant ID consumed by the API AzureAd configuration.')
param tenantId string

@description('SPA (public client) app registration ID. Surfaced as an informational SPA app setting.')
param spaClientId string

@description('API (confidential client) app registration ID consumed by the API AzureAd configuration.')
param apiClientId string

@description('Microsoft Entra authority instance, e.g. https://login.microsoftonline.com/.')
param aadInstance string = environment().authentication.loginEndpoint

@description('Expected audience for tokens presented to the API. Defaults to api://<apiClientId>.')
param apiAudience string = 'api://${apiClientId}'

@description('Name of the Key Vault that holds the API certificate and the App Insights connection string secret.')
param keyVaultName string

@description('Key Vault secret name holding the API confidential-client certificate (provisioned out-of-band).')
param certSecretName string = 'croesus-api-cert'

@description('Key Vault secret name holding the Application Insights connection string.')
param appInsightsConnectionStringSecretName string = 'appinsights-connection-string'

@description('Resource ID of the App Service integration subnet (snet-app) delegated to Microsoft.Web/serverFarms. Only the API site is integrated.')
param appSubnetId string

// An external governance process returns this subscription's App Service plans to F1 and sets
// publicNetworkAccess to Disabled on roughly a 24 hour cycle. Both are declared here so that
// redeploying this template reverses the drift rather than leaving the properties unmanaged,
// where ARM would preserve whatever the last writer set.
@description('App Service plan SKU. F1 and D1 cannot host Always On or a private endpoint.')
@allowed(['F1', 'D1', 'B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P0v3', 'P1v3', 'P2v3', 'P3v3'])
param appServicePlanSkuName string = 'B1'

@description('Site-level public ingress. Disabled with no private endpoint makes a site unreachable from every network path, including the SCM endpoint that package deployment uses.')
@allowed(['Enabled', 'Disabled'])
param publicNetworkAccess string = 'Enabled'

var appServicePlanSkuTiers = {
  F1: 'Free'
  D1: 'Shared'
  B1: 'Basic'
  B2: 'Basic'
  B3: 'Basic'
  S1: 'Standard'
  S2: 'Standard'
  S3: 'Standard'
  P0v3: 'PremiumV3'
  P1v3: 'PremiumV3'
  P2v3: 'PremiumV3'
  P3v3: 'PremiumV3'
}

// Build Key Vault secret URIs from the vault NAME only (a non-secret param),
// so this module does not need a hard dependency on the Key Vault resource and
// no secret value is ever embedded.
var keyVaultDnsSuffix = environment().suffixes.keyvaultDns
var certSecretUri = 'https://${keyVaultName}${keyVaultDnsSuffix}/secrets/${certSecretName}'
var appInsightsConnectionStringSecretUri = 'https://${keyVaultName}${keyVaultDnsSuffix}/secrets/${appInsightsConnectionStringSecretName}'

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: appServicePlanSkuName
    tier: appServicePlanSkuTiers[appServicePlanSkuName]
  }
  properties: {
    reserved: true
  }
}

resource spaApp 'Microsoft.Web/sites@2024-04-01' = {
  name: spaAppName
  location: location
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    publicNetworkAccess: publicNetworkAccess
    siteConfig: {
      linuxFxVersion: 'NODE|20-lts'
      // The SPA is a prebuilt static Vite bundle, not a Node server. Serve the
      // files from wwwroot with SPA history-fallback so client-side routes and
      // the MSAL redirect path resolve to index.html.
      appCommandLine: 'pm2 serve /home/site/wwwroot --no-daemon --spa'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'SPA_CLIENT_ID'
          value: spaClientId
        }
        {
          name: 'TENANT_ID'
          value: tenantId
        }
      ]
    }
  }
}

resource apiApp 'Microsoft.Web/sites@2024-04-01' = {
  name: apiAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    publicNetworkAccess: publicNetworkAccess
    // Regional VNet integration: outbound traffic (including Key Vault
    // reference resolution) egresses through snet-app so it can reach the
    // Key Vault private endpoint. vnetRouteAllEnabled forces all outbound
    // traffic and DNS through the VNet.
    virtualNetworkSubnetId: appSubnetId
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      vnetRouteAllEnabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureAd__Instance'
          value: aadInstance
        }
        {
          name: 'AzureAd__TenantId'
          value: tenantId
        }
        {
          name: 'AzureAd__ClientId'
          value: apiClientId
        }
        {
          name: 'AzureAd__Audience'
          value: apiAudience
        }
        {
          // The confidential-client certificate is loaded by Microsoft.Identity.Web
          // from its Key Vault backing secret (the full PKCS#12 bundle) via an App
          // Service Key Vault reference resolved by the API Managed Identity. This
          // binds to AzureAd:ClientCredentials[0] (SourceType=Base64Encoded), the
          // shape Microsoft.Identity.Web actually reads. No cert material is inline.
          name: 'AzureAd__ClientCredentials__0__SourceType'
          value: 'Base64Encoded'
        }
        {
          name: 'AzureAd__ClientCredentials__0__Base64EncodedValue'
          value: '@Microsoft.KeyVault(SecretUri=${certSecretUri})'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: '@Microsoft.KeyVault(SecretUri=${appInsightsConnectionStringSecretUri})'
        }
        {
          name: 'Cors__AllowedOrigins__0'
          value: 'https://${spaApp.properties.defaultHostName}'
        }
        {
          // Gates the Tier 2 replay demo endpoint (POST /api/replay). Defaults to
          // 'false' so the endpoint stays disabled unless explicitly opted in.
          name: 'Demo__EnableReplay'
          value: 'false'
        }
      ]
    }
  }
}

@description('Principal ID of the API Web App system-assigned Managed Identity (for Key Vault RBAC).')
output apiPrincipalId string = apiApp.identity.principalId

@description('Default host name of the API Web App.')
output apiHostName string = apiApp.properties.defaultHostName

@description('Default host name of the SPA Web App.')
output spaHostName string = spaApp.properties.defaultHostName

@description('Name of the API Web App.')
output apiAppName string = apiApp.name

@description('Name of the SPA Web App.')
output spaAppName string = spaApp.name
