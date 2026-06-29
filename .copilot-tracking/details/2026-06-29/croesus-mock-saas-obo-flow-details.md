<!-- markdownlint-disable-file -->
# Implementation Details: Mock Croesus SaaS App with Standards-Compliant MSAL OBO Flow

## Context Reference

Sources:
* .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md - Primary research; SELECTED scenarios 1-3 (SPA + ASP.NET Core OBO + App Service/GitHub Actions; OBO audience-binding evidence).
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md - OBO exchange protocol, MSAL config, ASP.NET Core Microsoft.Identity.Web recommendation, code snippets.
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md - Two-registration design, provisioning script, single-tenant decision, verification commands.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md - App Service hosting, GitHub Actions OIDC, Key Vault cert, evidence/logging strategy, KQL.
* assets/app-registration-analysis-findings.md - The "wrong" (token replay) baseline being contrasted.
* assets/croesus-escalation-packet.md - Vendor questions Q1-Q6 the demo answers.

Selected decisions (from research):
* SPA: React + Vite + @azure/msal-browser + @azure/msal-react (public client, requests API scope only).
* API: ASP.NET Core + Microsoft.Identity.Web (confidential client; OBO via EnableTokenAcquisitionToCallDownstreamApi + AddMicrosoftGraph).
* Hosting: Azure App Service, two Web Apps (croesus-spa, croesus-api).
* CI/CD: GitHub Actions with OIDC federated credential (no stored deploy secret).
* Confidential credential: certificate in Key Vault, surfaced via App Service Key Vault reference + Managed Identity (secret is a documented fallback - see Planning Log IP-01).
* Tenancy: single-tenant (AzureADMyOrg); multi-tenant onboarding documented as a callout only.
* Evidence: OBO audience-binding (App Insights claim logging + negative tests) as headline; Entra sign-in log KQL as corroboration; Token Protection 1008 demoted to optional advanced exhibit.

Repository layout (net-new; repo currently contains only analysis docs + assets):

```text
croesus/
├─ spa/                 # React + MSAL.js (public client)
├─ api/                 # ASP.NET Core + Microsoft.Identity.Web (confidential client, OBO)
├─ infra/               # Bicep: App Service x2, Key Vault, MI, App Insights, diag settings
├─ scripts/             # provision + teardown + verify app registrations, smoke/negative tests
└─ .github/workflows/   # deploy-croesus.yml (OIDC) + post-deploy evidence job
```

## Implementation Phase 1: Repository scaffolding and shared configuration

<!-- parallelizable: false -->

### Step 1.1: Create top-level directory structure and root tooling files

Create the net-new folder skeleton and root-level supporting files so subsequent parallel phases have stable paths to write into.

Files:
* spa/.gitkeep - Placeholder so the empty SPA folder is tracked until Phase 2 fills it.
* api/.gitkeep - Placeholder for the API folder until Phase 3.
* infra/.gitkeep - Placeholder for Bicep until Phase 5.
* scripts/.gitkeep - Placeholder for scripts until Phase 4.
* .github/workflows/.gitkeep - Placeholder for the workflow until Phase 6.
* .gitignore - Ignore node_modules, dist, bin, obj, *.pfx, *.pem, local.settings, .env, appsettings.Development.json secrets.
* .editorconfig - Basic indentation/EOL conventions for .ts/.tsx/.cs/.bicep/.sh/.yml.

Success criteria:
* All five top-level folders exist and are tracked by git.
* .gitignore prevents committing build output and credential material (*.pfx, *.pem, *.env).

Context references:
* .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Lines 255-265) - Suggested repository layout tree.

Dependencies:
* None (first step).

### Step 1.2: Author the demo configuration contract document

Create a single source-of-truth doc enumerating the public (non-secret) configuration values (tenant ID, SPA client ID, API client ID, API scope, app names, resource group, Key Vault name) and which GitHub Actions `vars.*` / app settings each maps to. This anchors SPA build vars, API appsettings, Bicep params, and the workflow so they stay consistent.

Files:
* docs/configuration-contract.md - Table of every config key, where it is a non-secret `vars.*` vs a Key Vault secret, and its consumer (SPA build / API appsettings / Bicep / workflow).

