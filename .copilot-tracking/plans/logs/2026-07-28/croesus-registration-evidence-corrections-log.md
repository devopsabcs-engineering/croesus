<!-- markdownlint-disable-file -->
# Planning Log: Croesus Registration Evidence and Test Corrections

## Discrepancy Log

Gaps and differences identified between research findings and the implementation plan.

### Unaddressed Research Items

* DR-01: The README "Reproduction confirmed (live evidence)" section claims a browser SPA-to-API sign-in produced Token Protection `Unbound (statusCode 1008)` with a `Client app: Browser` row.
  * Source: README.md (Reproduction confirmed section) and .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md (native-app-only; Graph unsupported)
  * Reason: The research states Token Protection is native-app-only and excludes Microsoft Graph, so a browser-to-Graph flow is out of binding scope. Whether the captured browser row legitimately carried a `1008` binding-status telemetry value (as opposed to enforcement) is a factual question the plan flags rather than resolves; Step 1.1 makes the surrounding claims evidence-qualified but does not delete the captured exhibit pending verification.
  * Impact: medium

* DR-02: The decisive live artifact — one captured `/token` request (or sign-in-log row) showing whether an `Origin` header is present — is a customer/vendor evidence dependency, not a repository change.
  * Source: .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Remaining Evidence Gaps)
  * Reason: Out of scope for repository corrections; belongs to the stakeholder evidence request.
  * Impact: high (for final classification), low (for this plan's deliverables)

* DR-03: Backlog item 3 asks that the *documentation narrative* describe Tier 1 as wrong-audience rejection and Tier 2 as bearer forwarding with a status-neutral interpretation. The plan covers the test-side reframe (Steps 2.1 and 2.3 rename/parameterize the negative-control and replay tests) but has no discrete doc-narrative step or success criterion that reframes the Tier 1 / Tier 2 prose in docs/evidence-narrative.md and docs/obo-demo-guide.md.
  * Source: .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Repository Test and Documentation Backlog, item 3); .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md (Tier 1 audience 401 vs Tier 2 telemetry)
  * Reason: Step 1.2 corrects specific overclaims (jti/iat, decoded audience, cert thumbprint, broad Token Protection proof) but its success criteria do not name the Tier 1 = wrong-audience / Tier 2 = status-neutral framing, so the doc-narrative half of item 3 is partial rather than fully covered.
  * Impact: low (docs partially reframe Tier 2 today at docs/obo-demo-guide.md; the gap is completeness of the explicit Tier 1/Tier 2 relabel, not a correctness or risk defect)

* DR-04: Backlog item 14 asks for a Token Protection non-signal assertion recorded in tests/docs — stating a browser/SPA-to-Graph flow is out of Token Protection scope so an observed `1008` is expected and is not replay evidence — explicitly to prevent the lab from re-introducing the "1008-proves-replay" overclaim. Steps 1.1 and 1.4 add and correct the customer-facing `1008` explanation, but no step or success criterion establishes this as a regression guard, and no test-level assertion pins the non-signal claim.
  * Source: .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Repository Test and Documentation Backlog, item 14); .copilot-tracking/research/subagents/2026-07-28/spa-platform-token-redemption-and-registration-correctness.md (falsifiable assertion 7, documentation-level)
  * Reason: The documentation half is covered (Steps 1.1 and 1.4), but the anti-reintroduction / regression-guard intent — and the optional test-level assertion — has no discrete step or measurable success criterion, so item 14 is partial. This overlaps with, but is narrower than, the unresolved browser-`1008` exhibit tracked by DR-01/DD-01/WI-04.
  * Impact: low-to-medium (prevents future re-introduction of the corrected overclaim; no current correctness or risk impact)

### Plan Deviations from Research

* DD-01: The research backlog lists documentation, test, and evidence corrections as a flat 14-item list. The plan groups them into phases by validation scope (docs, .NET tests, SPA/workflow, optional live tests) and defers the browser-1008 exhibit decision (DR-01) rather than rewriting that section.
  * Research recommends: correct all overclaims across README, docs, tests, workflow, and evidence
  * Plan implements: the same corrections, phased for parallel execution, with the browser-1008 exhibit flagged for verification instead of removed
  * Rationale: preserves a potentially valid captured exhibit while removing the categorical replay/OBO-required framing; avoids deleting evidence that may be defensible

