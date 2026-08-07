<!-- markdownlint-disable-file -->
# Implementation Details: Croesus Registration Evidence and Test Corrections

## Context Reference

Sources: .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (revised position, Registration Correctness, Explaining the 1008 Replay-Token Evidence, Corrected Assumptions, Repository Test and Documentation Backlog); .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (AADSTS citations, falsifiable assertions); .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md (1008 mechanics).

## Implementation Phase 1: Documentation Corrections

<!-- parallelizable: true -->

### Step 1.1: Make README replay, 1008, and OBO-required claims evidence-qualified

Rewrite the categorical claims so they present authorization code with PKCE as the leading hypothesis and `1008` as a device-binding status, not proof of replay.

Files:
* README.md - Bottom line bullet (~line 10) states the second sign-in is "confirmed by raw sign-in logs to be a token replay (Token Protection unbound, code 1008) ... not OBO"; V1 finding row (~line 16) labels the event "Token replay"; the How to fix it properly sequence (~lines 55-63) frames OBO as the required durable fix. Replace with evidence-qualified wording: the grant is not yet classified from a captured request, `spa`-platform registrations are consistent with browser authorization-code redemption, and OBO is one option contingent on a proven middle-tier requirement.

Discrepancy references:
* Addresses DR-01 (README reproduction section browser-1008 claim retained pending verification)

Success criteria:
* README no longer states the transaction is a confirmed token replay or that OBO is required
* `1008` is described as "client not integrated with the platform broker (WAM)", not replay proof
* The revised leading hypothesis (authorization code with PKCE) appears in the bottom line

Context references:
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Explaining the 1008 Replay-Token Evidence; Corrected Assumptions) - authoritative wording

Dependencies:
* None

### Step 1.2: Correct evidence-narrative and obo-demo-guide overclaims

Reconcile the docs with what the code actually emits and preserve the already-correct Token Protection hedging.

Files:
* docs/evidence-narrative.md - Q2 mapping row (~line 40) claims the App Insights OBO log "records the certificate thumbprint"; the headline audience-binding section (~line 24) states the API-audience token "is rejected by Graph with 401" as if proven live. Change the thumbprint claim to credential source/name only (matching api/Controllers/MeController.cs), and label the Graph-rejection direction as structurally asserted, not a live call. Leave the Token Protection 1008 section (~lines 44-48) intact because it is already correct.
* docs/obo-demo-guide.md - Steps around lines 137-187 assert downstream token-B `jti`/`iat`, decoded Graph audience, and certificate thumbprint. Align these with the code: token B is not decoded; leg 2 audience is Graph by construction; the credential is logged as source/name, not thumbprint.

Discrepancy references:
* Addresses DD-01 (docs describe evidence the code does not produce)

Success criteria:
* No doc claims a logged certificate thumbprint
* Downstream token-B `jti`/`iat` and decoded-audience claims are removed or marked as not emitted
* The Token Protection 1008 exhibit section remains accurate and separate from the OBO proof
* Tier 1 is described as wrong-audience rejection and Tier 2 as bearer forwarding with status-neutral interpretation (no binding cause assigned to a status)

Context references:
* api/Controllers/MeController.cs (leg 2 evidence and OBO request shape) - what is actually logged
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Corrected Assumptions rows on telemetry)

Dependencies:
* None

### Step 1.3: Correct the configuration-contract enforcement-path row

Files:
* docs/configuration-contract.md - The API delegated scope row (~line 34) maps `API_SCOPE` to the API app setting `AzureAd:Scopes`. The active enforcement path is the `[RequiredScope("access_as_user")]` attribute on the controllers, not `AzureAd:Scopes`. Correct the row (or add a note) so the enforcement mechanism is accurate; verify against api/Controllers/MeController.cs and api/Controllers/ReplayController.cs.

Success criteria:
* The configuration contract names `[RequiredScope("access_as_user")]` as the scope-enforcement path
* Any residual `AzureAd:Scopes` reference is clarified as not the enforcement mechanism

Context references:
* api/Controllers/MeController.cs (RequiredScope attribute)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 4)

Dependencies:
* None

### Step 1.4: Add the registration-correctness and 1008 explanation to customer-facing docs