Success criteria:
* Every config value used across spa/api/infra/scripts/workflow appears exactly once with a defined source.
* Secrets (API certificate) are explicitly marked Key Vault-only, never `vars.*` or repo.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 164-168) - Config placement summary (public vars vs Key Vault secret vs OIDC deploy auth).

Dependencies:
* Step 1.1 completion.

## Implementation Phase 2: SPA front end (React + MSAL.js public client)

<!-- parallelizable: true -->

### Step 2.1: Scaffold the Vite React TypeScript SPA project

Initialize a Vite React+TypeScript app under spa/ with MSAL dependencies and environment-variable-driven config.

Files:
* spa/package.json - Vite React TS app; deps: react, react-dom, @azure/msal-browser, @azure/msal-react; scripts: dev/build/preview.
* spa/tsconfig.json - TS config for Vite React.
* spa/vite.config.ts - Vite config (build output to dist/, dev server port 3000 to match redirect URI).
* spa/index.html - App shell.
* spa/.env.example - VITE_SPA_CLIENT_ID, VITE_TENANT_ID, VITE_API_SCOPE, VITE_API_BASE_URL placeholders.

Success criteria:
* npm ci && npm run build produces spa/dist.
* No Graph scope appears anywhere in SPA config (only the API scope).

Context references:
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 200-235) - msalConfig + apiRequest (API scope only) example.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 119-130) - SPA build vars (VITE_SPA_CLIENT_ID/VITE_TENANT_ID/VITE_API_SCOPE).

Dependencies:
* Step 1.1 completion (spa/ exists).

### Step 2.2: Implement MSAL auth config and token acquisition for the API scope

Add the MSAL configuration and a token helper that performs acquireTokenSilent for the API scope with interactive fallback. The SPA must request only `api://<API_CLIENT_ID>/access_as_user` and never a Graph scope.

Files:
* spa/src/authConfig.ts - msalConfig (clientId/authority/redirectUri from env), apiRequest with scopes = [VITE_API_SCOPE], PublicClientApplication export.
* spa/src/getApiToken.ts - acquireTokenSilent with InteractionRequiredAuthError -> acquireTokenPopup fallback returning token A.

Success criteria:
* apiRequest.scopes resolves to the API scope env var only.
* Token acquisition compiles and returns an access token string.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 207-243) - authConfig.ts and getApiToken.ts reference implementations.

Dependencies:
* Step 2.1 completion.

### Step 2.3: Build the demo UI showing sign-in, API call, and audience-binding evidence

Create a React UI that signs the user in, calls the API `/api/me`, and renders the decoded-claim evidence the API returns (token A aud/scp vs token B aud, distinct jti) plus a clearly-labeled "wrong vs right" contrast panel.

Files:
* spa/src/main.tsx - MsalProvider bootstrap with PublicClientApplication.
* spa/src/App.tsx - Sign-in button, "Call API" button, evidence panel rendering the claim summary from the API; a contrast panel summarizing the broken token-replay flow vs the OBO flow.
* spa/src/components/EvidencePanel.tsx - Renders leg-1/leg-2 claims (aud, scp, jti, iat) returned by the API.
* spa/src/api.ts - fetch wrapper attaching Bearer token A to GET /api/me.

Success criteria:
* Signed-in user can call the API and see leg-1 aud == API and leg-2 aud == Graph with different jti.
* The contrast panel cites the broken-state behavior (replay to Graph) vs the OBO behavior.

Context references:
* .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Lines 286-305) - Sequence diagram and the claim evidence the API returns.
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 320-333) - Demonstration assertions to surface (two distinct tokens, SPA never holds Graph token).

Dependencies:
* Step 2.2 completion.

### Step 2.4: Validate SPA build

Run the SPA build to confirm it compiles independently. Skip lint if it conflicts with the API toolchain (separate scope, so safe to run here).

Validation commands:
* cd spa && npm ci && npm run build - SPA build scope only.

Success criteria:
* spa/dist is produced with no TypeScript errors.

Dependencies:
* Step 2.3 completion.

## Implementation Phase 3: Middle-tier API (ASP.NET Core + Microsoft.Identity.Web OBO) with evidence logging

<!-- parallelizable: true -->

