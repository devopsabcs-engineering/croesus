<!-- markdownlint-disable-file -->
# Release Changes: Croesus Registration Evidence and Test Corrections

**Related Plan**: croesus-registration-evidence-corrections-plan.instructions.md
**Implementation Date**: 2026-07-28

## Summary

Correct the repository's documentation, tests, and evidence tooling to state the evidence-qualified revised position (authorization code with PKCE is the leading hypothesis; the Desjardins registrations are `spa`-platform public clients correct only for browser redemption; `1008` is a device-binding status, not proof of replay) and add mock-app coverage that can prove or falsify the SPA-versus-web redemption behavior.

## Changes

### Added

* api/Tests/MeControllerTests.cs - OBO happy-path test with stubbed ITokenAcquisition and canned Graph transport; asserts the two-leg evidence shape without decoding token B or a certificate thumbprint (Phase 2, Step 2.4)
* api/Tests/RegistrationShapeTests.cs - Static assertions over assets/dev-dev.txt, dev-prod.txt, prod-prod.txt proving public-SPA shape (empty web.redirectUris/keyCredentials/passwordCredentials/oauth2PermissionScopes/appRoles) and flagging server-rendered `.aspx`/`affwebservices` redirect URIs under the spa node (Phase 2, Step 2.5)
* api/Tests/LiveRedemptionTests.cs - Opt-in, skipped-by-default live Entra `/token` tests gated on CROESUS_LIVE_REDEMPTION_TESTS=1 via a custom LiveRedemptionFactAttribute; asserts AADSTS9002327 (no-Origin spa redemption), Origin success, web+certificate success, and AADSTS700025 (public client with secret); reads all inputs from environment, commits no secrets (Phase 4, Steps 4.1-4.3)
* api/Tests/AssemblyInfo.cs - Serializes the test assembly (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) so the in-process hosts are created and used in a single non-overlapping order (Phase 5 flaky-test fix)

### Modified

* README.md - Made replay/1008/OBO-required claims evidence-qualified: authorization code with PKCE is the leading unclassified hypothesis, 1008 relabeled as a broker-binding status, OBO reframed as one contingent option; browser-1008 exhibit retained with a DR-01 verification NOTE (Phase 1, Step 1.1)
* docs/evidence-narrative.md - Certificate-thumbprint claim changed to credential source/name; API-token-to-Graph 401 labeled structurally asserted; leg-2 jti/iat/decoded-audience replaced with correlation-id/token-source/expiry; added a Registration correctness section (spa-vs-web verdict, verbatim AADSTS9002327, Origin-header decisive artifact, 1008-not-replay guard) (Phase 1, Steps 1.2, 1.4)
* docs/obo-demo-guide.md - Downstream token-B jti/iat, decoded-audience, and thumbprint claims aligned to what the code emits; negative-test wording made status-neutral (Phase 1, Step 1.2)
* docs/configuration-contract.md - API delegated-scope row marks AzureAd:Scopes as not the enforcement path and names [RequiredScope("access_as_user")] on both controllers as the enforcement mechanism (Phase 1, Step 1.3)
* api/Tests/NegativeControlTests.cs - Renamed the audience-distinctness test to claim only what it proves; added a labeled Graph-audience rejection test; removed a flaky in-process admit-path test (replaced by the deterministic stubbed path in MeControllerTests); added a no-op Dispose override to the host factory (Phase 2 + Phase 5 fix)
* api/Tests/ReplayEndpointTests.cs - Parameterized replay outcomes (200/401/403/transport failure) asserting fixed-target forwarding and token redaction without assigning a binding cause; added a no-op Dispose override to the host factory base (Phase 2 + Phase 5 fix)
* api/Tests/MeControllerTests.cs - Added a no-op Dispose override to the OBO-success host factory so the shared-key host is not disposed mid-run (Phase 5 fix)
* .github/workflows/deploy-croesus.yml - Injected VITE_ENABLE_REPLAY_DEMO into the shared SPA build gated on vars.ENABLE_REPLAY_LAB; evidence/replay-lab queries switched to immutable app IDs with best-effort correlation notes and UTC-timestamped artifacts (Phase 3, Steps 3.1-3.2)
* scripts/evidence-kql.kusto - Filters switched from display names to immutable AppId / Graph well-known appId; correlation joins labeled best-effort (Phase 3, Step 3.2)

