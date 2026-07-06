<!-- markdownlint-disable-file -->
# Implementation Details: Tier 2 Conditional Access 1008 Token-Replay Reproduction & Reversible Demo

## Context Reference

Sources: .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md and its subagent docs (codebase-demo-map.md, token-protection-1008-mechanics.md, reversible-ca-provisioning-and-obo-gap.md).

## Implementation Phase 1: API Tier 2a replay endpoint, gate, CORS, telemetry, tests

<!-- parallelizable: true -->

### Step 1.1: Add Demo:EnableReplay=false to api/appsettings.json

Add a `Demo` section defaulting the replay gate OFF. Development file needs no change (inherits default false).

Files:
* api/appsettings.json - add `"Demo": { "EnableReplay": false }` alongside existing AzureAd/Graph/Cors sections.

Discrepancy references:
* Addresses DR-01 (new config value must have a contract row — see Phase 3 Step 3.2).

Success criteria:
* `Demo:EnableReplay` reads as `false` by default; existing keys unchanged.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 236-268) - appsettings key inventory.

Dependencies:
* None.

### Step 1.2: Create api/Controllers/ReplayController.cs

New controller mirroring MeController's security surface but performing a deliberate server-side replay of a client-forwarded Graph token to a FIXED target. Reject any caller-supplied target.

Files:
* api/Controllers/ReplayController.cs - `[Authorize][ApiController][Route("api/[controller]")][RequiredScope("access_as_user")]`; `[HttpPost]` action accepting a request body `{ graphToken: string }`; hard-coded target `https://graph.microsoft.com/v1.0/me`; server-side `HttpClient` (via `IHttpClientFactory`) presents the forwarded token; decode only non-sensitive claims (aud, scp, jti, iat, hasCnf) via a local decoder mirroring spa `decodeJwtClaims`; emit a `ReplayAttempt` event via `OboClaimLogger`; return claims-only evidence `{ attemptedTarget, tokenAudience, tokenScope, tokenJti, tokenIssuedAt, hasCnf, status, ok, interpretation }`. Never echo the raw token.

Discrepancy references:
* Addresses the user request (faithful server-side replay reproduction).

Success criteria:
* Endpoint requires a valid API-audience bearer token and the `access_as_user` scope.
* Only the fixed Graph `/me` target is called; caller-supplied targets are ignored/rejected.
* Response and logs contain no raw token (redaction verified in Step 1.5).

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 88-135) - MeController security + claims pattern.
* .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Implementation Details paragraph on POST /api/replay).

Dependencies:
* Step 1.4 (ReplayAttempt telemetry helper).

### Step 1.3: Gate endpoint registration + widen CORS to POST in api/Program.cs

Register the replay controller/route ONLY when `Demo:EnableReplay` is true (absent-when-off), and widen the CORS policy to allow POST so the SPA can call it.

Files:
* api/Program.cs - read `builder.Configuration.GetValue<bool>("Demo:EnableReplay")`; when false, exclude the ReplayController from the application model (e.g., conditional `AddControllers` feature/convention or an `IActionModelConvention`/`ExcludeControllerFeatureProvider` that removes ReplayController) so the route is not mapped; change CORS `.WithMethods("GET")` (line 41) to `.WithMethods("GET","POST")`.

Discrepancy references:
* Addresses DD-01 (absent-when-off gate chosen over present-but-refusing).

Success criteria:
* With `Demo:EnableReplay=false`, `POST /api/replay` returns 404 (route absent).
* With `Demo:EnableReplay=true`, the route is mapped and honors auth.
* CORS preflight for POST from the SPA origin succeeds (204).

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 41-77) - Program.cs CORS + middleware wiring.

Dependencies:
* Step 1.2 (controller must exist).

### Step 1.4: Add ReplayAttempt telemetry helper to api/Telemetry/OboClaimLogger.cs

Add a `LogReplayAttempt(...)` method emitting a distinct `ReplayAttempt` App Insights event, reusing the existing `Redact()` guard so no token-shaped value is ever logged.

