<!-- markdownlint-disable-file -->
# Planning Log: Mock Croesus SaaS App with Standards-Compliant MSAL OBO Flow

## Discrepancy Log

Gaps and differences identified between research findings and the implementation plan.

### Unaddressed Research Items

* DR-01: Live-tenant validation of `AADNonInteractiveUserSignInLogs` columns (`IncomingTokenType`, `ResourceIdentity`, `CrossTenantAccessType`) that distinguish the OBO leg.
  * Source: .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Lines 50-54)
  * Reason: Requires a live tenant with sign-in logs streamed to Log Analytics; not verifiable at build time. The KQL is authored in Phase 6 but column shapes are validated only during live deploy.
  * Impact: medium

* DR-02: Certificate / federated-identity-credential variant of the API confidential client and its `client_assertion` OBO request shape (vs client secret).
  * Source: .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 130-138)
  * Reason: The plan selects certificate-in-Key-Vault (IP-01) and Microsoft.Identity.Web handles the `client_assertion` transparently; the raw cert-assertion wire shape is not separately implemented.
  * Impact: low

* DR-03: Continuous Access Evaluation (CAE) on the middle tier to honor near-real-time revocation.
  * Source: .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 388-393)
  * Reason: Strengthens the "not a static bearer" story but is out of scope for the core OBO-vs-replay proof; deferred to follow-on WI-03.
  * Impact: low

* DR-04: A separate native-app + EXO/SPO exhibit producing a real Token Protection bound/unbound (1008) signal.
  * Source: .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Lines 30-46)
  * Reason: Research explicitly demotes 1008 to an optional advanced exhibit; building a native app is a materially different scope. Documented as a caveat in Phase 7, not implemented.
  * Impact: low

* DR-05: End-to-end `WWW-Authenticate` claims-challenge / MFA step-up handling between the middle-tier API and the SPA (API returns a 401 + claims challenge when Graph demands CA/MFA step-up; SPA re-acquires the API token with the propagated `claims`).
  * Source: .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 245-246); .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 388-393)
  * Reason: The SPA handles `InteractionRequiredAuthError` for its own first-leg acquisition (Step 2.2), but the plan does not implement propagation of a Graph-originated claims challenge back through the API to the SPA. Explicitly flagged in the app-registration "Recommended Next Research". Follow-on is partially tracked under WI-03 but had no DR-side traceability.
  * Impact: low

* DR-06: A safe, CI-runnable construction of the audience-mismatched ("Graph-audience") token used by the negative-control test, without leaking real tokens. — RESOLVED in Step 4.3.
  * Source: .copilot-tracking/research/subagents/2026-06-29/azure-hosting-cicd-evidence-research.md (Recommended Next Research, break-glass negative test, ~Line 279)
  * Resolution: Step 4.3 now specifies minting the Graph-audience token via the CI test service principal's client-credentials grant (`scope=https://graph.microsoft.com/.default`) to present to the API (expect 401), and reusing the smoke-test token A against Graph (expect 401). No real user token is reused.
  * Impact: medium (resolved)

### Plan Deviations from Research

* DD-01: The proof is anchored on OBO audience-binding, not Token Protection code 1008.
  * Research recommends: Re-anchor on audience-binding (1008 will not fire for a browser SPA + custom API + Graph; presenting it as the proof would fail scrutiny).
  * Plan implements: Audience-binding as the headline evidence (App Insights claims + negative tests); 1008 demoted to an optional, clearly-labeled advanced exhibit.
  * Rationale: Directly follows the research CRITICAL CORRECTION; keeps the demo technically defensible to a security-literate customer (Desjardins).

* DD-02: The API is ASP.NET Core (.NET), while the SPA is TypeScript/React — a mixed-language stack rather than uniform Node.
  * Research recommends: ASP.NET Core + Microsoft.Identity.Web (correct-by-construction) as the primary; Node `@azure/msal-node` acceptable if the team mandates JS.
  * Plan implements: ASP.NET Core API + React SPA.
  * Rationale: Microsoft.Identity.Web's fluent chain removes any place to accidentally implement a token replay — the single most important property for a demo whose purpose is to disprove replay. Node alternative recorded as IP-02.

