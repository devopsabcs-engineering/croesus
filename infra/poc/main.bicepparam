using './main.bicep'

param namePrefix = 'croesus-bff-poc'
param tenantId = readEnvironmentVariable('AZURE_TENANT_ID', '00000000-0000-0000-0000-000000000000')
param clientId = readEnvironmentVariable('CROESUS_POC_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param authenticationMode = 'SingleTenant'
param allowedTenantIds = []
param clientSecret = readEnvironmentVariable('CROESUS_DEPLOYMENT_CLIENT_SECRET')

// Public ingress is the POC posture. Phase 1 found no Azure Policy enforcing publicNetworkAccess on
// Microsoft.Web/sites at any reachable scope, so private ingress is elective rather than mandated.
param ingressMode = 'Public'

// Stated explicitly because the deployed plan was observed at F1 Free on 2026-09-22 while the template
// declared B1, and an external process returns it to F1 on roughly a 24 hour cycle. Expect to redeploy.
param appServicePlanSkuName = 'B1'

// Off until the application in poc/bff-yarp-net10 is ready to publish. The reference BFF hostname is
// croesus-bff-a3v24wppuvd34-bff.azurewebsites.net under this namePrefix, derived from uniqueSuffix in
// main.bicep, and it matches the BFF_BASE_URI default in scripts/provision-app-registrations.sh. That
// hostname is stable whether or not the site exists, so the registration can claim it either way.
param deployBffSite = false