Files:
* api/Telemetry/OboClaimLogger.cs - new method building a `replay.*` properties dictionary (each value passed through `Redact()`), `TrackEvent("ReplayAttempt", properties)` + structured `ILogger` info; do not alter the existing `LogExchange`/`OboExchange` path.

Discrepancy references:
* Addresses DD-02 (distinct event name for clean evidence queries).

Success criteria:
* A `ReplayAttempt` custom event is emitted; the existing `OboExchange` event is unchanged.
* Any token-shaped input is redacted to `[REDACTED:token]`.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 138-160) - OboClaimLogger redaction + event pattern.

Dependencies:
* None.

### Step 1.5: Create api/Tests/ReplayEndpointTests.cs

xUnit tests using the existing `WebApplicationFactory<Program>` host pattern, exercising the gated endpoint.

Files:
* api/Tests/ReplayEndpointTests.cs - tests: (a) with `Demo:EnableReplay=false`, `POST /api/replay` → 404 (absent); (b) with `Demo:EnableReplay=true` in in-memory config, no token → 401; (c) with a valid API-audience test token, the response body and any captured log contain no JWT-shaped substring (redaction); (d) a Graph-audience token is rejected by the auth middleware. Reuse the `ApiFactory`/`TestAuth` helpers (swapped symmetric trust anchor) from NegativeControlTests.

Discrepancy references:
* Addresses the README Tier 2 prereq "token-redaction tests".

Success criteria:
* All four assertions pass; no raw token appears in any response or captured log.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 163-200) - NegativeControlTests host + TestAuth helpers.

Dependencies:
* Steps 1.2, 1.3, 1.4.

### Step 1.6: Validate phase changes

Validation commands:
* `dotnet build api/Croesus.Api.csproj -c Debug` - API compiles.
* `dotnet test api/Tests/Croesus.Api.Tests.csproj` - existing + new tests pass.

Success criteria:
* Build succeeds; all tests green.

Dependencies:
* Steps 1.1-1.5.

## Implementation Phase 2: SPA Graph token + gated replay flow

<!-- parallelizable: true -->

### Step 2.1: Add graphRequest to spa/src/authConfig.ts

Add a Graph delegated-scope request object read from `VITE_GRAPH_SCOPE` (default `User.Read`), without altering the existing API-only `apiRequest`/`loginRequest`.

Files:
* spa/src/authConfig.ts - `const graphScope = import.meta.env.VITE_GRAPH_SCOPE ?? 'User.Read';` and `export const graphRequest: RedirectRequest = { scopes: [graphScope] };`; keep the comment forbidding Graph scopes on the API path.

Discrepancy references:
* Deviates from repo invariant "SPA requests only API_SCOPE" — see DD-03 (intentional, gated).

Success criteria:
* `apiRequest`/`loginRequest` remain API-scope-only; `graphRequest` requests only the Graph scope.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 283-296) - authConfig scopes.

Dependencies:
* None.

### Step 2.2: Create spa/src/getGraphToken.ts

Sibling of getApiToken.ts acquiring a Graph token (silent→popup fallback).

Files:
* spa/src/getGraphToken.ts - `getGraphToken(instance, account)` using `graphRequest`, `acquireTokenSilent` then `acquireTokenPopup` on `InteractionRequiredAuthError`.

Success criteria:
* Returns a Graph-audience access token; mirrors getApiToken error handling.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 298-303) - getApiToken pattern.

Dependencies:
* Step 2.1.

### Step 2.3: Add callApiReplay to spa/src/api.ts

New function that acquires a Graph token and POSTs it to the replay endpoint, returning claims-only evidence for rendering.

Files:
* spa/src/api.ts - `callApiReplay(instance, account)`: get Graph token, `fetch(${VITE_API_BASE_URL}/api/replay, { method: 'POST', headers: { Authorization: Bearer <apiToken>, 'Content-Type': 'application/json' }, body: JSON.stringify({ graphToken }) })`; decode the forwarded Graph token locally (reuse `decodeJwtClaims`) for display; return a typed result including the server's `ReplayAttempt` evidence. Note the API bearer is the API-audience token (for `[Authorize]`), while the forwarded token in the body is the Graph token.

