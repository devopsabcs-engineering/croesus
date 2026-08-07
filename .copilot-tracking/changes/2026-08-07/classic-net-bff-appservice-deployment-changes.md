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

### Modified

* `poc/legacy-net452/Configuration/LegacyAuthenticationSettings.cs` - Accepts absolute HTTPS callbacks while rejecting relative, HTTP, user-info, and fragment-bearing URIs
* `poc/legacy-net452/LegacyNet452.csproj` - Produces an IIS-ready App Service publish layout while retaining the `net452` target
* `poc/legacy-net452/Tests/LegacyAuthenticationSettingsTests.cs` - Adds regression coverage for deployed HTTPS callbacks and rejection of unsafe URI forms
* `poc/modern-net10/Security/BffAuthenticationSettings.cs` - Allows client secrets only in `Development` and explicit `Poc` environments
* `poc/modern-net10/Croesus.ModernBff.csproj` - Defines self-contained `win-x64` publishing with an executable app host
* `poc/modern-net10/Tests/StartupConfigurationTests.cs` - Adds regression coverage for the explicit `Poc` client-secret boundary
* `docs/classic-net-bff-poc.md` - Documents bootstrap permissions, configuration, deployment modes, credential rotation, demo flow, multitenancy, caveats, and teardown

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

## Release Summary

All five phases complete. The implementation adds seven deployment files, modifies seven application and documentation files, creates four tracking artifacts, and removes no files.

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
* Both workflow files and all 17 embedded PowerShell blocks parsed successfully
* Credential, RBAC, workflow-permission, tier, whitespace, and editor diagnostics checks passed for the changed deployment surface

No live Azure what-if, resource deployment, Microsoft Graph mutation, browser sign-in, or teardown was performed. Repository administrators must configure the `poc-demo` environment, its reviewers, and the documented public OIDC variables before the first validation or deployment run.