### Step 3.1: Scaffold the ASP.NET Core Web API project

Create the .NET API project with Microsoft.Identity.Web, Graph, and Application Insights packages and the fluent OBO chain.

Files:
* api/Croesus.Api.csproj - net8.0 web project; PackageReferences: Microsoft.Identity.Web, Microsoft.Identity.Web.MicrosoftGraph, Microsoft.ApplicationInsights.AspNetCore.
* api/Program.cs - AddMicrosoftIdentityWebApi(AzureAd).EnableTokenAcquisitionToCallDownstreamApi().AddMicrosoftGraph(Graph).AddInMemoryTokenCaches(); App Insights wiring; authn/authz middleware.
* api/appsettings.json - AzureAd (Instance/TenantId/ClientId/Audience), Graph (BaseUrl/Scopes), ClientCredentials referencing Key Vault (SourceType KeyVault) for the certificate.
* api/appsettings.Development.json - Local dev overrides (no secrets committed).

Success criteria:
* dotnet build succeeds.
* ClientCredentials is wired to a Key Vault certificate source, not an inline secret.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 130-150) - AddMicrosoftIdentityWebApi fluent chain.
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 360-372) - Certificate via ClientCredentials SourceType KeyVault (no code change).

Dependencies:
* Step 1.1 completion (api/ exists).

### Step 3.2: Implement the /api/me OBO controller with audience/scope enforcement

Add the controller that enforces inbound token validation (aud == API, scp == access_as_user), performs the OBO Graph call, and returns a decoded-claim evidence payload to the SPA.

Files:
* api/Controllers/MeController.cs - [Authorize][RequiredScope("access_as_user")] GET /api/me; injects GraphServiceClient; returns user profile + evidence summary (leg-1 aud/scp/appid/oid/jti from the inbound token, leg-2 aud/scp/jti/iat from the MSAL result).

Success criteria:
* A token whose aud != API is rejected with 401 (RequiredScope/audience validation).
* The endpoint returns both legs' claim summaries demonstrating distinct audiences and jti.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 280-300) - MeController OBO + Graph /me example.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 173-183) - Leg-1/leg-2 claims to capture; do not crack the Graph JWT (use MSAL result for token B).

Dependencies:
* Step 3.1 completion.

### Step 3.3: Add App Insights structured claim logging (evidence layer 3a)

Add a logging helper that records decoded non-sensitive claims of both legs to Application Insights (claims only, never raw tokens), including the OBO request shape (grant_type jwt-bearer, requested_token_use on_behalf_of, credential thumbprint/kid only).

Files:
* api/Telemetry/OboClaimLogger.cs - Logs leg-1 aud/scp/appid/oid/iss/iat/jti; OBO request shape + credential thumbprint; leg-2 aud == graph, scp, distinct jti/iat; with a redaction helper ensuring raw tokens are never logged.
* api/Program.cs - Register OboClaimLogger; ensure App Insights connection string comes from app settings (Key Vault/Managed Identity, not committed).

Success criteria:
* App Insights receives structured events for both legs with audience and jti fields.
* No raw token string is ever written to logs (redaction enforced).

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 173-183) - Backend structured logging (PRIMARY) claim list + do-not-log-raw-token guidance.

Dependencies:
* Step 3.2 completion.

### Step 3.4: Add the negative-control endpoints/tests (evidence layer)

Provide a gated mechanism proving aud-mismatched tokens are rejected: the API returns 401 for a Graph-audience token, and a documented check that token A presented directly to Graph is rejected. Implemented as a test-only endpoint or an automated test invoked by CI.

Files:
* api/Tests/NegativeControlTests.cs - Asserts a Graph-audience token to /api/me yields 401; documents/asserts token A -> Graph yields 401.
* api/Controllers/MeController.cs - Ensure the rejection path returns 401 (no OBO attempted on a foreign-aud token), per the OBO "reject the token" rule.

Success criteria:
* Negative test fails the build if a foreign-audience token is ever accepted.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 60-72) - OBO must reject a token whose aud != API.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 178-183) - Negative-test evidence as the strongest "cannot replay" demonstration.

Dependencies:
* Step 3.2 completion.

### Step 3.5: Validate API build and tests

