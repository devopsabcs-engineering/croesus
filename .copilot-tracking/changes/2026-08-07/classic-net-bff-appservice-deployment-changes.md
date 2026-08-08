<!-- markdownlint-disable-file -->
# Release Changes: Classic BFF App Service Deployment

**Related Plan**: classic-net-bff-appservice-deployment-plan.instructions.md
**Implementation Date**: 2026-08-07

## Summary

Implemented a repeatable B1-only Windows Azure App Service deployment for both classic BFF proofs, including secure Entra provisioning, manual GitHub Actions deployment and teardown, and operator guidance.

## Changes

### Added

* `infra/poc/main.bicep` - Isolated deployment containing one Windows B1 plan and two hardened web apps with secure identity configuration and non-secret outputs
* `infra/poc/main.bicepparam` - Environment-backed parameter entry point that requires the deployment secret from process scope
* `scripts/provision-classic-net-bff-deployment.ps1` - Non-interactive Graph convergence for deployed callbacks and masked, short-lived credential rotation
* `scripts/cleanup-classic-net-bff-deployment.ps1` - Ownership-scoped removal of deployment credentials and script-created directory objects
* `scripts/test-classic-net-bff-deployment-entra-static.ps1` - Static regression checks for callback platform, rotation ordering, masking, secret sinks, RBAC absence, and teardown ownership
* `.github/workflows/classic-net-bff-poc.yml` - Manual validation/deployment pipeline using Windows builds, GitHub OIDC, Bicep what-if, same-step secret handling, package deployment, and redirect smoke checks
* `.github/workflows/teardown-classic-net-bff-poc.yml` - Typed-confirmation teardown for the deterministic resource group and ownership-tagged Entra objects
* `.copilot-tracking/research/subagents/2026-08-07/github-actions-run-31219189056.md` - Read-only evidence for the Graph permission failure that informed the least-privilege bootstrap correction

### Modified

* `poc/legacy-net452/Configuration/LegacyAuthenticationSettings.cs` - Accepts absolute HTTPS callbacks while rejecting relative, HTTP, user-info, and fragment-bearing URIs
* `poc/legacy-net452/LegacyNet452.csproj` - Produces an IIS-ready App Service publish layout while retaining the `net452` target
* `poc/legacy-net452/Tests/LegacyAuthenticationSettingsTests.cs` - Adds regression coverage for deployed HTTPS callbacks and rejection of unsafe URI forms
* `poc/modern-net10/Security/BffAuthenticationSettings.cs` - Allows client secrets only in `Development` and explicit `Poc` environments
* `poc/modern-net10/Croesus.ModernBff.csproj` - Defines self-contained `win-x64` publishing with an executable app host
* `poc/modern-net10/Tests/StartupConfigurationTests.cs` - Adds regression coverage for the explicit `Poc` client-secret boundary
* `docs/classic-net-bff-poc.md` - Documents bootstrap permissions, configuration, deployment modes, credential rotation, demo flow, multitenancy, caveats, and teardown
* `.github/workflows/classic-net-bff-poc.yml` - Fails validation when Azure CLI cannot inspect the deterministic resource group and rejects malformed existence responses
* `.github/workflows/classic-net-bff-poc.yml` - Uses bounded readiness checks for protected endpoints and validates each Entra challenge host, client ID, and callback URI
* `infra/poc/main.bicep` - Runs the self-contained modern `win-x64` application in a 64-bit App Service worker
* `poc/legacy-net452/web.config` - Redirects Newtonsoft.Json references through the deployed 13.0.0.0 assembly
* `scripts/provision-classic-net-bff-deployment.ps1` - Sends Graph JSON through temporary files and retries bounded replication and credential-concurrency failures
* `scripts/test-classic-net-bff-deployment-entra-static.ps1` - Covers Azure CLI error ordering, Graph request transport and retries, runtime configuration, and authentication readiness checks

### Removed

* (none)

## Additional or Deviating Changes

* Created a separate PoC deployment design instead of extending the existing Linux OBO infrastructure.
  * Reason: the two BFF proofs require Windows App Service, while the existing stack has a distinct Linux SPA/API purpose and larger resource footprint.
