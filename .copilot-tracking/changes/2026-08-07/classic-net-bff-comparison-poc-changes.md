<!-- markdownlint-disable-file -->
# Release Changes: Classic .NET BFF Comparison PoC

**Related Plan**: classic-net-bff-comparison-poc-plan.instructions.md
**Implementation Date**: 2026-08-07

## Summary

Implemented the protocol-faithful .NET Framework 4.5.2 BFF and the supported .NET 10 reference BFF. Both use confidential code flow with PKCE, keep OAuth tokens out of browser responses, and enforce tenant policy before session establishment.

## Changes

### Added

* `poc/legacy-net452/LegacyNet452.csproj` - Buildable .NET Framework 4.5.2 System.Web/Katana BFF host with no MSAL.NET or Microsoft.Identity.Web dependency
* `poc/legacy-net452/Startup.cs` - TLS 1.2, secure cookie, OpenID Connect middleware, sign-in route, and token-free session projection
* `poc/legacy-net452/Authentication/` - Native Katana code-only PKCE, confidential redemption, cookie options, strict issuer-to-tenant validation, and minimal claims projection
* `poc/legacy-net452/Configuration/LegacyAuthenticationSettings.cs` - Environment-backed secret loading, single-/multi-tenant startup validation, localhost callback validation, and non-empty GUID enforcement
* `poc/legacy-net452/Telemetry/` - Privacy-safe authentication event redaction
* `poc/legacy-net452/Tests/` - 27 deterministic .NET Framework 4.5.2 tests for authentication options, tenant policy, redaction, cookie security, and browser projection
* `poc/legacy-net452/web.config` and `poc/legacy-net452/Properties/launchSettings.json` - IIS/IIS Express host configuration
* `poc/legacy-net452/README.md` - Legacy host prerequisites and operator guidance
* `poc/modern-net10/Croesus.ModernBff.csproj` - .NET 10 reference BFF using Microsoft.Identity.Web 4.14.2
* `poc/modern-net10/Program.cs` - Code-only PKCE, secure cookie session, antiforgery-protected sign-out, strict issuer validation, and token-free session API
* `poc/modern-net10/Security/` - Startup configuration validation and single-/multi-tenant issuer-to-`tid` allowlisting
* `poc/modern-net10/Models/SessionResponse.cs` - Allowlisted browser session projection
* `poc/modern-net10/Tests/` - 10 deterministic .NET 10 tests for startup settings, tenant policy, cookie security, and session response shape
* `poc/modern-net10/README.md` - Modern reference host prerequisites and operator guidance
* `scripts/provision-classic-net-bff-poc.ps1` - Idempotent Microsoft Graph provisioning for a confidential web registration with default single-tenant and explicit multi-tenant modes, two web callbacks, optional User.Read, and opt-in short-lived Dev credentials
* `scripts/cleanup-classic-net-bff-poc.ps1` - Ownership-scoped cleanup that removes only credentials and directory objects recorded as script-created
* `scripts/test-provision-classic-net-bff-poc-static.ps1` - Static regression checks for credential-safety ordering, ACL preservation, ownership persistence, and output redaction
* `docs/classic-net-bff-poc.md` - Architecture, provisioning, runbook, privacy-safe evidence guidance, multitenant boundaries, and 4.5.2/4.8/.NET 10 caveat matrix

### Modified

* `.gitignore` - Excludes generated classic-BFF state and protected credential files
* `README.md` - Links the R8/R9 classic .NET BFF comparison guide without replacing the authoritative findings document

### Removed

* (none)

## Additional or Deviating Changes

* Rejected subscription-level Contributor assignment suggested by generic CLI generation because this PoC requires Entra directory operations only.
  * Reason: least privilege; app registration and consent do not require an Azure resource role assignment.
* Replaced the proposed custom .NET Framework 4.5.2 PKCE verifier store and manual token client with Katana 4.2.3 native code-only PKCE and built-in redemption.
  * Reason: package/source research and a disposable local build verified `UsePkce=true`, `RedeemCode=true`, and `SaveTokens=false` on `net452`; native middleware preserves its state, nonce, correlation, and token-validation pipeline with less custom protocol risk.
* Reordered Dev credential creation so a protected Windows file and ACL are verified before Graph `addPassword`; the returned `keyId` is persisted before secret-file writing.
  * Reason: review found that a platform or ACL failure after `addPassword` could otherwise leave an unrecorded tenant credential. The static regression script now prevents that operation-order regression.

## Release Summary

All four phases complete. The comparison adds 43 implementation/documentation files and five tracking artifacts, modifies README and `.gitignore`, and removes no files.

The .NET Framework 4.5.2 host proves confidential code-only PKCE and server-side session establishment with Katana 4.2.3 while explicitly remaining unsupported evidence code. The .NET 10 host demonstrates the supported Microsoft.Identity.Web/MSAL.NET destination. The Entra script provisions one confidential `web` registration with both callbacks, defaults to `AzureADMyOrg`, and enables `AzureADMultipleOrgs` only through an explicit switch and application-enforced tenant allowlists.

Validation:

* Legacy Release build passed; 27 tests passed, 0 failed, 0 skipped
* Modern Release build passed; 10 tests passed, 0 failed, 0 skipped
* Three PowerShell scripts parsed with 0 errors and passed PSScriptAnalyzer with 0 warnings or errors
* Credential-safety static regression passed
* Dependency, RBAC, callback-platform, audience-default, secret-literal, browser-response, ignore-pattern, whitespace, EOF, and repository diagnostics checks passed
* No generated state or credential file exists or is tracked

Deployment notes: no live Entra/Graph mutation, interactive authentication, IIS Express hosting, or browser callback validation was executed. Those checks require explicit tenant authorization and are tracked as follow-on work in the planning log.