Run the .NET build and negative-control tests to confirm the API compiles and enforces audience binding independently.

Validation commands:
* cd api && dotnet build - API build scope only.
* cd api && dotnet test - Negative-control tests.

Success criteria:
* Build succeeds and negative-control tests pass.

Dependencies:
* Step 3.4 completion.

## Implementation Phase 4: App registration provisioning, teardown, and verification scripts

<!-- parallelizable: true -->

### Step 4.1: Author the idempotent provisioning script (two single-tenant registrations)

Create a look-up-or-create az CLI script that provisions Registration A (SPA public client) and Registration B (API confidential client) with exposed `access_as_user` scope, Application ID URI, preAuthorizedApplications + knownClientApplications, Graph User.Read delegated + admin consent, and a Key Vault-stored certificate credential. Idempotent so CI re-runs do not create duplicates.

Files:
* scripts/provision-app-registrations.sh - Look-up-or-create by display name; create API + SPA apps; PATCH api.oauth2PermissionScopes (access_as_user) + knownClientApplications + preAuthorizedApplications; SPA spa.redirectUris; create/import certificate into Key Vault and attach as the API credential; Graph User.Read + admin consent; SPA->API delegated permission + admin consent; emit appIds and api scope as outputs (never print the credential).

Success criteria:
* Re-running the script does not create duplicate registrations.
* API has exposed scope + Application ID URI + Key Vault certificate credential + Graph User.Read admin-consented.
* SPA is pre-authorized for the API scope (no extra consent prompt).

Context references:
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 176-228) - Ready-to-run provisioning script (single-tenant, two registrations).
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 60-94) - access_as_user PATCH + secret/cert + Graph User.Read + admin consent.
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 95-120) - knownClientApplications + preAuthorizedApplications.

Dependencies:
* Step 1.1 completion (scripts/ exists).

### Step 4.2: Author the teardown and verification scripts

Provide a teardown script (delete the two registrations + Key Vault cert) and a verification script that asserts OBO-capability (exposed scope, confidential credential, pre-auth, Graph delegated + consent) versus the broken baseline.

Files:
* scripts/teardown-app-registrations.sh - Idempotent delete of API + SPA registrations and the Key Vault certificate.
* scripts/verify-app-registrations.sh - az ad app show/permission list checks matching the research verification table (exposed scope present, credential present, preAuthorizedApplications/knownClientApplications set, Graph User.Read consented).

Success criteria:
* Verification script exits non-zero if any OBO-capability check fails.
* Teardown removes all created artifacts without error when run twice.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 143-175) - Verification commands + OBO-capable vs broken table.

Dependencies:
* Step 4.1 completion.

### Step 4.3: Author smoke-test and negative-test scripts for CI

Provide scripts the CI evidence job invokes: a smoke test that drives the SPA->API->Graph path against the deployed environment and returns the decoded claim summary, and a negative test asserting aud-mismatched tokens yield 401.

Resolves DR-06 (safe negative-control token construction): the negative test must NOT reuse a real interactive user token. Instead, mint the mismatched-audience token deterministically using a dedicated CI test identity:
* Graph-audience token: acquire a Microsoft Graph token via the deploy/test service principal's client-credentials grant (`scope=https://graph.microsoft.com/.default`) and present it to the API (its `aud` = Graph, not the API) -> expect 401.
* API-audience token presented to Graph: use the token A obtained in the smoke test against `https://graph.microsoft.com/v1.0/me` -> expect 401 (wrong audience).
This keeps the test reproducible and avoids leaking any real user token.

Files:
* scripts/smoke-test.sh - Acquire a token for the API scope (test user or service principal), call /api/me, print the leg-1/leg-2 claim summary.
* scripts/negative-test.sh - Mint a Graph-audience token via the CI test SP client-credentials grant and present it to the API (expect 401); present token A directly to Graph (expect 401); exit non-zero if either succeeds.

Success criteria:
* Smoke test prints distinct audiences and jti for the two legs.
* Negative test enforces 401 on both mismatched-audience attempts using a CI-minted token (no real user token reused).

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 228-238) - Post-deploy smoke-test + gated negative test in CI.

Dependencies:
* Step 4.1 completion.