* The first modern `--no-restore` publish did not have runtime-specific assets; a normal self-contained publish restored them and succeeded.
  * Reason: `win-x64` was a new runtime identifier for this project and required its initial restore.
* Bound the optional `.bicepparam` entry point to `CROESUS_DEPLOYMENT_CLIENT_SECRET` with no fallback.
  * Reason: consolidation found the parameter file in the worktree; an environment-only binding makes it compilable without committing credential material. The workflow still supplies parameters directly.
* Used a deterministic application ownership tag instead of publishing Entra state between workflow runs.
  * Reason: teardown must rediscover its target safely without treating a mutable or public artifact as authorization to delete directory objects.
* Hosted validation run `31215402276` passed builds and tests but exposed a false-green optional what-if.
  * Reason: `az group exists` returned `Forbidden`; the workflow parsed its empty output before checking `$LASTEXITCODE` and incorrectly reported a successful skip.
* Bootstrapped the GitHub OIDC deployment identity with Microsoft Graph `Application.ReadWrite.All` application permission and tenant-admin consent.
  * Reason: Azure resource-group Contributor does not grant Microsoft Graph directory access required to converge the demonstration registration.
* Added the protected-environment federated credential and kept Azure Contributor scoped to `croesus-bff-poc-rg`.
  * Reason: the `poc-demo` job uses an environment OIDC subject, while resource-group scope is sufficient for this deployment.
* Repaired Windows Azure CLI JSON transport and bounded Graph eventual-consistency handling during live deployment.
  * Reason: inline JSON was corrupted by native argument handling; immediate post-create and credential-rotation operations also returned transient Graph replication and concurrency errors.
* Corrected App Service runtime assumptions found after the first successful package deployment.
  * Reason: the modern `win-x64` executable required a 64-bit worker, and the legacy challenge path required a Newtonsoft.Json binding redirect.

## Release Summary

All six phases complete. The implementation adds seven deployment files, modifies the application, infrastructure, workflow, tests, and documentation surfaces, creates four tracking artifacts, and removes no files.

The deployment uses one shared Windows B1 App Service plan with two HTTPS-only web apps. The legacy project remains compiled for .NET Framework 4.5.2 and runs on App Service's installed .NET Framework 4.8 runtime. The modern project publishes self-contained for `win-x64`. One confidential Entra `web` registration holds both callback URIs; a deterministic ownership tag enables guarded teardown without cross-run state artifacts.

GitHub Actions uses workload identity federation, a protected `poc-demo` environment, Bicep what-if, a masked credential with a maximum seven-day lifetime, same-step secure parameter deployment, package deployment, and redirect smoke checks. The separate teardown workflow requires typed confirmation.

Validation:

* Legacy Release build passed; 33 tests passed
* Modern Release build passed; 14 tests passed
* Legacy IIS and modern self-contained `win-x64` publish artifacts passed inspection
* Six PowerShell scripts parsed; PSScriptAnalyzer reported no warnings or errors
* Both credential-safety static suites passed
* Bicep and Bicep parameters compiled without diagnostics
* Compiled infrastructure contains one Windows B1 plan, two sites, no forbidden resources, a secure secret parameter, and no secret output
* Both workflow files and their embedded PowerShell blocks parsed successfully
* Credential, RBAC, workflow-permission, tier, whitespace, and editor diagnostics checks passed for the changed deployment surface
* Live deployment run `31225818740` completed successfully
* Resource group `croesus-bff-poc-rg` contains one B1 plan, `croesus-bff-a3v24wppuvd34-plan`, and two deployed web apps
* Legacy `/api/session` and `/signin` returned `302` Entra challenges with the expected client ID and legacy callback URI
* Modern `/api/session` and `/` returned `302` Entra challenges with the expected client ID and modern callback URI

The deployed applications are available at `https://croesus-bff-a3v24wppuvd34-legacy.azurewebsites.net` and `https://croesus-bff-a3v24wppuvd34-modern.azurewebsites.net`. The shared Entra registration client ID is `3ee7b866-1d40-4746-a880-f7fda6d2d53e`. Redirect validation did not follow the Microsoft Entra login flow, so interactive sign-in and authorization-code redemption remain a user-interactive verification step. The demo credential expires one day after the successful deployment and should be rotated by redeploying or removed with the guarded teardown workflow.