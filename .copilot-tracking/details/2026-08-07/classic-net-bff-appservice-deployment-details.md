<!-- markdownlint-disable-file -->
# Implementation Details: Classic BFF App Service Deployment

## Shared Deployment Contract

Create an isolated resource-group deployment under `infra/poc/`. Do not alter the existing Linux SPA/API topology in `infra/main.bicep`. The PoC stack contains one Windows `Microsoft.Web/serverfarms` resource at B1 and two `Microsoft.Web/sites` resources on that plan. Do not add Key Vault, Application Insights, Log Analytics, private networking, deployment slots, databases, or other paid resources.

Use one confidential Microsoft Entra `web` registration for a controlled runtime comparison. Its deployed callbacks are `https://<legacy-host>/signin-oidc` and `https://<modern-host>/signin-oidc`. Default to single-tenant. Organizations mode remains opt-in and requires an explicit allowlist containing the home tenant and every approved customer tenant.

GitHub Actions authenticates with workload identity federation. The bootstrap identity requires resource-group deployment permissions and Microsoft Graph application-management permissions granted by a tenant administrator. It must not use a stored client secret. Bicep cannot create Entra directory objects, so the workflow performs directory operations through Microsoft Graph after OIDC login.

No live deployment or directory mutation is part of implementation validation.

## Implementation Phase 1: Prepare Both Applications for App Service

### Step 1.1: Generalize legacy callback validation

Replace localhost-only callback validation with absolute HTTPS URI validation. Reject user info, fragments, malformed values, and non-HTTPS schemes. Keep the redirect and post-logout URI values configuration-driven so App Service settings can override `web.config` values.

Add focused tests proving Azure App Service HTTPS hosts pass and HTTP, relative, credential-bearing, or fragment-bearing values fail.

### Step 1.2: Add the modern PoC credential boundary

Allow `AzureAd:ClientSecret` only when the ASP.NET Core environment is `Development` or exactly `Poc`. Preserve the existing Production rule that requires a certificate or managed-identity-backed client credential. Do not broaden this exception to arbitrary non-production environment names.

Add focused startup tests for `Poc`, Production, and missing credentials.

### Step 1.3: Define publish behavior

Keep the legacy project targeted to `net452`; package its IIS content and dependencies for Windows App Service. Publish the modern app self-contained for `win-x64` with the app host enabled. Ensure the modern web app starts the published executable and uses `ASPNETCORE_ENVIRONMENT=Poc`.

The first validation after source edits is the corresponding focused test project.

## Implementation Phase 2: Add the B1 Bicep Stack

Add `infra/poc/main.bicep` and a non-secret parameter example. Parameters include location, deterministic name prefix, tenant and client identifiers, authentication mode, allowed tenant IDs, and the client secret as `@secure()`.

Configure:

* One Windows B1 App Service plan with `reserved: false`
* Two HTTPS-only web apps using TLS 1.2 or newer and FTPS disabled
* Legacy app settings mapped to `ClientId`, `TenantId`, `AuthorityMode`, `AllowedTenantIds`, callback URIs, and `CROESUS_LEGACY_CLIENT_SECRET`
* Modern app settings mapped to `AzureAd__ClientId`, `AzureAd__TenantId`, authentication mode and allowlist, `AzureAd__ClientSecret`, and `ASPNETCORE_ENVIRONMENT=Poc`
* Modern process startup suitable for a self-contained Windows deployment

Keep the secret output-free. Output only app names, host names, base URLs, and callback URIs. Compile the Bicep file immediately after the edit.

## Implementation Phase 3: Add Entra Deployment Automation

Reuse the existing Graph request and ownership-state patterns from `scripts/provision-classic-net-bff-poc.ps1`. Add deployment-specific automation rather than weakening the local Windows-only secret-file safety path.

The deployment automation must:

* Accept the two deployed callback URIs and validate them as absolute HTTPS values
* Create or converge one app registration and service principal
* Set only `web.redirectUris` and clear `spa.redirectUris`
* Default to `AzureADMyOrg`; use `AzureADMultipleOrgs` only when explicitly selected
* Create one named credential with a maximum lifetime of seven days
* Remove an earlier credential with the same deterministic display name after the replacement has been created
* Register the returned secret with GitHub masking before any later command
* Return secret material only to the current workflow process, never to logs, files, artifacts, job outputs, or Bicep outputs
* Record non-secret ownership state for guarded teardown

Add static checks for operation order, callback platform, RBAC absence, masking, and output hygiene.

## Implementation Phase 4: Add GitHub Actions Workflows

Add a manual workflow with a validation-only default and an explicit deploy switch. Use repository or environment variables for public identifiers and OIDC login fields. Use a protected GitHub environment for deployment approval.

The deployment sequence is:

1. Check out source and authenticate with Azure using OIDC.
2. Build and test both projects on a Windows runner.
3. Publish the legacy IIS package and modern self-contained `win-x64` package.
4. Compile Bicep and run resource-group what-if.
5. In deploy mode, provision or converge the app registration and capture the masked short-lived secret in process scope.
6. Deploy Bicep with the secret as a secure parameter.
7. Deploy both application packages.
8. Verify each root URL returns an authentication redirect without following it.
9. Write app URLs, registration client ID, caveats, and demo steps to the run summary without secret values.

Add a separate guarded teardown workflow. Require a typed confirmation matching the deterministic name prefix, delete the resource group, and delete only directory objects proven by the ownership state. Do not delete shared bootstrap identities or customer-tenant service principals.

## Implementation Phase 5: Document and Validate the Deployment

Extend `docs/classic-net-bff-poc.md` with the deployment architecture, required GitHub variables, least-privilege bootstrap, single- and multi-tenant consent behavior, workflow operation, credential rotation, demo sequence, and teardown.

State these caveats directly:

* B1 is for demonstration, not production scale or resilience
* Both apps share one worker and have no deployment slots
* The `net452` assembly runs on App Service's installed .NET Framework 4.8 runtime
* The modern app is self-contained because platform runtime availability can lag SDK releases
* The explicit `Poc` secret exception is temporary and must not become Production policy
* A production destination should use certificate or managed-identity-backed client credentials, higher availability, monitoring, and tested key synchronization where applicable

Run all focused and aggregate validation commands without a live deployment.