* DD-02: The plan's Step 2.2 acceptance test proved flaky and was REMOVED in Phase 5; the suite was made deterministic (10/10 consecutive green runs).
  * Plan specifies: assert an API-audience token passes the auth gate
  * Implementation differs: the flaky in-process assertion was removed. The deterministic 200 admit path already lives in MeControllerTests (OBO/Graph stubbed). Root cause of the flakiness: several `WebApplicationFactory<Program>` hosts validate JWTs signed with one shared `TestAuth.SigningKey`; disposing one host mid-run tears down shared System.IdentityModel state another still-live host depends on, intermittently yielding a spurious 401 (serial execution made it worse, confirming host-disposal poisoning). Fixed by (a) a no-op `Dispose(bool)` override on each host factory so hosts stay alive for the whole run and (b) `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
  * Rationale: test-host lifecycle artifact, not a production defect; keeps the suite deterministic and credential-free

* DD-03: The plan's Step 3.1 assumed a separate replay-lab SPA build step; the workflow has a single shared `build-spa` job.
  * Plan specifies: add VITE_ENABLE_REPLAY_DEMO=true to the replay-lab SPA build step
  * Implementation differs: inject `VITE_ENABLE_REPLAY_DEMO: ${{ vars.ENABLE_REPLAY_LAB }}` into the shared build env, gated on the same variable that gates the replay-lab job
  * Rationale: faithful equivalent that keeps a single artifact/deploy path; no separate build step exists

* DD-04: Earlier current-facing analysis treated `.aspx` redirect paths, one visible URL, and PKCE as evidence that Central was a multi-page application rather than a SPA.
  * Plan previously implied: Central's server-rendered UI made the exported `spa` platform intrinsically incorrect
  * Implementation differs: Phase 7 records UI topology as unresolved and makes registration correctness depend on the component that redeems the authorization code and safeguards credentials and tokens
  * Rationale: SPA and BFF are compatible; `.aspx` paths, a single URL, and PKCE do not classify the frontend, and the sampled HAR proves only that `/token` did not occur in that browser transaction

## Implementation Paths Considered

### Selected: Phased corrections grouped by validation scope with an optional live-test phase

* Approach: Four implementation phases (docs, .NET tests, SPA/workflow/evidence, optional gated live tests) plus a final validation phase. Docs, .NET tests, and SPA/workflow phases touch disjoint file sets and each own a distinct validation scope, so they are parallelizable; the optional live-test phase shares the .NET test project and is sequential and opt-in.
* Rationale: Maximizes parallel execution, keeps CI deterministic by isolating the real-Entra tests behind a gate, and maps cleanly onto the research backlog.
* Evidence: .copilot-tracking/research/2026-07-28/croesus-token-endpoint-revised-position-research.md (Repository Test and Documentation Backlog, items 1-14)

### IP-01: Single sequential "fix everything" phase

* Approach: One ordered list of edits across all files.
* Trade-offs: Simplest to author; loses parallelism and mixes doc, .NET, and npm validation scopes into one pass.
* Rejection rationale: Slower and harder to validate incrementally; the file sets are naturally disjoint.

### IP-02: Include live redemption tests in the default CI pass

* Approach: Run the `spa`-versus-`web` redemption tests as normal unit tests.
* Trade-offs: Strongest end-to-end proof, but requires real Entra apps, a certificate, and an interactive grant, which are non-deterministic and secret-bearing in CI.
* Rejection rationale: Violates deterministic, credential-free CI; the tests are provided but gated opt-in (Phase 4).

## Suggested Follow-On Work

Items identified during planning that fall outside current scope.

* WI-01: Obtain one redacted captured `/token` request (or sign-in-log row) from Croesus to confirm the `Origin` header and classify the grant — (high)
  * Source: research Remaining Evidence Gaps and DR-02
  * Dependency: vendor evidence request (Olivier response draft in the research doc)
* WI-02: Obtain the complete application and service-principal inventory to confirm no separate `web`-platform confidential registration exists — (high)
  * Source: research Remaining Evidence Gaps
  * Dependency: Desjardins tenant export
* WI-03: Compare Central and Conseiller transactions for divergent client, redirect, backend, or token handling — (medium)
  * Source: research Actionable Next Steps
  * Dependency: WI-01
* WI-04: Decide the fate of the README browser-1008 exhibit after verifying whether the captured browser row is a legitimate binding-status telemetry value — (medium)
  * Source: DR-01
  * Dependency: sign-in-log verification
* WI-01: Advanced (ready-to-send) — the vendor ask for one successful and one failing correlated transaction, with the decisive `/token` `Origin`-header capture and a privacy-safe presence/hashes-only rule, is now in assets/croesus-escalation-packet.md (Q7, §3). Obtaining the capture still requires Croesus. (high)
  * Source: research Remaining Evidence Gaps and DR-02
  * Dependency: vendor response
* WI-02: Advanced (ready-to-send) — the complete app-registration / service-principal inventory ask (including any confidential `web`/API registration) is now Q8 in the escalation packet. Obtaining it still requires the Desjardins/Croesus tenants. (high)
  * Source: research Remaining Evidence Gaps
  * Dependency: tenant export
* WI-03: Advanced (ready-to-send) — the Central-vs-Conseiller comparison ask is now Q9 in the escalation packet. (medium)
  * Source: research Actionable Next Steps
  * Dependency: vendor response
* WI-05: Fixed (in-repo) — two-part resolution. (1) The evidence correlation step no longer emits a misleading empty `AppId == ''` clause when vars.API_CLIENT_ID is unset; it filters the Microsoft Graph leg only and prints a note. (2) scripts/provision-app-registrations.sh now auto-persists API_CLIENT_ID, SPA_CLIENT_ID, and API_SCOPE as repository variables via the `gh` CLI when authenticated (PERSIST_REPO_VARIABLES, default auto), or prints the exact `gh variable set` commands otherwise. The app registration itself is created by the same script (`az ad app create`), so one provisioning run now both creates the API app and sets the variable — no manual repo-settings step. (medium)
  * Source: Phase 3, Step 3.2
  * Dependency: a provisioning run in the tenant (creates the app + the GUID); `gh auth login` for automatic variable writes
  * Verified 2026-07-29 (demo tenant MngEnvMCAP675646): the mock app registrations and repo variables already exist — API_CLIENT_ID `bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8`, SPA_CLIENT_ID `06ef7c0a-9df3-4bcd-8b6f-ee275ca0adc2`, API_SCOPE `api://bc6338a5-a02a-4ddf-b1f4-9a9234bed8a8/access_as_user`, KEY_VAULT_NAME `kv-croesus-a65e90` (all repo variables set 2026-06-29). Both service principals present. Nothing to create; WI-05 is satisfied end to end. Note: invoking `az` through WSL interop produced transient `JSONDecodeError` responses (native Windows `az` works reliably), so provisioning should be run from Windows/Cloud Shell rather than WSL.