Success criteria:
* The API call is authorized with the API token; the Graph token is only in the body; no raw token is displayed.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 305-330) - api.ts callApiMe + decodeJwtClaims + Tier 1 replay.

Dependencies:
* Steps 2.1, 2.2; API endpoint contract (Phase 1).

### Step 2.4: Add VITE_ENABLE_REPLAY_DEMO-gated Tier 2 section (App.tsx + ReplayAttemptPanel.tsx)

Render a new Tier 2 section only when the gate is on, showing wrong (2a server-side replay) beside right (OBO), and the five-item gap. Keep the always-on Tier 1 button.

Files:
* spa/src/App.tsx - guard `import.meta.env.VITE_ENABLE_REPLAY_DEMO === 'true'`; add state + `handleServerReplay()` calling `callApiReplay`; render the gated section.
* spa/src/components/ReplayAttemptPanel.tsx - extend (or add a variant) to render the server-side replay result and a clear "reproduces the replay SHAPE, not the literal 1008 signal" note, plus the five-item wrong→right checklist.

Success criteria:
* With the gate off, no Tier 2 section renders and Tier 1 is unaffected.
* With the gate on, wrong-vs-right + the five-item gap are shown.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 332-360) - App.tsx buttons + panels.

Dependencies:
* Step 2.3.

### Step 2.5: Validate phase changes

Validation commands:
* `npm --prefix spa run build` - `tsc && vite build` succeeds.

Success criteria:
* Type-check + build pass.

Dependencies:
* Steps 2.1-2.4.

## Implementation Phase 3: Infrastructure + configuration contract

<!-- parallelizable: true -->

### Step 3.1: Add Demo__EnableReplay app setting to infra/modules/appservice.bicep

Add `Demo__EnableReplay='false'` to the API Web App appSettings so the gate has a platform default; the replay-lab job flips it live (Phase 5).

Files:
* infra/modules/appservice.bicep - add the setting alongside AzureAd__*/Cors__* (around lines 116-152); default `'false'`.

Discrepancy references:
* Addresses DR-01 (config value wiring).

Success criteria:
* API app setting `Demo__EnableReplay='false'` present; no other settings changed.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 424-448) - appservice API settings threading.

Dependencies:
* None.

### Step 3.2: Add four new rows to docs/configuration-contract.md

Register `Demo:EnableReplay` (Demo__EnableReplay), `VITE_ENABLE_REPLAY_DEMO`, `VITE_GRAPH_SCOPE`, and optional `VITE_GRAPH_BASE_URL` in the contract, each defaulting OFF/minimal.

Files:
* docs/configuration-contract.md - add rows to the public configuration table with source, consumer, and default; note the intentional SPA-Graph-scope exception is gated.

Success criteria:
* Every new config value has a contract row before it is wired elsewhere.

Context references:
* .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Configuration Example rows).

Dependencies:
* None.

### Step 3.3: Validate phase changes

Validation commands:
* `az bicep build --file infra/main.bicep` - bicep compiles.

Success criteria:
* Bicep builds without error.

Dependencies:
* Step 3.1.

## Implementation Phase 4: Reversible provisioning & teardown scripts

<!-- parallelizable: true -->

### Step 4.1: Extend scripts/provision-app-registrations.sh to grant SPA Graph User.Read + admin consent

Add the SPA Graph `User.Read` delegated permission + admin consent (currently API-only), and record the created `oauth2PermissionGrant` id to the demo state file for reversibility.

Files:
* scripts/provision-app-registrations.sh - after the SPA→API consent block, `az ad app permission add --id $SPA_CLIENT_ID --api 00000003-0000-0000-c000-000000000000 --api-permissions e1fe6dd8-ba31-4d61-89e7-88639da4683d=Scope` then `az ad app permission admin-consent --id $SPA_CLIENT_ID`; resolve and record the SPA→Graph grant id into `${STATE_FILE:-.demo-state.json}` (jq merge).

Discrepancy references:
* Addresses the README Tier 2 prereq "Grant the SPA Microsoft Graph User.Read delegated consent".