## Implementation Phase 5: Infrastructure as Code (Bicep)

<!-- parallelizable: true -->

### Step 5.1: Author Bicep for App Service, Key Vault, Managed Identity, App Insights, and diagnostics

Create Bicep modules provisioning two App Service Web Apps (croesus-spa, croesus-api) on one plan, a Key Vault holding the API certificate, system-assigned Managed Identity on the API with Key Vault Secrets User RBAC, Application Insights, a Log Analytics workspace, and diagnostic settings streaming Entra sign-in logs to Log Analytics for the evidence KQL.

Files:
* infra/main.bicep - Orchestrates modules; outputs app names, Key Vault URI, App Insights connection string reference.
* infra/modules/appservice.bicep - App Service plan + croesus-spa + croesus-api; API has system-assigned MI; API app settings include AzureAd config and @Microsoft.KeyVault(SecretUri=...) for the certificate and App Insights connection string.
* infra/modules/keyvault.bicep - Key Vault + RBAC role assignment (Key Vault Secrets User) for the API Managed Identity.
* infra/modules/monitoring.bicep - Log Analytics workspace + Application Insights + diagnostic settings (sign-in logs -> Log Analytics).
* infra/main.bicepparam - Non-secret parameters only (names, tenant/client IDs, scope); no secret/cert material.

Success criteria:
* az bicep build / what-if validates without errors.
* No secret or certificate value appears in any .bicep or .bicepparam file (Key Vault reference + MI only).

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 70-101) - App Service two-Web-App recommendation + Managed Identity -> Key Vault reference.
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 184-223) - Sign-in logs -> Log Analytics for the corroborating KQL.

Dependencies:
* Step 1.1 completion (infra/ exists).

### Step 5.2: Validate Bicep

Build/lint the Bicep to confirm templates compile independently.

Validation commands:
* az bicep build --file infra/main.bicep - Bicep build scope only.

Success criteria:
* Bicep compiles with no errors or blocking warnings.

Dependencies:
* Step 5.1 completion.

## Implementation Phase 6: CI/CD pipeline and post-deploy evidence job

<!-- parallelizable: false -->

### Step 6.1: Author the GitHub Actions deploy workflow (OIDC, no deploy secret)

Create the workflow that builds the SPA and API, logs in to Azure via OIDC federated credential, and deploys both Web Apps. Public IDs are injected as non-secret `vars.*`; the API certificate stays in Key Vault (never in the workflow).

Files:
* .github/workflows/deploy-croesus.yml - permissions id-token:write/contents:read; build-spa job (inject VITE_* public vars), build-api job (dotnet publish), deploy job (azure/login@v2 OIDC + azure/webapps-deploy@v3 for both apps); environment: production gate.

Success criteria:
* Workflow references only `vars.*` for identifiers and no client secret for deploy.
* Both apps deploy via azure/webapps-deploy.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 102-163) - Full example workflow shape (OIDC, build/deploy jobs, vars placement).
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 93-101) - Two distinct credentials (deploy OIDC FIC vs API Key Vault cert).

Dependencies:
* Phase 2, Phase 3, Phase 4, and Phase 5 completion.

### Step 6.2: Add the post-deploy evidence job

Add a CI job that runs after deploy: executes smoke-test + gated negative tests, runs the correlation KQL via az monitor log-analytics query, and writes the two-leg claim evidence plus App Insights / Entra sign-in deep links to $GITHUB_STEP_SUMMARY.

Files:
* .github/workflows/deploy-croesus.yml - evidence job (needs: deploy) invoking scripts/smoke-test.sh + scripts/negative-test.sh; az monitor log-analytics query with the correlation KQL; write results + portal deep links to $GITHUB_STEP_SUMMARY; upload claim summary artifact.
* scripts/evidence-kql.kusto - The SigninLogs + AADNonInteractiveUserSignInLogs correlation query (and the optional Token Protection advanced query, clearly labeled).

Success criteria:
* The job fails if a negative test passes (aud-mismatched token accepted).
* The run summary shows the two legs (distinct audiences) and links to App Insights + Entra sign-in logs.

Context references:
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 228-238) - Post-deploy evidence job (smoke/negative tests, KQL, step summary, deep links).
* .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 184-223) - Correlation KQL + Token Protection advanced/optional KQL.

