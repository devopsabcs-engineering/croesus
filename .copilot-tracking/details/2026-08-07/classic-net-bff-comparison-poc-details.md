<!-- markdownlint-disable-file -->
# Implementation Details: Classic .NET BFF Comparison PoC

## Shared Architecture Contract

Use one Entra confidential `web` application registration for the comparison, with these callbacks:

* Legacy: `https://localhost:44352/signin-oidc`
* Modern: `https://localhost:7100/signin-oidc`

Default to `AzureADMyOrg`. Permit `AzureADMultipleOrgs` only through an explicit provisioning argument. Both applications must require a non-empty allowed-tenant list when multi-tenant mode is selected. Neither application may return access tokens, refresh tokens, authorization codes, PKCE verifiers, client credentials, or raw cookies to browser code or logs.

Use `User.Read` only as an optional server-side downstream demonstration. Do not create an exposed API scope or OBO chain for this PoC because the behavior under test is Central's reported web-client redemption and BFF custody, not the repository's existing SPA-to-API OBO demonstration.

## Implementation Phase 1: Build the Legacy 4.5.2 BFF

<!-- parallelizable: true -->

### Step 1.1: Add the classic project

Create `poc/legacy-net452/` as a buildable SDK-style .NET Framework 4.5.2 project that represents classic ASP.NET hosted by IIS/System.Web. Use the .NET Framework reference-assemblies package when needed for deterministic command-line builds. Include an explicit local IIS/IIS Express run profile or operator instructions because `dotnet run` is not the hosting model.

Keep source files small and separate:

* OWIN startup and cookie/OIDC configuration
* tenant allowlist validation
* redacted telemetry helpers
* a minimal authenticated page or endpoint proving the browser receives session-backed data rather than OAuth tokens

### Step 1.2: Implement the legacy confidential flow

Use Katana 4.2.3 packages that support .NET Framework 4.5.2. Configure native code-only PKCE and middleware-owned redemption explicitly with `ResponseType=code`, `ResponseMode=query`, `UsePkce=true`, `RedeemCode=true`, and `SaveTokens=false`. Do not implement a custom verifier store or token client. Katana protects the verifier in authentication properties and removes it after callback processing; the System.Web protection boundary therefore depends on stable ASP.NET machine keys across every farm instance.

The Dev secret must come from an environment variable or protected local configuration excluded from Git. Keep token validation inside the OIDC middleware so issuer, signature, nonce, state, and correlation checks remain intact. Omit downstream Graph access from the legacy sample: retaining tokens would require a custom cache and lifecycle on a runtime that cannot use current MSAL.NET, which would obscure the sign-in comparison.

Document that this is protocol-feasibility code on an unsupported runtime. It is not the production recommendation.

### Step 1.3: Test the legacy security helpers

Add deterministic tests that can run without an Entra tenant:

* RFC 7636 verifier and S256 challenge behavior
* effective OIDC settings for code-only PKCE, built-in redemption, and no token persistence
* tenant allowlist acceptance and rejection
* token and credential redaction
* browser response projection excludes OAuth token material

The first focused validation is the legacy project build and test command.

## Implementation Phase 2: Build the .NET 10 Reference BFF

<!-- parallelizable: true -->

### Step 2.1: Add the modern project

Create `poc/modern-net10/` as a .NET 10 ASP.NET Core application. Use current Microsoft.Identity.Web and MSAL.NET-compatible packages. Configure cookie plus OpenID Connect sign-in, authorization code with PKCE, and server-side token acquisition. Use environment variables or .NET user secrets locally; commit no secret value.

### Step 2.2: Preserve the BFF boundary

Add a minimal authenticated UI and `/api/session` endpoint returning identity/session metadata only. If demonstrating Graph `User.Read`, call it from the server and project a minimal response. Never serialize an OAuth token to the browser. Configure secure cookies, antiforgery for state-changing requests, forwarded headers only from known proxies, bounded downstream timeouts, and privacy-safe logs.

For multi-tenant mode, validate `tid` against `AllowedTenantIds` after cryptographic token validation and before establishing the application session. Reject empty allowlists outside single-tenant mode.

### Step 2.3: Test modern configuration

Add deterministic tests for:

* default single-tenant configuration
* multi-tenant startup failure with no allowlist
* allowed and denied tenant decisions
* session endpoint response excludes OAuth token material
* production cookie security settings

The first focused validation is the modern project test command.

## Implementation Phase 3: Add Entra Provisioning and Operator Guidance

<!-- depends-on: phases 1, 2 -->

### Step 3.1: Provision the registration

Add an idempotent script under `scripts/` that:

* uses Microsoft Graph through authenticated Azure CLI only for directory operations
* creates or reuses one app registration and home-tenant service principal
* defaults to `AzureADMyOrg`
* accepts an explicit multi-tenant switch mapping to `AzureADMultipleOrgs`
* configures both callback URIs under `web.redirectUris`, never `spa.redirectUris`
* requests only OpenID Connect defaults and optional delegated Microsoft Graph `User.Read`
* creates no Azure subscription RBAC assignment
* records non-secret object IDs for idempotence and cleanup

### Step 3.2: Protect credentials and support cleanup

For a Dev-only secret, use a short lifetime, never echo the value, and write it only to an explicitly supplied protected output path that is ignored by Git. Prefer a certificate for the operational destination. Add a cleanup script that deletes only objects recorded by the provisioning state file.

Do not automate customer-tenant admin consent. Document it as a separate tenant-administrator action for multi-tenant mode.

### Step 3.3: Document the comparison

Add `docs/classic-net-bff-poc.md` and link it from README. Include:

* architecture and request sequence
* setup and run instructions
* single-tenant and multi-tenant behavior
* evidence to capture without collecting secrets or tokens
* caveat matrix for 4.5.2, 4.8, and .NET 10
* explicit distinction between installing 4.8 and retargeting/recompiling/testing
* migration recommendation: Dev proof on 4.5.2, operational bridge on 4.8 when needed, strategic destination on .NET 10
* threat and limitation list, including unsupported runtime, custom PKCE glue, process-local token cache, secret rotation, SameSite compatibility, TLS 1.2, farm key synchronization, and multi-tenant consent/issuer isolation

## Implementation Phase 4: Validate the Comparison PoC

Run the narrow project tests and builds, then the combined validation. Validate shell syntax without executing tenant changes. Scan tracked text for secret-like values and ensure generated state/credential files are ignored. Run repository diagnostics on every changed source and documentation file.

Do not execute app-registration provisioning without explicit tenant authorization from the user.