Success criteria:
* The SPA has a Graph `User.Read` delegated grant; its grant id is recorded in the state file.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 461-483) - provision script grant blocks.
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Section 2.3) - grant + state-file pattern.

Dependencies:
* jq available.

### Step 4.2: Create scripts/provision-ca-policy.sh

New script creating a report-only Token Protection CA policy against the beta Graph endpoint, scoped to a test user + a supported resource (Exchange Online) + native client apps, recording the policy id to the state file.

Files:
* scripts/provision-ca-policy.sh - required env: TEST_USER_OBJECT_ID, BREAK_GLASS_USER_OBJECT_ID; optional RESOURCE_APP_ID (default Exchange Online `00000002-0000-0ff1-ce00-000000000000`), POLICY_DISPLAY_NAME (`croesus-demo-token-protection`), STATE_FILE (`.demo-state.json`); `az rest --method POST --uri https://graph.microsoft.com/beta/identity/conditionalAccess/policies` with `state=enabledForReportingButNotEnforced`, `clientAppTypes=["mobileAppsAndDesktopClients"]`, `sessionControls.secureSignInSession.isEnabled=true`; record `caPolicyId`. Guard: if a policy with the prefix already exists, PATCH instead of duplicating (idempotent). Include a commented optional PATCH to flip `state=enabled` for the enforcement beat.

Discrepancy references:
* Addresses the user request (reproduce 1008) and DD-04 (beta endpoint required).

Success criteria:
* A report-only token-protection policy exists scoped to the test user + supported resource; its id is recorded.
* Re-running does not create duplicates.

Context references:
* .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Complete Example — token-protection CA policy).
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Sections 1.1-1.4).

Dependencies:
* Conditional Access Administrator role + `Policy.ReadWrite.ConditionalAccess`; jq.

### Step 4.3: Create scripts/teardown-ca-policy.sh

New script deleting the CA policy by recorded id, with a prefix-sweep fallback if the state file is lost.

Files:
* scripts/teardown-ca-policy.sh - read `caPolicyId` from STATE_FILE; `az rest --method DELETE --uri .../beta/identity/conditionalAccess/policies/$ID` guarded (204 or already-gone); fallback: list policies with `starts_with(displayName,'croesus-demo-')` and delete each; clear the id from the state file.

Discrepancy references:
* Addresses the user request (reversible undo).

Success criteria:
* The demo CA policy is deleted; re-running is a no-op; no non-demo policy is touched.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Sections 2.1, 2.2, 2.5).

Dependencies:
* Same roles/permissions as 4.2.

### Step 4.4: Extend scripts/teardown-app-registrations.sh to revoke SPA Graph grant(s), reset the live gate, and remove the replay-lab FIC

Extend the existing idempotent teardown to (a) revoke the SPA's Graph `oauth2PermissionGrant`(s) (both `Principal` and `AllPrincipals`) before deleting the registrations, (b) reset the live `Demo__EnableReplay` app setting on the API Web App (the code-only deploy path means the Bicep `false` default is not reapplied), and (c) remove the `replay-lab` federated credential from the deploy identity so the tenant returns to its exact prior state.

Files:
* scripts/teardown-app-registrations.sh - resolve SPA SP id + Graph SP id; list SPA→Graph grants; `az rest --method DELETE --uri .../v1.0/oauth2PermissionGrants/$GRANT_ID` for each (guarded); prefer the recorded `spaGraphGrant` id from the state file, fallback to filter query; reset the live gate via `az webapp config appsettings set --name $API_APP_NAME --resource-group $RESOURCE_GROUP --settings Demo__EnableReplay=false` (or delete the setting) guarded on app existence; remove the replay-lab FIC via `az ad app federated-credential delete` (resolve the credential by its `replay-lab` subject/name, guarded); keep existing app + cert deletion. All new actions tolerate already-removed state (idempotent).

Discrepancy references:
* Addresses the user request (reversible undo) + DR-02, DR-06 (live gate reset), DR-07 (replay-lab FIC removal).

