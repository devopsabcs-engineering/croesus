<!-- markdownlint-disable-file -->
# Release Changes: Croesus Registration Evidence and Test Corrections

**Related Plan**: croesus-registration-evidence-corrections-plan.instructions.md
**Implementation Date**: 2026-07-28

## Summary

Correct the repository's documentation, tests, and evidence tooling to state the evidence-qualified revised position (authorization code with PKCE is the leading hypothesis; the Desjardins registrations are `spa`-platform public clients correct only for browser redemption; `1008` is a device-binding status, not proof of replay) and add mock-app coverage that can prove or falsify the SPA-versus-web redemption behavior.

## Changes

### Added

* api/Tests/MeControllerTests.cs - OBO happy-path test with stubbed ITokenAcquisition and canned Graph transport; asserts the two-leg evidence shape without decoding token B or a certificate thumbprint (Phase 2, Step 2.4)
* api/Tests/RegistrationShapeTests.cs - Static assertions over assets/dev-dev.txt, dev-prod.txt, prod-prod.txt proving public-SPA shape (empty web.redirectUris/keyCredentials/passwordCredentials/oauth2PermissionScopes/appRoles); Phase 7 limits redirect-path assertions to the observable `.aspx`/`affwebservices` values without inferring UI topology or registration correctness (Phase 2, Step 2.5; Phase 7, Step 7.2)
* api/Tests/LiveRedemptionTests.cs - Opt-in, skipped-by-default live Entra `/token` tests gated on CROESUS_LIVE_REDEMPTION_TESTS=1 via a custom LiveRedemptionFactAttribute; asserts AADSTS9002327 (no-Origin spa redemption), Origin success, web+certificate success, and AADSTS700025 (public client with secret); reads all inputs from environment, commits no secrets (Phase 4, Steps 4.1-4.3)
* api/Tests/AssemblyInfo.cs - Serializes the test assembly (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) so the in-process hosts are created and used in a single non-overlapping order (Phase 5 flaky-test fix)

### Modified

* assets/croesus-3way-session-findings.md - Added R9's staged Central authentication ladder; corrected R5 so workload federation requires an external OIDC workload-token source; separated runtime installation from retargeting and regression testing; fenced Conseiller as a separate assessment; Phase 7 records Central's UI topology as unresolved, separates SPA/BFF compatibility from registration shape, and adds Q15 for topology and BFF evidence
* README.md - Made replay/1008/OBO-required claims evidence-qualified; Phase 7 removes categorical SPA/MPA claims, states that PKCE and one URL do not classify the UI, and routes registration correctness through the code redeemer and token custodian
* docs/evidence-narrative.md - Corrected token-evidence overclaims and registration guidance; Phase 7 distinguishes the sampled HAR's no-browser-redemption result from UI topology and treats the reported BFF boundary as provisional pending Q15
* docs/obo-demo-guide.md - Downstream token-B jti/iat, decoded-audience, and thumbprint claims aligned to what the code emits; negative-test wording made status-neutral (Phase 1, Step 1.2); fixed two pre-existing markdownlint findings (MD028 blank line between adjacent alerts, MD012 double blank line) (WI-06)
* assets/app-registration-analysis-findings.md - Added revised-position notices for the 1008 correction and Phase 7's evidence-qualified UI-topology/BFF position while preserving historical analysis as historical record
* docs/configuration-contract.md - API delegated-scope row marks AzureAd:Scopes as not the enforcement path and names [RequiredScope("access_as_user")] on both controllers as the enforcement mechanism (Phase 1, Step 1.3)
* api/Tests/NegativeControlTests.cs - Renamed the audience-distinctness test to claim only what it proves; added a labeled Graph-audience rejection test; removed a flaky in-process admit-path test (replaced by the deterministic stubbed path in MeControllerTests); added a no-op Dispose override to the host factory (Phase 2 + Phase 5 fix)
* api/Tests/ReplayEndpointTests.cs - Parameterized replay outcomes (200/401/403/transport failure) asserting fixed-target forwarding and token redaction without assigning a binding cause; added a no-op Dispose override to the host factory base (Phase 2 + Phase 5 fix)
* api/Tests/MeControllerTests.cs - Added a no-op Dispose override to the OBO-success host factory so the shared-key host is not disposed mid-run (Phase 5 fix)
* .github/workflows/deploy-croesus.yml - Injected VITE_ENABLE_REPLAY_DEMO into the shared SPA build gated on vars.ENABLE_REPLAY_LAB; evidence/replay-lab queries switched to immutable app IDs with best-effort correlation notes and UTC-timestamped artifacts (Phase 3, Steps 3.1-3.2); made the evidence correlation step resilient to a missing vars.API_CLIENT_ID (Graph-only filter + explanatory note instead of an empty-GUID clause) (WI-05)
* scripts/evidence-kql.kusto - Filters switched from display names to immutable AppId / Graph well-known appId; correlation joins labeled best-effort (Phase 3, Step 3.2)
* scripts/provision-app-registrations.sh - Added optional GitHub repo-variable persistence (PERSIST_REPO_VARIABLES, default auto): sets API_CLIENT_ID, SPA_CLIENT_ID, and API_SCOPE via the gh CLI when authenticated, or prints the exact gh commands otherwise — removes the manual repo-settings step for WI-05 (WI-05)
* docs/configuration-contract.md - API delegated-scope row marks AzureAd:Scopes as not the enforcement path and names [RequiredScope("access_as_user")] on both controllers as the enforcement mechanism (Phase 1, Step 1.3); documented the provision-script auto-persistence of API_CLIENT_ID/SPA_CLIENT_ID/API_SCOPE (WI-05)
* assets/croesus-escalation-packet.md - Aligned with the revised position; added the decisive `/token` evidence and inventory asks; Phase 7 adds Q15 for concrete UI-topology, PKCE-verifier, token-custody, cookie, and backend-mediation evidence
* spa/src/components/RegistrationShapePanel.tsx - Separates unresolved UI topology from the code-redeemer decision, explains SPA/BFF compatibility, and links Q15 and all nine remediation routes (Phase 7, Step 7.2)
* docs/deck/generate-deck.py - Corrected the English and French content packs to make UI topology unresolved, PKCE non-classifying, and registration dependent on the redeemer; updated route count to nine (Phase 7, Step 7.3)
* docs/deck/croesus-session-deck.pptx - Regenerated the corrected 14-slide English deck (Phase 7, Step 7.3)
* docs/deck/croesus-session-deck-fr.pptx - Regenerated the corrected 14-slide French deck (Phase 7, Step 7.3)

