<!-- markdownlint-disable-file -->
# Release Changes: Tier 2 Conditional Access 1008 Token-Replay Reproduction & Reversible Demo

**Related Plan**: tier2-ca-1008-replay-plan.instructions.md
**Implementation Date**: 2026-07-06

## Summary

Adds a gated, fully reversible Tier 2 to the Croesus OBO demo: a server-side token-replay endpoint (2a), a report-only Token Protection Conditional Access policy for real 1008 telemetry (2b), wrong-vs-right + five-item OBO gap presentation, reversible provisioning/teardown that restores the demo tenant to its original state, and a manually-approved replay-lab CI/CD path.

## Changes

### Added

* api/Controllers/ReplayController.cs - gated POST /api/replay; [Authorize][RequiredScope("access_as_user")]; server-side replay of a client-forwarded Graph token to the FIXED target https://graph.microsoft.com/v1.0/me; claims-only evidence; emits ReplayAttempt; never returns/logs raw tokens.
* api/Tests/ReplayEndpointTests.cs - xUnit tests: 404 when disabled, 401 no token, redaction in body + logs, 401 for Graph-audience token.
* spa/src/getGraphToken.ts - silent→popup Microsoft Graph token acquisition (mirrors getApiToken.ts).
* scripts/provision-ca-policy.sh - creates a report-only Token Protection CA policy (beta Graph, secureSignInSession) scoped to a test user + Exchange Online + native clients; records caPolicyId to the state file; idempotent PATCH guard; commented enforcement-flip.
* scripts/teardown-ca-policy.sh - deletes the CA policy by recorded id with a croesus-demo- prefix-sweep fallback; clears the state file.
* scripts/verify-clean.sh - five PASS/FAIL checks (no demo CA policy, no SPA Graph grant, no demo registrations, live gate off/absent, no replay-lab FIC).

### Modified

* api/appsettings.json - added "Demo": { "EnableReplay": false }.
* api/Program.cs - gate ReplayController route via a lazy ExcludeControllerFeatureProvider (absent-when-off); widened CORS to .WithMethods("GET","POST"); registered AddHttpClient().
* api/Telemetry/OboClaimLogger.cs - added LogReplayAttempt(...) emitting a distinct ReplayAttempt event; reuses Redact(); OboExchange path unchanged.
* spa/src/authConfig.ts - added graphRequest (VITE_GRAPH_SCOPE ?? User.Read); apiRequest/loginRequest remain API-scope-only.
* spa/src/api.ts - added callApiReplay (API token for Authorization, Graph token in body to /api/replay); local decode for display only.
* spa/src/App.tsx - added VITE_ENABLE_REPLAY_DEMO-gated Tier 2 section (wrong-vs-right); Tier 1 button unchanged.
* spa/src/components/ReplayAttemptPanel.tsx - added server-side replay result rendering + SHAPE-not-1008 note + five-item OBO gap checklist.
* spa/src/vite-env.d.ts - typed VITE_GRAPH_SCOPE / VITE_ENABLE_REPLAY_DEMO (optional).
* infra/modules/appservice.bicep - added API app setting Demo__EnableReplay='false'.
* docs/configuration-contract.md - added four rows (Demo:EnableReplay, VITE_ENABLE_REPLAY_DEMO, VITE_GRAPH_SCOPE, VITE_GRAPH_BASE_URL); noted the gated SPA-Graph-scope exception.
* scripts/provision-app-registrations.sh - grants SPA Graph User.Read + admin consent; records SPA→Graph grant id to .demo-state.json; API-only grant preserved.
* scripts/teardown-app-registrations.sh - revokes SPA→Graph grant(s) (Principal + AllPrincipals) before app deletion; resets live Demo__EnableReplay=false (guarded); removes the replay-lab FIC by subject suffix; existing app + cert deletion preserved.
* scripts/evidence-kql.kusto - corrected display names to "Croesus GPD Central SPA/API (mock)"; added the Unbound/1008 query and the two-leg interactive↔non-interactive correlation query; ReplayAttempt event note; refreshed the 1008 support-matrix caveat.
* docs/obo-demo-guide.md - added the Tier 2 walkthrough (Step 7 provision/consent/gates/exercise/evidence, Step 8 teardown + verify-clean); corrected Step 6 display name.
* docs/evidence-narrative.md - mapped replay reproduction + 1008 telemetry to escalation questions; kept the honest 2a/2b boundary; added the residual-token-validity note.
* README.md - replaced the "deferred" Tier 2 framing with the delivered 2a/2b design; corrected Token Protection licensing P2→P1 (deployment-guide citation); added the five-item OBO gap; documented the reversible teardown + gates + intentionally-retained replay-lab environment.
* scripts/setup-deploy-identity.sh - added the replay-lab federated credential (subject repo:${REPO}:environment:replay-lab) via add_fic; documented in trailing notes.
* .github/workflows/deploy-croesus.yml - added a double-gated replay-lab job (if: vars.ENABLE_REPLAY_LAB == 'true' + environment: replay-lab): OIDC login, provision-ca-policy.sh, set live Demo__EnableReplay=true, inline 1008 + correlation KQL, artifact upload; no CI teardown; production jobs unchanged.