Success criteria:
* No SPA→Graph delegated grant remains; live `Demo__EnableReplay` is false/removed; the `replay-lab` FIC is removed; app + cert deletion still works; re-running is a no-op.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 485-510) - existing teardown pattern.
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Section 2.3).

Dependencies:
* `DelegatedPermissionGrant.ReadWrite.All`; jq.

### Step 4.5: Create scripts/verify-clean.sh

New script asserting the tenant is clean: no demo CA policy, no SPA→Graph grant, no demo app registrations, the live gate is off, and no replay-lab federated credential remains.

Files:
* scripts/verify-clean.sh - five checks (empty/false expected): (1) no CA policy with `starts_with(displayName,'croesus-demo-')`; (2) no SPA→Graph `oauth2PermissionGrant`; (3) no demo app registrations by display name; (4) live `Demo__EnableReplay` is false or absent (`az webapp config appsettings list`), skipped gracefully if the app is gone; (5) no `replay-lab` federated credential on the deploy identity (`az ad app federated-credential list`). Exit non-zero if any residue is found; print a clear PASS/FAIL summary per check.

Discrepancy references:
* Addresses the user request (prove restoration to original state) + DR-06, DR-07.

Success criteria:
* Returns success only when all five checks are clean.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/reversible-ca-provisioning-and-obo-gap.md (Section 2.4).

Dependencies:
* Steps 4.3, 4.4 (teardown must run first for a clean assertion).

### Step 4.6: Validate phase changes

Validation commands:
* `bash -n scripts/provision-app-registrations.sh scripts/provision-ca-policy.sh scripts/teardown-ca-policy.sh scripts/teardown-app-registrations.sh scripts/verify-clean.sh` - syntax check.

Success criteria:
* All scripts parse with no syntax error.

Dependencies:
* Steps 4.1-4.5.

## Implementation Phase 5: CI/CD replay-lab environment gating

<!-- parallelizable: false -->

### Step 5.1: Add replay-lab federated-credential subject to scripts/setup-deploy-identity.sh

Add a federated credential subject for a `replay-lab` GitHub environment using the existing add_fic pattern.

Files:
* scripts/setup-deploy-identity.sh - add FIC subject `repo:${REPO}:environment:replay-lab` (guarded like the existing environment/branch subjects); document in the trailing notes.

Success criteria:
* A `replay-lab` federated credential exists on the deploy identity; idempotent.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 512-534) - setup-deploy-identity FIC pattern.

Dependencies:
* None.

### Step 5.2: Add gated Tier 2 provision + evidence steps to .github/workflows/deploy-croesus.yml

Add a manually-approved `replay-lab` job (or gated steps) that provisions the SPA Graph consent + CA policy, sets `Demo__EnableReplay=true` live via `az webapp config appsettings set` (code-only deploy won't apply bicep), runs the Tier 2 evidence + KQL, and does not run by default.

Files:
* .github/workflows/deploy-croesus.yml - new job `environment: replay-lab` gated by `if: vars.ENABLE_REPLAY_LAB == 'true'` (mirroring the `ENABLE_ROPC_EVIDENCE` + `environment: production` patterns); steps: OIDC login, `bash scripts/provision-ca-policy.sh`, set `Demo__EnableReplay=true`, run Tier 2 evidence KQL, upload artifact; do NOT run teardown automatically.

Discrepancy references:
* Addresses the README Tier 2 prereq "Route the deploy through a manually approved replay-lab environment".

Note: the `replay-lab` GitHub environment is a repo-side object (no secrets, no Entra effect). It is intentionally retained as durable lab infrastructure; document a one-line manual-removal note (Settings → Environments) in docs Step 6.2 for operators who want the repo returned to its exact prior state.

Success criteria:
* The replay-lab job only runs when explicitly enabled + approved; production path unaffected.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 536-580) - workflow structure + evidence job + gating patterns.

Dependencies:
* Step 5.1; Phase 4 scripts.

### Step 5.3: Validate workflow YAML

Validation commands:
* `actionlint .github/workflows/deploy-croesus.yml` (or a YAML parse) - workflow is valid.

