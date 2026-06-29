// monitoring.bicep
// Provisions a workspace-based Application Insights resource backed by a
// Log Analytics workspace, plus an Entra ID diagnostic setting that streams
// interactive and non-interactive sign-in logs to Log Analytics so the
// OBO audience-binding evidence KQL has data to query.

@description('Azure region for the Log Analytics workspace and Application Insights resource.')
param location string

@description('Name of the Log Analytics workspace.')
param logAnalyticsWorkspaceName string

@description('Name of the workspace-based Application Insights component.')
param appInsightsName string

@description('Name of the Entra ID (aadiam) diagnostic setting that streams sign-in logs to Log Analytics.')
param diagnosticSettingName string = 'croesus-entra-signin-diagnostics'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Entra ID sign-in logs are a tenant-level (aadiam) signal. This diagnostic
// setting routes interactive (SigninLogs), non-interactive
// (AADNonInteractiveUserSignInLogs), and audit categories into the workspace
// for the corroborating two-leg OBO evidence query.
resource entraDiagnostics 'microsoft.aadiam/diagnosticSettings@2017-04-01' = {
  name: diagnosticSettingName
  scope: tenant()
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        category: 'SignInLogs'
        enabled: true
      }
      {
        category: 'NonInteractiveUserSignInLogs'
        enabled: true
      }
      {
        category: 'AuditLogs'
        enabled: true
      }
    ]
  }
}

@description('Resource ID of the Log Analytics workspace.')
output logAnalyticsWorkspaceId string = logAnalytics.id

@description('Name of the Application Insights component.')
output appInsightsName string = appInsights.name

@description('Application Insights connection string. Passed by reference to the Key Vault module for storage; never written as a literal in source.')
output appInsightsConnectionString string = appInsights.properties.ConnectionString