Add a short, sourced subsection capturing the registration-correctness verdict and the `1008` explanation so the customer-facing narrative reflects the verified position.

Files:
* docs/evidence-narrative.md or README.md - Add a concise "Registration correctness" note: the exported apps are `spa`-platform public clients (no secret/cert, no exposed API scope), correct for browser authorization-code redemption; a literal server-side redemption of a `spa` code is rejected with `AADSTS9002327`, so a real backend redeemer would need a `web`-platform confidential client with a certificate. State the decisive artifact is one captured `/token` request showing whether an `Origin` header is present.

Discrepancy references:
* Addresses the user requests on registration correctness and 1008 explanation

Success criteria:
* A customer-facing doc states the `spa`-versus-`web` verdict and the `Origin`-header decisive artifact
* The `1008` meaning is stated correctly and separated from replay classification
* The doc names the "`1008` is out of Token Protection scope for browser-to-Graph and is not replay evidence" statement as a guard against reintroducing the overclaim

Context references:
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Registration Correctness for the Intended Flow)
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (AADSTS9002327 verbatim)

Dependencies:
* None

## Implementation Phase 2: .NET Test Suite Corrections and New Coverage

<!-- parallelizable: true -->

### Step 2.1: Rename the string-comparison negative-control test to state only what it proves

Files:
* api/Tests/NegativeControlTests.cs - The test `TokenA_PresentedDirectlyToGraph_WouldBeRejected_BecauseAudienceIsNotGraph` (~lines 58-65) only asserts `Assert.NotEqual(GraphAudience, ApiAudience)`; it does not call Graph. Rename to reflect that it proves audience distinctness only (for example `ApiAudience_IsDistinctFromGraphAudience_SoTokenAReuseCannotTargetGraph`) and adjust the comment to avoid implying a live Graph rejection.

Discrepancy references:
* Addresses DD-01 (test name overstates proof)

Success criteria:
* The test name and comment claim only audience distinctness, not a live Graph rejection
* The assertion is unchanged and still passes

Context references:
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 5)

Dependencies:
* None

### Step 2.2: Add labeled audience acceptance and rejection middleware tests

Files:
* api/Tests/NegativeControlTests.cs - Add two clearly labeled middleware tests using the existing `ApiFactory` and `TestAuth`: an API-audience token with `access_as_user` reaches the controller path (acceptance, distinct from the OBO exchange which is out of scope here), and a Graph-audience token is rejected with 401 (rejection). Label them as local audience-middleware coverage, not live OBO or Croesus behavior.

Success criteria:
* One test asserts an API-audience token passes the authentication/authorization gate
* One test asserts a Graph-audience token is rejected with 401
* Comments state these prove middleware audience handling only

Context references:
* api/Tests/NegativeControlTests.cs (ApiFactory, TestAuth)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 6)

Dependencies:
* None

### Step 2.3: Parameterize the replay endpoint tests for 200, 401, 403, and transport failure

Files:
* api/Tests/ReplayEndpointTests.cs - Replace or extend the stubbed single-status coverage (~lines 21-220) with a parameterized theory driving the ReplayController's `Interpret` outcomes for 200, 401, 403, and transport failure (status 0), asserting forwarding to the fixed Graph target and redaction of the raw token, without assigning a token-binding cause to any status.

Discrepancy references:
* Addresses DD-01 (status-specific replay interpretation overclaims)

Success criteria:
* Tests cover 200, 401, 403, and transport-failure outcomes
* Assertions verify fixed-target forwarding and token redaction
* No assertion claims a status proves token binding or its absence

Context references:
* api/Controllers/ReplayController.cs (Interpret helper, FixedGraphTarget)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 7)

Dependencies:
* None

### Step 2.4: Add a MeController success-path test with mocked token acquisition and Graph

Files:
* api/Tests/NegativeControlTests.cs or a new api/Tests/MeControllerTests.cs - Add a success-path test that mocks `ITokenAcquisition` and the Graph client so `GET /api/me` returns 200 with leg 1 and leg 2 evidence, without asserting unavailable token-B fields (no decoded token B, no thumbprint). Register the mocks via `ConfigureTestServices`.