### Removed

* (none)

## Additional or Deviating Changes

* DD-02: Phase 2's accept-path middleware test (`AudienceMiddleware_ApiAudienceTokenWithScope_PassesAuthGate`) was found to be flaky during Phase 5 and was REMOVED. Root cause: the suite runs several `WebApplicationFactory<Program>` hosts that all validate JWTs signed with one shared `TestAuth.SigningKey`; disposing one host mid-run tears down shared System.IdentityModel state another still-live host depends on, so a valid API-audience token is intermittently rejected with a spurious 401. The deterministic admit path already lives in api/Tests/MeControllerTests.cs (OBO/Graph stubbed). The suite was made deterministic (verified 10/10 green) by (a) suppressing per-fixture host disposal via a no-op `Dispose(bool)` override on each factory and (b) serializing the assembly with `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
* DD-03: The workflow has a single shared `build-spa` job, not a separate replay-lab SPA build. `VITE_ENABLE_REPLAY_DEMO` was injected into the shared build gated on `vars.ENABLE_REPLAY_LAB` (the same variable that gates the replay-lab job).
* The evidence-job inline KQL previously filtered on `AppDisplayName == "Croesus API"`, which never matched the provisioned display name; corrected to immutable app IDs. A new repository variable `vars.API_CLIENT_ID` is required for that filter (tracked as WI-05).
* Two pre-existing markdownlint findings remain in docs/obo-demo-guide.md (MD028 line 76, MD012 line 80), outside the edited regions and left untouched (tracked as WI-06).

## Release Summary

All five phases complete. 4 files added, 8 files modified, 0 removed.

Added:
* api/Tests/MeControllerTests.cs - deterministic OBO happy-path coverage
* api/Tests/RegistrationShapeTests.cs - static public-SPA / OBO-impossible assertions over the three exports
* api/Tests/LiveRedemptionTests.cs - opt-in, skipped-by-default live redemption tests (spa-vs-web behavior)
* api/Tests/AssemblyInfo.cs - serializes the test assembly to keep the flaky-fix deterministic

Modified:
* README.md, docs/evidence-narrative.md, docs/obo-demo-guide.md, docs/configuration-contract.md - evidence-qualified position, correct 1008 framing, registration-correctness note, enforcement-path fix
* api/Tests/NegativeControlTests.cs - renamed audience-distinctness test; added labeled middleware tests; deterministic accept-path assertion
* api/Tests/ReplayEndpointTests.cs - parameterized 200/401/403/transport outcomes, status-neutral
* .github/workflows/deploy-croesus.yml - VITE_ENABLE_REPLAY_DEMO wiring; immutable app-ID evidence filters; timestamped artifacts
* scripts/evidence-kql.kusto - immutable app-ID filters, best-effort correlation labels

Validation: `dotnet test api/Tests/Croesus.Api.Tests.csproj` = 19 passed / 4 skipped / 0 failed, verified deterministic across 10 consecutive runs; `npm --prefix spa run build` (with VITE_ENABLE_REPLAY_DEMO=true) = clean. Documentation validated via VS Code markdownlint diagnostics (no new findings).

Deployment notes: the evidence job requires a new `vars.API_CLIENT_ID` repository variable; the replay-lab SPA control renders only when `vars.ENABLE_REPLAY_LAB == 'true'`. Phase 4 live tests require a nonproduction Entra tenant and run only when `CROESUS_LIVE_REDEMPTION_TESTS=1`.