Success criteria:
* No workflow syntax errors.

Dependencies:
* Steps 5.1, 5.2.

## Implementation Phase 6: Evidence & documentation

<!-- parallelizable: true -->

### Step 6.1: Update scripts/evidence-kql.kusto

Correct the display-name literals to match the provisioned "Croesus GPD Central SPA/API (mock)", add the 1008 query and the two-leg correlation query, and a `ReplayAttempt` App Insights note.

Files:
* scripts/evidence-kql.kusto - fix display names; add the Unbound/1008 KQL (research Complete Example) and the interactive↔non-interactive correlation query; keep header caveat that 1008 appears only for native-app → EXO/SPO/Teams.

Success criteria:
* Queries reference correct names; the 1008 + correlation queries are present and documented.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/token-protection-1008-mechanics.md (Sections 4a-4c).

Dependencies:
* None.

### Step 6.2: Update docs/obo-demo-guide.md + docs/evidence-narrative.md

Add the Tier 2a/2b walkthrough (provision CA policy, enable gates, exercise the replay, read `ReplayAttempt` + 1008 evidence, teardown) and reinforce the 1008 boundary.

Files:
* docs/obo-demo-guide.md - add Tier 2 provision/deploy/exercise/evidence/teardown steps.
* docs/evidence-narrative.md - map the replay reproduction + 1008 telemetry to the escalation questions; keep the honest boundary; note that revoking the SPA Graph grant leaves already-issued tokens valid until natural (short) expiry (DR-08).

Success criteria:
* A reader can run Tier 2 end-to-end and undo it from the docs alone.

Context references:
* .copilot-tracking/research/subagents/2026-07-06/codebase-demo-map.md (Lines 620-660) - docs structure.

Dependencies:
* None (content aligns with Phases 1-5).

### Step 6.3: Update README.md Tier 2 sections

Replace the "deferred" framing with the delivered 2a/2b design, correct P2→P1 for Token Protection, add the five-item OBO gap table, and document the reversible teardown.

Files:
* README.md - update "Tier 2 replay lab" + "Entra ID licensing" sections; add the five-item wrong→right gap; link the new scripts and gates.

Discrepancy references:
* Addresses DR-03 (README P2 correction) + user request (what's missing, reversibility).

Success criteria:
* README reflects the implemented Tier 2, the P1 correction, the gap, and the undo.

Context references:
* .copilot-tracking/research/2026-07-06/tier2-ca-1008-replay-reproduction-research.md (Key Discoveries 1, 2, 4).

Dependencies:
* None.

## Implementation Phase 7: Validation

<!-- parallelizable: false -->

### Step 7.1: Run full project validation

Execute all validation commands:
* `dotnet build api/Croesus.Api.csproj -c Release` and `dotnet test api/Tests/Croesus.Api.Tests.csproj`
* `npm --prefix spa run build`
* `az bicep build --file infra/main.bicep`
* `bash -n` on all changed/new scripts
* markdown lint on changed docs (repo `.mega-linter.yml` scope; `.copilot-tracking/**` is exempt)

### Step 7.2: Fix minor validation issues

Iterate on lint errors, build warnings, and test failures; apply straightforward isolated fixes directly.

### Step 7.3: Report blocking issues + next steps

Document any issue requiring changes beyond minor fixes (e.g., tenant permission gaps for CA policy creation, or the WI-02 support-matrix reconfirmation); provide the user next steps rather than large-scale inline refactoring.

## Dependencies

* .NET 8 SDK; Node 22 + Vite; Azure CLI + Graph access; jq.
* Tenant: Conditional Access Administrator (or Security Administrator), `Policy.ReadWrite.ConditionalAccess`, `DelegatedPermissionGrant.ReadWrite.All`; test user + break-glass account object ids; Entra ID P1.

## Success Criteria

* Gated, reversible Tier 2 (2a replay shape + 2b report-only Token Protection CA policy) implemented; wrong-vs-right + five-item gap shown; teardown restores the tenant exactly; all builds/tests/linters pass; no raw tokens logged or returned.