### Removed

* None.

## Additional or Deviating Changes

* Phase 1 gate evaluated lazily inside the ExcludeControllerFeatureProvider rather than up-front — required so WebApplicationFactory test-host config overrides are honored (production behavior unchanged: default false → route absent).
  * Reason: a top-level synchronous GetValue<bool> runs before the test host merges config.
* Phase 1 replay tests use shared IClassFixture factories with a per-host validation key + StaticConfigurationManager (avoids poisoning shared IdentityModel crypto state and network OIDC fetch); the JWT-shape redaction detector requires ≥24-char segments to avoid false matches on dotted framework type names in logs.
  * Reason: per-test host disposal caused spurious 401s on the success path.
* Phase 5 sources TEST_USER_OBJECT_ID / BREAK_GLASS_USER_OBJECT_ID from vars.* (non-secret GUIDs); operators must set vars.ENABLE_REPLAY_LAB, vars.TEST_USER_OBJECT_ID, vars.BREAK_GLASS_USER_OBJECT_ID and create the replay-lab environment manually.
  * Reason: consistent with the workflow's public-identifier convention.
* Follow-on (WI-03/DR-05): README bottom-line and finding V1 still describe the customer's real incident as "1008 to Microsoft Graph"; the documented support matrix says 1008 should not surface for a Graph target — reconcile or footnote once the customer's real target resource is confirmed.
  * Reason: out of implementation scope; depends on the customer's raw sign-in logs.
* Two pre-existing markdown lint nits in docs/obo-demo-guide.md (MD028/MD012 around the Step 1 blockquotes) were left untouched.
  * Reason: outside Phase 6 scope (implementation discipline).

## Release Summary

All 7 phases complete; validation green across every gate.

* Files created (6): api/Controllers/ReplayController.cs, api/Tests/ReplayEndpointTests.cs, spa/src/getGraphToken.ts, scripts/provision-ca-policy.sh, scripts/teardown-ca-policy.sh, scripts/verify-clean.sh.
* Files modified (18): api/appsettings.json, api/Program.cs, api/Telemetry/OboClaimLogger.cs, spa/src/authConfig.ts, spa/src/api.ts, spa/src/App.tsx, spa/src/components/ReplayAttemptPanel.tsx, spa/src/vite-env.d.ts, infra/modules/appservice.bicep, docs/configuration-contract.md, scripts/provision-app-registrations.sh, scripts/teardown-app-registrations.sh, scripts/setup-deploy-identity.sh, scripts/evidence-kql.kusto, docs/obo-demo-guide.md, docs/evidence-narrative.md, README.md, .github/workflows/deploy-croesus.yml.
* Files removed: none.

Validation results (Phase 7):

* dotnet build api/Croesus.Api.csproj -c Release — succeeded, 0 warnings, 0 errors.
* dotnet test api/Tests/Croesus.Api.Tests.csproj — Passed 7/7 (3 negative-control + 4 replay-endpoint).
* npm --prefix spa run build (tsc && vite build) — succeeded, 179 modules.
* az bicep build --file infra/main.bicep — exit 0.
* bash -n on all 6 scripts — OK each.
* Workflow YAML parse — YAML_OK.

Dependency / infrastructure notes:

* New config: Demo:EnableReplay (API app setting Demo__EnableReplay, default false) + VITE_ENABLE_REPLAY_DEMO / VITE_GRAPH_SCOPE / VITE_GRAPH_BASE_URL (SPA build vars) — all default OFF/minimal.
* New tenant objects at demo time (reversible): SPA Graph User.Read delegated grant; a report-only Token Protection CA policy (croesus-demo-token-protection). Both recorded to .demo-state.json and removed by teardown; verify-clean.sh asserts a clean tenant.
* New CI: a manually-approved replay-lab GitHub environment + federated credential; operators must set vars.ENABLE_REPLAY_LAB, vars.TEST_USER_OBJECT_ID, vars.BREAK_GLASS_USER_OBJECT_ID and create the environment. Gate is OFF by default.

Deployment notes:

* Deploy is code-only (no Bicep step), so the replay-lab job sets Demo__EnableReplay=true live via az webapp config appsettings set; teardown resets it to false.
* Token Protection (P1) does not emit 1008 for browser-SPA -> custom-API -> Graph; Tier 2a reproduces the replay shape, Tier 2b reproduces the real 1008 telemetry on Exchange Online. Documented honestly in README/docs.

Outstanding (non-blocking, tracked as WI-03/DR-05): reconcile the README bottom-line / finding V1 wording ("1008 to Microsoft Graph") with the documented support matrix once the customer's real target resource is confirmed.