Success criteria:
* A test exercises the MeController happy path with mocked token acquisition and Graph
* Assertions cover leg 1 claims and the leg 2 shape the code actually emits
* No assertion references decoded token-B `jti`/`iat` or a certificate thumbprint

Context references:
* api/Controllers/MeController.cs (Get action, leg1/leg2 evidence)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 8)

Dependencies:
* None

### Step 2.5: Add static registration-shape and correctness assertions over the exports

Files:
* api/Tests/RegistrationShapeTests.cs (new) - Parse assets/dev-dev.txt, assets/dev-prod.txt, and assets/prod-prod.txt as JSON and assert: `spa.redirectUris` non-empty; `web.redirectUris`, `keyCredentials`, `passwordCredentials`, `api.oauth2PermissionScopes`, and `appRoles` empty (public SPA client, OBO structurally impossible). Add a correctness assertion flagging any redirect URI ending in `.aspx` or containing `affwebservices` when it appears under the `spa` node. Resolve the assets path relative to the test project or repository root.

Discrepancy references:
* Addresses the registration-correctness user request

Success criteria:
* Static tests assert the public-SPA shape and OBO impossibility for all three exports
* A test flags server-rendered redirect URIs under the `spa` node
* Tests read the exports from a stable path and pass in CI

Context references:
* assets/dev-dev.txt, assets/prod-prod.txt (spa.redirectUris, empty credentials/scopes)
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (assertions 6, and the .aspx/affwebservices note)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog items 11-12)

Dependencies:
* None

### Step 2.6: Validate the .NET test project

Run the API test project and confirm all tests pass. `dotnet` is not on PATH in the current shell; resolve the SDK path first.

Validation commands:
* `dotnet test api/Tests/Croesus.Api.Tests.csproj` - full API test project (rename, new middleware tests, parameterized replay, MeController success path, registration-shape)

Success criteria:
* All API tests pass, including the renamed and newly added tests

Dependencies:
* Steps 2.1-2.5 completion

## Implementation Phase 3: SPA Replay-Lab, Workflow, and Evidence Corrections

<!-- parallelizable: true -->

### Step 3.1: Rebuild the replay-lab SPA with VITE_ENABLE_REPLAY_DEMO=true

Files:
* .github/workflows/deploy-croesus.yml - The replay-lab job enables the API replay route but the SPA build (~lines 123-324) does not inject `VITE_ENABLE_REPLAY_DEMO=true`, so the client-side control is absent. Add the build-time variable to the replay-lab SPA build step so the wrong-vs-right control renders.
* spa/src/api.ts, spa/src/components/ReplayAttemptPanel.tsx - Confirm the gate reads `VITE_ENABLE_REPLAY_DEMO`; adjust only if the flag name or gating is inconsistent.

Success criteria:
* The replay-lab SPA build injects `VITE_ENABLE_REPLAY_DEMO=true`
* The replay control is present when the lab is enabled

Context references:
* .github/workflows/deploy-croesus.yml (replay-lab build step)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 9)

Dependencies:
* None

### Step 3.2: Filter evidence by immutable app ID and label correlation joins best-effort

Files:
* scripts/evidence-kql.kusto - Filter queries by immutable application IDs rather than display names, and annotate correlation joins as best-effort (they cannot prove identical bearer bytes across requests).
* .github/workflows/deploy-croesus.yml - Where the evidence job embeds inline KQL or a display-name filter (~lines 123-324), align it with immutable app IDs and persist timestamped evidence artifacts.

Discrepancy references:
* Addresses DD-01 (evidence framing overclaims continuity)

Success criteria:
* Evidence queries filter by immutable app ID
* Correlation joins are labeled best-effort and do not claim token-byte continuity
* Evidence artifacts are persisted with timestamps

Context references:
* scripts/evidence-kql.kusto (existing queries)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 10)

Dependencies:
* None

### Step 3.3: Validate the SPA build

Validation commands:
* `npm --prefix spa run build` - SPA compiles with the demo flag set

Success criteria:
* The SPA builds without errors with `VITE_ENABLE_REPLAY_DEMO=true`

