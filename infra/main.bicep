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

@description('Name of the virtual network.')
param vnetName string = 'croesus-vnet'

@description('Name of the private endpoint subnet.')
param peSubnetName string = 'snet-pe'

@description('Name of the App Service integration subnet.')
param appSubnetName string = 'snet-app'

@description('Name of the Key Vault private endpoint.')
param keyVaultPrivateEndpointName string = 'croesus-kv-pe'

module networking 'modules/networking.bicep' = {
  name: 'networking'
  params: {
    location: location
    vnetName: vnetName
    peSubnetName: peSubnetName
    appSubnetName: appSubnetName
  }
}

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
    appSubnetId: networking.outputs.appSubnetId
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
    peSubnetId: networking.outputs.peSubnetId
    keyVaultPrivateDnsZoneId: networking.outputs.keyVaultPrivateDnsZoneId
    keyVaultPrivateEndpointName: keyVaultPrivateEndpointName
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

@description('Resource ID of the virtual network.')
output vnetId string = networking.outputs.vnetId

@description('Resource ID of the App Service integration subnet.')
output appSubnetId string = networking.outputs.appSubnetId

@description('Resource ID of the private endpoint subnet.')
output peSubnetId string = networking.outputs.peSubnetId
