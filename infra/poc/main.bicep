targetScope = 'resourceGroup'

@description('Azure region for the App Service plan and both web apps.')
param location string = resourceGroup().location

@description('Name prefix used as the deterministic deployment seed.')
@minLength(3)
@maxLength(24)
param namePrefix string

@description('Home Microsoft Entra tenant ID.')
@minLength(36)
@maxLength(36)
param tenantId string

@description('Client ID of the shared confidential Microsoft Entra web registration.')
@minLength(36)
@maxLength(36)
param clientId string

@description('Authentication authority mode for both applications.')
@allowed([
  'SingleTenant'
  'Organizations'
])
param authenticationMode string = 'SingleTenant'

@description('Allowed Microsoft Entra tenant IDs. Organizations mode requires this list to contain the home tenant.')
param allowedTenantIds string[] = []

@description('Short-lived client secret for the shared confidential web registration.')
@secure()
param clientSecret string

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id, namePrefix)
var appServicePlanName = 'croesus-bff-${uniqueSuffix}-plan'
var legacyAppName = 'croesus-bff-${uniqueSuffix}-legacy'
var modernAppName = 'croesus-bff-${uniqueSuffix}-modern'
var legacyHostName = '${legacyAppName}.azurewebsites.net'
var modernHostName = '${modernAppName}.azurewebsites.net'
var legacyBaseUrl = 'https://${legacyHostName}'
var modernBaseUrl = 'https://${modernHostName}'
var legacyCallbackUri = '${legacyBaseUrl}/signin-oidc'
var modernCallbackUri = '${modernBaseUrl}/signin-oidc'
var effectiveAllowedTenantIds = authenticationMode == 'SingleTenant'
  ? [tenantId]
  : union([tenantId], allowedTenantIds)
var authorityTenant = authenticationMode == 'Organizations' ? 'organizations' : tenantId
var modernAllowedTenantSettings = [for (allowedTenantId, index) in effectiveAllowedTenantIds: {
  name: 'Authentication__AllowedTenantIds__${index}'
  value: allowedTenantId
}]

resource appServicePlan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: appServicePlanName
  location: location
  kind: 'app'
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: false
  }
}

resource legacyApp 'Microsoft.Web/sites@2025-03-01' = {
  name: legacyAppName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      netFrameworkVersion: 'v4.0'
      appSettings: [
        {
          name: 'ClientId'
          value: clientId
        }
        {
          name: 'TenantId'
          value: tenantId
        }
        {
          name: 'AuthorityMode'
          value: authenticationMode
        }
        {
          name: 'AllowedTenantIds'
          value: join(effectiveAllowedTenantIds, ',')
        }
        {
          // Windows App Service prefixes app-setting environment variables with APPSETTING_.
          name: 'ClientSecretEnvironmentVariable'
          value: 'APPSETTING_CROESUS_LEGACY_CLIENT_SECRET'
        }
        {
          name: 'CROESUS_LEGACY_CLIENT_SECRET'
          value: clientSecret
        }
        {
          name: 'RedirectUri'
          value: legacyCallbackUri
        }
        {
          name: 'PostLogoutRedirectUri'
          value: '${legacyBaseUrl}/'
        }
      ]
    }
  }
}

resource modernApp 'Microsoft.Web/sites@2025-03-01' = {
  name: modernAppName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      alwaysOn: true
      appCommandLine: 'Croesus.ModernBff.exe'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      use32BitWorkerProcess: false
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Poc'
        }
        {
          name: 'AzureAd__Instance'
          value: environment().authentication.loginEndpoint
        }
        {
          name: 'AzureAd__TenantId'
          value: authorityTenant
        }
        {
          name: 'AzureAd__ClientId'
          value: clientId
        }
        {
          name: 'AzureAd__ClientSecret'
          value: clientSecret
        }
        {
          name: 'AzureAd__CallbackPath'
          value: '/signin-oidc'
        }
        {
          name: 'Authentication__Mode'
          value: authenticationMode
        }
        {
          name: 'AllowedHosts'
          value: modernHostName
        }
      ], modernAllowedTenantSettings)
    }
  }
}

@description('Name of the shared Windows B1 App Service plan.')
output appServicePlanName string = appServicePlan.name

@description('Name of the legacy .NET Framework web app.')
output legacyAppName string = legacyApp.name

@description('Name of the modern self-contained .NET web app.')
output modernAppName string = modernApp.name

@description('Default host name of the legacy web app.')
output legacyHostName string = legacyApp.properties.defaultHostName

@description('Default host name of the modern web app.')
output modernHostName string = modernApp.properties.defaultHostName

@description('HTTPS base URL of the legacy web app.')
output legacyBaseUrl string = legacyBaseUrl

@description('HTTPS base URL of the modern web app.')
output modernBaseUrl string = modernBaseUrl

@description('Microsoft Entra web callback URI of the legacy web app.')
output legacyCallbackUri string = legacyCallbackUri

@description('Microsoft Entra web callback URI of the modern web app.')
output modernCallbackUri string = modernCallbackUri

@description('Non-secret inputs for converging the shared Microsoft Entra web registration.')
output registrationInputs object = {
  allowedTenantIds: effectiveAllowedTenantIds
  authenticationMode: authenticationMode
  clientId: clientId
  displayName: 'croesus-bff-${uniqueSuffix}-web'
  homeTenantId: tenantId
  signInAudience: authenticationMode == 'Organizations' ? 'AzureADMultipleOrgs' : 'AzureADMyOrg'
  webRedirectUris: [
    legacyCallbackUri
    modernCallbackUri
  ]
}