## Implementation Paths Considered

### Selected: ASP.NET Core (Microsoft.Identity.Web) API + React/Vite SPA + App Service + GitHub Actions OIDC + audience-binding evidence

* Approach: SPA requests the API scope only; .NET API validates aud/scope and performs OBO to Graph via the fluent chain; two App Service Web Apps; OIDC deploy; certificate in Key Vault; App Insights claim logging + negative tests + correlation KQL as evidence.
* Rationale: Correct-by-construction OBO, credible enterprise hosting, no stored deploy secret, defensible evidence.
* Evidence: .copilot-tracking/research/2026-06-29/croesus-mock-saas-obo-flow-research.md (Lines 230-245); .copilot-tracking/research/subagents/2026-06-29/msal-obo-architecture-research.md (Lines 100-160)

### IP-01: Client secret instead of certificate for the API confidential client

* Approach: Use `az ad app credential reset` to create a client secret rather than a Key Vault certificate.
* Trade-offs: Simpler provisioning and no Key Vault dependency; but long-lived shared secret, weaker "we do OAuth properly" message, and no centralized rotation/audit.
* Rejection rationale: Research recommends certificate + Key Vault as the production-grade, hardened confidential-client posture; secret remains documented as an acceptable fallback for a purely local demo.

### IP-02: Uniform Node.js stack (SPA + `@azure/msal-node` middle tier)

* Approach: Implement the middle tier in Node with explicit `acquireTokenOnBehalfOf`.
* Trade-offs: Single language across SPA and API; shows the exchange explicitly; but hand-wires token validation, scope checks, and caching — more surface area to accidentally implement a replay.
* Rejection rationale: For a demo whose purpose is to disprove replay, correct-by-construction (.NET) outweighs stack uniformity. Acceptable only if the team mandates Node, in which case explicit OBO-vs-replay assertions must be added.

### IP-03: Azure Static Web Apps + managed Functions (instead of App Service)

* Approach: Single SWA resource with a managed Functions API.
* Trade-offs: Cheapest, single workflow; but SWA built-in `/.auth` is not MSAL OBO (must be ignored), and the confidential-client/Key Vault story is weaker.
* Rejection rationale: App Service is the simplest credible place for a confidential client (native MI + Key Vault references + App Insights). Use SWA only if "simplest possible" outranks "most credible".

### IP-04: Azure Container Apps

* Approach: Containerized SPA + API on ACA.
* Trade-offs: Maximum portability; but Dockerfiles + ACR + image build/push is overkill for a two-tier demo.
* Rejection rationale: Highest infra/CI overhead with no benefit for this scope.

## Suggested Follow-On Work

Items identified during planning that fall outside current scope.

* WI-01: Live-tenant evidence validation pass — run the deployed demo in a real Entra tenant + Log Analytics and confirm the sign-in-log columns and the two-leg KQL render as expected (low/medium).
  * Source: DR-01
  * Dependency: A demo Entra tenant (P1 if Token Protection is desired) + subscription + the deployed app.

* WI-02: Native-app + EXO/SPO Token Protection exhibit — a separate, clearly-labeled advanced artifact that produces a real bound/unbound (1008) signal (low).
  * Source: DR-04
  * Dependency: A registered Windows device + Conditional Access Token Protection policy.

* WI-03: Add Continuous Access Evaluation (CAE) to the middle tier and document the WWW-Authenticate claims-challenge step-up handling (low).
  * Source: DR-03
  * Dependency: Core OBO flow completed (Phases 2-3).

* WI-04: Multi-tenant SaaS onboarding walkthrough — a second artifact showing `/v2.0/adminconsent` provisioning for the realistic vendor-hosted multi-tenant model (low).
  * Source: .copilot-tracking/research/subagents/2026-06-29/app-registration-design-research.md (Lines 176-200)
  * Dependency: Single-tenant demo completed (documented as a callout in Phase 7).
