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
    name: 'B1'
    tier: 'Basic'
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
    siteConfig: {
      linuxFxVersion: 'NODE|20-lts'
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
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
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
          name: 'AzureAd__ClientCertificate'
          value: '@Microsoft.KeyVault(SecretUri=${certSecretUri})'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: '@Microsoft.KeyVault(SecretUri=${appInsightsConnectionStringSecretUri})'
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
