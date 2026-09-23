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

// Public is the deliberate POC posture, not drift. The plan for this work assumed Azure Policy
// prohibited public network access on Microsoft.Web/sites, and Phase 1 falsified that: no assignment
// at subscription or tenant root scope enforces it. Private ingress is therefore elective here rather
// than mandated, and the choice belongs in a parameter instead of a literal on each site. See DD-17.
@description('Ingress posture applied to every site in this deployment.')
@allowed([
  'Public'
  'Private'
])
param ingressMode string = 'Public'

@description('App Service plan SKU name. Private ingress requires B1 or better.')
@allowed([
  'F1'
  'D1'
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P0v3'
  'P1v3'
  'P2v3'
  'P3v3'
])
param appServicePlanSkuName string = 'B1'

@description('Provision the reference BFF site. Off until the application in poc/bff-yarp-net10 is ready to publish.')
param deployBffSite bool = false

@description('Resource ID of the subnet hosting the private endpoints. Required only when ingressMode is Private.')
param privateEndpointSubnetId string = ''

@description('Resource ID of the privatelink.azurewebsites.net private DNS zone. Required only when ingressMode is Private.')
param privateDnsZoneId string = ''

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id, namePrefix)
var appServicePlanName = 'croesus-bff-${uniqueSuffix}-plan'
var legacyAppName = 'croesus-bff-${uniqueSuffix}-legacy'
var modernAppName = 'croesus-bff-${uniqueSuffix}-modern'
// The reference BFF gets its own hostname. It must never reuse the modern app's, because the modern
// app already claims /signin-oidc through modernCallbackUri and two registrations claiming one
// redirect URI is a provisioning conflict. See DD-21.
var bffAppName = 'croesus-bff-${uniqueSuffix}-bff'
var legacyHostName = '${legacyAppName}.azurewebsites.net'
var modernHostName = '${modernAppName}.azurewebsites.net'
var bffHostName = '${bffAppName}.azurewebsites.net'
var legacyBaseUrl = 'https://${legacyHostName}'
var modernBaseUrl = 'https://${modernHostName}'
var bffBaseUrl = 'https://${bffHostName}'
var legacyCallbackUri = '${legacyBaseUrl}/signin-oidc'
var modernCallbackUri = '${modernBaseUrl}/signin-oidc'
var bffCallbackUri = '${bffBaseUrl}/signin-oidc'
var publicNetworkAccess = ingressMode == 'Public' ? 'Enabled' : 'Disabled'
var deployPrivateEndpoints = ingressMode == 'Private'

// The deployed plan was observed at F1 Free on 2026-09-22 while this template declared B1, and an
// external process is understood to repeat that degradation on roughly a 24 hour cycle. The tier is a
// parameter so the divergence is visible in source rather than only in the portal.
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
var appServicePlanSkuTier = appServicePlanSkuTiers[appServicePlanSkuName]
// F1 Free and D1 Shared support neither Always On nor an inbound private endpoint.
var isFreeOrSharedTier = contains(['F1', 'D1'], appServicePlanSkuName)
var alwaysOn = !isFreeOrSharedTier

// Deploy-time guard. An unsupported combination resolves a key that does not exist, so ARM halts the
// deployment and surfaces the message below instead of attempting an endpoint the tier cannot host.
var ingressTierGuardOutcomes = {
  supported: 'Supported: ${ingressMode} ingress on ${appServicePlanSkuName}.'
}
var ingressTierGuard = deployPrivateEndpoints && isFreeOrSharedTier
  ? any(ingressTierGuardOutcomes)['ingressMode Private requires an App Service plan at B1 or better. F1 Free and D1 Shared cannot host a private endpoint.']
  : ingressTierGuardOutcomes.supported

var effectiveAllowedTenantIds = authenticationMode == 'SingleTenant'
  ? [tenantId]
  : union([tenantId], allowedTenantIds)
var authorityTenant = authenticationMode == 'Organizations' ? 'organizations' : tenantId
var allowedTenantAppSettings = [for (allowedTenantId, index) in effectiveAllowedTenantIds: {
  name: 'Authentication__AllowedTenantIds__${index}'
  value: allowedTenantId
}]

resource appServicePlan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: appServicePlanName
  location: location
  kind: 'app'
  sku: {
    name: appServicePlanSkuName
    tier: appServicePlanSkuTier
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
    publicNetworkAccess: publicNetworkAccess
    siteConfig: {
      alwaysOn: alwaysOn
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
    publicNetworkAccess: publicNetworkAccess
    siteConfig: {
      alwaysOn: alwaysOn
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
      ], allowedTenantAppSettings)
    }
  }
}

resource bffApp 'Microsoft.Web/sites@2025-03-01' = if (deployBffSite) {
  name: bffAppName
  location: location
  kind: 'app'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    publicNetworkAccess: publicNetworkAccess
    siteConfig: {
      alwaysOn: alwaysOn
      appCommandLine: 'Croesus.BffYarp.exe'
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
          value: bffHostName
        }
      ], allowedTenantAppSettings)
    }
  }
}

module legacyPrivateEndpoint 'modules/privateendpoint.bicep' = if (deployPrivateEndpoints) {
  name: 'croesus-bff-legacy-private-endpoint'
  params: {
    location: location
    privateEndpointName: '${legacyAppName}-pe'
    siteResourceId: legacyApp.id
    subnetResourceId: privateEndpointSubnetId
    privateDnsZoneResourceId: privateDnsZoneId
  }
}

module modernPrivateEndpoint 'modules/privateendpoint.bicep' = if (deployPrivateEndpoints) {
  name: 'croesus-bff-modern-private-endpoint'
  params: {
    location: location
    privateEndpointName: '${modernAppName}-pe'
    siteResourceId: modernApp.id
    subnetResourceId: privateEndpointSubnetId
    privateDnsZoneResourceId: privateDnsZoneId
  }
}

module bffPrivateEndpoint 'modules/privateendpoint.bicep' = if (deployPrivateEndpoints && deployBffSite) {
  name: 'croesus-bff-reference-private-endpoint'
  params: {
    location: location
    privateEndpointName: '${bffAppName}-pe'
    siteResourceId: bffApp!.id
    subnetResourceId: privateEndpointSubnetId
    privateDnsZoneResourceId: privateDnsZoneId
  }
}

@description('Name of the shared Windows App Service plan.')
output appServicePlanName string = appServicePlan.name

@description('Resolved App Service plan SKU name, so verification can read intent rather than infer it.')
output resolvedAppServicePlanSkuName string = appServicePlanSkuName

@description('Resolved ingress posture applied to every site in this deployment.')
output resolvedIngressMode string = ingressMode

@description('Outcome of the ingress and plan tier compatibility guard.')
output ingressTierGuard string = ingressTierGuard

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

@description('Whether the reference BFF site was provisioned by this deployment.')
output bffSiteDeployed bool = deployBffSite

@description('Name of the reference BFF web app, distinct from the legacy and modern comparison apps.')
output bffAppName string = bffAppName

@description('Default host name of the reference BFF web app. Stable whether or not the site is provisioned, so a registration can claim it before hosting exists.')
output bffHostName string = bffHostName

@description('HTTPS base URL of the reference BFF web app.')
output bffBaseUrl string = bffBaseUrl

@description('Microsoft Entra web callback URI of the reference BFF web app.')
output bffCallbackUri string = bffCallbackUri

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