### Removed

* (none)

## Additional or Deviating Changes

* DD-02: Phase 2's accept-path middleware test (`AudienceMiddleware_ApiAudienceTokenWithScope_PassesAuthGate`) was found to be flaky during Phase 5 and was REMOVED. Root cause: the suite runs several `WebApplicationFactory<Program>` hosts that all validate JWTs signed with one shared `TestAuth.SigningKey`; disposing one host mid-run tears down shared System.IdentityModel state another still-live host depends on, so a valid API-audience token is intermittently rejected with a spurious 401. The deterministic admit path already lives in api/Tests/MeControllerTests.cs (OBO/Graph stubbed). The suite was made deterministic (verified 10/10 green) by (a) suppressing per-fixture host disposal via a no-op `Dispose(bool)` override on each factory and (b) serializing the assembly with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
* DD-03: The workflow has a single shared `build-spa` job, not a separate replay-lab SPA build. `VITE_ENABLE_REPLAY_DEMO` was injected into the shared build gated on `vars.ENABLE_REPLAY_LAB` (the same variable that gates the replay-lab job).
* The evidence-job inline KQL previously filtered on `AppDisplayName == "Croesus API"`, which never matched the provisioned display name; corrected to immutable app IDs. A new repository variable `vars.API_CLIENT_ID` is required for that filter (tracked as WI-05).
* The first Phase 7 deck-generation attempt selected a UV-managed interpreter without `python-pptx`; regeneration completed with the Windows Store Python 3.13 environment where `python-pptx` was already installed.

## Release Summary

All seven phases complete. Phase 7 corrects the final categorical UI-topology inference across the authoritative brief, packet, README, evidence narrative, demo panel, regression test, and bilingual decks. Central may be SPA, multi-page Web Forms, or hybrid; PKCE, `.aspx` paths, and one visible URL do not decide that question. Registration correctness follows the component that redeems the code and safeguards credentials and tokens. Q15 now requests the concrete evidence needed to confirm the reported BFF boundary.

Added:
* api/Tests/MeControllerTests.cs - deterministic OBO happy-path coverage
* api/Tests/RegistrationShapeTests.cs - static public-SPA / OBO-impossible assertions over the three exports, with path-only redirect assertions that do not infer UI topology
* api/Tests/LiveRedemptionTests.cs - opt-in, skipped-by-default live redemption tests (spa-vs-web behavior)
* api/Tests/AssemblyInfo.cs - serializes the test assembly to keep the flaky-fix deterministic

Modified:
* assets/croesus-3way-session-findings.md - Central authentication ladder, corrected workload-federation constraints, fenced Conseiller assessment, unresolved UI topology, and Q15 BFF evidence request
* README.md, docs/evidence-narrative.md, docs/obo-demo-guide.md, docs/configuration-contract.md - evidence-qualified position, correct 1008 framing, UI-topology/token-architecture separation, registration-correctness note, and enforcement-path fix
* assets/app-registration-analysis-findings.md - revised-position notices for replay/1008 and UI-topology corrections
* assets/croesus-escalation-packet.md - revised framing plus `/token`, inventory, Central-vs-Conseiller, and Q15 topology/BFF evidence asks
* spa/src/components/RegistrationShapePanel.tsx - evidence-safe registration-shape exhibit
* docs/deck/generate-deck.py and both generated PPTX files - corrected English/French 14-slide decks
* api/Tests/NegativeControlTests.cs - renamed audience-distinctness test; added labeled middleware tests; removed a flaky admit-path test; no-op Dispose override
* api/Tests/ReplayEndpointTests.cs - parameterized 200/401/403/transport outcomes, status-neutral; no-op Dispose override
* .github/workflows/deploy-croesus.yml - VITE_ENABLE_REPLAY_DEMO wiring; immutable app-ID evidence filters; timestamped artifacts; evidence step resilient to a missing API_CLIENT_ID (WI-05)
* scripts/evidence-kql.kusto - immutable app-ID filters, best-effort correlation labels

Validation: focused `RegistrationShapeTests` = 6 passed / 0 failed; `npm --prefix spa run build` = clean; both generated decks contain 14 slides and no withdrawn "not a SPA" or "eight routes" text. Current-facing Markdown, TypeScript, and C# diagnostics are clean. Pylance's `pptx` import warning reflects its selected interpreter; deck generation succeeds under the repository terminal's Windows Store Python 3.13 environment.

Deployment notes: the evidence job requires a new `vars.API_CLIENT_ID` repository variable; the replay-lab SPA control renders only when `vars.ENABLE_REPLAY_LAB == 'true'`. Phase 4 live tests require a nonproduction Entra tenant and run only when `CROESUS_LIVE_REDEMPTION_TESTS=1`.
