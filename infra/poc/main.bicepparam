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

// On as of 2026-09-23. The application in poc/bff-yarp-net10 builds clean and passes its test suite, so
// the reference BFF is now hosted rather than merely reserved. The hostname is
// croesus-bff-a3v24wppuvd34-bff.azurewebsites.net under this namePrefix, derived from uniqueSuffix in
// main.bicep, and it matches the BFF_BASE_URI default in scripts/provision-app-registrations.sh. Sign-in
// through this host additionally requires https://<bffHostName>/signin-oidc on the registration's redirect
// URI list, because main.bicep points the BFF at the same clientId as the legacy and modern sites.
param deployBffSite = true

// The BFF fails closed when it has no downstream: BffAuthenticationSettings rejects an empty
// DownstreamApi:Scopes, and ProxyDestinationPolicy rejects a route with no permitted destination. Hosting
// the reference BFF therefore requires an owned API to proxy to, which is what this site provides.
// Its registration is separate from the web registration on purpose. The question this proof-of-concept
// exists to answer is about the audience boundary, and that boundary only exists when the resource and
// the client are two different applications. Converge it with:
//   ./scripts/provision-owned-api-registration.ps1 \
//     -DisplayName croesus-bff-a3v24wppuvd34-api \
//     -ClientAppId <the clientId above>
// and pass the clientId it reports as CROESUS_OWNED_API_CLIENT_ID.
param deployOwnedApiSite = true
param ownedApiClientId = readEnvironmentVariable('CROESUS_OWNED_API_CLIENT_ID', '00000000-0000-0000-0000-000000000000')
param ownedApiScopeName = 'access_as_user'
