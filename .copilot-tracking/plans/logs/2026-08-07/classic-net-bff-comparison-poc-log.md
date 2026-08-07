<!-- markdownlint-disable-file -->
# Planning Log: Classic .NET BFF Comparison PoC

**Related Plan**: classic-net-bff-comparison-poc-plan.instructions.md

## Discrepancy Log

### Unaddressed Research Items

* DR-01: Live IIS Express startup discovery, HTTPS callback, and Entra authorization-code round trip require a provisioned tenant registration.
  * Source: `.copilot-tracking/research/2026-08-07/classic-net-bff-comparison-poc-research.md`
  * Reason: tenant mutation and live authentication require explicit authorization and credentials outside the repository
  * Impact: medium

### Implementation Deviations

* DD-01: The original details proposed custom PKCE state and manual code redemption on .NET Framework 4.5.2.
  * Plan specifies: add PKCE explicitly if compatible middleware lacks it and construct the token request in application code
  * Implementation differs: Katana 4.2.3 owns code-only PKCE and confidential redemption with `UsePkce=true`, `RedeemCode=true`, and `SaveTokens=false`
  * Rationale: package metadata, source inspection, and a disposable local `net452` build verified native support; this retains middleware state, nonce, correlation, issuer, signature, and code-redemption handling

* DD-02: The legacy and modern comparison omits Microsoft Graph access.
  * Plan specifies: Graph `User.Read` is optional
  * Implementation differs: both core hosts stop at authenticated, token-free session projection
  * Rationale: Graph is not needed to prove confidential BFF redemption; legacy Graph access would require a custom token cache on a runtime that cannot use current MSAL.NET

* DD-03: Phase 3 review found that the initial Dev-secret sequence created the directory credential before verifying local Windows ACL protection.
  * Plan specifies: never leave credential material unprotected and retain deterministic cleanup ownership
  * Implementation differs: the script now creates and validates a protected no-overwrite placeholder before `addPassword`, then persists the returned `keyId` before writing secret material
  * Rationale: preflight failures now occur before tenant mutation; post-creation write failures retain cleanup state without printing the secret

## Suggested Follow-On Work

* WI-01: Run an authorized live single-tenant sign-in against both callbacks and capture privacy-safe protocol metadata (high)
  * Source: Phase 1 and Phase 2 completion reports
  * Dependency: Phase 3 provisioning and tenant authorization
* WI-02: Validate IIS Express OWIN startup discovery and local HTTPS trust for the legacy host (medium)
  * Source: research remaining verification
  * Dependency: local IIS Express execution
* WI-03: Configure stable shared machine keys before any multi-instance legacy deployment (high)
  * Source: System.Web protection-boundary research
  * Dependency: target IIS farm topology

## User Decisions

* ID-01: PoC implementation shape - Option A selected
  * Rationale: compare a protocol-faithful .NET Framework 4.5.2 proof with the supported .NET 10 destination and optional multi-tenant provisioning