using './main.bicep'

param namePrefix = 'croesus-bff-poc'
param tenantId = readEnvironmentVariable('AZURE_TENANT_ID', '00000000-0000-0000-0000-000000000000')
param clientId = readEnvironmentVariable('CROESUS_POC_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param authenticationMode = 'SingleTenant'
param allowedTenantIds = []
param clientSecret = readEnvironmentVariable('CROESUS_DEPLOYMENT_CLIENT_SECRET')
