// keyvault.bicep
// Provisions the Key Vault that backs the API confidential-client credential,
// grants the API Web App's Managed Identity the "Key Vault Secrets User" role
// via RBAC, and stores the Application Insights connection string as a secret.
//
// The API confidential-client certificate (croesus-api-cert) is provisioned
// into this vault OUT OF BAND and is intentionally NOT declared here, so no
// certificate material ever appears in source. The only secret written by this
// template is the Application Insights connection string, which arrives by
// reference from the monitoring module rather than as a literal.

@description('Azure region for the Key Vault.')
param location string

@description('Name of the Key Vault.')
param keyVaultName string

@description('Microsoft Entra tenant ID that owns the Key Vault.')
param tenantId string

@description('Principal ID of the API Web App Managed Identity granted Key Vault Secrets User.')
param apiPrincipalId string

@description('Application Insights connection string, passed by reference from the monitoring module.')
@secure()
param appInsightsConnectionString string

@description('Name of the secret that stores the Application Insights connection string.')
param appInsightsConnectionStringSecretName string = 'appinsights-connection-string'

// Built-in role: Key Vault Secrets User.
var keyVaultSecretsUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '4633458b-17de-408a-b874-0445c86b69e6'
)

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    publicNetworkAccess: 'Enabled'
  }
}

resource apiSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: apiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource appInsightsConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: appInsightsConnectionStringSecretName
  properties: {
    value: appInsightsConnectionString
  }
}

@description('URI of the Key Vault.')
output keyVaultUri string = keyVault.properties.vaultUri

@description('Name of the Key Vault.')
output keyVaultName string = keyVault.name

@description('Key Vault reference URI for the Application Insights connection string secret.')
output appInsightsConnectionStringSecretUri string = '${keyVault.properties.vaultUri}secrets/${appInsightsConnectionStringSecretName}'
