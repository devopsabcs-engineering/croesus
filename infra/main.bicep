// main.bicep
// Orchestrates the Croesus mock SaaS OBO-flow infrastructure:
//   * monitoring  - Log Analytics + workspace-based App Insights + Entra sign-in diagnostics
//   * appservice  - one Linux plan + croesus-spa + croesus-api (API has a system-assigned MI)
//   * keyvault    - Key Vault + RBAC (Key Vault Secrets User -> API MI) + App Insights connstring secret
//
// All parameters are non-secret. The API confidential-client certificate lives
// in Key Vault only and is surfaced to the API through a Key Vault reference
// resolved by its Managed Identity. No secret or certificate value appears here.

targetScope = 'resourceGroup'

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Microsoft Entra tenant ID.')
param tenantId string

@description('SPA (public client) app registration ID.')
param spaClientId string

@description('API (confidential client) app registration ID.')
param apiClientId string

@description('Microsoft Entra authority instance.')
param aadInstance string = environment().authentication.loginEndpoint

@description('Expected audience for tokens presented to the API. Defaults to api://<apiClientId>.')
param apiAudience string = 'api://${apiClientId}'

@description('Name of the SPA Web App.')
param spaAppName string = 'croesus-spa'

@description('Name of the API Web App.')
param apiAppName string = 'croesus-api'

@description('Name of the shared Linux App Service plan.')
param appServicePlanName string = 'croesus-asp'

@description('Name of the Key Vault.')
param keyVaultName string

@description('Name of the Log Analytics workspace.')
param logAnalyticsWorkspaceName string = 'croesus-law'

@description('Name of the Application Insights component.')
param appInsightsName string = 'croesus-appi'

@description('Key Vault secret name for the API confidential-client certificate (provisioned out-of-band).')
param certSecretName string = 'croesus-api-cert'

@description('Key Vault secret name for the Application Insights connection string.')
param appInsightsConnectionStringSecretName string = 'appinsights-connection-string'

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    appInsightsName: appInsightsName
  }
}

module appService 'modules/appservice.bicep' = {
  name: 'appservice'
  params: {
    location: location
    appServicePlanName: appServicePlanName
    spaAppName: spaAppName
    apiAppName: apiAppName
    tenantId: tenantId
    spaClientId: spaClientId
    apiClientId: apiClientId
    aadInstance: aadInstance
    apiAudience: apiAudience
    keyVaultName: keyVaultName
    certSecretName: certSecretName
    appInsightsConnectionStringSecretName: appInsightsConnectionStringSecretName
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    keyVaultName: keyVaultName
    tenantId: tenantId
    apiPrincipalId: appService.outputs.apiPrincipalId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    appInsightsConnectionStringSecretName: appInsightsConnectionStringSecretName
  }
}

@description('Name of the SPA Web App.')
output spaAppName string = appService.outputs.spaAppName

@description('Name of the API Web App.')
output apiAppName string = appService.outputs.apiAppName

@description('Default host name of the SPA Web App.')
output spaHostName string = appService.outputs.spaHostName

@description('Default host name of the API Web App.')
output apiHostName string = appService.outputs.apiHostName

@description('URI of the Key Vault.')
output keyVaultUri string = keyVault.outputs.keyVaultUri

@description('Key Vault reference URI for the Application Insights connection string (no literal value emitted).')
output appInsightsConnectionStringSecretUri string = keyVault.outputs.appInsightsConnectionStringSecretUri