Dependencies:
* Steps 3.1-3.2 completion

## Implementation Phase 4: Optional Gated Live Redemption Tests

<!-- parallelizable: false -->

### Step 4.1: Scaffold an opt-in integration harness for real Entra redemption

These tests require real Entra apps and a completed interactive authorization-code grant, so they must be opt-in (skipped by default) and never run in the standard CI unit pass. Gate them behind an environment variable (for example `CROESUS_LIVE_REDEMPTION_TESTS=1`) and read app IDs, redirect URIs, and the tenant from environment/config, never from committed secrets.

Files:
* api/Tests/LiveRedemptionTests.cs (new) - xUnit tests decorated to skip unless the gate variable is set; helper to POST to `/{tenant}/oauth2/v2.0/token` with and without an `Origin` header.

Success criteria:
* The harness is skipped by default and only runs when explicitly enabled
* No secrets, codes, verifiers, or tokens are committed

Context references:
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (falsifiable assertions 1-5)
* .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (backlog item 13)

Dependencies:
* None (independent of Phases 1-3), but shares the .NET test project with Phase 2 so runs after Step 2.6

### Step 4.2: Assert spa-node no-Origin redemption fails with AADSTS9002327 and Origin succeeds

Files:
* api/Tests/LiveRedemptionTests.cs - Against a `spa`-platform app, POST a valid `authorization_code` plus `code_verifier` with no `Origin` header and assert an error containing `AADSTS9002327`; repeat with a matching `Origin` header and assert a 200 with an access token.

Success criteria:
* No-`Origin` server redemption of a `spa` code asserts `AADSTS9002327`
* The same request with an `Origin` header succeeds

Context references:
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (Q1 verbatim error)

Dependencies:
* Step 4.1 completion

### Step 4.3: Assert web-node confidential redemption succeeds and public-client-with-credential fails with AADSTS700025

Files:
* api/Tests/LiveRedemptionTests.cs - Against a `web`-platform app with a certificate, POST `code` plus `code_verifier` plus `client_assertion` with no `Origin` header and assert a 200; against the `spa`/public app, present a `client_secret` and assert `AADSTS700025`.

Success criteria:
* `web`-platform confidential server redemption succeeds
* A public client presenting a credential asserts `AADSTS700025`

Context references:
* .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (Q3, AADSTS700025)

Dependencies:
* Step 4.1 completion

## Implementation Phase 5: Validation

<!-- parallelizable: false -->

### Step 5.1: Run full project validation

Execute all validation for changed scopes:
* markdownlint / mega-linter on changed markdown docs
* `dotnet test api/Tests/Croesus.Api.Tests.csproj`
* `npm --prefix spa run build`

### Step 5.2: Fix minor validation issues

Iterate on lint errors, build warnings, and test failures when corrections are straightforward and isolated.

### Step 5.3: Report blocking issues

When a failure requires a live tenant (Phase 4) or additional research, document it and provide next steps rather than attempting large-scale inline fixes.

## Dependencies

* .NET 8 SDK, Node.js/npm, markdownlint/mega-linter
* Optional Phase 4: a nonproduction Microsoft Entra tenant with `spa` and `web` apps and a certificate credential

## Implementation Phase 6: Central Authentication Ladder and Conseiller Boundary

<!-- parallelizable: false -->

### Step 6.1: Add the secure Central authentication-shape ladder

Update assets/croesus-3way-session-findings.md with an R9 route that distinguishes the immediate proof from the maintainable destination. State that Central already has the BFF boundary: server-rendered pages, a server-held session, and server-side token redemption. Rank the practical options as follows:

1. Correct `spa` to `web` and prove confidential redemption with a client secret stored outside plaintext configuration.
2. Install and retarget to .NET Framework 4.8, centralize authentication in OWIN middleware, keep PKCE, and use a non-exportable certificate credential.
3. Introduce an ASP.NET Core authentication gateway only as a longer-term strangler architecture, with network isolation and cryptographically protected identity forwarding.

Do not imply that 130 `.aspx` pages need modification. Q10 remains the check for duplicated token acquisition across the reported 27 files.

### Step 6.2: Correct R5 workload-federation applicability