Dependencies:
* Step 6.1 completion.

## Implementation Phase 7: Documentation and evidence narrative

<!-- parallelizable: true -->

### Step 7.1: Write the demo README and setup guide

Document the end-to-end demo: architecture (SPA -> API -> Graph OBO), the contrast with the broken token-replay baseline, prerequisites, provisioning steps, deploy steps, and how to read the evidence. Frame as "we are the Croesus vendor; Desjardins stands up the registrations".

Files:
* README.md - Update to add a "Mock Croesus SaaS OBO demo" section (architecture, wrong-vs-right contrast, run instructions) while preserving the existing analysis content; link to docs/ and scripts/.
* docs/obo-demo-guide.md - Step-by-step: provision (scripts/), deploy (workflow), exercise the flow, interpret App Insights claims + sign-in KQL; single-tenant note + multi-tenant onboarding callout (/v2.0/adminconsent).
* docs/evidence-narrative.md - Maps the escalation-packet questions Q1-Q6 to the demo's concrete evidence; explains why audience-binding (not Token Protection 1008) is the headline proof.

Success criteria:
* The guide lets a reader provision, deploy, and read the evidence without referencing the research docs.
* The Token Protection 1008 caveat is documented (advanced/optional exhibit, not the OBO proof).

Context references:
* .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Lines 175-205) - CRITICAL CORRECTION: anchor proof on audience-binding, not 1008.
* assets/croesus-escalation-packet.md - Vendor questions Q1-Q6 the narrative answers.
* .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 176-200) - Single-tenant decision + multi-tenant onboarding callout.

Dependencies:
* None beyond Phase 1 (docs reference designs defined in research; can be authored in parallel with Phase 6).

## Implementation Phase 8: Validation

<!-- parallelizable: false -->

### Step 8.1: Run full project validation

Execute all validation commands across the components:
* cd spa && npm ci && npm run build
* cd api && dotnet build && dotnet test
* az bicep build --file infra/main.bicep
* Lint/parse the workflow YAML and shell scripts (e.g., actionlint if available; bash -n on scripts).

Success criteria:
* All builds, tests, and template validations pass.

Dependencies:
* Phases 2-7 completion.

### Step 8.2: Fix minor validation issues

Iterate on lint errors, build warnings, and test failures. Apply fixes directly when corrections are straightforward and isolated (e.g., package version pins, import fixes, env-var naming).

Dependencies:
* Step 8.1 completion.

### Step 8.3: Report blocking issues

When validation failures require more than minor fixes (e.g., live-tenant-only behaviors such as actual sign-in log column shapes, certificate provisioning that needs a real Key Vault, or action-version drift):
* Document the issues and affected files.
* Provide the user with next steps (which require a live tenant/subscription to verify).
* Recommend additional research/planning rather than inline large-scale fixes.

Dependencies:
* Step 8.2 completion.

## Dependencies

* Node.js 20 + npm (SPA build).
* .NET 8 SDK (API build/test).
* Azure CLI with the `bicep` extension (provisioning, Bicep build).
* An Azure subscription + demo Entra tenant for live deploy/evidence (not required for build-time validation).
* GitHub repository `devopsabcs-engineering/croesus` with OIDC federated credential and `vars.*` configured (live deploy only).

## Success Criteria

* Runnable mock SPA (public client) + ASP.NET Core API (confidential client) demonstrating a standards OBO exchange (SPA -> API -> Graph) with the SPA never holding a Graph token.
* Idempotent provisioning + teardown + verification scripts producing two correctly-shaped single-tenant registrations (exposed scope, Key Vault certificate credential, pre-authorization, Graph User.Read consent).
* Bicep provisioning App Service x2 + Key Vault + Managed Identity + App Insights + Log Analytics + sign-in diagnostic settings, with no secret material in IaC.
* GitHub Actions OIDC deploy + post-deploy evidence job that gates on negative tests and surfaces two-leg audience-binding evidence with portal deep links.
* Documentation framing the wrong-vs-right contrast and anchoring the proof on OBO audience-binding (Token Protection 1008 demoted to an optional advanced exhibit).
