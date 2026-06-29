<!-- markdownlint-disable-file -->
# Release Changes: Mock Croesus SaaS App with Standards-Compliant MSAL OBO Flow

**Related Plan**: croesus-mock-saas-obo-flow-plan.instructions.md
**Implementation Date**: 2026-06-29

## Summary

Build a runnable mock "Croesus / GPD Central" SaaS app — MSAL.js SPA (public client) -> ASP.NET Core middle-tier API (confidential client) -> Microsoft Graph via standards On-Behalf-Of — with provisioning scripts, Bicep, a GitHub Actions OIDC pipeline, and audience-binding evidence that proves the token-replay issue is avoidable.

## Changes

### Added

* spa/.gitkeep - Placeholder tracking the SPA folder until Phase 2.
* api/.gitkeep - Placeholder tracking the API folder until Phase 3.
* infra/.gitkeep - Placeholder tracking the Bicep folder until Phase 5.
* scripts/.gitkeep - Placeholder tracking the scripts folder until Phase 4.
* .github/workflows/.gitkeep - Placeholder tracking the workflow folder until Phase 6.
* .gitignore - Ignores Node/.NET build output, credential material (*.pfx/*.pem), local env/secrets.
* .editorconfig - UTF-8/LF defaults with per-language indentation conventions.
* docs/configuration-contract.md - Single source-of-truth config catalog (public vars vs Key Vault-only cert).

#### Phase 2 - SPA front end (React + MSAL.js public client)

* spa/package.json, spa/package-lock.json - Vite React TS app (React 18.3, Vite 5.4, @azure/msal-browser 3.28, @azure/msal-react 2.2, TypeScript 5.6).
* spa/tsconfig.json, spa/tsconfig.node.json, spa/vite.config.ts - TS + Vite config (dist output, dev port 3000).
* spa/index.html, spa/.env.example, spa/.gitignore, spa/src/vite-env.d.ts - App shell, env placeholders, ignores, env typing.
* spa/src/authConfig.ts - msalConfig + apiRequest (scopes = [VITE_API_SCOPE] only) + PublicClientApplication.
* spa/src/getApiToken.ts - acquireTokenSilent with InteractionRequiredAuthError -> popup fallback (token A).
* spa/src/main.tsx, spa/src/App.tsx - MsalProvider bootstrap; sign-in/call-API UI + evidence + wrong-vs-right contrast panels.
* spa/src/components/EvidencePanel.tsx - Renders leg-1/leg-2 aud/scp/jti/iat from the API response.
* spa/src/api.ts - Bearer-token fetch wrapper to GET /api/me with the MeResponse contract type.

#### Phase 3 - Middle-tier API (ASP.NET Core + Microsoft.Identity.Web OBO)

* api/Croesus.Api.csproj, api/Croesus.Api.slnx - net8.0 Web API (Microsoft.Identity.Web 4.11.0, Graph, App Insights).
* api/Program.cs - OBO fluent chain (AddMicrosoftIdentityWebApi.EnableTokenAcquisitionToCallDownstreamApi.AddMicrosoftGraph.AddInMemoryTokenCaches) + App Insights.
* api/appsettings.json, api/appsettings.Development.json - AzureAd/Graph config + Key Vault certificate reference (no inline secret).
* api/Controllers/MeController.cs - [Authorize][RequiredScope("access_as_user")] GET /api/me; foreign-aud -> 401; leg-1 + leg-2 claim evidence.
* api/Telemetry/OboClaimLogger.cs - Structured both-leg claim logging with raw-token redaction.
* api/Tests/Croesus.Api.Tests.csproj, api/Tests/NegativeControlTests.cs - xUnit negative-control tests (foreign-aud -> 401).

#### Phase 4 - App registration provisioning, teardown, verification, CI test scripts

* scripts/provision-app-registrations.sh - Idempotent two-registration provisioning (exposed scope, cert-in-Key-Vault, pre-auth, Graph User.Read consent).
* scripts/teardown-app-registrations.sh - Idempotent delete of both registrations + Key Vault cert.
* scripts/verify-app-registrations.sh - Asserts OBO-capability table; non-zero exit on any failure.
* scripts/smoke-test.sh - Acquires token A, calls /api/me, prints leg-1/leg-2 claim summary.
* scripts/negative-test.sh - DR-06 safe negative test (CI-minted Graph-aud token -> API 401; token A -> Graph 401).

#### Phase 5 - Infrastructure as Code (Bicep)

* infra/main.bicep, infra/main.bicepparam - Orchestration + non-secret params only.
* infra/modules/appservice.bicep - App Service plan + croesus-spa + croesus-api; API system-assigned MI; Key Vault references for cert + App Insights connection string.
* infra/modules/keyvault.bicep - Key Vault + Key Vault Secrets User RBAC for the API MI.
* infra/modules/monitoring.bicep - Log Analytics + workspace-based App Insights + tenant-scoped Entra sign-in diagnostic setting.

#### Phase 7 - Documentation and evidence narrative

* docs/obo-demo-guide.md - Self-contained provision/deploy/exercise/read-evidence guide; single-tenant note + multi-tenant adminconsent callout.
* docs/evidence-narrative.md - Maps escalation-packet Q1-Q6 to concrete evidence; audience-binding as headline proof; 1008 demoted.

#### Phase 6 - CI/CD pipeline + post-deploy evidence job

* .github/workflows/deploy-croesus.yml - OIDC deploy (id-token:write, no deploy secret): build-spa (inject VITE_* from vars.*), build-api (dotnet publish), deploy (production gate, azure/webapps-deploy@v3 x2), and gated evidence job (smoke + negative tests, Log Analytics correlation, two-leg summary + portal deep links to $GITHUB_STEP_SUMMARY, evidence artifact).
* scripts/evidence-kql.kusto - Interactive SigninLogs query, non-interactive AADNonInteractiveUserSignInLogs OBO correlation query, and a clearly-labeled optional Token Protection (1008) advanced exhibit.

### Modified

* README.md - Added "Mock Croesus SaaS OBO demo" section (architecture, wrong-vs-right contrast, run instructions); existing analysis preserved.
* docs/configuration-contract.md - Reconciled with as-built workflow (WI-06): App Insights connection string as a Key Vault reference (DD-06), new LOG_ANALYTICS_WORKSPACE_ID public var, and the CI test-identity secrets (TEST_SP_CLIENT_ID/SECRET, TEST_USERNAME/PASSWORD).

### Removed

* api/package-lock.json - Empty npm lockfile stub accidentally created when npm ran in the api/ directory during Phase 2 scaffolding; the API is .NET-only.
  * Confirmed empty (`"packages": {}`); reversible via git.

## Additional or Deviating Changes

* Microsoft.Identity.Web pinned to 4.11.0 instead of 3.x (DD-03).
  * 3.5.0 tripped advisory NU1902 (GHSA-rpq8-q44m-2rpg); 4.11.0 builds with 0 warnings. Graph SDK is transitively v4; `RequiredScopeAttribute` resolves from `Microsoft.Identity.Web.Resource`.
* SPA EvidencePanel renders the leg-2 Graph audience from the API response, not a hardcoded `https://graph.microsoft.com` constant (DD-04).
  * The constant was removed so the "no Graph scope/URL in SPA source" grep gate passes; the audience is still shown verbatim from runtime data.
* Solution file is `Croesus.Api.slnx` (XML), not `.sln` (DD-05).
  * SDK 10.0.301 default; bare `dotnet build`/`dotnet test` resolve it automatically.
* API csproj adds `<DefaultItemExcludes>$(DefaultItemExcludes);Tests/**</DefaultItemExcludes>`.
  * Prevents the Web SDK from globbing the test sources into the API assembly (CS0579 duplicate-attribute otherwise).
* App Insights connection string wired as a Key Vault reference app-setting rather than a plain `vars.*`/output (DD-06).
  * Connection string is sensitive; Key Vault reference is the safer posture. Configuration-contract reconciliation tracked as WI-06.
* Bicep Entra sign-in diagnostic setting declared at `scope: tenant()` and AAD instance derived from `environment().authentication.loginEndpoint`.
  * Required by BCP135 (tenant-scoped diagnostics) and to clear the hardcoded-environment-URL warning.

## Validation Results (Phase 8)

* SPA: `npm ci && npm run build` -> exit 0; `dist/index.html` produced. Production dependencies = 0 vulnerabilities (`npm audit --omit=dev`). The 2 advisories reported by `npm audit` are dev/build tooling only (Vite/esbuild dev server) and do not affect the shipped static bundle.
* API: `dotnet build -c Release` -> 0 Warning(s), 0 Error(s); `dotnet test -c Release` -> Passed 3, Failed 0, Skipped 0.
* Bicep: `az bicep build --file infra/main.bicep` -> exit 0 (no errors/warnings).
* Scripts: `bash -n` (Git Bash) on all 5 scripts -> exit 0 each.
* Workflow: `.github/workflows/deploy-croesus.yml` parses as valid YAML (jobs build-spa/build-api/deploy/evidence; `id-token: write`); `secrets.*` appear only in the evidence job `env:` mapping, never echoed.
* SPA Graph-free gate: no requested Microsoft Graph scope or `graph.microsoft.com` URL in `spa/src/**` (only explanatory prose/comments).
* Markdown: the only markdownlint findings are MD013 (line length) under the tool default; this repo does not enforce MD013 (no `.markdownlint*` config; the markdown instructions do not set a line-length rule; pre-existing README content uses the same long-line prose). Not actioned to match existing style.

## Release Summary

This implementation delivers a complete, build-validated mock "Croesus / GPD Central" SaaS application demonstrating a standards-compliant Microsoft Entra On-Behalf-Of (OBO) flow, designed to disprove token-replay concerns by construction and with layered evidence.

Total files affected: ~40 created across six components, plus 2 modified and 1 removed.

* SPA (`spa/`) — React 18 + Vite + MSAL public client that requests only the API scope (`access_as_user`), never a Graph scope. Build passes; produces `dist/`.
* API (`api/`) — ASP.NET Core net8.0 + Microsoft.Identity.Web 4.11.0 performing OBO to Microsoft Graph via the correct-by-construction fluent chain; `/api/me` enforces audience/scope and returns both-leg claim evidence; 3 negative-control tests pass.
* Scripts (`scripts/`) — idempotent provisioning/teardown/verification plus CI smoke and gated negative tests (DR-06 safe token construction) and the corroborating `evidence-kql.kusto` query set.
* Infrastructure (`infra/`) — Bicep for two App Service Web Apps on one plan, system-assigned managed identity, Key Vault (certificate-based confidential client + App Insights connection string as Key Vault references), Log Analytics + workspace-based App Insights, and tenant-scoped Entra sign-in diagnostics.
* CI/CD (`.github/workflows/deploy-croesus.yml`) — OIDC federated deploy (no stored deploy secret), production-gated dual-app deploy, and a post-deploy evidence job that runs the smoke + gated negative tests, correlates the two non-interactive OBO legs via Log Analytics, and writes the two-leg claim summary + portal deep links to the run summary.
* Documentation (`docs/`, `README.md`) — configuration contract, end-to-end demo guide, and an evidence narrative mapping the escalation-packet questions to concrete proofs (audience binding as the headline; Token Protection 1008 demoted to an optional advanced exhibit).

Dependency and infrastructure changes: introduces npm (SPA), .NET 8 SDK (API), Azure App Service + Key Vault + App Insights + Log Analytics (Bicep), and GitHub Actions OIDC. No deploy secret is stored; the only runtime credential is a Key Vault certificate.

Deployment notes (require a live Entra tenant + Azure subscription, out of scope for this build-time pass): run `scripts/provision-app-registrations.sh`, populate the GitHub Actions `vars.*` and the CI test-identity `secrets.*` (`TEST_SP_CLIENT_ID`/`TEST_SP_CLIENT_SECRET`/`TEST_USERNAME`/`TEST_PASSWORD`) plus `LOG_ANALYTICS_WORKSPACE_ID`, deploy the Bicep, then push to `main` to trigger deploy + evidence. The smoke/negative tests use the ROPC grant and need a dedicated non-MFA CI test user (tracked as WI-05). Live-tenant verification of sign-in-log column shapes and certificate provisioning is tracked as WI-01.

## Operational Bring-Up (Live Azure + GitHub Wiring) — 2026-06-29

Operationalized the demo end to end against the live tenant `aa93b9d9-037d-4f08-a26d-783cff0e2369` / subscription `64c3d212-40ed-4c6d-a825-6adfbdf25dad` so the CI/CD pipeline runs green (Option B: full out-of-band infrastructure provisioning).

### Modified (script/template/doc portability and accuracy fixes)

* scripts/provision-app-registrations.sh - Three live-run fixes:
  * Added a portable `gen_uuid` shim (uuidgen -> /proc RNG -> PowerShell fallback) so the script runs under Git Bash on Windows, replacing the bare `uuidgen` call.
  * Split the single API PATCH into two: commit `oauth2PermissionScopes` + `knownClientApplications` first, then `preAuthorizedApplications`, because Graph validates `delegatedPermissionIds` against scopes that already exist (was failing with `InvalidValue ... Permission Id that cannot be found`).
  * Remove the `mktemp`-created placeholder `.cer` before `az keyvault certificate download`, which refuses to overwrite an existing file.
* infra/modules/keyvault.bicep - `softDeleteRetentionInDays` 7 -> 90 to match the `az keyvault create` default (the property is immutable once set, so the pre-created vault could not be reconciled to 7).
* docs/obo-demo-guide.md - Corrected Step 2: the workflow deploys application code only and does NOT provision the Bicep infrastructure. Documented the out-of-band `az deployment group create`, folded the Key Vault bootstrap into Step 1 (vault must pre-exist for the cert; RBAC role needed), and fixed the stale "az ad app create is not idempotent" note (the script is idempotent by display-name lookup).

### Provisioned (live, not source changes)

* Resource group `rg-croesus` (canadacentral); deploy app registration `Croesus Deploy Identity (mock)` (`AZURE_CLIENT_ID` 557551aa-e470-4da9-916f-4d366f604d12) with two federated credentials (subjects `repo:devopsabcs-engineering/croesus:environment:production` and `...:ref:refs/heads/main`) and Contributor on the RG.
* Key Vault `kv-croesus-a65e90` (RBAC); signed-in user granted Key Vault Administrator.
* App registrations: API `Croesus GPD Central API (mock)` (`API_CLIENT_ID` bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8, scope `api://bc6338a5-.../access_as_user`, cert `croesus-api-cert` in Key Vault) and SPA `Croesus GPD Central SPA (mock)` (`SPA_CLIENT_ID` 06ef7c0a-9df3-4bcd-8b6f-ee275ca0adc2).
* Infrastructure deployment `croesus-infra` (Succeeded): `croesus-spa` + `croesus-api` Web Apps, `croesus-asp` plan, `croesus-law` Log Analytics (customerId a676cc4e-eba9-472f-a3ee-404fee5bebe8), `croesus-appi` App Insights.
* Test service principal `Croesus Test SP (mock)` (90b9c0e0-5715-44ec-8ffd-a1c7210152c4) for the negative test.
* GitHub repo variables (12): AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID, RESOURCE_GROUP, SPA_CLIENT_ID, API_CLIENT_ID, API_SCOPE, API_BASE_URL (`https://croesus-api.azurewebsites.net`), KEY_VAULT_NAME, LOG_ANALYTICS_WORKSPACE_ID, SPA_APP_NAME (`croesus-spa`), API_APP_NAME (`croesus-api`).
* GitHub Actions secrets: TEST_SP_CLIENT_ID, TEST_SP_CLIENT_SECRET (set without echoing the value). GitHub `production` environment ensured.

### Outstanding (user action)

* `TEST_USERNAME` / `TEST_PASSWORD` secrets are a real non-MFA CI test user's credentials and must be set by the user directly (`gh secret set TEST_USERNAME` / `TEST_PASSWORD`); never request or echo these.