Revise R5 so workload identity federation is conditional on the host having a supported external OIDC workload-token source. A classic Windows VM running IIS does not provide one merely because it is hosted on AWS. For Central's reported shape, a certificate credential is the realistic security ceiling until hosting changes. Remove the claim that R8 alone makes R5 actionable.

### Step 6.3: Add a fenced Conseiller federation-direction assessment

Add the newly reported facts without broadening Central's escalation scope: Conseiller uses an in-house identity server and SAML and/or OIDC/OAuth technology such as Duende IdentityServer. Document the preferred direction as Entra upstream of the identity server so Conditional Access, MFA, device compliance, and sign-in risk evaluate at the interactive Entra sign-in. Treat SAML as a legacy compatibility path, not a target for new work. Flag IdentityServer4 end-of-support, Duende commercial licensing, and signing-key custody/rotation for the separate Conseiller assessment. Do not assert the exact product or version until Croesus confirms it.

### Step 6.4: Validate the authoritative findings document

Run focused Markdown diagnostics on assets/croesus-3way-session-findings.md. The file must have no new diagnostics.

## Implementation Phase 7: Separate UI Topology from Token Architecture

<!-- parallelizable: false -->

### Step 7.1: Correct current-facing documentation

Revise README.md, assets/croesus-3way-session-findings.md, assets/croesus-escalation-packet.md, assets/app-registration-analysis-findings.md, and docs/evidence-narrative.md. Replace categorical claims that Central is not a SPA or is necessarily a server-rendered multi-page UI with this evidence-safe position:

* Croesus reports .NET Framework 4.5.2, roughly 130 `.aspx` pages, a BFF, and a single URL for functionality, but has alternated between SPA and multi-page descriptions.
* The UI topology is unresolved and may be SPA, multi-page Web Forms, or hybrid. A single visible URL and `.aspx` paths do not classify it.
* The sampled HAR proves that `/token` redemption did not occur in that browser transaction. It does not prove how the UI renders.
* PKCE is recommended for public and confidential authorization-code clients and does not imply SPA.
* If the same backend redeems the code and retains tokens for the browser session, it is a confidential `web` client/BFF boundary regardless of whether the browser UI is SPA, multi-page, or hybrid.
* Calling the application a BFF remains provisional until Croesus confirms that browser JavaScript never receives OAuth tokens and downstream API calls are mediated by the backend.

Add a vendor question that requests the concrete UI and BFF evidence: document reload versus client-side routing, JavaScript shell/bundles, Web Forms postbacks, component holding the PKCE verifier, token exposure to browser JavaScript, session-cookie properties, and whether all downstream API calls traverse the backend. Update route counts from eight to nine where current-facing text names the count.

### Step 7.2: Correct the demo panel and registration-shape test

Revise spa/src/components/RegistrationShapePanel.tsx so it explains that SPA and BFF are compatible and that PKCE does not classify the UI. Keep the registration conclusion tied to the server-side redeemer and complete registration inventory, not to `.aspx` or a single URL.

Revise api/Tests/RegistrationShapeTests.cs so the second test asserts only that the exports contain `.aspx` or `affwebservices` redirect paths. Rename the test and comments to remove the false claim that those paths prove a server-rendered UI or a registration mismatch. Preserve the first test's structural public-client and OBO-impossible assertions.

### Step 7.3: Correct and regenerate both deck language packs

Revise docs/deck/generate-deck.py in English and French. Remove categorical multi-page/not-SPA language; state that UI topology is unresolved, SPA+BFF is possible, PKCE is not a classifier, and server-side redemption is the registration-relevant fact. Regenerate both 14-slide PPTX files.

### Step 7.4: Run focused validation

Run diagnostics on changed Markdown, TypeScript, Python, and C# files; `dotnet test api/Tests/Croesus.Api.Tests.csproj --filter FullyQualifiedName~RegistrationShapeTests`; `npm --prefix spa run build`; and `python docs/deck/generate-deck.py`. Confirm both decks contain 14 slides.

## Success Criteria

* Docs, tests, and evidence tooling reflect the evidence-qualified revised position with accurate `1008` and registration-correctness framing, and all non-optional tests pass