* WI-06: Fixed — the two pre-existing markdownlint findings in docs/obo-demo-guide.md (MD028 line 76, MD012 line 80) were resolved (HTML-comment separator between the adjacent alerts; collapsed the double blank line). (low)
  * Source: Phase 1 self-review
  * Dependency: none
* WI-07: Fixed — assets/app-registration-analysis-findings.md (linked from README) carried the same categorical replay framing; added a dated revised-position banner and evidence-qualified the "1008 confirms token replay" claims while keeping the correct OBO-impossible reasoning. (low)
  * Source: Phase 1 suggested additional steps
  * Dependency: none
* WI-08: Consider a Testing-only global exception handler in the API so downstream failures surface as HTTP 500 rather than propagating out of the TestServer, enabling a concrete-status accept-path assertion — (low)
  * Source: Phase 2, DD-02
  * Dependency: production/design decision
* WI-09: Confirm Conseiller's exact identity-server product, edition, and supported version before selecting a migration or federation implementation — (high)
  * Source: Phase 6, Step 6.3
  * Dependency: Croesus deployment inventory
* WI-10: Confirm whether a future Central hosting model can supply a supported external OIDC workload token before reconsidering workload identity federation — (medium)
  * Source: Phase 6, Step 6.2
  * Dependency: Croesus hosting and workload-identity roadmap
* WI-11: Obtain the Q15 evidence needed to classify Central's UI topology and confirm the reported BFF boundary — (high)
  * Source: Phase 7, Steps 7.1-7.3
  * Dependency: Croesus evidence covering document reload versus client routing, JavaScript bundles, Web Forms postbacks, PKCE-verifier ownership, browser token visibility, session-cookie properties, and backend mediation of downstream API calls
